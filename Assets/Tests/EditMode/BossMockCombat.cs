// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    // 서버 몸체 하나와 가짜 플레이어 한 명으로 틱을 돌린다. 물리 벽은 넣지 않아 전장 경계만 막는다.
    // 피해 판정은 요청을 그대로 적용됐다고만 답해 플레이어가 죽지 않으므로, 보스가 끝까지 대상을 잃지 않고 패턴을 고른다.
    // 플레이어는 틱마다 궤도 위치로 옮기므로 밀림으로 바뀐 위치는 다음 틱에 덮인다. 밀림은 호출 기록으로만 본다
    /// <summary> 보스 서버 몸체의 모의 전투 한 판, 사건은 Events에 순서대로 쌓인다 </summary>
    public sealed class BossMockCombat
    {
        /****************************************
        *                Fields
        ****************************************/

        public const double TICK_SECONDS = 1.0 / Constants.Network.SERVER_TICK_RATE;

        private const float REFERENCE_ATTACK  = 644.32f;
        private const float REFERENCE_HEALTH  = 3541f;
        private const float HEALTH_SCALE      = 1f;
        private const float PLAYER_MAX_HEALTH = 1000000f;
        private const float ORBIT_SPEED       = 0.7f;   // 라디안/초
        private const float RADIUS_SPEED      = 0.23f;  // 라디안/초
        private const float MIN_ORBIT         = 3f;     // m
        private const float ORBIT_SPAN        = 11f;    // m, 근거리부터 원거리 구간까지 고르게 지난다
        private const float HALF              = 0.5f;
        private const float BELOW_TRANSITION  = 0.02f;  // 전환 비율보다 이만큼 아래로 체력을 맞춘다

        private readonly CombatTargetRegistry _registry;
        private readonly float                _transitionRate;

        /****************************************
        *              Properties
        ****************************************/

        public BossServer               Boss     { get; }
        public BossPatternCatalogSource Catalog  { get; }
        public BossEventRecorder        Events   { get; }
        public EnemyTestRigPlayer       Player   { get; }
        public RecordingDisplaceLookup  Displace { get; }
        public double                   Now      { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        // withConnections가 거짓이면 접속 상태 입력을, withDisplace가 거짓이면 밀림 대상 조회를 넘기지 않는다
        public BossMockCombat(BossPatternCatalogSO asset, string bossId, EBossDifficulty difficulty, int seed,
            bool withConnections, bool withDisplace)
        {
            Catalog   = new BossPatternCatalogSource(asset, difficulty);
            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);

            int bossCombatId   = _registry.IssueCombatId();
            int playerCombatId = _registry.IssueCombatId();
            Player   = new EnemyTestRigPlayer(playerCombatId, new Vector3(0f, 0f, MIN_ORBIT), PLAYER_MAX_HEALTH);
            Displace = new RecordingDisplaceLookup(Player);

            if (Catalog.TryGetBoss(bossId, out BossPatternCatalogSO.BossDefinition definition))
            {
                _transitionRate = definition.PhaseTransitionHealthRate;
            }

            Boss = new BossServer(bossId, bossCombatId, Vector3.zero, Vector3.forward, Vector3.zero, REFERENCE_ATTACK,
                REFERENCE_HEALTH, HEALTH_SCALE, Catalog, _registry, new AlwaysAppliedResolver(), null,
                new SeededBossRandom(seed), null, withConnections ? new AlwaysConnected() : null,
                withDisplace ? Displace : null);

            Events = new BossEventRecorder(() => ((IBossEventRelayHost)Boss).ActivePatternId);
            Boss.SetEventListener(Events);
        }

        /// <summary> 플레이어와 보스를 전투 대상으로 등록하고 전투를 연다 </summary>
        public bool Begin()
        {
            Now = 0.0;
            if ((!_registry.Register(Player)) || (!Boss.Begin(Now)))
            {
                return false;
            }

            return _registry.Register(Boss);
        }

        // 플레이어를 보스 둘레의 원 궤도로 돌리고 반경을 바꿔 거리 구간을 고르게 지나게 한다.
        // 전장 중심이 아니라 보스를 따라 도는 것은 돌진으로 자리를 옮기는 보스에게도 근거리 구간이 충분히 나오게 하기 위해서다.
        // 전장 중심 궤도로는 무철 1페이즈 900초 동안 근거리 선택이 19번뿐이라 가중치가 낮은 근거리 패턴이 한 번도 나오지 않았다
        public void Step()
        {
            Now += TICK_SECONDS;
            float t      = (float)Now;
            float radius = MIN_ORBIT + (ORBIT_SPAN * (HALF + (HALF * Mathf.Sin(t * RADIUS_SPEED))));
            float angle  = t * ORBIT_SPEED;

            Vector3 position = Boss.Position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Vector3 offset   = position - Boss.ArenaCenter;
            offset.y = 0f;
            if (offset.magnitude > Boss.ArenaRadius)
            {
                position = Boss.ArenaCenter + (offset.normalized * Boss.ArenaRadius);
            }

            Player.Position = position;

            Events.Phase = Boss.Phase;
            Boss.Tick(Now);
        }

        /// <summary> 조건이 참이 되거나 모의 시간 상한이 찰 때까지 돌린다, 조건이 참이 됐으면 참이다 </summary>
        public bool StepUntil(Func<bool> isDone, int maxSeconds)
        {
            int maxTicks = Constants.Network.SERVER_TICK_RATE * maxSeconds;
            for (int i = 0; (i < maxTicks) && (!isDone()); ++i)
            {
                Step();
            }

            return isDone();
        }

        public void StepSeconds(int seconds)
        {
            int ticks = Constants.Network.SERVER_TICK_RATE * seconds;
            for (int i = 0; i < ticks; ++i)
            {
                Step();
            }
        }

        /// <summary> 체력을 전환 비율 바로 아래로 깎고 한 틱 돌려 2페이즈 전환을 일으킨다 </summary>
        public void ForceTransition()
        {
            float target = Boss.MaxHealth * (_transitionRate - BELOW_TRANSITION);
            Boss.ApplyDamage(Boss.Health - target, false, Now);
            Step();
        }

        /// <summary> 보스를 치명타로 죽인다, 정리는 다음 Step에서 일어난다 </summary>
        public void KillBoss()
        {
            Boss.ApplyDamage(0f, true, Now);
        }

        /****************************************
        *                Fakes
        ****************************************/

        private sealed class AlwaysAppliedResolver : IDamageResolver
        {
            private int _nextId = 1;

            public DamageResult Apply(in DamageRequest request)
            {
                return DamageResult.Applied(request.RawDamage, false, false);
            }

            public int IssueInstanceId()
            {
                int id = _nextId;
                ++_nextId;
                return id;
            }
        }

        private sealed class AlwaysConnected : IBossParticipantConnection
        {
            public bool IsConnected(int combatId)
            {
                return true;
            }
        }

        /// <summary> 가짜 플레이어 한 명만 밀 수 있다고 답하고 밀림 호출을 적는 조회 </summary>
        public sealed class RecordingDisplaceLookup : IBossDisplaceLookup, IBossDisplaceTarget
        {
            private readonly EnemyTestRigPlayer _player;

            public int     CombatId       => _player.CombatId;
            public Vector3 Position       => _player.Position;
            public float   BodyRadius     => _player.BodyRadius;
            public bool    CanBeDisplaced => true;

            public int           Lookups       { get; private set; }
            public List<Vector3> Origins       { get; } = new List<Vector3>();
            public List<Vector3> Destinations  { get; } = new List<Vector3>();
            public List<double>  DisplaceTimes { get; } = new List<double>();

            public RecordingDisplaceLookup(EnemyTestRigPlayer player)
            {
                _player = player;
            }

            public bool TryGetDisplaceTarget(int combatId, out IBossDisplaceTarget target)
            {
                ++Lookups;
                target = (combatId == _player.CombatId) ? this : null;
                return target != null;
            }

            public void Displace(Vector3 position, double serverTime)
            {
                Origins.Add(_player.Position);
                Destinations.Add(position);
                DisplaceTimes.Add(serverTime);
                _player.Position = position;
            }
        }
    }
}
