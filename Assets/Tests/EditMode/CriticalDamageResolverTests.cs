// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public sealed class CriticalDamageResolverTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int PLAYER_ID = 1;
        private const int ENEMY_ID = 2;
        private const int SECOND_ENEMY_ID = 3;
        private const int SUMMON_ID = 4;
        private const float RAW_DAMAGE = 100f;
        private const float HEALTH = 10000f;
        private const double NOW = 50.0;

        private PlayerConfigSO _config;
        private CombatTargetRegistry _registry;
        private DamageResolver _resolver;
        private Damageable _owner;
        private Damageable _enemy;
        private Damageable _secondEnemy;
        private readonly List<AppliedDamageNotice> _notices = new List<AppliedDamageNotice>();
        private int _rolls;
        private float _nextRoll;
        private IDamageable _retained;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver = new DamageResolver(_registry, ledger, _config.DefenseReductionConstant, _config.DamageReductionCap,
                _config.CritDamageMultiplier, Roll, Retained);
            _owner = new Damageable(PLAYER_ID, ECombatFaction.Player) { CritChance = 0.5f };
            _enemy = new Damageable(ENEMY_ID, ECombatFaction.Enemy);
            _secondEnemy = new Damageable(SECOND_ENEMY_ID, ECombatFaction.Enemy);
            _registry.Register(_owner);
            _registry.Register(_enemy);
            _registry.Register(_secondEnemy);
            _notices.Clear();
            _retained = null;
            _rolls = 0;
            _nextRoll = 0.1f;
            _resolver.DamageApplied += _notices.Add;
            _resolver.BeginTick(NOW);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(0f, false)]
        [TestCase(1f, true)]
        public void Apply_ChanceEndpoint_DecidesWithoutRandom(float chance, bool expected)
        {
            _owner.CritChance = chance;
            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1));
            Assert.AreEqual(expected, result.IsCritical);
            Assert.AreEqual(expected ? RAW_DAMAGE * _config.CritDamageMultiplier : RAW_DAMAGE, result.AppliedAmount);
            Assert.AreEqual(0, _rolls);
        }

        [Test]
        public void Apply_SameActionDifferentTargets_RollsEachTargetIndependently()
        {
            DamageResult first = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1));
            _nextRoll = 0.9f;
            DamageResult second = _resolver.Apply(Request(PLAYER_ID, SECOND_ENEMY_ID, 1));
            Assert.IsTrue(first.IsCritical);
            Assert.IsFalse(second.IsCritical);
            Assert.AreEqual(2, _rolls);
            Assert.AreEqual(2, _notices.Count);
        }

        [Test]
        public void Apply_PersistentTicks_RollEveryNewAction()
        {
            DamageResult first = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, "persistent"));
            _nextRoll = 0.9f;
            DamageResult second = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 2, "persistent"));
            Assert.IsTrue(first.IsCritical);
            Assert.IsFalse(second.IsCritical);
            Assert.AreEqual(2, _rolls);
        }

        [Test]
        public void Apply_Duplicate_DoesNotRollOrEmitAgain()
        {
            DamageRequest request = Request(PLAYER_ID, ENEMY_ID, 1);
            _resolver.Apply(in request);
            float health = _enemy.Health;
            DamageResult duplicate = _resolver.Apply(in request);
            Assert.AreEqual(EDamageOutcome.DuplicateHit, duplicate.Outcome);
            Assert.AreEqual(health, _enemy.Health);
            Assert.AreEqual(1, _rolls);
            Assert.AreEqual(1, _notices.Count);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(0f)]
        [TestCase(-1f)]
        public void Apply_InvalidRawDamage_DoesNotRollOrApply(float amount)
        {
            var request = new DamageRequest(PLAYER_ID, ENEMY_ID, 1, 1, amount, EDamageFlags.None, Vector3.forward, "skill");
            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(in request).Outcome);
            Assert.AreEqual(0, _rolls);
            Assert.AreEqual(0, _notices.Count);
            Assert.AreEqual(HEALTH, _enemy.Health);
            Assert.IsTrue(_resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1)).IsApplied, "invalid request did not consume the ledger");
        }

        [Test]
        public void Apply_InvalidTargetState_DoesNotRollOrApply()
        {
            _enemy.Position = new Vector3(float.NaN, 0f, 0f);
            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1)).Outcome);
            _enemy.Position = Vector3.zero;
            _enemy.Defense = float.PositiveInfinity;
            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1)).Outcome);
            _enemy.Defense = 0f;
            _enemy.Health = float.NaN;
            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1)).Outcome);
            Assert.AreEqual(0, _rolls);
            Assert.AreEqual(0, _notices.Count);
        }

        [Test]
        public void Apply_FiniteDefenseCalculationOverflow_DoesNotConsumeLedger()
        {
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            var resolver = new DamageResolver(_registry, ledger, 1f, float.MinValue, _config.CritDamageMultiplier, Roll);
            resolver.BeginTick(NOW);
            _enemy.Defense = 1f;
            DamageRequest request = Request(PLAYER_ID, ENEMY_ID, 1);
            Assert.AreEqual(EDamageOutcome.InvalidRequest, resolver.Apply(in request).Outcome);
            Assert.AreEqual(0, _rolls);
            Assert.AreEqual(HEALTH, _enemy.Health);
            _enemy.Defense = 0f;
            Assert.IsTrue(resolver.Apply(in request).IsApplied, "finite intermediate overflow must not poison the hit ledger");
        }

        [Test]
        public void Apply_FiniteMalformedDefenseWhileGuarded_StillRecordsBlockedAction()
        {
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            var resolver = new DamageResolver(_registry, ledger, 1f, float.MinValue, _config.CritDamageMultiplier, Roll);
            resolver.BeginTick(NOW);
            _enemy.Defense = 1f;
            _enemy.IsGuardingFront = true;
            DamageRequest request = Request(PLAYER_ID, ENEMY_ID, 1);
            Assert.AreEqual(EDamageOutcome.Guarded, resolver.Apply(in request).Outcome);
            _enemy.IsGuardingFront = false;
            _enemy.Defense = 0f;
            Assert.AreEqual(EDamageOutcome.DuplicateHit, resolver.Apply(in request).Outcome);
            Assert.AreEqual(0, _rolls);
        }

        [Test]
        public void Apply_GuardedInvulnerableAndFriendly_DoesNotRollOrEmit()
        {
            _enemy.IsGuardingFront = true;
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1)).Outcome);
            _enemy.IsGuardingFront = false;
            Assert.AreEqual(EDamageOutcome.DuplicateHit, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1)).Outcome);
            _enemy.IsInvulnerable = true;
            Assert.AreEqual(EDamageOutcome.Invulnerable, _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 2)).Outcome);
            Assert.AreEqual(EDamageOutcome.SameFaction, _resolver.Apply(Request(PLAYER_ID, PLAYER_ID, 3)).Outcome);
            Assert.AreEqual(0, _rolls);
            Assert.AreEqual(0, _notices.Count);
        }

        [Test]
        public void Apply_Summon_UsesOwnerChanceAndOwnerAttribution()
        {
            var summon = new Damageable(SUMMON_ID, ECombatFaction.Player)
            {
                IsSummon = true, OwnerCombatId = PLAYER_ID, CritChance = 0f
            };
            _registry.Register(summon);
            DamageResult result = _resolver.Apply(Request(SUMMON_ID, ENEMY_ID, 1, "summon"));
            Assert.IsTrue(result.IsCritical);
            Assert.AreEqual(PLAYER_ID, _notices[0].OwnerCombatId);
            Assert.AreEqual(1, _rolls);
        }

        [Test]
        public void Apply_MissingSummonOwner_AppliesWithoutFalseCriticalOrAttribution()
        {
            var summon = new Damageable(SUMMON_ID, ECombatFaction.Player)
            {
                IsSummon = true, OwnerCombatId = 99, CritChance = 1f
            };
            _registry.Register(summon);
            DamageResult result = _resolver.Apply(Request(SUMMON_ID, ENEMY_ID, 1, "summon"));
            Assert.IsTrue(result.IsApplied);
            Assert.IsFalse(result.IsCritical);
            Assert.AreEqual(CombatIds.NONE, _notices[0].OwnerCombatId);
            Assert.AreEqual(0, _rolls);
        }

        [Test]
        public void Apply_FiniteFloatMaximum_ClampsOverkillAndDoesNotCreateInfinity()
        {
            _owner.CritChance = 1f;
            var request = new DamageRequest(PLAYER_ID, ENEMY_ID, 1, 1, float.MaxValue, EDamageFlags.None, Vector3.forward, "skill");
            DamageResult result = _resolver.Apply(in request);
            Assert.IsTrue(result.IsCritical);
            Assert.IsTrue(result.IsLethal);
            Assert.AreEqual(HEALTH, result.AppliedAmount);
            Assert.IsTrue(DamageFeedbackRules.IsFinite(result.AppliedAmount));
        }

        [Test]
        public void Apply_GrowthChangesChance_ReadsLatestOwnerValue()
        {
            _owner.CritChance = 0f;
            Assert.IsFalse(_resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1)).IsCritical);
            _owner.CritChance = 1f;
            Assert.IsTrue(_resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 2)).IsCritical);
        }

        [Test]
        public void Apply_DetachedAnchor_PreservesCriticalChanceForPersistentAndSummonHits()
        {
            var anchor = new SkillCasterAnchor(_owner);
            anchor.Detach();
            _owner.CritChance = 0f;
            _registry.Unregister(PLAYER_ID);
            _retained = anchor;
            Assert.IsTrue(_resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, "persistent")).IsCritical);
            var summon = new Damageable(SUMMON_ID, ECombatFaction.Player) { IsSummon = true, OwnerCombatId = PLAYER_ID };
            _registry.Register(summon);
            Assert.IsTrue(_resolver.Apply(Request(SUMMON_ID, SECOND_ENEMY_ID, 2, "summon")).IsCritical);
            Assert.AreEqual(2, _rolls);
            Assert.AreEqual(PLAYER_ID, _notices[0].OwnerCombatId);
            Assert.AreEqual(PLAYER_ID, _notices[1].OwnerCombatId);
        }

        [Test]
        public void Apply_ReboundAnchor_DoesNotReuseOldOwnerId()
        {
            var anchor = new SkillCasterAnchor(_owner);
            anchor.Detach();
            _registry.Unregister(PLAYER_ID);
            _retained = anchor;
            var replacement = new Damageable(6, ECombatFaction.Player) { CritChance = 1f };
            anchor.Rebind(replacement);
            DamageResult staleOwner = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1, "persistent"));
            Assert.IsFalse(staleOwner.IsCritical);
            Assert.AreEqual(CombatIds.NONE, _notices[0].OwnerCombatId);
        }

        [Test]
        public void Apply_Overkill_RemainingHealthAndCriticalFlagAreCapturedBeforeRemoval()
        {
            _enemy.Health = 10f;
            Vector3 point = new Vector3(1f, 2f, 3f);
            _enemy.Position = point;
            _enemy.OnApply = () =>
            {
                _registry.Unregister(ENEMY_ID);
                _enemy.PositionUnavailable = true;
            };
            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1));
            Assert.IsTrue(result.IsCritical);
            Assert.IsTrue(result.IsLethal);
            Assert.AreEqual(10f, result.AppliedAmount);
            Assert.AreEqual(point, _notices[0].TargetPosition);
            Assert.AreEqual(ENEMY_ID, _notices[0].TargetCombatId);
            Assert.AreEqual(PLAYER_ID, _notices[0].OwnerCombatId);
        }

        [Test]
        public void Apply_DisplaySubscriberThrows_HealthAndResultRemainApplied()
        {
            _resolver.DamageApplied += ThrowDisplay;
            LogAssert.Expect(LogType.Error, "[Combat] 피해 적용 후 숫자 전달 실패: display failure");
            DamageResult result = _resolver.Apply(Request(PLAYER_ID, ENEMY_ID, 1));
            Assert.IsTrue(result.IsApplied);
            Assert.AreEqual(HEALTH - result.AppliedAmount, _enemy.Health);
        }

        [Test]
        public void Apply_DisplayBudgetFull_DamageStillApplies()
        {
            var budget = new DamageFeedbackBudget(1);
            int sent = 0;
            _resolver.DamageApplied += notice =>
            {
                if (budget.TryTake(notice.OwnerCombatId, notice.ServerTime, out uint sequence))
                {
                    ++sent;
                }
            };
            for (int i = 0; i <= DamageFeedbackBudget.OWNER_PER_SECOND; ++i)
            {
                Assert.IsTrue(_resolver.Apply(Request(PLAYER_ID, ENEMY_ID, i + 1)).IsApplied);
                _enemy.Health = HEALTH;
                _enemy.LifeState = EParticipantLifeState.Alive;
            }
            Assert.AreEqual(DamageFeedbackBudget.OWNER_PER_SECOND, sent);
            Assert.AreEqual(DamageFeedbackBudget.OWNER_PER_SECOND + 1, _rolls);
        }

        [Test]
        public void Applied_ExistingThreeArguments_DefaultsToNonCritical()
        {
            Assert.IsFalse(DamageResult.Applied(1f, false, false).IsCritical);
            Assert.IsFalse(DamageResult.Guarded().IsCritical);
            Assert.IsFalse(DamageResult.Ignored(EDamageOutcome.InvalidRequest).IsCritical);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private float Roll() { ++_rolls; return _nextRoll; }
        private IDamageable Retained(int id) => ((_retained != null) && (_retained.CombatId == id)) ? _retained : null;
        private static void ThrowDisplay(AppliedDamageNotice notice) => throw new InvalidOperationException("display failure");

        private static DamageRequest Request(int attackerId, int targetId, int actionId, string source = "skill")
        {
            return new DamageRequest(attackerId, targetId, 1, actionId, RAW_DAMAGE, EDamageFlags.None, Vector3.forward, source);
        }

        private sealed class Damageable : ISkillCaster, ICriticalDamageSource
        {
            private Vector3 _position;
            public int CombatId { get; }
            public ECombatFaction Faction { get; }
            public EParticipantLifeState LifeState { get; set; } = EParticipantLifeState.Alive;
            public bool IsSummon { get; set; }
            public int OwnerCombatId { get; set; }
            public Vector3 Position
            {
                get
                {
                    if (PositionUnavailable)
                    {
                        throw new InvalidOperationException("position was removed");
                    }
                    return _position;
                }
                set => _position = value;
            }
            public Vector3 Forward => Vector3.forward;
            public float BodyRadius => 0f;
            public float Health { get; set; } = HEALTH;
            public float MaxHealth => HEALTH;
            public float Defense { get; set; }
            public bool IsInvulnerable { get; set; }
            public bool IsGuardingFront { get; set; }
            public float CritChance { get; set; }
            public float AttackPower => RAW_DAMAGE;
            public Action OnApply { get; set; }
            public bool PositionUnavailable { get; set; }

            public Damageable(int id, ECombatFaction faction) { CombatId = id; Faction = faction; }
            public void SetSkillPosition(Vector3 position) { Position = position; }
            public void SetSkillFacing(Vector3 direction) { }
            public void ApplyDamage(float amount, bool lethal, double serverTime)
            {
                Health -= amount;
                if (lethal)
                {
                    LifeState = EParticipantLifeState.Dead;
                }
                OnApply?.Invoke();
            }
        }
    }
}
