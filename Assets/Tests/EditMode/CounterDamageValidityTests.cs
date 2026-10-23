// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class CounterDamageValidityTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME     = 10.0;
        private const float  ATTACK_POWER   = 100f;
        private const float  TARGET_HEALTH  = 10000f;
        private const int    PARTICIPANT_ID = 1;
        private const int    LEDGER_SIZE    = 256;
        private const double LEDGER_AGE     = 30.0;
        private const double ACTION_AGE     = 10.0;
        private const int    COUNTER_SLOT   = 3;
        private const float  GUARD_SECONDS  = 0.5f;
        private const float  ENEMY_DAMAGE   = 10f;

        private SkillCatalogSO       _catalog;
        private PlayerConfigSO       _player;
        private CombatTargetRegistry _registry;
        private DamageResolver       _core;
        private SkillDamageResolver  _resolver;
        private FakeCombatant        _owner;
        private SkillServer          _server;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog  = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _player   = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(LEDGER_SIZE, LEDGER_AGE, ACTION_AGE);
            _core     = new DamageResolver(_registry, ledger, _player.DefenseReductionConstant, _player.DamageReductionCap);
            _resolver = new SkillDamageResolver(_core, _registry, ledger);
            _owner    = new FakeCombatant(_registry.IssueCombatId(), ECombatFaction.Player);
            Assert.IsTrue(_registry.Register(_owner));
            _core.BeginTick(START_TIME);
            _server = new SkillServer(_owner, EPlayerClass.Warrior, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                new CombatLineOfSight(0, 0f), START_TIME);
            _owner.Skills = _server;
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Shutdown();
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_player);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Apply_CounterWindowInvalidRawDamage_ReturnsInvalidWithoutConsumingLedger(float rawDamage)
        {
            FakeCombatant enemy = OpenCounterWindow();
            DamageRequest invalid = Incoming(enemy, 1, 2, rawDamage, enemy.Position);

            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(in invalid).Outcome);

            DamageRequest valid = Incoming(enemy, 1, 2, ENEMY_DAMAGE, enemy.Position);
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in valid).Outcome);
            Assert.AreEqual(EDamageOutcome.DuplicateHit, _resolver.Apply(in valid).Outcome);
            Assert.AreEqual(0, _owner.DamageCalls);
        }

        [Test]
        public void Apply_CounterWindowNonFiniteSourcePosition_ReturnsInvalidWithoutConsumingLedger()
        {
            FakeCombatant enemy = OpenCounterWindow();
            DamageRequest invalid = Incoming(enemy, 3, 4, ENEMY_DAMAGE, new Vector3(float.NaN, 0f, 0f));

            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(in invalid).Outcome);

            DamageRequest valid = Incoming(enemy, 3, 4, ENEMY_DAMAGE, enemy.Position);
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in valid).Outcome);
        }

        [Test]
        public void Tick_CounterWindowOnlyInvalidHit_DoesNotCountAsGuardSuccess()
        {
            FakeCombatant enemy = OpenCounterWindow();
            DamageRequest invalid = Incoming(enemy, 5, 6, float.NaN, enemy.Position);
            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(in invalid).Outcome);

            Tick(START_TIME + GUARD_SECONDS);
            Tick(START_TIME + GUARD_SECONDS);

            Assert.AreEqual(0, enemy.DamageCalls);
        }

        [Test]
        public void Tick_CounterWindowValidHitAfterInvalidHit_CountersOnce()
        {
            FakeCombatant enemy = OpenCounterWindow();
            DamageRequest invalid = Incoming(enemy, 7, 8, 0f, enemy.Position);
            DamageRequest valid   = Incoming(enemy, 7, 8, ENEMY_DAMAGE, enemy.Position);
            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(in invalid).Outcome);
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in valid).Outcome);

            Tick(START_TIME + GUARD_SECONDS);
            Tick(START_TIME + GUARD_SECONDS);

            Assert.AreEqual(1, enemy.DamageCalls);
        }

        [Test]
        public void Apply_CounterWindowTargetWithInvalidDefense_ReturnsInvalidAndKeepsCounterAvailable()
        {
            FakeCombatant enemy = OpenCounterWindow();
            _owner.DefenseValue = float.NaN;
            DamageRequest first = Incoming(enemy, 9, 10, ENEMY_DAMAGE, enemy.Position);
            Assert.AreEqual(EDamageOutcome.InvalidRequest, _resolver.Apply(in first).Outcome);

            _owner.DefenseValue = 0f;
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in first).Outcome);
        }

        [Test]
        public void Apply_PlainValidHitWithoutCounterWindow_StillAppliesDamage()
        {
            var enemy = new FakeCombatant(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = Vector3.forward };
            Assert.IsTrue(_registry.Register(enemy));
            DamageRequest request = Incoming(enemy, 11, 12, ENEMY_DAMAGE, enemy.Position);

            Assert.AreEqual(EDamageOutcome.Applied, _resolver.Apply(in request).Outcome);
            Assert.AreEqual(1, _owner.DamageCalls);
        }

        [Test]
        public void IsValidRequest_ValidAndMalformedRequests_FollowsSharedRule()
        {
            var enemy = new FakeCombatant(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = Vector3.forward };
            DamageRequest valid     = Incoming(enemy, 13, 14, ENEMY_DAMAGE, enemy.Position);
            DamageRequest zero      = Incoming(enemy, 13, 14, 0f, enemy.Position);
            DamageRequest badSource = Incoming(enemy, 13, 14, ENEMY_DAMAGE, new Vector3(0f, float.PositiveInfinity, 0f));

            Assert.IsTrue(_core.IsValidRequest(in valid, _owner));
            Assert.IsFalse(_core.IsValidRequest(in zero, _owner));
            Assert.IsFalse(_core.IsValidRequest(in badSource, _owner));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private FakeCombatant OpenCounterWindow()
        {
            var enemy = new FakeCombatant(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = Vector3.forward };
            Assert.IsTrue(_registry.Register(enemy));
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Regular, COUNTER_SLOT, Vector3.zero,
                FacingRules.EncodeYaw(Vector3.forward), START_TIME, true, out ERejectReason reason), reason.ToString());
            Tick(START_TIME);
            return enemy;
        }

        private void Tick(double now)
        {
            _core.BeginTick(now);
            _server.Tick(now, 0f, null);
        }

        private DamageRequest Incoming(FakeCombatant enemy, int castId, int actionId, float rawDamage, Vector3 source)
        {
            return new DamageRequest(enemy.CombatId, _owner.CombatId, castId, actionId, rawDamage, EDamageFlags.None, source,
                "test_enemy_hit");
        }

        private sealed class FakeCombatant : ISkillCombatant
        {
            public int                   CombatId        { get; }
            public ECombatFaction        Faction         { get; }
            public EParticipantLifeState LifeState       { get; set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; set; }
            public Vector3               Forward         { get; private set; } = Vector3.forward;
            public float                 BodyRadius      => 0f;
            public float                 Health          { get; private set; } = TARGET_HEALTH;
            public float                 MaxHealth       => TARGET_HEALTH;
            public float                 Defense         => DefenseValue;
            public float                 DefenseValue    { get; set; }
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;
            public float                 AttackPower     => ATTACK_POWER;
            public SkillServer           Skills          { get; set; }
            public int                   DamageCalls     { get; private set; }

            public FakeCombatant(int combatId, ECombatFaction faction)
            {
                CombatId = combatId;
                Faction  = faction;
            }

            public void SetSkillPosition(Vector3 position) { Position = position; }
            public void SetSkillFacing(Vector3 direction) { Forward = direction; }

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
                ++DamageCalls;
                Health -= appliedAmount;
                if (isLethal)
                {
                    Health    = 0f;
                    LifeState = EParticipantLifeState.Dead;
                }
            }
        }
    }
}
