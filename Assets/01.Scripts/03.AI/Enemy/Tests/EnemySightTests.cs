// System
using System;

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
    public class EnemySightTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/EnemyCatalog.asset";

        private const float REFERENCE_ATTACK = 644.32f;
        private const float REFERENCE_HEALTH = 3541f;
        private const float DEFENSE_CONSTANT = 1000f;
        private const float REDUCTION_CAP    = 0.6f;

        private const int   ENEMY_ID      = 100;
        private const int   PLAYER_ID     = 1;
        private const float PLAYER_HEALTH = 100000f;
        private const float BODY_RADIUS   = 0.5f;
        private const float GAP_HALF      = 0.4f;  // 벽 양쪽에 붙어 선 두 몸체의 중심 간격 절반, 직선 0.8m
        private const float WALL_HALF_Z   = 3f;    // 벽 폭 절반
        private const float DETOUR_Z      = 6f;    // 길찾기가 알려 주는 우회 지점
        private const float STEP_SECONDS  = 1f / 30f;
        private const double START_TIME   = 50.0;

        /****************************************
        *                Types
        ****************************************/

        // x = 0 평면의 얇은 벽, 폭은 z 방향 WALL_HALF_Z
        private sealed class ThinWall : IBossWallQuery
        {
            public bool Blocked { get; set; } = true;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                if ((!Blocked) || (Mathf.Abs(direction.x) < 0.0001f))
                {
                    return maxDistance;
                }

                float t = -origin.x / direction.x;
                if ((t < 0f) || (t > maxDistance))
                {
                    return maxDistance;
                }

                return (Mathf.Abs(origin.z + (direction.z * t)) <= WALL_HALF_Z) ? t : maxDistance;
            }
        }

        private sealed class DetourNavigation : IEnemyNavigation
        {
            public bool TryMeasurePath(Vector3 from, Vector3 to, out float length)
            {
                length = 20f;
                return true;
            }

            public bool TryGetSteerPoint(Vector3 from, Vector3 to, out Vector3 point)
            {
                point = new Vector3(0f, 0f, DETOUR_Z);
                return true;
            }

            public bool IsWalkable(Vector3 point)
            {
                return true;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private EnemyCatalogSO       _catalog;
        private CombatTargetRegistry _registry;
        private DamageResolver       _resolver;
        private ThinWall             _wall;
        private DetourNavigation     _navigation;
        private EnemyTestPlayer      _player;
        private double               _now;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(_catalog, "EnemyCatalog 에셋을 찾지 못했다");

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver   = new DamageResolver(_registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);
            _wall       = new ThinWall();
            _navigation = new DetourNavigation();
            _player     = new EnemyTestPlayer(PLAYER_ID, new Vector3(GAP_HALF, 0f, 0f), PLAYER_HEALTH, 0f);
            Assert.IsTrue(_registry.Register(_player));
            _now = START_TIME;
        }

        /****************************************
        *            EnemySight
        ****************************************/

        [Test]
        public void IsClear_NullWalls_IsAlwaysClear()
        {
            Assert.IsTrue(EnemySight.IsClear(null, Vector3.zero, new Vector3(5f, 0f, 0f)));
        }

        [Test]
        public void IsClear_ThinWallBetween_IsBlockedUntilOpened()
        {
            Vector3 from = new Vector3(-GAP_HALF, 0f, 0f);
            Vector3 to   = new Vector3(GAP_HALF, 0f, 0f);

            Assert.IsFalse(EnemySight.IsClear(_wall, from, to));

            _wall.Blocked = false;
            Assert.IsTrue(EnemySight.IsClear(_wall, from, to));
        }

        /****************************************
        *            Melee
        ****************************************/

        [Test]
        public void Melee_WallBetweenInsideRange_KeepsSteeringAndNeverAttacks()
        {
            MeleeEnemyBrain brain = CreateMelee(out EnemyServer enemy, _navigation, _wall);

            int blockedTicks = RunWhileLineBlocked(enemy, () => Tick(brain),
                () => (brain.State == EEnemyState.Telegraph) || (brain.ExecutedCount > 0));

            Assert.Greater(blockedTicks, 0);
            Assert.Greater(enemy.Position.z, 1f, "벽을 마주 보고 서지 않고 우회 지점으로 걸어가야 한다");
        }

        // 길찾기가 없으면 시험용 이동이 벽을 뚫고 지나므로, 직선이 막혀 있는 동안만 공격이 없음을 본다
        [Test]
        public void Melee_WallBetweenWithoutNavigation_NeverAttacksWhileLineIsBlocked()
        {
            MeleeEnemyBrain brain = CreateMelee(out EnemyServer enemy, null, _wall);

            int blockedTicks = RunWhileLineBlocked(enemy, () => Tick(brain),
                () => (brain.State == EEnemyState.Telegraph) || (brain.ExecutedCount > 0));

            Assert.Greater(blockedTicks, 0);
        }

        [Test]
        public void Melee_ClearLineOfSight_StillAttacks()
        {
            _wall.Blocked = false;
            MeleeEnemyBrain brain = CreateMelee(out _, _navigation, _wall);

            RunUntil(brain, () => brain.ExecutedCount == 1, 3f);

            Assert.AreEqual(1, _player.DamageCalls);
        }

        [Test]
        public void Melee_WallAppearsDuringTelegraph_DealsNoDamage()
        {
            _wall.Blocked = false;
            MeleeEnemyBrain brain = CreateMelee(out _, _navigation, _wall);
            RunUntil(brain, () => brain.State == EEnemyState.Telegraph, 2f);

            _wall.Blocked = true;
            RunUntil(brain, () => brain.ExecutedCount == 1, 2f);

            Assert.AreEqual(0, brain.LastHitCount);
            Assert.AreEqual(0, _player.DamageCalls);
        }

        [Test]
        public void Melee_LegacyConstructionWithoutWalls_StillAttacks()
        {
            MeleeEnemyBrain brain = CreateMelee(out _, null, null);

            RunUntil(brain, () => brain.ExecutedCount == 1, 3f);

            Assert.AreEqual(1, _player.DamageCalls);
        }

        /****************************************
        *            Charger
        ****************************************/

        [Test]
        public void Charger_WallBetweenInsideRange_KeepsSteeringAndNeverTelegraphs()
        {
            ChargerEnemyBrain brain = CreateCharger(out EnemyServer enemy, _wall);

            int blockedTicks = RunWhileLineBlocked(enemy, () => Tick(brain),
                () => (brain.State == EEnemyState.Telegraph) || (brain.State == EEnemyState.Dash) || (brain.ExecutedCount > 0));

            Assert.Greater(blockedTicks, 0);
            Assert.Greater(enemy.Position.z, 1f, "벽을 마주 보고 서지 않고 우회 지점으로 걸어가야 한다");
        }

        [Test]
        public void Charger_ClearLineOfSight_StillTelegraphs()
        {
            _wall.Blocked = false;
            ChargerEnemyBrain brain = CreateCharger(out _, _wall);

            RunUntil(brain, () => brain.State == EEnemyState.Telegraph, 3f);

            Assert.AreEqual(EEnemyState.Telegraph, brain.State);
        }

        /****************************************
        *            Ranged
        ****************************************/

        [Test]
        public void Ranged_WallBetweenInsideRange_KeepsSteeringAndNeverTelegraphs()
        {
            RangedEnemyBrain brain = CreateRanged(out EnemyServer enemy, _wall);

            int blockedTicks = RunWhileLineBlocked(enemy, () => Tick(brain),
                () => (brain.State == EEnemyState.Telegraph) || (brain.ExecutedCount > 0));

            Assert.Greater(blockedTicks, 0);
            Assert.AreEqual(0, brain.RetreatCount, "벽 너머 대상에게서는 물러나지 않는다");
            Assert.Greater(enemy.Position.z, 1f, "벽을 마주 보고 서지 않고 우회 지점으로 걸어가야 한다");
        }

        [Test]
        public void Ranged_ClearLineOfSight_StillTelegraphs()
        {
            _wall.Blocked = false;
            RangedEnemyBrain brain = CreateRanged(out _, _wall);

            RunUntil(brain, () => brain.State == EEnemyState.Telegraph, 3f);

            Assert.AreEqual(EEnemyState.Telegraph, brain.State);
        }

        [Test]
        public void Ranged_WallAppearsAfterClear_StopsTelegraphingNewShots()
        {
            _wall.Blocked = false;
            RangedEnemyBrain brain = CreateRanged(out EnemyServer enemy, _wall);
            RunUntil(brain, () => brain.ExecutedCount == 1, 6f);

            _wall.Blocked = true;
            Assert.IsFalse(EnemySight.IsClear(_wall, enemy.Position, _player.Position), "벽을 세운 자리에서 직선이 실제로 막혀야 한다");

            // 첫 발이 맞았을 수 있으므로 피해 횟수는 벽 이후의 증가분만 본다
            int executed    = brain.ExecutedCount;
            int damageCalls = _player.DamageCalls;
            int blockedTicks = 0;
            for (int i = 0; i < 120; ++i)
            {
                Tick(brain);
                if (EnemySight.IsClear(_wall, enemy.Position, _player.Position))
                {
                    break;
                }

                ++blockedTicks;
                Assert.AreNotEqual(EEnemyState.Telegraph, brain.State, $"직선이 막힌 채로 새 예고를 시작했다, tick={i}");
                Assert.AreEqual(executed, brain.ExecutedCount, $"직선이 막힌 채로 새 발사를 했다, tick={i}");
                Assert.AreEqual(damageCalls, _player.DamageCalls, $"벽 너머로 피해를 줬다, tick={i}");
            }

            Assert.Greater(blockedTicks, 0);
        }

        /****************************************
        *                Helpers
        ****************************************/

        // 적과 대상을 잇는 직선이 막혀 있는 틱에서만 공격 시작과 피해가 없음을 확인한다, 우회로 직선이 트이면 정당한 공격이므로 멈춘다
        private int RunWhileLineBlocked(EnemyServer enemy, Action tick, Func<bool> attackStarted)
        {
            const int MAX_TICKS = 120;

            int blockedTicks = 0;
            for (int i = 0; i < MAX_TICKS; ++i)
            {
                if (EnemySight.IsClear(_wall, enemy.Position, _player.Position))
                {
                    break;
                }

                tick();
                if (EnemySight.IsClear(_wall, enemy.Position, _player.Position))
                {
                    break;
                }

                ++blockedTicks;
                Assert.IsFalse(attackStarted(), $"직선이 막힌 채로 공격을 시작했다, tick={i}");
                Assert.AreEqual(0, _player.DamageCalls, $"벽 너머로 피해를 줬다, tick={i}");
            }

            return blockedTicks;
        }

        private RangedEnemyBrain CreateRanged(out EnemyServer enemy, IBossWallQuery walls)
        {
            EnemyDefinition definition = _catalog.FindByRole(EEnemyRole.Ranged);
            Assert.IsNotNull(definition, "원거리 산귀 정의가 없다");

            enemy = new EnemyServer(definition, ENEMY_ID, new Vector3(-GAP_HALF, 0f, 0f), Vector3.right, REFERENCE_ATTACK, BODY_RADIUS);
            Assert.IsTrue(_registry.Register(enemy));

            var projectiles = new BossProjectileManager(_registry, walls);
            var gate        = new BossDamageGate(_resolver);
            gate.Open();
            return new RangedEnemyBrain(enemy, definition, REFERENCE_HEALTH, _registry, projectiles, gate, new EnemyTestMovement(enemy),
                null, _navigation, null, walls: walls);
        }

        private void Tick(RangedEnemyBrain brain)
        {
            _now += STEP_SECONDS;
            _resolver.BeginTick(_now);
            brain.Tick(_now);
        }

        private void RunUntil(RangedEnemyBrain brain, Func<bool> condition, float timeoutSeconds)
        {
            int limit = Mathf.RoundToInt(timeoutSeconds / STEP_SECONDS);
            for (int i = 0; (i < limit) && (!condition()); ++i)
            {
                Tick(brain);
            }

            Assert.IsTrue(condition(), "제한 시간 안에 조건에 닿지 않았다");
        }

        private MeleeEnemyBrain CreateMelee(out EnemyServer enemy, IEnemyNavigation navigation, IBossWallQuery walls)
        {
            EnemyDefinition definition = _catalog.FindByRole(EEnemyRole.Melee);
            Assert.IsNotNull(definition, "근접 산귀 정의가 없다");

            enemy = new EnemyServer(definition, ENEMY_ID, new Vector3(-GAP_HALF, 0f, 0f), Vector3.right, REFERENCE_ATTACK, BODY_RADIUS);
            Assert.IsTrue(_registry.Register(enemy));
            return new MeleeEnemyBrain(enemy, definition, REFERENCE_HEALTH, _registry, _resolver,
                MeleeEnemyBrain.PROTOTYPE_RECOVER_SECONDS, null, navigation, null, walls);
        }

        private ChargerEnemyBrain CreateCharger(out EnemyServer enemy, IBossWallQuery walls)
        {
            EnemyDefinition definition = _catalog.FindByRole(EEnemyRole.Charger);
            Assert.IsNotNull(definition, "돌진 산귀 정의가 없다");

            enemy = new EnemyServer(definition, ENEMY_ID, new Vector3(-GAP_HALF, 0f, 0f), Vector3.right, REFERENCE_ATTACK, BODY_RADIUS);
            Assert.IsTrue(_registry.Register(enemy));
            return new ChargerEnemyBrain(enemy, definition, REFERENCE_HEALTH, _registry, _resolver, new EnemyTestMovement(enemy),
                ChargerEnemyBrain.PROTOTYPE_RECOVER_SECONDS, null, _navigation, null, walls);
        }

        private void Tick(MeleeEnemyBrain brain)
        {
            _now += STEP_SECONDS;
            _resolver.BeginTick(_now);
            brain.Tick(_now);
        }

        private void Tick(ChargerEnemyBrain brain)
        {
            _now += STEP_SECONDS;
            _resolver.BeginTick(_now);
            brain.Tick(_now);
        }

        private void RunUntil(MeleeEnemyBrain brain, Func<bool> condition, float timeoutSeconds)
        {
            int limit = Mathf.RoundToInt(timeoutSeconds / STEP_SECONDS);
            for (int i = 0; (i < limit) && (!condition()); ++i)
            {
                Tick(brain);
            }

            Assert.IsTrue(condition(), "제한 시간 안에 조건에 닿지 않았다");
        }

        private void RunUntil(ChargerEnemyBrain brain, Func<bool> condition, float timeoutSeconds)
        {
            int limit = Mathf.RoundToInt(timeoutSeconds / STEP_SECONDS);
            for (int i = 0; (i < limit) && (!condition()); ++i)
            {
                Tick(brain);
            }

            Assert.IsTrue(condition(), "제한 시간 안에 조건에 닿지 않았다");
        }
    }
}
