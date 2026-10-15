// System
using System;

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
        private const int    CAPPED_ID     = 6;    // 받는 피해 상한을 가진 적
        private const float  CAPPED_AMOUNT    = 30f;  // 방어 적용 피해보다 작은 상한
        private const float  CAPPED_REMAINDER = 100f; // 상한이 남기는 체력
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
        public void Apply_CapTarget_UsesCappedAmountForHealthLifeStealAndNotice()
        {
            CappedDamageable capped = RegisterCapped((health, damage) => CAPPED_AMOUNT);
            float uncapped = RAW_DAMAGE * (1f - (DEFENSE / (DEFENSE + DEFENSE_CONSTANT)));
            _player.LifeStealRate = LIFE_STEAL_RATE;
            int   notices        = 0;
            float noticedAmount  = 0f;
            _resolver.DamageApplied += notice =>
            {
                ++notices;
                noticedAmount = notice.Result.AppliedAmount;
            };

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, CAPPED_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.AreEqual(CAPPED_AMOUNT, result.AppliedAmount, TOLERANCE);
            Assert.IsFalse(result.IsLethal);
            Assert.AreEqual(ENEMY_HEALTH - CAPPED_AMOUNT, capped.Health, TOLERANCE);
            Assert.AreEqual(1, capped.CapCalls);
            Assert.AreEqual(ENEMY_HEALTH, capped.LastCapHealth, TOLERANCE, "상한에는 적용 전 체력을 넘긴다");
            Assert.AreEqual(uncapped, capped.LastCapDamage, TOLERANCE, "상한에는 방어를 적용한 피해를 넘긴다");
            Assert.AreEqual(CAPPED_AMOUNT, _player.LastLifeStealDamage, TOLERANCE, "흡혈은 깎인 값으로 계산한다");
            Assert.AreEqual(1, notices);
            Assert.AreEqual(CAPPED_AMOUNT, noticedAmount, TOLERANCE, "피해 통지는 깎인 값을 싣는다");
        }

        [Test]
        public void Apply_CapTarget_OverkillCappedAboveZeroHealth_IsNotLethal()
        {
            CappedDamageable capped = RegisterCapped((health, damage) => health - CAPPED_REMAINDER);

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, CAPPED_ID, 1, HUGE_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.IsFalse(result.IsLethal, "치명 여부는 깎인 값으로 정한다");
            Assert.AreEqual(ENEMY_HEALTH - CAPPED_REMAINDER, result.AppliedAmount, TOLERANCE);
            Assert.AreEqual(CAPPED_REMAINDER, capped.Health, TOLERANCE);
            Assert.AreEqual(EParticipantLifeState.Alive, capped.LifeState);
        }

        [Test]
        public void Apply_CapTarget_CapAllowsAllRemainingHealth_IsLethal()
        {
            CappedDamageable capped = RegisterCapped((health, damage) => health);

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, CAPPED_ID, 1, HUGE_DAMAGE));

            Assert.IsTrue(result.IsLethal);
            Assert.AreEqual(ENEMY_HEALTH, result.AppliedAmount, TOLERANCE);
            Assert.AreEqual(EParticipantLifeState.Dead, capped.LifeState);
        }

        // 상한은 피해를 줄이기만 한다. 줄인 값이 아닌 반환은 자르지 않고 버려 원래 피해를 쓴다
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-1f)]
        [TestCase(HUGE_DAMAGE)]
        public void Apply_CapTarget_InvalidOrLargerCap_IgnoresCapAndUsesOriginalDamage(float returned)
        {
            CappedDamageable capped = RegisterCapped((health, damage) => returned);
            float expected = RAW_DAMAGE * (1f - (DEFENSE / (DEFENSE + DEFENSE_CONSTANT)));

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, CAPPED_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.AreEqual(expected, result.AppliedAmount, TOLERANCE);
            Assert.AreEqual(ENEMY_HEALTH - expected, capped.Health, TOLERANCE);
        }

        [Test]
        public void Apply_CapTarget_CapReturnsSameDamage_SameAsUncappedTarget()
        {
            CappedDamageable capped = RegisterCapped((health, damage) => damage);

            DamageResult cappedResult = _resolver.Apply(Request(PLAYER_ID, CAPPED_ID, 1, RAW_DAMAGE));
            DamageResult plainResult  = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 2, RAW_DAMAGE));

            Assert.AreEqual(plainResult.AppliedAmount, cappedResult.AppliedAmount, TOLERANCE);
            Assert.AreEqual(_enemy.Health, capped.Health, TOLERANCE);
        }

        [Test]
        public void Apply_CapTarget_CapReturnsZero_IgnoredWithoutDamageNoticeOrLifeSteal()
        {
            CappedDamageable capped = RegisterCapped((health, damage) => 0f);
            _player.LifeStealRate = LIFE_STEAL_RATE;
            int notices = 0;
            _resolver.DamageApplied += notice => ++notices;

            DamageResult result = _resolver.Apply(Request(PLAYER_ID, CAPPED_ID, 1, RAW_DAMAGE));

            Assert.AreEqual(EDamageOutcome.Invulnerable, result.Outcome, "전부 깎인 피해는 없던 것이다");
            Assert.IsFalse(result.CountsAsHit);
            Assert.AreEqual(0, capped.DamageCalls);
            Assert.AreEqual(ENEMY_HEALTH, capped.Health);
            Assert.AreEqual(0, notices);
            Assert.AreEqual(0, _player.LifeStealCalls);
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

        private CappedDamageable RegisterCapped(Func<float, float, float> cap)
        {
            var capped = new CappedDamageable(new FakeDamageable(CAPPED_ID, ECombatFaction.Enemy, ENEMY_HEALTH, DEFENSE), cap);
            Assert.IsTrue(_registry.Register(capped));
            return capped;
        }

        // FakeDamageable을 감싸 받는 피해 상한만 더한 대상, 상한이 받은 인자를 남긴다
        private sealed class CappedDamageable : IDamageable, IDamageCapTarget
        {
            private readonly FakeDamageable            _inner;
            private readonly Func<float, float, float> _cap;

            public int                   CombatId        => _inner.CombatId;
            public ECombatFaction        Faction         => _inner.Faction;
            public EParticipantLifeState LifeState       => _inner.LifeState;
            public bool                  IsSummon        => _inner.IsSummon;
            public int                   OwnerCombatId   => _inner.OwnerCombatId;
            public Vector3               Position        => _inner.Position;
            public Vector3               Forward         => _inner.Forward;
            public float                 BodyRadius      => _inner.BodyRadius;
            public float                 Health          => _inner.Health;
            public float                 MaxHealth       => _inner.MaxHealth;
            public float                 Defense         => _inner.Defense;
            public bool                  IsInvulnerable  => _inner.IsInvulnerable;
            public bool                  IsGuardingFront => _inner.IsGuardingFront;
            public int                   DamageCalls     => _inner.DamageCalls;

            public int   CapCalls      { get; private set; }
            public float LastCapHealth { get; private set; }
            public float LastCapDamage { get; private set; }

            public CappedDamageable(FakeDamageable inner, Func<float, float, float> cap)
            {
                _inner = inner;
                _cap   = cap;
            }

            public float CapIncomingDamage(float currentHealth, float damage)
            {
                ++CapCalls;
                LastCapHealth = currentHealth;
                LastCapDamage = damage;
                return _cap(currentHealth, damage);
            }

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
                _inner.ApplyDamage(appliedAmount, isLethal, serverTime);
            }
        }
    }
}
