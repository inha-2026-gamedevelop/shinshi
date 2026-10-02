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
    // 벽은 z = WallZ 평면인 가짜 이동 창구로 확인한다. 실제 NavMesh, 물리 벽, 서버 틱은 거치지 않는다
    public class ChargerEnemyBrainTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/EnemyCatalog.asset";

        // 지역 기준값은 이 시험이 명시적으로 주입한다. 값은 체험판 숲 기준(Aref 644.32, Href 3541)이고 플레이어 능력치가 아니다
        private const float REFERENCE_ATTACK = 644.32f;
        private const float REFERENCE_HEALTH = 3541f;

        // PlayerConfigSO 초안과 같은 피해 감소 공식 값
        private const float DEFENSE_CONSTANT = 1000f;
        private const float REDUCTION_CAP    = 0.6f;

        private const int    ENEMY_ID      = 100;
        private const int    PLAYER_A      = 1;
        private const int    PLAYER_B      = 2;
        private const int    PLAYER_C      = 3;
        private const float  PLAYER_HEALTH = 100000f;
        private const float  PLAYER_DEFENSE = 100f;
        private const float  BODY_RADIUS   = 0.5f;
        private const float  FAR_DISTANCE  = 15f;
        private const float  STEP_SECONDS  = 1f / 30f;
        private const float  TOLERANCE     = 0.01f;
        private const double START_TIME    = 50.0;
        private const double HUGE_GAP      = 100.0;

        private CombatTargetRegistry _registry;
        private DamageResolver       _resolver;
        private EnemyDefinition      _definition;
        private EnemyServer          _enemy;
        private ChargerEnemyBrain    _brain;
        private EnemyTestMovement    _movement;
        private EnemyTestPlayer      _playerA;
        private double               _now;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            EnemyCatalogSO catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog, "EnemyCatalog 에셋을 찾지 못했다");
            _definition = catalog.FindByRole(EEnemyRole.Charger);
            Assert.IsNotNull(_definition, "돌진 산귀 정의가 없다");

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);

            _enemy = new EnemyServer(_definition, ENEMY_ID, Vector3.zero, Vector3.forward, REFERENCE_ATTACK, BODY_RADIUS);
            _registry.Register(_enemy);
            _movement = new EnemyTestMovement(_enemy);
            _brain    = new ChargerEnemyBrain(_enemy, _definition, REFERENCE_HEALTH, _registry, _resolver, _movement);

            _playerA = AddPlayer(PLAYER_A, new Vector3(0f, 0f, FAR_DISTANCE));
            _now     = START_TIME;
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Catalog_ChargerDefinition_MatchesPlanValues()
        {
            Assert.AreEqual(3.2f, _definition.HealthRef, TOLERANCE);
            Assert.AreEqual(4.0f, _definition.MoveSpeed, TOLERANCE);
            Assert.AreEqual(0.14f, _definition.AttackDamageRef, TOLERANCE);
            Assert.AreEqual(4f, _definition.AttackInterval, TOLERANCE);
            Assert.AreEqual(0.9f, _definition.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0.45f, _definition.AttackActiveSeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Dash, _definition.AttackShape.Shape);
            Assert.AreEqual(6f, _definition.AttackShape.Range, TOLERANCE);
            Assert.AreEqual(1.5f, _definition.AttackShape.Width, TOLERANCE);
        }

        [Test]
        public void Enemy_MaxHealth_IsHealthRefTimesInjectedAref()
        {
            Assert.AreEqual(3.2f * REFERENCE_ATTACK, _enemy.MaxHealth, TOLERANCE);
        }

        [Test]
        public void Tick_TargetBeyondRange_ApproachesAtCatalogSpeedAndTelegraphsAtSixMeters()
        {
            Step(1f);
            Assert.AreEqual(EEnemyState.Chase, _brain.State);
            Assert.AreEqual(4f, _enemy.Position.z, 0.2f, "접근 속도 4.0m/s");

            RunUntil(() => _brain.State == EEnemyState.Telegraph, 5f);

            Assert.AreEqual(6f, _playerA.Position.z - _enemy.Position.z, TOLERANCE, "6m 거리에서 멈춰 예고한다");
            Assert.AreEqual(0, _playerA.DamageCalls);
        }

        [Test]
        public void Telegraph_TargetMoves_PositionAndAimStayFixed()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            Vector3 aim      = _brain.AimDirection;
            Vector3 position = _enemy.Position;
            Assert.AreEqual(Vector3.forward, aim);

            _playerA.Position = new Vector3(-4f, 0f, 3f);
            Step(0.6f);

            Assert.AreEqual(EEnemyState.Telegraph, _brain.State);
            Assert.AreEqual(aim, _brain.AimDirection);
            Assert.AreEqual(aim, _enemy.Forward);
            Assert.AreEqual(position, _enemy.Position, "예고 중에는 이동하지 않는다");
            Assert.AreEqual(position, _brain.DashOrigin);
        }

        [Test]
        public void Telegraph_TargetSidestepsOutOfLane_DashMisses()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            // 폭의 절반 0.75m 밖
            _playerA.Position = new Vector3(2f, 0f, 5f);
            RunUntil(() => _brain.ExecutedCount == 1, 3f);

            Assert.AreEqual(0, _brain.LastHitCount);
            Assert.AreEqual(0, _playerA.DamageCalls);
            Assert.AreEqual(6f, _enemy.Position.z, TOLERANCE, "빗나가도 정의의 거리만큼 간다");
        }

        [Test]
        public void Dash_PassingThroughTarget_HitsOnceWithPlanDamage()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            RunUntil(() => _brain.ExecutedCount == 1, 3f);

            float expected = DamageRules.ApplyDefense(0.14f * REFERENCE_HEALTH, PLAYER_DEFENSE, DEFENSE_CONSTANT, REDUCTION_CAP);

            Assert.AreEqual(1, _brain.LastHitCount);
            Assert.AreEqual(1, _playerA.DamageCalls);
            Assert.AreEqual(expected, PLAYER_HEALTH - _playerA.Health, TOLERANCE);
        }

        [Test]
        public void Dash_MultipleTargetsInLane_EachHitOnceAndOutsideNever()
        {
            // 가장 가까운 A가 정면이라 방향이 +z로 고정된다
            _playerA.Position = new Vector3(0f, 0f, 2f);
            EnemyTestPlayer inLane   = AddPlayer(PLAYER_B, new Vector3(0.7f, 0f, 4.5f));
            EnemyTestPlayer outLane  = AddPlayer(PLAYER_C, new Vector3(1.0f, 0f, 3f));

            RunUntil(() => _brain.ExecutedCount == 1, 3f);
            Step(1f);

            Assert.AreEqual(1, _playerA.DamageCalls);
            Assert.AreEqual(1, inLane.DamageCalls);
            Assert.AreEqual(0, outLane.DamageCalls);
            Assert.AreEqual(2, _brain.LastHitCount);
        }

        [Test]
        public void Dash_SingleHugeTickGap_NeverExceedsRangeAndNeverDoubleHits()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            // 예고가 끝난 뒤 매우 늦게 한 번만 틱한다
            _now += HUGE_GAP;
            _resolver.BeginTick(_now);
            _brain.Tick(_now);

            Assert.AreEqual(6f, _enemy.Position.z, TOLERANCE, "이동 거리는 정의의 거리를 넘지 않는다");
            Assert.AreEqual(6f, _brain.TraveledDistance, TOLERANCE);
            Assert.AreEqual(1, _playerA.DamageCalls);
            Assert.AreEqual(EEnemyState.Recover, _brain.State);

            // 같은 시각에 다시 틱해도 한 돌진의 피해는 한 번이다
            for (int i = 0; i < 5; ++i)
            {
                _resolver.BeginTick(_now);
                _brain.Tick(_now);
            }

            Assert.AreEqual(1, _playerA.DamageCalls);
            Assert.AreEqual(1, _brain.ExecutedCount);
        }

        [Test]
        public void Dash_LargeTickSteps_StillHitsTargetBetweenTicksOnce()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            EnemyTestPlayer between = AddPlayer(PLAYER_B, new Vector3(0f, 0f, 2.5f));
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            // 돌진 구간을 0.4초 틱으로 건너뛴다. 대상이 틱 사이 구간에 있어도 빠지지 않아야 한다
            while (_brain.ExecutedCount == 0)
            {
                _now += 0.4;
                _resolver.BeginTick(_now);
                _brain.Tick(_now);
            }

            Assert.AreEqual(1, between.DamageCalls);
            Assert.AreEqual(1, _playerA.DamageCalls);
            Assert.LessOrEqual(_brain.TraveledDistance, 6f + TOLERANCE);
        }

        [Test]
        public void Dash_WallAhead_StopsBeforeWallAndBlocksDamageBehindIt()
        {
            _movement.WallZ = 4f;
            _playerA.Position = new Vector3(0f, 0f, 3f);
            EnemyTestPlayer behind = AddPlayer(PLAYER_C, new Vector3(0f, 0f, 5f));

            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);
            Assert.AreEqual(3.5f, _brain.DashLength, TOLERANCE, "예고 길이는 벽 앞까지다");

            RunUntil(() => _brain.ExecutedCount == 1, 3f);

            Assert.AreEqual(3.5f, _enemy.Position.z, TOLERANCE, "벽 앞에서 멈춘다");
            Assert.AreEqual(EEnemyState.Recover, _brain.State);
            Assert.AreEqual(1, _playerA.DamageCalls, "벽 앞의 대상은 맞는다");
            Assert.AreEqual(0, behind.DamageCalls, "벽 뒤의 대상은 맞지 않는다");
        }

        [Test]
        public void Dash_WallAppearsAfterTelegraph_StopsAtWallWithoutPassingIt()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            // 예고 길이를 잰 뒤에 생긴 벽
            _movement.HiddenWallZ = 2.5f;
            RunUntil(() => _brain.ExecutedCount == 1, 3f);

            Assert.AreEqual(2.0f, _enemy.Position.z, TOLERANCE);
            Assert.AreEqual(0, _playerA.DamageCalls, "벽 뒤의 대상은 맞지 않는다");
        }

        [Test]
        public void Attack_Repeating_KeepsIntervalFromTelegraphStart()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            List<double> starts = CollectTelegraphStarts(14f);

            Assert.GreaterOrEqual(starts.Count, 3);
            for (int i = 1; i < starts.Count; ++i)
            {
                Assert.GreaterOrEqual(starts[i] - starts[i - 1], _definition.AttackInterval - 1e-6,
                    "예고 시작부터 다음 예고 시작까지 최소 공격 주기를 지켜야 한다");
            }
        }

        [Test]
        public void Recover_LongerThanInterval_BlocksNewTelegraphUntilRecoverEnds()
        {
            const float LONG_RECOVER = 6f;
            _brain = new ChargerEnemyBrain(_enemy, _definition, REFERENCE_HEALTH, _registry, _resolver, _movement, LONG_RECOVER);
            _playerA.Position = new Vector3(0f, 0f, 5f);

            List<double> starts = CollectTelegraphStarts(16f);

            Assert.GreaterOrEqual(starts.Count, 2);
            float minimum = _definition.TelegraphSeconds + _definition.AttackActiveSeconds + LONG_RECOVER;
            Assert.GreaterOrEqual(starts[1] - starts[0], minimum - 0.1f, "후딜 동안은 주기가 지나도 새 예고를 시작하지 않는다");
        }

        [Test]
        public void Death_DuringTelegraph_CancelsDash()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            Step(0.7f);
            Vector3 position = _enemy.Position;
            _enemy.ApplyDamage(0f, true, _now);
            Step(2f);

            Assert.AreEqual(EEnemyState.Dead, _brain.State);
            Assert.AreEqual(0, _brain.ExecutedCount);
            Assert.AreEqual(0, _playerA.DamageCalls);
            Assert.AreEqual(position, _enemy.Position);
        }

        [Test]
        public void Death_DuringDash_StopsMovementAndPendingDamage()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            RunUntil(() => _brain.State == EEnemyState.Dash, 2f);
            Step(0.1f);

            Assert.Less(_brain.TraveledDistance, 5f, "플레이어에 닿기 전에 죽인다");
            _enemy.ApplyDamage(0f, true, _now);
            Vector3 position = _enemy.Position;
            Step(2f);

            Assert.AreEqual(EEnemyState.Dead, _brain.State);
            Assert.AreEqual(0, _playerA.DamageCalls, "미적용 피해는 취소된다");
            Assert.AreEqual(position, _enemy.Position, "사망하면 더 이동하지 않는다");
        }

        [Test]
        public void Death_AfterHit_KeepsAppliedDamage()
        {
            _playerA.Position = new Vector3(0f, 0f, 5f);
            RunUntil(() => _brain.ExecutedCount == 1, 3f);
            float health = _playerA.Health;
            Assert.Less(health, PLAYER_HEALTH);

            _enemy.ApplyDamage(0f, true, _now);
            Step(2f);

            Assert.AreEqual(health, _playerA.Health, TOLERANCE);
            Assert.AreEqual(1, _playerA.DamageCalls);
        }

        [Test]
        public void Target_NoLivingPlayer_IdlesInPlace()
        {
            Step(0.5f);
            Assert.AreEqual(EEnemyState.Chase, _brain.State);

            _playerA.LifeState = EParticipantLifeState.Dead;
            Step(0.1f);
            Vector3 position = _enemy.Position;
            Step(2f);

            Assert.AreEqual(EEnemyState.Idle, _brain.State);
            Assert.AreEqual(position, _enemy.Position);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private EnemyTestPlayer AddPlayer(int id, Vector3 position)
        {
            var player = new EnemyTestPlayer(id, position, PLAYER_HEALTH, PLAYER_DEFENSE);
            Assert.IsTrue(_registry.Register(player));
            return player;
        }

        private void Tick()
        {
            _now += STEP_SECONDS;
            _resolver.BeginTick(_now);
            _brain.Tick(_now);
        }

        private void Step(float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds / STEP_SECONDS);
            for (int i = 0; i < ticks; ++i)
            {
                Tick();
            }
        }

        private void RunUntil(Func<bool> condition, float timeoutSeconds)
        {
            int limit = Mathf.RoundToInt(timeoutSeconds / STEP_SECONDS);
            for (int i = 0; (i < limit) && (!condition()); ++i)
            {
                Tick();
            }

            Assert.IsTrue(condition(), "제한 시간 안에 조건에 닿지 않았다");
        }

        private List<double> CollectTelegraphStarts(float seconds)
        {
            var starts = new List<double>();
            EEnemyState previous = _brain.State;
            int ticks = Mathf.RoundToInt(seconds / STEP_SECONDS);

            for (int i = 0; i < ticks; ++i)
            {
                Tick();
                if ((_brain.State == EEnemyState.Telegraph) && (previous != EEnemyState.Telegraph))
                {
                    starts.Add(_now);
                }

                previous = _brain.State;
            }

            return starts;
        }
    }
}
