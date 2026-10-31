// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Systems;

using EnemyDefinition = Shinshi.Data.EnemyCatalogSO.EnemyDefinition;

namespace Shinshi.AI
{
    // 웨이브 계획에 묶이지 않고 종류와 번호를 지정해 산귀를 하나씩 내보낸다. 웨이브 세션의 적 생성, 두뇌 연결, 틱, 예고 동기화, 정리를
    // 필요한 만큼만 따로 갖는다. 드롭, 경험치, 처치 기록은 만들지 않는다(프롤로그 산귀는 출정 보상과 섞이지 않는다)
    /// <summary> 프롤로그 숲의 산귀를 지시대로 소환하고 처치와 예고를 사건으로 알린다, 서버 틱 안에서만 부른다 </summary>
    public sealed class PrologueEnemyField : IDisposable
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string NORMAL_ENEMY_ID    = "enemy_melee";
        public const string TELEGRAPH_ENEMY_ID = "enemy_charger";

        private const float MOVE_EPSILON = 0.0005f; // m

        /****************************************
        *                Types
        ****************************************/

        private sealed class ActiveEnemy
        {
            public EnemyServer       Enemy;
            public NetworkIdentity   Identity;
            public MeleeEnemyBrain   Melee;
            public ChargerEnemyBrain Charger;
            public int               SpawnIndex;
            public EnemyDefinition   Definition;
            public EEnemyState       Behavior;
            public bool              TelegraphOn;
            public double            TelegraphStart;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly WaveSessionDependencies _deps;
        private readonly IEnemyPresentationHost  _presentation;
        private readonly EnemyCrowd              _crowd = new EnemyCrowd();
        private readonly EnemyTelegraphLimiter   _limiter;
        private readonly float                   _healthScale;
        private readonly List<ActiveEnemy>       _active   = new List<ActiveEnemy>();
        private readonly List<ActiveEnemy>       _defeated = new List<ActiveEnemy>();
        private bool                             _disposed = false;

        /****************************************
        *              Properties
        ****************************************/

        public int ActiveCount => _active.Count;

        // 처치한 적의 소환 번호
        public event Action<int> EnemyKilled;

        // 예고 공격이 시작되거나 끝났다
        public event Action TelegraphStarted;
        public event Action TelegraphEnded;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueEnemyField(WaveSessionDependencies deps, float enemyHealthScale)
        {
            _deps         = deps ?? throw new ArgumentNullException(nameof(deps));
            _healthScale  = enemyHealthScale;
            _presentation = deps.Objects as IEnemyPresentationHost;
            _limiter      = new EnemyTelegraphLimiter(deps.Enemies.MaxCloseTelegraphsPerTarget, deps.Enemies.MaxProjectileTelegraphsPerTarget);
        }

        /// <summary> 종류에 맞는 산귀를 지점에 소환한다. 정의나 개체를 만들지 못하면 거짓이다 </summary>
        public bool Spawn(EPrologueEnemyKind kind, int spawnIndex, Vector3 point)
        {
            if (_disposed)
            {
                return false;
            }

            string enemyId = (kind == EPrologueEnemyKind.Telegraph) ? TELEGRAPH_ENEMY_ID : NORMAL_ENEMY_ID;
            if (!_deps.Enemies.TryGetEnemy(enemyId, out EnemyDefinition definition))
            {
                Debug.LogError($"[Prologue] 산귀 정의가 없어 소환하지 못한다, enemy={enemyId}");
                return false;
            }

            int     combatId = _deps.Registry.IssueCombatId();
            Vector3 forward  = FaceNearestPlayer(point);
            var enemy = new EnemyServer(definition, combatId, point, forward, _deps.ReferenceAttack, _deps.BodyRadius,
                _healthScale, _deps.SeparationRadius);

            if (!_deps.Registry.Register(enemy))
            {
                Debug.LogError($"[Prologue] 산귀를 전투 대상에 등록하지 못했다, enemy={enemyId}");
                return false;
            }

            _presentation?.DescribeEnemy(combatId, definition.Role);
            if (!_deps.Objects.TrySpawn(enemy, out NetworkIdentity identity))
            {
                _deps.Registry.Unregister(combatId);
                Debug.LogError($"[Prologue] 산귀 개체를 스폰하지 못했다, enemy={enemyId}");
                return false;
            }

            _crowd.Add(enemy);
            var active = new ActiveEnemy
            {
                Enemy = enemy, Identity = identity, SpawnIndex = spawnIndex, Definition = definition, Behavior = EEnemyState.Idle
            };
            if (!AttachBrain(active))
            {
                Release(active);
                return false;
            }

            _active.Add(active);
            return true;
        }

        /// <summary> 서버 틱마다 부른다, 죽은 적을 정리해 처치를 알리고 살아 있는 적의 두뇌와 예고 표시를 진행한다 </summary>
        public void Tick(double serverTime)
        {
            if (_disposed)
            {
                return;
            }

            CollectDefeats();
            for (int i = 0; i < _active.Count; ++i)
            {
                ActiveEnemy active = _active[i];
                EnemyServer enemy  = active.Enemy;
                Vector3     before = enemy.Position;

                if (active.Melee != null)
                {
                    active.Melee.Tick(serverTime);
                }
                else
                {
                    active.Charger.Tick(serverTime);
                }

                ConfineAndSnap(enemy, before);
                _deps.Objects.Publish(enemy, active.Identity);
                SyncPresentation(active, enemy);
            }
        }

        /// <summary> 남은 적과 표시를 모두 내린다, 처치 사건은 알리지 않는다 </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int i = 0; i < _active.Count; ++i)
            {
                Release(_active[i]);
            }

            _active.Clear();
        }

        // 근접은 부채꼴과 원형, 돌진은 활성 구간이 있는 돌진만 두뇌가 있다. 프롤로그 산귀는 둘뿐이라 그 밖의 모양은 만들지 않는다
        private bool AttachBrain(ActiveEnemy active)
        {
            EnemyDefinition   definition = active.Definition;
            EEnemyAttackShape shape      = (definition.AttackShape != null) ? definition.AttackShape.Shape : EEnemyAttackShape.Circle;

            if ((shape == EEnemyAttackShape.Fan) || (shape == EEnemyAttackShape.Circle))
            {
                active.Melee = new MeleeEnemyBrain(active.Enemy, definition, _deps.ReferenceHealth, _deps.Registry, _deps.Resolver,
                    MeleeEnemyBrain.PROTOTYPE_RECOVER_SECONDS, _crowd, _deps.Navigation, _limiter, _deps.Walls);
                return true;
            }

            if ((shape == EEnemyAttackShape.Dash) && (definition.AttackActiveSeconds > 0f))
            {
                active.Charger = new ChargerEnemyBrain(active.Enemy, definition, _deps.ReferenceHealth, _deps.Registry, _deps.Resolver,
                    _deps.Movement, ChargerEnemyBrain.PROTOTYPE_RECOVER_SECONDS, _crowd, _deps.Navigation, _limiter, _deps.Walls);
                return true;
            }

            Debug.LogError($"[Prologue] 공격 모양 {shape}에는 프롤로그 두뇌가 없다, enemy={definition.DefinitionId}");
            return false;
        }

        private Vector3 FaceNearestPlayer(Vector3 from)
        {
            var players = new List<ICombatTarget>(1);
            _deps.Registry.GetLivingPlayers(players);

            float   best    = float.MaxValue;
            Vector3 forward = Vector3.forward;
            for (int i = 0; i < players.Count; ++i)
            {
                Vector3 offset = players[i].Position - from;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                if ((sqr < best) && (sqr > 0f))
                {
                    best    = sqr;
                    forward = offset;
                }
            }

            return forward;
        }

        // 죽은 몸체는 레지스트리, 무리, 네트워크 개체에서 내린 뒤 소환 번호 순서로 알린다
        private void CollectDefeats()
        {
            _defeated.Clear();
            int write = 0;
            for (int read = 0; read < _active.Count; ++read)
            {
                ActiveEnemy active = _active[read];
                if (active.Enemy.LifeState == EParticipantLifeState.Alive)
                {
                    _active[write] = active;
                    ++write;
                }
                else
                {
                    _defeated.Add(active);
                }
            }

            if (_defeated.Count == 0)
            {
                return;
            }

            _active.RemoveRange(write, _active.Count - write);
            for (int i = 0; i < _defeated.Count; ++i)
            {
                ActiveEnemy defeated       = _defeated[i];
                bool        telegraphWasOn = defeated.TelegraphOn;
                Release(defeated);
                if (telegraphWasOn)
                {
                    TelegraphEnded?.Invoke();
                }

                EnemyKilled?.Invoke(defeated.SpawnIndex);
            }

            _defeated.Clear();
        }

        // 수평 이동을 벽 앞에서 끊고 높이를 지면에 맞춘다
        private void ConfineAndSnap(EnemyServer enemy, Vector3 before)
        {
            Vector3 after = enemy.Position;
            if (_deps.Walls != null)
            {
                var flatAfter = new Vector3(after.x, before.y, after.z);
                if ((flatAfter - before).sqrMagnitude > (MOVE_EPSILON * MOVE_EPSILON))
                {
                    Vector3 reached = _deps.Movement.Move(before, flatAfter, enemy.BodyRadius);
                    if ((reached - flatAfter).sqrMagnitude > (MOVE_EPSILON * MOVE_EPSILON))
                    {
                        after = new Vector3(reached.x, after.y, reached.z);
                        enemy.SetPosition(after);
                    }
                }
            }

            if ((_deps.Spawns.TrySnapToGround(0, after, out Vector3 grounded))
                && (Mathf.Abs(grounded.y - after.y) > MOVE_EPSILON))
            {
                enemy.SetPosition(new Vector3(after.x, grounded.y, after.z));
            }
        }

        // 상태가 바뀔 때만 행동을 알리고, 예고는 시작 시각이 새로 잡힐 때 한 번만 시작하며 상태를 벗어나면 끝낸다
        private void SyncPresentation(ActiveEnemy active, EnemyServer enemy)
        {
            EEnemyState state = (active.Melee != null) ? active.Melee.State : active.Charger.State;
            if ((_presentation != null) && (state != active.Behavior))
            {
                _presentation.SetBehavior(active.Identity, state);
            }

            active.Behavior = state;
            if (state != EEnemyState.Telegraph)
            {
                EndTelegraph(active);
                return;
            }

            double start = (active.Melee != null) ? active.Melee.TelegraphStartTime : active.Charger.TelegraphStartTime;
            if ((active.TelegraphOn) && (active.TelegraphStart == start))
            {
                return;
            }

            EndTelegraph(active);
            BeginTelegraph(active, enemy, start);
        }

        private void BeginTelegraph(ActiveEnemy active, EnemyServer enemy, double start)
        {
            EnemyDefinition   definition = active.Definition;
            EEnemyAttackShape shape      = definition.AttackShape.Shape;

            if (_presentation != null)
            {
                if (active.Melee != null)
                {
                    MeleeEnemyBrain brain = active.Melee;
                    float width = (shape == EEnemyAttackShape.Fan) ? brain.AttackArcAngle : 0f;
                    _presentation.BeginTelegraph(active.Identity, shape, enemy.Position, brain.AimDirection, brain.AttackRange, width,
                        start, brain.TelegraphEndTime);
                }
                else
                {
                    ChargerEnemyBrain brain = active.Charger;
                    _presentation.BeginTelegraph(active.Identity, shape, brain.DashOrigin, brain.AimDirection, brain.DashLength,
                        brain.DashWidth, start, brain.TelegraphEndTime);
                }
            }

            active.TelegraphOn    = true;
            active.TelegraphStart = start;
            TelegraphStarted?.Invoke();
        }

        private void EndTelegraph(ActiveEnemy active)
        {
            if (!active.TelegraphOn)
            {
                return;
            }

            active.TelegraphOn = false;
            _presentation?.EndTelegraph(active.Identity);
            TelegraphEnded?.Invoke();
        }

        private void Release(ActiveEnemy active)
        {
            if ((_presentation != null) && (active.Identity != null))
            {
                if (active.TelegraphOn)
                {
                    active.TelegraphOn = false;
                    _presentation.EndTelegraph(active.Identity);
                }

                if ((active.Enemy.LifeState != EParticipantLifeState.Alive) && (active.Behavior != EEnemyState.Dead))
                {
                    active.Behavior = EEnemyState.Dead;
                    _presentation.SetBehavior(active.Identity, EEnemyState.Dead);
                }
            }

            _limiter.Release(active.Enemy.CombatId);
            _deps.Registry.Unregister(active.Enemy.CombatId);
            _crowd.Remove(active.Enemy);
            if (active.Identity != null)
            {
                _deps.Objects.Despawn(active.Identity);
            }
        }
    }
}
