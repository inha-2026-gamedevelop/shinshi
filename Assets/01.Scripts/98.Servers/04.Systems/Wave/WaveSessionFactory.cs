// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Systems
{
    // CombatServerRuntime의 레지스트리, 판정기, 적 오브젝트 호스트는 부팅 순서상 이 팩토리보다 늦게 채워질 수 있어
    // 생성 때가 아니라 Create(출정 시작) 때 읽는다. 피해 배율은 출정 컨텍스트 값으로 직접 감싼다
    /// <summary> 출정마다 WaveSession을 만드는 운영용 팩토리, 만들 수 없으면 사유를 남기고 null을 돌려준다 </summary>
    public sealed class WaveSessionFactory : IWaveSessionFactory
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float PROTOTYPE_BODY_RADIUS       = 0.5f; // m, 적 몸체 반경의 운영 기준값이 아직 없다
        public const float PROTOTYPE_SEPARATION_RADIUS = 0.5f; // m

        /****************************************
        *                Fields
        ****************************************/

        private readonly Func<CombatServerRuntime>  _runtimeProvider;
        private readonly Func<ParticipantRoster>    _rosterProvider;
        private readonly IEnemyNavigation           _navigation;
        private readonly IWaveSpawnPointSource _spawns;
        private readonly EnemyCatalogSO       _enemies;
        private readonly ProgressionCatalogSO _progression;
        private readonly IBossWallQuery       _walls;
        private readonly IWaveKillerSource    _killers;
        private readonly float                _bodyRadius;
        private readonly float                _separationRadius;

        /****************************************
        *                Methods
        ****************************************/

        // 런타임과 참가자 명단은 설치 순서상 팩토리보다 늦게 생길 수 있어 공급자를 받아 Create 때 읽는다.
        // walls는 정적 벽 검사(예: BossTerrainProbe)이고 null이면 직선 이동을 그대로 둔다. killers는 처치자 조회이고 null이면 빈 문자열이다.
        // navigation이 null이면 적은 NavMesh 없이 직선으로 접근한다
        public WaveSessionFactory(Func<CombatServerRuntime> runtimeProvider, Func<ParticipantRoster> rosterProvider,
            IWaveSpawnPointSource spawns, EnemyCatalogSO enemies, ProgressionCatalogSO progression, IBossWallQuery walls = null,
            IWaveKillerSource killers = null, float bodyRadius = PROTOTYPE_BODY_RADIUS,
            float separationRadius = PROTOTYPE_SEPARATION_RADIUS, IEnemyNavigation navigation = null)
        {
            _runtimeProvider  = runtimeProvider;
            _rosterProvider   = rosterProvider;
            _navigation       = navigation;
            _spawns           = spawns;
            _enemies          = enemies;
            _progression      = progression;
            _walls            = walls;
            _killers          = killers;
            _bodyRadius       = bodyRadius;
            _separationRadius = separationRadius;
        }

        public IWaveSession Create(in WaveSessionContext context)
        {
            CombatServerRuntime runtime = _runtimeProvider?.Invoke();
            if ((runtime == null) || (_spawns == null) || (_enemies == null) || (_progression == null))
            {
                return Fail("팩토리 협력 객체가 비어 있다");
            }

            if (context.Waves == null)
            {
                return Fail("웨이브 정의가 없다");
            }

            if ((!(context.DamageScale > 0f)) || (float.IsInfinity(context.DamageScale)))
            {
                return Fail($"적 피해 배율이 올바르지 않다, scale={context.DamageScale}");
            }

            if (!_progression.TryGetRegion(context.Waves.RegionId, out ProgressionCatalogSO.RegionDefinition region))
            {
                return Fail($"지역 정의가 없다, regionId={context.Waves.RegionId}");
            }

            if ((runtime.Registry == null) || (runtime.Resolver == null) || (runtime.EnemyObjects == null))
            {
                return Fail("전투 런타임에 레지스트리, 판정기, 적 오브젝트 호스트가 아직 없다");
            }

            if (_spawns.GetPointCount(0) <= 0)
            {
                return Fail("첫 웨이브의 출현 지점이 없다, 숲 씬과 DungeonSpawnLayout을 확인할 것");
            }

            var scaledResolver = new BossDamageScaler(runtime.Resolver, context.DamageScale);
            var connections    = new RegistryParticipantConnections(runtime.Registry, _rosterProvider);
            var deps = new WaveSessionDependencies(runtime.Registry, scaledResolver, runtime.EnemyObjects, _spawns, _enemies,
                new WaveEnemyMovement(_walls), _walls, _killers, _progression.GetReferenceAttack(region),
                _progression.GetReferenceHealth(region), _bodyRadius, _separationRadius, _navigation, connections);

            if (!WaveSession.TryCreate(in context, deps, out WaveSession session, out string error))
            {
                return Fail(error);
            }

            return session;
        }

        /// <summary> 프롤로그 숲의 산귀 소환기를 만든다. 웨이브 팩토리와 같은 협력 객체와 숲 기준값을 쓴다, 만들 수 없으면 null이다 </summary>
        public PrologueEnemyField CreatePrologueEnemyField(float damageScale, float enemyHealthScale)
        {
            CombatServerRuntime runtime = _runtimeProvider?.Invoke();
            if ((runtime == null) || (_spawns == null) || (_enemies == null) || (_progression == null))
            {
                Debug.LogError("[Prologue] 산귀 소환기의 협력 객체가 비어 있다");
                return null;
            }

            if ((!(damageScale > 0f)) || (float.IsInfinity(damageScale)) || (!(enemyHealthScale > 0f)))
            {
                Debug.LogError($"[Prologue] 적 배율이 올바르지 않다, damage={damageScale}, health={enemyHealthScale}");
                return null;
            }

            if (!_progression.TryGetRegion(Constants.Combat.DEV_BOSS_REGION_ID, out ProgressionCatalogSO.RegionDefinition region))
            {
                Debug.LogError($"[Prologue] 숲 지역 정의가 없다, regionId={Constants.Combat.DEV_BOSS_REGION_ID}");
                return null;
            }

            if ((runtime.Registry == null) || (runtime.Resolver == null) || (runtime.EnemyObjects == null))
            {
                Debug.LogError("[Prologue] 전투 런타임에 레지스트리, 판정기, 적 오브젝트 호스트가 아직 없다");
                return null;
            }

            var scaledResolver = new BossDamageScaler(runtime.Resolver, damageScale);
            var connections    = new RegistryParticipantConnections(runtime.Registry, _rosterProvider);
            var deps = new WaveSessionDependencies(runtime.Registry, scaledResolver, runtime.EnemyObjects, _spawns, _enemies,
                new WaveEnemyMovement(_walls), _walls, _killers, _progression.GetReferenceAttack(region),
                _progression.GetReferenceHealth(region), _bodyRadius, _separationRadius, _navigation, connections);
            return new PrologueEnemyField(deps, enemyHealthScale);
        }

        private static IWaveSession Fail(string reason)
        {
            Debug.LogError($"[Wave] 웨이브 세션을 만들 수 없다: {reason}");
            return null;
        }
    }
}
