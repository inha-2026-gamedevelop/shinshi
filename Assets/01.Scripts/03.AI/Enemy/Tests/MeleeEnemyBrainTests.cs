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
    public class MeleeEnemyBrainTests
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

        private const int   ENEMY_ID        = 100;
        private const int   PLAYER_A         = 1;
        private const int   PLAYER_B         = 2;
        private const int   SUMMON_ID        = 3;
        private const int   PLAYER_OUTSIDE   = 4;
        private const float PLAYER_HEALTH    = 100000f;
        private const float PLAYER_DEFENSE   = 100f;
        private const float BODY_RADIUS      = 0.5f;
        private const float FAR_DISTANCE     = 10f;
        private const float STEP_SECONDS     = 1f / 30f;
        private const float TOLERANCE        = 0.01f;
        private const double START_TIME      = 50.0;

        private CombatTargetRegistry _registry;
        private DamageResolver       _resolver;
        private EnemyDefinition      _definition;
        private EnemyServer          _enemy;
        private MeleeEnemyBrain      _brain;
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
            _definition = catalog.FindByRole(EEnemyRole.Melee);
            Assert.IsNotNull(_definition, "근접 산귀 정의가 없다");

            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, ledger, DEFENSE_CONSTANT, REDUCTION_CAP);

            _enemy = new EnemyServer(_definition, ENEMY_ID, Vector3.zero, Vector3.forward, REFERENCE_ATTACK, BODY_RADIUS);
            _registry.Register(_enemy);
            _brain = new MeleeEnemyBrain(_enemy, _definition, REFERENCE_HEALTH, _registry, _resolver);

            _playerA = AddPlayer(PLAYER_A, new Vector3(0f, 0f, FAR_DISTANCE));
            _now     = START_TIME;
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Catalog_MeleeDefinition_MatchesPlanValues()
        {
            Assert.AreEqual(2.4f, _definition.HealthRef, TOLERANCE);
            Assert.AreEqual(3.8f, _definition.MoveSpeed, TOLERANCE);
            Assert.AreEqual(0.08f, _definition.AttackDamageRef, TOLERANCE);
            Assert.AreEqual(1.8f, _definition.AttackInterval, TOLERANCE);
            Assert.AreEqual(0.45f, _definition.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Fan, _definition.AttackShape.Shape);
            Assert.AreEqual(1.5f, _definition.AttackShape.Range, TOLERANCE);
            Assert.AreEqual(90f, _definition.AttackShape.ArcAngle, TOLERANCE);
        }

        [Test]
        public void Enemy_MaxHealth_IsHealthRefTimesInjectedAref()
        {
            Assert.AreEqual(2.4f * REFERENCE_ATTACK, _enemy.MaxHealth, TOLERANCE);
        }

        [Test]
        public void Tick_FarTarget_ApproachesAndStopsAtEngageDistance()
        {
            Step(1f);
            Assert.AreEqual(EEnemyState.Chase, _brain.State);
            Assert.AreEqual(3.8f, _enemy.Position.z, 0.2f, "이동 속도 3.8m/s로 접근해야 한다");

            RunUntil(() => _brain.State == EEnemyState.Telegraph, 5f);

            float engage = _definition.AttackShape.Range * MeleeEnemyBrain.PROTOTYPE_ENGAGE_RATIO;
            float gap    = _playerA.Position.z - _enemy.Position.z;
            Assert.AreEqual(engage, gap, TOLERANCE, "공격 거리 안에서 멈춰야 한다");
            Assert.AreEqual(0, _playerA.DamageCalls, "예고만 시작했고 아직 때리지 않았다");
        }

        [Test]
        public void Telegraph_TargetSidestepsOutOfFan_AttackMisses()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            // 반경 안이지만 90도 부채꼴 밖
            _playerA.Position = new Vector3(1.2f, 0f, 0.1f);
            RunUntil(() => _brain.ExecutedCount == 1, 1f);

            Assert.AreEqual(1, _brain.ExecutedCount);
            Assert.AreEqual(0, _brain.LastHitCount);
            Assert.AreEqual(0, _playerA.DamageCalls);
            Assert.AreEqual(PLAYER_HEALTH, _playerA.Health, TOLERANCE);
        }

        [Test]
        public void Telegraph_TargetMovesBehind_AimAndPoseStayFixed()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            Vector3 aim      = _brain.AimDirection;
            Vector3 position = _enemy.Position;

            _playerA.Position = new Vector3(0f, 0f, -1f);
            Step(0.3f);

            Assert.AreEqual(EEnemyState.Telegraph, _brain.State);
            Assert.AreEqual(aim, _brain.AimDirection);
            Assert.AreEqual(aim, _enemy.Forward);
            Assert.AreEqual(position, _enemy.Position, "예고 중에는 이동하지 않는다");

            RunUntil(() => _brain.ExecutedCount == 1, 1f);
            Assert.AreEqual(0, _playerA.DamageCalls, "방향이 고정이라 뒤로 돈 대상은 맞지 않는다");
        }

        [Test]
        public void Execute_PlayersInsideFan_EachHitExactlyOnceWithPlanDamage()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            EnemyTestPlayer inFan   = AddPlayer(PLAYER_B, new Vector3(0.3f, 0f, 1.2f));
            EnemyTestPlayer outside = AddPlayer(PLAYER_OUTSIDE, new Vector3(0f, 0f, -1f));

            // 공격 한 번이 끝나고 다음 예고 전까지 충분히 흘려 보낸다
            RunUntil(() => _brain.ExecutedCount == 1, 2f);
            Step(1f);

            float expected = DamageRules.ApplyDefense(0.08f * REFERENCE_HEALTH, PLAYER_DEFENSE, DEFENSE_CONSTANT, REDUCTION_CAP);

            Assert.AreEqual(1, _brain.ExecutedCount);
            Assert.AreEqual(1, _playerA.DamageCalls);
            Assert.AreEqual(1, inFan.DamageCalls);
            Assert.AreEqual(0, outside.DamageCalls);
            Assert.AreEqual(expected, PLAYER_HEALTH - _playerA.Health, TOLERANCE);
        }

        [Test]
        public void Attack_Repeating_KeepsIntervalFromTelegraphStart()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            var starts = CollectTelegraphStarts(8f);

            Assert.GreaterOrEqual(starts.Count, 4);
            for (int i = 1; i < starts.Count; ++i)
            {
                Assert.GreaterOrEqual(starts[i] - starts[i - 1], _definition.AttackInterval - 1e-6,
                    "예고 시작부터 다음 예고 시작까지 최소 공격 주기를 지켜야 한다");
            }
        }

        [Test]
        public void Recover_LongerThanInterval_BlocksNewAttackUntilRecoverEnds()
        {
            const float LONG_RECOVER = 3f;
            _brain = new MeleeEnemyBrain(_enemy, _definition, REFERENCE_HEALTH, _registry, _resolver, LONG_RECOVER);
            _playerA.Position = new Vector3(0f, 0f, 1f);

            var starts = CollectTelegraphStarts(9f);

            Assert.GreaterOrEqual(starts.Count, 2);
            Assert.GreaterOrEqual(starts[1] - starts[0], _definition.TelegraphSeconds + LONG_RECOVER - 0.1f,
                "후딜 동안은 주기가 지나도 새 예고를 시작하지 않는다");
        }

        [Test]
        public void Death_DuringTelegraph_CancelsPendingStrike()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            Step(0.4f);
            _enemy.ApplyDamage(0f, true, _now);
            Step(1f);

            Assert.AreEqual(EEnemyState.Dead, _brain.State);
            Assert.AreEqual(0, _brain.ExecutedCount);
            Assert.AreEqual(0, _playerA.DamageCalls);
        }

        [Test]
        public void Death_AfterStrike_KeepsAppliedDamageAndStopsMoving()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            RunUntil(() => _brain.ExecutedCount == 1, 2f);
            float healthAfterStrike = _playerA.Health;
            Assert.Less(healthAfterStrike, PLAYER_HEALTH);

            _enemy.ApplyDamage(0f, true, _now);
            _playerA.Position = new Vector3(0f, 0f, 8f);
            Vector3 position = _enemy.Position;
            Step(3f);

            Assert.AreEqual(healthAfterStrike, _playerA.Health, TOLERANCE);
            Assert.AreEqual(position, _enemy.Position, "사망하면 더 이동하지 않는다");
        }

        [Test]
        public void Hit_DuringTelegraph_CancelsStrikeAndEntersHit()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);

            Step(0.2f);
            _enemy.ApplyDamage(10f, false, _now);
            Step(STEP_SECONDS);

            Assert.AreEqual(EEnemyState.Hit, _brain.State);

            Step((float)_definition.TelegraphSeconds);
            Assert.AreEqual(0, _brain.ExecutedCount, "취소된 예고는 타격하지 않는다");
            Assert.AreEqual(0, _playerA.DamageCalls);
        }

        [Test]
        public void Hit_WhileStunned_DoesNotExtendStun()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);
            _enemy.ApplyDamage(10f, false, _now);
            Step(STEP_SECONDS);
            Assert.AreEqual(EEnemyState.Hit, _brain.State);

            Step(MeleeEnemyBrain.PROTOTYPE_HIT_STUN_SECONDS * 0.5f);
            _enemy.ApplyDamage(10f, false, _now);
            Step((MeleeEnemyBrain.PROTOTYPE_HIT_STUN_SECONDS * 0.5f) + (STEP_SECONDS * 2f));

            Assert.AreNotEqual(EEnemyState.Hit, _brain.State, "굳는 동안 또 맞아도 시간이 늘지 않는다");
        }

        [Test]
        public void Hit_AfterStun_AllowsImmediateNewTelegraph()
        {
            _playerA.Position = new Vector3(0f, 0f, 1f);
            RunUntil(() => _brain.State == EEnemyState.Telegraph, 1f);
            _enemy.ApplyDamage(10f, false, _now);
            Step(STEP_SECONDS);
            Assert.AreEqual(EEnemyState.Hit, _brain.State);

            RunUntil(() => _brain.State == EEnemyState.Telegraph, MeleeEnemyBrain.PROTOTYPE_HIT_STUN_SECONDS + 0.2f);

            Assert.AreEqual(EEnemyState.Telegraph, _brain.State, "공격 간격이 남아 있어도 경직이 끝나면 바로 다시 예고한다");
        }

        [Test]
        public void Hit_OutsideTelegraph_DoesNotInterrupt()
        {
            _playerA.Position = new Vector3(0f, 0f, FAR_DISTANCE);
            Step(STEP_SECONDS * 3f);
            Assert.AreEqual(EEnemyState.Chase, _brain.State);

            _enemy.ApplyDamage(10f, false, _now);
            Step(STEP_SECONDS);

            Assert.AreEqual(EEnemyState.Chase, _brain.State, "예고가 아닌 때의 피격은 행동을 끊지 않는다");
        }

        [Test]
        public void Target_StaysWhileValid_ThenReselectsNearestLivingPlayer()
        {
            _playerA.Position = new Vector3(0f, 0f, 4f);
            EnemyTestPlayer b = AddPlayer(PLAYER_B, new Vector3(5f, 0f, 9f));

            Step(0.2f);
            Assert.AreEqual(PLAYER_A, _brain.TargetId);

            // 더 가까워져도 유효한 동안은 바꾸지 않는다
            b.Position = new Vector3(0.5f, 0f, 0.5f);
            Step(0.2f);
            Assert.AreEqual(PLAYER_A, _brain.TargetId);

            _playerA.LifeState = EParticipantLifeState.Dead;
            Step(0.1f);
            Assert.AreEqual(PLAYER_B, _brain.TargetId);
        }

        [Test]
        public void Target_SummonIsNeverSelected()
        {
            _playerA.Position = new Vector3(0f, 0f, 6f);
            EnemyTestPlayer summon = AddPlayer(SUMMON_ID, new Vector3(0f, 0f, 2f));
            summon.IsSummon      = true;
            summon.OwnerCombatId = PLAYER_A;

            Step(0.2f);

            Assert.AreEqual(PLAYER_A, _brain.TargetId);
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
            Assert.AreEqual(CombatIds.NONE, _brain.TargetId);
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
