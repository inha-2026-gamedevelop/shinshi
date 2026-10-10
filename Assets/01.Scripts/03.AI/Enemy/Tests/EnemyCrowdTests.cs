// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using EnemyDefinition = Shinshi.Data.EnemyCatalogSO.EnemyDefinition;

namespace Shinshi.Tests
{
    // 간격 계산 단위 시험과, 두뇌를 여러 마리 돌려 보는 밀집 시험을 함께 둔다. 벽은 z = WallZ 평면인 가짜 이동 창구이고
    // 실제 물리, NavMesh, 서버 틱은 거치지 않는다. Play Mode 밀집 모습은 EnemyCrowdTestRig로 따로 본다
    public class EnemyCrowdTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/EnemyCatalog.asset";

        private const float REFERENCE_ATTACK = 644.32f;
        private const float REFERENCE_HEALTH = 3541f;
        private const float DEFENSE_CONSTANT = 1000f;
        private const float REDUCTION_CAP    = 0.6f;

        private const int   PLAYER_ID         = 1;
        private const int   ENEMY_ID_BASE     = 100;
        private const float PLAYER_HEALTH     = 1000000000f;
        private const float PLAYER_DEFENSE    = 100f;
        private const float BODY_RADIUS       = 0.5f;
        private const float SEPARATION_RADIUS = 0.3f;
        private const float STEP_SECONDS      = 1f / 30f;
        private const float TOLERANCE         = 0.01f;
        private const double START_TIME       = 50.0;

        private CombatTargetRegistry _registry;
        private DamageResolver       _resolver;
        private EnemyDefinition      _meleeDefinition;
        private EnemyDefinition      _chargerDefinition;
        private EnemyCrowd           _crowd;
        private EnemyTestPlayer      _player;
        private double               _now;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            EnemyCatalogSO catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog, "EnemyCatalog 에셋을 찾지 못했다");
            _meleeDefinition   = catalog.FindByRole(EEnemyRole.Melee);
            _chargerDefinition = catalog.FindByRole(EEnemyRole.Charger);
            Assert.IsNotNull(_meleeDefinition, "근접 산귀 정의가 없다");
            Assert.IsNotNull(_chargerDefinition, "돌진 산귀 정의가 없다");

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);
            _crowd    = new EnemyCrowd();
            _now      = START_TIME;

            _player = new EnemyTestPlayer(PLAYER_ID, Vector3.zero, PLAYER_HEALTH, PLAYER_DEFENSE);
            Assert.IsTrue(_registry.Register(_player));
        }

        /****************************************
        *            EnemyServer Tests
        ****************************************/

        [Test]
        public void EnemyServer_DefaultSeparationRadius_IsZeroAndBodyRadiusIsKept()
        {
            var enemy = new EnemyServer(_meleeDefinition, ENEMY_ID_BASE, Vector3.zero, Vector3.forward,
                REFERENCE_ATTACK, BODY_RADIUS);

            Assert.AreEqual(0f, enemy.SeparationRadius);
            Assert.AreEqual(BODY_RADIUS, enemy.BodyRadius);
        }

        [Test]
        public void EnemyServer_SeparationRadius_IsIndependentFromBodyRadius()
        {
            var enemy = new EnemyServer(_meleeDefinition, ENEMY_ID_BASE, Vector3.zero, Vector3.forward,
                REFERENCE_ATTACK, BODY_RADIUS, 1f, SEPARATION_RADIUS);

            Assert.AreEqual(SEPARATION_RADIUS, enemy.SeparationRadius);
            Assert.AreEqual(BODY_RADIUS, enemy.BodyRadius);
        }

        [Test]
        public void EnemyServer_NegativeSeparationRadius_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyServer(_meleeDefinition, ENEMY_ID_BASE,
                Vector3.zero, Vector3.forward, REFERENCE_ATTACK, BODY_RADIUS, 1f, -0.1f));
        }

        /****************************************
        *            EnemyCrowd Tests
        ****************************************/

        [Test]
        public void Resolve_NoNeighborInRange_ReturnsDesired()
        {
            EnemyServer self = AddEnemy(0, Vector3.zero);
            AddEnemy(1, new Vector3(5f, 0f, 0f));

            Vector3 desired = new Vector3(0.5f, 0f, 0f);
            Assert.AreEqual(desired, _crowd.Resolve(self, desired, 1f));
        }

        [Test]
        public void Resolve_OverlapsNeighbor_PushesSelfToBoundaryAndLeavesNeighbor()
        {
            EnemyServer self     = AddEnemy(0, new Vector3(2f, 0f, 0f));
            EnemyServer neighbor = AddEnemy(1, Vector3.zero);

            Vector3 resolved = _crowd.Resolve(self, new Vector3(0.4f, 0f, 0f), 5f);

            Assert.AreEqual(SEPARATION_RADIUS * 2f, resolved.x, TOLERANCE, "이웃 몸체 경계까지만 밀려야 한다");
            Assert.AreEqual(0f, resolved.z, TOLERANCE);
            Assert.AreEqual(Vector3.zero, neighbor.Position, "이웃 위치는 바뀌지 않는다");
        }

        [Test]
        public void Resolve_StepFromCurrentPositionIsCappedPerCall()
        {
            EnemyServer self = AddEnemy(0, new Vector3(0.1f, 0f, 0f));
            AddEnemy(1, Vector3.zero);

            Vector3 resolved = _crowd.Resolve(self, self.Position, 0.05f);

            Assert.AreEqual(0.15f, resolved.x, TOLERANCE, "한 번에 상한만큼만 벌어진다");
        }

        [Test]
        public void Resolve_SamePoint_UsesDifferentDirectionsPerCombatId()
        {
            EnemyServer a = AddEnemy(0, Vector3.zero);
            EnemyServer b = AddEnemy(1, Vector3.zero);
            EnemyServer c = AddEnemy(2, Vector3.zero);

            Vector3 fromB = _crowd.Resolve(b, Vector3.zero, 10f);
            Vector3 fromC = _crowd.Resolve(c, Vector3.zero, 10f);

            Assert.Greater(Vector3.Distance(fromB, Vector3.zero), 0f, "겹친 자리에서 벗어나야 한다");
            Assert.Greater(Vector3.Distance(fromB, fromC), 0.1f, "번호가 다르면 다른 방향으로 갈라진다");
            Assert.AreEqual(Vector3.zero, a.Position);
        }

        [Test]
        public void Resolve_SandwichedBetweenTwoNeighbors_DoesNotMoveIntoWorseOverlap()
        {
            // 두 이웃 사이 틈이 몸체 하나보다 좁아 어디로 가도 겹친다
            EnemyServer self = AddEnemy(0, new Vector3(0f, 0f, 0f));
            AddEnemy(1, new Vector3(-0.4f, 0f, 0f));
            AddEnemy(2, new Vector3(0.4f, 0f, 0f));

            Vector3 resolved = _crowd.Resolve(self, new Vector3(0.2f, 0f, 0f), 5f);

            float nowOverlap   = (SEPARATION_RADIUS * 2f - 0.4f) * 2f;
            float afterOverlap = Mathf.Max(0f, SEPARATION_RADIUS * 2f - Mathf.Abs(resolved.x + 0.4f))
                + Mathf.Max(0f, SEPARATION_RADIUS * 2f - Mathf.Abs(resolved.x - 0.4f));
            Assert.LessOrEqual(afterOverlap, nowOverlap + TOLERANCE, "더 깊이 겹치는 곳으로 가면 안 된다");
        }

        [Test]
        public void Resolve_DeadNeighborAndZeroRadiusNeighbor_AreIgnored()
        {
            EnemyServer self = AddEnemy(0, new Vector3(2f, 0f, 0f));
            EnemyServer dead = AddEnemy(1, Vector3.zero);
            dead.ApplyDamage(0f, true, 0.0);

            var ghost = new EnemyServer(_meleeDefinition, ENEMY_ID_BASE + 9, Vector3.zero, Vector3.forward,
                REFERENCE_ATTACK, BODY_RADIUS);
            _crowd.Add(ghost);

            Vector3 desired = new Vector3(0.1f, 0f, 0f);
            Assert.AreEqual(desired, _crowd.Resolve(self, desired, 5f));
        }

        [Test]
        public void Resolve_SelfWithoutSeparationRadius_ReturnsDesired()
        {
            var self = new EnemyServer(_meleeDefinition, ENEMY_ID_BASE, Vector3.zero, Vector3.forward,
                REFERENCE_ATTACK, BODY_RADIUS);
            AddEnemy(1, Vector3.zero);

            Vector3 desired = new Vector3(0.1f, 0f, 0f);
            Assert.AreEqual(desired, _crowd.Resolve(self, desired, 5f));
        }

        [Test]
        public void Remove_RemovedEnemy_NoLongerBlocks()
        {
            EnemyServer self     = AddEnemy(0, new Vector3(2f, 0f, 0f));
            EnemyServer neighbor = AddEnemy(1, Vector3.zero);

            Assert.IsTrue(_crowd.Remove(neighbor));
            Assert.IsFalse(_crowd.Remove(neighbor));

            Vector3 desired = new Vector3(0.1f, 0f, 0f);
            Assert.AreEqual(desired, _crowd.Resolve(self, desired, 5f));
        }

        /****************************************
        *          Melee Crowd Tests
        ****************************************/

        [Test]
        public void MeleeCrowd_SamePointSpawn_SeparatesSmoothlyWithoutFlingingAway()
        {
            _player.Position = new Vector3(0f, 0f, 12f);
            List<MeleeEntry> crowd = SpawnMelees(24, _ => new Vector3(0f, 0f, 0f));

            float limit      = (_meleeDefinition.MoveSpeed + EnemyCrowd.PROTOTYPE_DEPENETRATION_SPEED) * STEP_SECONDS;
            float maxStep    = 0f;
            RunMelees(crowd, 12f, (entry, step) => maxStep = Mathf.Max(maxStep, step));

            Assert.LessOrEqual(maxStep, limit + TOLERANCE, "한 틱 이동은 이동 속도와 벌어지는 속도의 합을 넘지 않는다");
            Assert.LessOrEqual(_crowd.MeasureWorstOverlap(), 0.05f, "충분히 지나면 겹침이 남지 않는다");

            for (int i = 0; i < crowd.Count; ++i)
            {
                Vector3 offset = crowd[i].Server.Position - _player.Position;
                offset.y = 0f;
                Assert.LessOrEqual(offset.magnitude, 6f, "밀려서 플레이어에게서 멀리 흩어지면 안 된다");
            }
        }

        // 한쪽에서 몰려와 플레이어를 둘러싸는 상황. 안착한 뒤 3초 동안 서로 겹치지 않고, 방향을 뒤집으며 떠는 개체가 없어야 한다
        [TestCase(7)]
        [TestCase(11)]
        [TestCase(23)]
        public void MeleeCrowd_Surrounding_SettlesWithoutOverlapOrJitter(int seed)
        {
            const int   COUNT               = 24;
            const float SETTLE_SECONDS      = 12f;
            const float OBSERVE_SECONDS     = 3f;
            const float REVERSAL_MIN_STEP   = 0.005f;
            const float REVERSAL_DOT        = -0.5f;
            const int   MAX_REVERSALS       = 8;   // 틱 단위 떨림은 같은 구간에서 29~118회였다. 자리 없는 개체의 느린 재시도만 허용하는 상한
            const int   MIN_ATTACKERS       = 8;
            const float MAX_OVERLAP         = 0.05f;

            _player.Position = Vector3.zero;
            var random = new System.Random(seed);
            List<MeleeEntry> crowd = SpawnMelees(COUNT, _ => new Vector3(
                ((float)random.NextDouble() - 0.5f) * 8f, 0f, 6f + ((float)random.NextDouble() * 4f)));

            RunMelees(crowd, SETTLE_SECONDS, null);

            int reversals = 0;
            var last      = new Dictionary<MeleeEntry, Vector3>();
            RunMelees(crowd, OBSERVE_SECONDS, (entry, step) =>
            {
                Vector3 delta = entry.Step;
                if ((delta.magnitude > REVERSAL_MIN_STEP) && last.TryGetValue(entry, out Vector3 previous)
                    && (previous.magnitude > REVERSAL_MIN_STEP)
                    && (Vector3.Dot(delta.normalized, previous.normalized) < REVERSAL_DOT))
                {
                    ++reversals;
                }

                last[entry] = delta;
            });

            int attackers = 0;
            for (int i = 0; i < crowd.Count; ++i)
            {
                if (crowd[i].Brain.ExecutedCount > 0)
                {
                    ++attackers;
                }
            }

            Assert.LessOrEqual(_crowd.MeasureWorstOverlap(), MAX_OVERLAP);
            Assert.LessOrEqual(reversals, MAX_REVERSALS, "안착한 무리가 방향을 뒤집으며 떨면 안 된다");
            Assert.GreaterOrEqual(attackers, MIN_ATTACKERS, "앞줄 뒤에서 교착해 대부분이 공격에 못 닿으면 안 된다");
        }

        // 같은 자리에 겹쳐 태어난 무리가 정착한 뒤, 개체마다 3초 동안 움직인 범위(상자 대각선)가 작아야 한다.
        // 방향 반전 횟수는 진폭을 모르므로 이 값으로 눈에 보이는 떨림을 따로 잰다. 자리를 못 찾은 개체는 포기와 휴식을
        // 길게 거듭하며 가끔 재시도하므로 완전한 정지가 아니라 폭의 상한을 본다
        [TestCase(12.0f, 0.0f)]
        [TestCase(0.0f, 12.0f)]
        public void MeleeCrowd_SamePointSpawn_SettledWobbleStaysSmall(float spawnZ, float playerZ)
        {
            const int   COUNT           = 24;
            const float SETTLE_SECONDS  = 40f;   // 자리를 못 찾은 개체가 포기와 휴식을 거듭해 가만히 서기까지 걸리는 시간
            const float OBSERVE_SECONDS = 3f;
            const float MAX_WOBBLE      = 0.3f;  // m, 간격 반경과 같다. 자리 없는 개체가 포기와 휴식 끝에 한 번 재시도하는 폭이 상한이다

            _player.Position = new Vector3(0f, 0f, playerZ);
            List<MeleeEntry> crowd = SpawnMelees(COUNT, _ => new Vector3(0f, 0f, spawnZ));

            RunMelees(crowd, SETTLE_SECONDS, null);

            var min = new Dictionary<MeleeEntry, Vector3>();
            var max = new Dictionary<MeleeEntry, Vector3>();
            RunMelees(crowd, OBSERVE_SECONDS, (entry, step) =>
            {
                Vector3 p = entry.Server.Position;
                if (!min.ContainsKey(entry))
                {
                    min[entry] = p;
                    max[entry] = p;
                    return;
                }

                min[entry] = Vector3.Min(min[entry], p);
                max[entry] = Vector3.Max(max[entry], p);
            });

            float worst = 0f;
            foreach (MeleeEntry entry in crowd)
            {
                Vector3 span = max[entry] - min[entry];
                span.y = 0f;
                worst = Mathf.Max(worst, span.magnitude);
            }

            Assert.LessOrEqual(worst, MAX_WOBBLE, "정착한 개체가 눈에 띄게 흔들리면 안 된다");
        }

        [Test]
        public void MeleeCrowd_BlockedBehindFrontRow_StillAttacksWhenInsideAttackRange()
        {
            // 앞줄이 접근 정지 거리에 서 있고 뒤 개체가 그 옆으로 비껴 들어와 공격 반경 안에서 멈춘다
            _player.Position = Vector3.zero;
            float engage = _meleeDefinition.AttackShape.Range * MeleeEnemyBrain.PROTOTYPE_ENGAGE_RATIO;
            MeleeEntry front = SpawnMelee(0, new Vector3(0f, 0f, engage));
            MeleeEntry back  = SpawnMelee(1, new Vector3(0.2f, 0f, 6f));

            var entries = new List<MeleeEntry> { front, back };
            RunMelees(entries, 8f, null);

            Assert.GreaterOrEqual(back.Brain.ExecutedCount, 1, "막힌 개체도 반경 안이면 공격을 시작해야 한다");
            Assert.LessOrEqual(_crowd.MeasureWorstOverlap(), 0.05f);
        }

        [Test]
        public void MeleeCrowd_WithoutCrowd_KeepsLegacyBehavior()
        {
            // crowd를 넘기지 않으면 같은 자리 개체가 그대로 겹쳐 같이 움직인다
            _player.Position = new Vector3(0f, 0f, 10f);
            var a = new EnemyServer(_meleeDefinition, ENEMY_ID_BASE, Vector3.zero, Vector3.forward, REFERENCE_ATTACK,
                BODY_RADIUS, 1f, SEPARATION_RADIUS);
            var b = new EnemyServer(_meleeDefinition, ENEMY_ID_BASE + 1, Vector3.zero, Vector3.forward, REFERENCE_ATTACK,
                BODY_RADIUS, 1f, SEPARATION_RADIUS);
            _registry.Register(a);
            _registry.Register(b);
            var brainA = new MeleeEnemyBrain(a, _meleeDefinition, REFERENCE_HEALTH, _registry, _resolver);
            var brainB = new MeleeEnemyBrain(b, _meleeDefinition, REFERENCE_HEALTH, _registry, _resolver);

            for (int i = 0; i < 60; ++i)
            {
                _now += STEP_SECONDS;
                _resolver.BeginTick(_now);
                brainA.Tick(_now);
                brainB.Tick(_now);
            }

            Assert.AreEqual(a.Position, b.Position);
        }

        /****************************************
        *         Charger Crowd Tests
        ****************************************/

        [Test]
        public void ChargerDash_WallStopDistanceUsesBodyRadiusNotSeparationRadius()
        {
            const float WALL_Z = 4f;

            ChargerEntry charger = SpawnCharger(0, Vector3.zero, WALL_Z);
            _player.Position = new Vector3(0f, 0f, _chargerDefinition.AttackShape.Range);

            RunMixed(new List<MeleeEntry>(), new List<ChargerEntry> { charger }, 2f);

            Assert.GreaterOrEqual(charger.Brain.ExecutedCount, 1);
            Assert.AreEqual(WALL_Z - BODY_RADIUS, charger.Brain.DashLength, TOLERANCE, "벽 앞 정지 거리는 몸체 반경(0.5) 기준");
            Assert.AreEqual(WALL_Z - BODY_RADIUS, charger.Server.Position.z, TOLERANCE, "간격 반경(0.3)으로 멈추면 안 된다");
        }

        [Test]
        public void ChargerDash_PassesThroughCrowdKeepingLengthAndHit_ThenSeparates()
        {
            // 돌진은 정적 지형만 막으므로 경로에 이웃이 있어도 거리와 피해는 그대로이고, 끝난 뒤 겹침이 풀린다
            _player.Position = new Vector3(0f, 0f, 8f);
            ChargerEntry charger = SpawnCharger(0, Vector3.zero, float.PositiveInfinity);
            MeleeEntry   blocker = SpawnMelee(1, new Vector3(0f, 0f, 5f));
            var melees   = new List<MeleeEntry> { blocker };
            var chargers = new List<ChargerEntry> { charger };

            RunMixed(melees, chargers, 3f);

            Assert.GreaterOrEqual(charger.Brain.ExecutedCount, 1);
            Assert.AreEqual(_chargerDefinition.AttackShape.Range, charger.Brain.DashLength, TOLERANCE, "돌진 길이는 정의대로");
            Assert.AreEqual(_chargerDefinition.AttackShape.Range, charger.Brain.TraveledDistance, TOLERANCE, "이웃에 막혀 일찍 서면 안 된다");
            Assert.AreEqual(1, charger.Brain.LastHitCount, "플레이어 피해는 그대로");

            RunMixed(melees, chargers, 3f);
            Assert.LessOrEqual(_crowd.MeasureWorstOverlap(), 0.05f, "돌진이 끝난 뒤 겹침이 풀려야 한다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private sealed class MeleeEntry
        {
            public EnemyServer     Server;
            public MeleeEnemyBrain Brain;
            public Vector3         Step;
        }

        private sealed class ChargerEntry
        {
            public EnemyServer       Server;
            public ChargerEnemyBrain Brain;
            public Vector3           Step;
        }

        private EnemyServer AddEnemy(int index, Vector3 position)
        {
            var enemy = new EnemyServer(_meleeDefinition, ENEMY_ID_BASE + index, position, Vector3.forward,
                REFERENCE_ATTACK, BODY_RADIUS, 1f, SEPARATION_RADIUS);
            _crowd.Add(enemy);
            return enemy;
        }

        private MeleeEntry SpawnMelee(int index, Vector3 position)
        {
            var server = new EnemyServer(_meleeDefinition, ENEMY_ID_BASE + index, position, -position, REFERENCE_ATTACK,
                BODY_RADIUS, 1f, SEPARATION_RADIUS);
            Assert.IsTrue(_registry.Register(server));
            _crowd.Add(server);

            return new MeleeEntry
            {
                Server = server,
                Brain  = new MeleeEnemyBrain(server, _meleeDefinition, REFERENCE_HEALTH, _registry, _resolver,
                    MeleeEnemyBrain.PROTOTYPE_RECOVER_SECONDS, _crowd)
            };
        }

        private ChargerEntry SpawnCharger(int index, Vector3 position, float wallZ)
        {
            var server = new EnemyServer(_chargerDefinition, ENEMY_ID_BASE + index, position, Vector3.forward, REFERENCE_ATTACK,
                BODY_RADIUS, 1f, SEPARATION_RADIUS);
            Assert.IsTrue(_registry.Register(server));
            _crowd.Add(server);

            var movement = new EnemyTestMovement(server) { WallZ = wallZ };
            return new ChargerEntry
            {
                Server = server,
                Brain  = new ChargerEnemyBrain(server, _chargerDefinition, REFERENCE_HEALTH, _registry, _resolver, movement,
                    ChargerEnemyBrain.PROTOTYPE_RECOVER_SECONDS, _crowd)
            };
        }

        private List<MeleeEntry> SpawnMelees(int count, Func<int, Vector3> positionOf)
        {
            var list = new List<MeleeEntry>(count);
            for (int i = 0; i < count; ++i)
            {
                list.Add(SpawnMelee(i, positionOf(i)));
            }

            return list;
        }

        // 틱마다 모두 한 번씩 돌리고 각 개체의 이번 틱 이동 길이를 콜백으로 알린다
        private void RunMelees(List<MeleeEntry> entries, float seconds, Action<MeleeEntry, float> onStep)
        {
            int ticks = Mathf.RoundToInt(seconds / STEP_SECONDS);
            for (int t = 0; t < ticks; ++t)
            {
                _now += STEP_SECONDS;
                _resolver.BeginTick(_now);

                for (int i = 0; i < entries.Count; ++i)
                {
                    MeleeEntry entry  = entries[i];
                    Vector3    before = entry.Server.Position;
                    entry.Brain.Tick(_now);

                    Vector3 step = entry.Server.Position - before;
                    step.y     = 0f;
                    entry.Step = step;
                    onStep?.Invoke(entry, step.magnitude);
                }
            }
        }

        private void RunMixed(List<MeleeEntry> melees, List<ChargerEntry> chargers, float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds / STEP_SECONDS);
            for (int t = 0; t < ticks; ++t)
            {
                _now += STEP_SECONDS;
                _resolver.BeginTick(_now);

                for (int i = 0; i < melees.Count; ++i)
                {
                    melees[i].Brain.Tick(_now);
                }

                for (int i = 0; i < chargers.Count; ++i)
                {
                    chargers[i].Brain.Tick(_now);
                }
            }
        }
    }
}
