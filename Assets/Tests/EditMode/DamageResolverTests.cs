// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class DamageResolverTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // PlayerConfigSO 초안과 같은 피해 감소 공식 값
        private const float DEFENSE_CONSTANT = 1000f;
        private const float REDUCTION_CAP    = 0.6f;
        private const float PLAYER_DEFENSE_CONSTANT = 750f;
        private const float LIFE_STEAL_RATE         = 0.1f;

        private const int    PLAYER_ID     = 1;
        private const int    ENEMY_ID      = 2;
        private const int    SECOND_ENEMY  = 3;
        private const int    ALLY_ID       = 4;
        private const float  PLAYER_HEALTH = 1500f;
        private const float  ENEMY_HEALTH  = 500f;
        private const float  DEFENSE       = 100f;
        private const float  RAW_DAMAGE    = 100f;
        private const float  HUGE_DAMAGE   = 100000f;
        private const float  HUGE_DEFENSE  = 1000000f;
        private const double NOW           = 50.0;
        private const float  TOLERANCE     = 0.001f;

        private CombatTargetRegistry _registry;
        private HitLedger            _ledger;
        private DamageResolver       _resolver;
        private FakeDamageable       _player;
        private FakeDamageable       _enemy;
        private FakeDamageable       _secondEnemy;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            _ledger   = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, _ledger, DEFENSE_CONSTANT, REDUCTION_CAP);

            _player      = new FakeDamageable(PLAYER_ID, ECombatFaction.Player, PLAYER_HEALTH, DEFENSE);
            _enemy       = new FakeDamageable(ENEMY_ID, ECombatFaction.Enemy, ENEMY_HEALTH, DEFENSE);
            _secondEnemy = new FakeDamageable(SECOND_ENEMY, ECombatFaction.Enemy, ENEMY_HEALTH, DEFENSE);

            _registry.Register(_player);
            _registry.Register(_enemy);
            _registry.Register(_secondEnemy);
            _resolver.BeginTick(NOW);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Apply_WithDefense_ReducesByDefenseFormula()
        {
            float expected = RAW_DAMAGE * (1f - (DEFENSE / (DEFENSE + DEFENSE_CONSTANT)));

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.AreEqual(expected, result.AppliedAmount, TOLERANCE);
            Assert.AreEqual(ENEMY_HEALTH - expected, _enemy.Health, TOLERANCE);
            Assert.IsFalse(result.IsLethal);
        }

        [Test]
        public void Apply_HugeDefense_CapsReduction()
        {
            _enemy.Defense = HUGE_DEFENSE;

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(RAW_DAMAGE * (1f - REDUCTION_CAP), result.AppliedAmount, TOLERANCE);
        }

        [Test]
        public void Apply_SameActionSameTarget_SecondIsDuplicateAndHealthDropsOnce()
        {
            int action = _resolver.IssueInstanceId();

            DamageResult first  = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE));
            DamageResult second = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Applied, first.Outcome);
            Assert.AreEqual(EDamageOutcome.DuplicateHit, second.Outcome);
            Assert.AreEqual(0f, second.AppliedAmount);
            Assert.AreEqual(1, _enemy.DamageCalls);
            Assert.AreEqual(ENEMY_HEALTH - first.AppliedAmount, _enemy.Health, TOLERANCE);
        }

        [Test]
        public void Apply_SameActionDifferentTargets_HitsEachOnce()
        {
            int action = _resolver.IssueInstanceId();

            DamageResult first  = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE));
            DamageResult second = _resolver.Apply(Request(PLAYER_ID, SECOND_ENEMY, action, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Applied, first.Outcome);
            Assert.AreEqual(EDamageOutcome.Applied, second.Outcome);
        }

        [Test]
        public void Apply_NewActionSameTarget_HitsAgain()
        {
            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, _resolver.IssueInstanceId(), RAW_DAMAGE));

            DamageResult next = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, _resolver.IssueInstanceId(), RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Applied, next.Outcome);
            Assert.AreEqual(2, _enemy.DamageCalls);
        }

        [Test]
        public void Apply_AfterRetention_SameActionCanHitAgain()
        {
            int action = _resolver.IssueInstanceId();
            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE));

            _resolver.BeginTick(NOW + Constants.Combat.HIT_LEDGER_RETENTION_SECONDS);

            Assert.AreEqual(EDamageOutcome.Applied, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE)).Outcome);
        }

        [Test]
        public void Apply_LethalOverkill_AppliesRemainingHealthAndKills()
        {
            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, HUGE_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.IsTrue(result.IsLethal);
            Assert.AreEqual(ENEMY_HEALTH, result.AppliedAmount, TOLERANCE);
            Assert.AreEqual(0f, _enemy.Health);
            Assert.AreEqual(EParticipantLifeState.Dead, _enemy.LifeState);
            Assert.AreEqual(NOW, _enemy.DeathTime);
            Assert.IsTrue(result.CountsAsHit);
        }

        [Test]
        public void Apply_ToDeadTarget_TargetNotAliveAndNoChange()
        {
            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, HUGE_DAMAGE));

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 2, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.TargetNotAlive, result.Outcome);
            Assert.AreEqual(1, _enemy.DamageCalls);
        }

        [Test]
        public void Apply_UnknownTarget_TargetNotFound()
        {
            const int UNKNOWN_ID = 99;

            Assert.AreEqual(EDamageOutcome.TargetNotFound, _resolver.Apply(Request(PLAYER_ID, UNKNOWN_ID, 1, RAW_DAMAGE)).Outcome);
        }

        [Test]
        public void Apply_NoneIds_TargetNotFound()
        {
            Assert.AreEqual(EDamageOutcome.TargetNotFound, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, CombatIds.NONE, RAW_DAMAGE)).Outcome);
        }

        [Test]
        public void Apply_SameFaction_Ignored()
        {
            var ally = new FakeDamageable(ALLY_ID, ECombatFaction.Player, PLAYER_HEALTH, DEFENSE);
            _registry.Register(ally);

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ALLY_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.SameFaction, result.Outcome);
            Assert.AreEqual(PLAYER_HEALTH, ally.Health);
        }

        [Test]
        public void Apply_AttackerAlreadyRemoved_StillHits()
        {
            // 공격자가 먼저 전장에서 빠진 뒤 남은 투사체가 맞히는 경우
            _registry.Unregister(PLAYER_ID);

            Assert.AreEqual(EDamageOutcome.Applied, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, RAW_DAMAGE)).Outcome);
        }

        [Test]
        public void Apply_Invulnerable_IgnoredWithoutRecording()
        {
            _enemy.IsInvulnerable = true;
            int action = _resolver.IssueInstanceId();

            DamageResult blocked = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE));
            _enemy.IsInvulnerable = false;
            DamageResult later = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Invulnerable, blocked.Outcome);
            Assert.AreEqual(EDamageOutcome.Applied, later.Outcome);
        }

        [Test]
        public void Apply_GuardingFrontFromFront_Guarded()
        {
            _enemy.IsGuardingFront = true;
            _enemy.Forward         = Vector3.forward;

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, RAW_DAMAGE, Vector3.forward));

            Assert.AreEqual(EDamageOutcome.Guarded, result.Outcome);
            Assert.AreEqual(ENEMY_HEALTH, _enemy.Health);
        }

        [Test]
        public void Apply_GuardedAction_CannotHitAgainAfterGuardEnds()
        {
            _enemy.IsGuardingFront = true;
            int action = _resolver.IssueInstanceId();

            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE, Vector3.forward));
            _enemy.IsGuardingFront = false;

            Assert.AreEqual(EDamageOutcome.DuplicateHit, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE, Vector3.forward)).Outcome);
        }

        [Test]
        public void Apply_GuardingFrontHitFromRear_AppliedAsRearHit()
        {
            _enemy.IsGuardingFront = true;
            _enemy.Forward         = Vector3.forward;

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, RAW_DAMAGE, Vector3.back));

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.IsTrue(result.IsRearHit);
        }

        [Test]
        public void Apply_GuardingFrontUnblockable_Applied()
        {
            _enemy.IsGuardingFront = true;
            var request = new DamageRequest(PLAYER_ID, ENEMY_ID, 1, 1, RAW_DAMAGE, EDamageFlags.Unblockable,
                Vector3.forward, Constants.Combat.BASIC_ATTACK_SOURCE_ID);

            Assert.AreEqual(EDamageOutcome.Applied, _resolver.Apply(request).Outcome);
        }

        [Test]
        public void Apply_PlayerTargetWithPlayerConstant_UsesPlayerConstant()
        {
            var resolver = new DamageResolver(_registry, _ledger, DEFENSE_CONSTANT, REDUCTION_CAP, 1f, null, null,
                PLAYER_DEFENSE_CONSTANT);
            resolver.BeginTick(NOW);
            float expected = RAW_DAMAGE * (1f - (DEFENSE / (DEFENSE + PLAYER_DEFENSE_CONSTANT)));

            DamageResult result = resolver.Apply(Request(ENEMY_ID, PLAYER_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(expected, result.AppliedAmount, TOLERANCE);
        }

        [Test]
        public void Apply_EnemyTargetWithPlayerConstant_KeepsEnemyConstant()
        {
            var resolver = new DamageResolver(_registry, _ledger, DEFENSE_CONSTANT, REDUCTION_CAP, 1f, null, null,
                PLAYER_DEFENSE_CONSTANT);
            resolver.BeginTick(NOW);
            float expected = RAW_DAMAGE * (1f - (DEFENSE / (DEFENSE + DEFENSE_CONSTANT)));

            DamageResult result = resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(expected, result.AppliedAmount, TOLERANCE);
        }

        [Test]
        public void Apply_PlayerTargetWithoutPlayerConstant_UsesSharedConstant()
        {
            float expected = RAW_DAMAGE * (1f - (DEFENSE / (DEFENSE + DEFENSE_CONSTANT)));

            DamageResult result = _resolver.Apply(Request(ENEMY_ID, PLAYER_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(expected, result.AppliedAmount, TOLERANCE);
        }

        [Test]
        public void Apply_PlayerWithLifeSteal_HealsByAppliedDamageAndNotifies()
        {
            _player.LifeStealRate = LIFE_STEAL_RATE;
            _player.Health        = PLAYER_HEALTH - RAW_DAMAGE;
            int   notifiedOwner  = CombatIds.NONE;
            float notifiedHealed = 0f;
            _resolver.LifeStealApplied += (owner, healed) =>
            {
                notifiedOwner  = owner;
                notifiedHealed = healed;
            };

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, RAW_DAMAGE));

            float expected = result.AppliedAmount * LIFE_STEAL_RATE;
            Assert.AreEqual(result.AppliedAmount, _player.LastLifeStealDamage, TOLERANCE);
            Assert.AreEqual((PLAYER_HEALTH - RAW_DAMAGE) + expected, _player.Health, TOLERANCE);
            Assert.AreEqual(PLAYER_ID, notifiedOwner);
            Assert.AreEqual(expected, notifiedHealed, TOLERANCE);
        }

        [Test]
        public void Apply_LethalOverkillWithLifeSteal_UsesRemainingHealthOnly()
        {
            _player.LifeStealRate = LIFE_STEAL_RATE;

            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, HUGE_DAMAGE));

            Assert.AreEqual(ENEMY_HEALTH, _player.LastLifeStealDamage, TOLERANCE);
        }

        [Test]
        public void Apply_GuardedInvulnerableOrDuplicate_NoLifeSteal()
        {
            _player.LifeStealRate = LIFE_STEAL_RATE;
            int action = _resolver.IssueInstanceId();

            _enemy.IsInvulnerable = true;
            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE));
            _enemy.IsInvulnerable  = false;
            _enemy.IsGuardingFront = true;
            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE, Vector3.forward));
            _enemy.IsGuardingFront = false;
            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, action, RAW_DAMAGE, Vector3.forward));

            Assert.AreEqual(0, _player.LifeStealCalls);
        }

        [Test]
        public void Apply_SummonHit_LifeStealGoesToOwner()
        {
            const int SUMMON_ID = 5;

            var summon = new FakeDamageable(SUMMON_ID, ECombatFaction.Player, PLAYER_HEALTH, DEFENSE)
            {
                IsSummon      = true,
                OwnerCombatId = PLAYER_ID,
                LifeStealRate = LIFE_STEAL_RATE
            };
            _registry.Register(summon);
            _player.LifeStealRate = LIFE_STEAL_RATE;

            _resolver.Apply(Request(SUMMON_ID, ENEMY_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(1, _player.LifeStealCalls);
            Assert.AreEqual(0, summon.LifeStealCalls);
        }

        [Test]
        public void Apply_EnemyHitsPlayer_NoLifeStealForEnemy()
        {
            _enemy.LifeStealRate = LIFE_STEAL_RATE;

            _resolver.Apply(Request(ENEMY_ID, PLAYER_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(0, _enemy.LifeStealCalls);
        }

        [Test]
        public void Apply_DeadOwner_NoLifeStealNotice()
        {
            _player.LifeStealRate = LIFE_STEAL_RATE;
            _player.LifeState     = EParticipantLifeState.Dead;
            bool notified = false;
            _resolver.LifeStealApplied += (owner, healed) => notified = true;

            _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, RAW_DAMAGE));

            Assert.IsFalse(notified);
        }

        [Test]
        public void IssueInstanceId_RepeatedCalls_NeverReturnsNoneAndIncreases()
        {
            int first  = _resolver.IssueInstanceId();
            int second = _resolver.IssueInstanceId();

            Assert.AreNotEqual(CombatIds.NONE, first);
            Assert.Greater(second, first);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static DamageRequest Request(int attacker, int target, int actionId, float rawDamage)
        {
            return Request(attacker, target, actionId, rawDamage, Vector3.forward);
        }

        private static DamageRequest Request(int attacker, int target, int actionId, float rawDamage, Vector3 source)
        {
            return new DamageRequest(attacker, target, actionId, actionId, rawDamage, EDamageFlags.None, source,
                Constants.Combat.BASIC_ATTACK_SOURCE_ID);
        }
    }
}
