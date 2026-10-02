// System
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

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
    public class SkillServerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME      = 10.0;
        private const double TIME_EPSILON    = 0.00001;
        private const float  ASSERT_EPSILON  = 0.001f;
        private const float  ATTACK_POWER    = 100f;
        private const float  TARGET_HEALTH   = 10000f;
        private const int    PARTICIPANT_ID  = 1;
        private const int    LEDGER_SIZE     = 256;
        private const double LEDGER_AGE      = 30.0;
        private const double ACTION_AGE      = 10.0;
        private const int    PATH_BUDGET     = 4;
        private const int    COUNTER_SLOT    = 3;
        private const int    FLAME_SLOT      = 1;
        private const int    BARRIER_SLOT    = 3;
        private const int    CONTROL_SLOT    = 2;
        private const int    FIELD_TICKS     = 8;
        private const float  FLAME_INTERVAL  = 0.5f;
        private const float  FLAME_DAMAGE    = 0.5625f;
        private const float  SKILL_RESONANCE = 4f;
        private const float  COUNTER_DAMAGE  = 3.5f;
        private const float  GUARD_SECONDS   = 0.5f;
        private const float  ENEMY_DAMAGE    = 10f;
        private const float  POINT_DISTANCE  = 4f;

        private SkillCatalogSO       _catalog;
        private PlayerConfigSO       _player;
        private CombatTargetRegistry _registry;
        private DamageResolver       _core;
        private SkillDamageResolver  _resolver;
        private FakeCaster           _owner;
        private FakeNavigation       _navigation;
        private PathRequestBudget    _budget;
        private SkillServer          _server;
        private double               _lastTick;

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
            _owner    = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Player);
            Assert.IsTrue(_registry.Register(_owner));
            _navigation = new FakeNavigation();
            _budget     = new PathRequestBudget(PATH_BUDGET);
            _lastTick   = START_TIME;
            _core.BeginTick(START_TIME);
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

        [TestCaseSource(nameof(RegularSkills))]
        public void TryCast_DeadOwner_RejectsWithoutChangingResources(EPlayerClass playerClass, int slot)
        {
            CreateServer(playerClass);
            _owner.LifeState = EParticipantLifeState.Dead;
            AssertRejected(ESkillRequestKind.Regular, slot, Vector3.zero, START_TIME, true, ERejectReason.Dead);
        }

        [TestCaseSource(nameof(RegularSkills))]
        public void TryCast_CombatDisallowed_RejectsWithoutChangingResources(EPlayerClass playerClass, int slot)
        {
            CreateServer(playerClass);
            AssertRejected(ESkillRequestKind.Regular, slot, Vector3.zero, START_TIME, false, ERejectReason.WrongPhase);
        }

        [TestCaseSource(nameof(RegularSkills))]
        public void TryCast_AlreadyCasting_RejectsSecondCast(EPlayerClass playerClass, int slot)
        {
            CreateServer(playerClass);
            CastRegular(slot, Vector3.zero);
            AssertRejected(ESkillRequestKind.Regular, slot, Vector3.zero, START_TIME, true, ERejectReason.WrongPhase);
        }

        [TestCaseSource(nameof(RegularSkills))]
        public void TryCast_CancelledCastStillCoolingDown_RejectsWithoutRefund(EPlayerClass playerClass, int slot)
        {
            CreateServer(playerClass);
            CastRegular(slot, Vector3.zero);
            _server.Cancel();
            AssertRejected(ESkillRequestKind.Regular, slot, Vector3.zero, START_TIME, true, ERejectReason.Cooldown);
        }

        [TestCaseSource(nameof(RegularSkills))]
        public void TryCast_InsufficientEnergy_RejectsWithoutStartingCooldown(EPlayerClass playerClass, int slot)
        {
            CreateServer(playerClass);
            var snapshot = new SkillCombatSnapshot(0f, 0f, 0.0, 0.0, 0.0, 0.0, 0.0);
            _server.Restore(in snapshot);
            AssertRejected(ESkillRequestKind.Regular, slot, Vector3.zero, START_TIME, true, ERejectReason.InsufficientEnergy);
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
        }

        [TestCase(2)]
        [TestCase(3)]
        public void Constructor_SummonBeyondCompanionSlots_ThrowsBeforeAnyCast(int slot)
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Summoner, slot, out SkillCatalogSO.SkillDefinition skill));
            typeof(SkillCatalogSO.SkillArea).GetField("_shape", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(skill.Area, ESkillShape.Summon);

            Assert.Throws<System.InvalidOperationException>(() => CreateServer(EPlayerClass.Summoner),
                "동료 칸 수를 넘는 슬롯의 소환은 시전 중 범위 밖 접근이 되므로 만들 때 막는다");
            _server = null;
        }

        [TestCase(0)]
        [TestCase(1)]
        public void TryCast_SummonWithFullRegistry_RejectsWithoutCostCooldownOrPhaseChange(int slot)
        {
            CreateServer(EPlayerClass.Summoner);
            while (_registry.Count < Constants.Combat.MAX_COMBAT_TARGETS)
            {
                Enemy(Vector3.forward * POINT_DISTANCE);
            }
            ESkillPhase phase    = _server.Phase;
            int         castId   = _server.CastInstanceId;
            int         actionId = _server.ActionInstanceId;
            LogAssert.Expect(LogType.Error, new Regex("상한"));

            AssertRejected(ESkillRequestKind.Regular, slot, Vector3.zero, START_TIME, true, ERejectReason.NoCapacity);

            Assert.AreEqual(Constants.Combat.MAX_COMBAT_TARGETS, _registry.Count);
            Assert.AreEqual(phase, _server.Phase);
            Assert.AreEqual(castId, _server.CastInstanceId);
            Assert.AreEqual(actionId, _server.ActionInstanceId);
            Assert.IsFalse(_server.GetCompanion(slot).IsActive);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void TryCast_SummonWindup_ReservesDeadPlaceholderAndCancelReleasesIt(int slot)
        {
            CreateServer(EPlayerClass.Summoner);
            int originalCount = _registry.Count;
            CastRegular(slot, Vector3.zero);
            CompanionServer     companion  = _server.GetCompanion(slot);
            int                 reservedId = companion.CombatId;
            double              resolvesAt = _server.ResolveAt;
            SkillCombatSnapshot spent      = _server.CaptureSnapshot();

            Assert.AreEqual(originalCount + 1, _registry.Count);
            Assert.Greater(reservedId, CombatIds.NONE);
            Assert.IsTrue(_registry.TryGet(reservedId, out ICombatTarget placeholder));
            Assert.AreSame(companion, placeholder);
            Assert.IsFalse(companion.IsActive);
            Assert.AreEqual(EParticipantLifeState.Dead, companion.LifeState);
            Assert.AreEqual(originalCount, _registry.CountLivingInRadius(Vector3.zero, _catalog.CompanionSearchRadius,
                ECombatFaction.Player, true));

            _server.Cancel();
            _server.Cancel();

            AssertSnapshotEqual(spent, _server.CaptureSnapshot());
            Assert.AreEqual(originalCount, _registry.Count);
            Assert.IsFalse(_registry.TryGet(reservedId, out _));
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
            Tick(resolvesAt);
            Assert.IsFalse(companion.IsActive);
            Assert.AreEqual(originalCount, _registry.Count);
        }

        [TestCase(EPlayerClass.Warrior, 0, 20f, 6f, 0.2f, ESkillShape.Cone)]
        [TestCase(EPlayerClass.Warrior, 1, 20f, 8f, 0f, ESkillShape.Line)]
        [TestCase(EPlayerClass.Warrior, 2, 30f, 12f, 0.6f, ESkillShape.Circle)]
        [TestCase(EPlayerClass.Warrior, 3, 25f, 12f, 0f, ESkillShape.Cone)]
        [TestCase(EPlayerClass.Sorcerer, 0, 15f, 4f, 0.15f, ESkillShape.Projectile)]
        [TestCase(EPlayerClass.Sorcerer, 1, 30f, 12f, 0.3f, ESkillShape.Circle)]
        [TestCase(EPlayerClass.Sorcerer, 2, 25f, 14f, 0.3f, ESkillShape.Circle)]
        [TestCase(EPlayerClass.Sorcerer, 3, 30f, 10f, 0.7f, ESkillShape.Circle)]
        [TestCase(EPlayerClass.Summoner, 0, 30f, 20f, 0.5f, ESkillShape.Summon)]
        [TestCase(EPlayerClass.Summoner, 1, 30f, 20f, 0.5f, ESkillShape.Summon)]
        [TestCase(EPlayerClass.Summoner, 2, 15f, 5f, 0.2f, ESkillShape.Projectile)]
        [TestCase(EPlayerClass.Summoner, 3, 25f, 12f, 0.3f, ESkillShape.Circle)]
        public void TryCast_DefaultSkill_ChargesCostAndLocksServerTimeline(EPlayerClass playerClass, int slot,
            float cost, float cooldown, float windup, ESkillShape shape)
        {
            CreateServer(playerClass);
            CastRegular(slot, Vector3.zero);

            Assert.AreEqual(_player.BaseMaxEnergy - cost, _server.Energy, ASSERT_EPSILON);
            Assert.AreEqual(START_TIME + cooldown, _server.GetReadyAt(slot), TIME_EPSILON);
            Assert.AreEqual(START_TIME + windup, _server.ResolveAt, TIME_EPSILON);
            Assert.AreEqual(ESkillPhase.Windup, _server.Phase);
            Assert.IsTrue(_server.BlocksMovement);
            Assert.Greater(_server.CastInstanceId, CombatIds.NONE);
            Assert.Greater(_server.ActionInstanceId, _server.CastInstanceId);
            Assert.IsTrue(_catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill));
            Assert.AreEqual(shape, skill.Area.Shape);
        }

        [TestCase(EPlayerClass.Sorcerer, 1)]
        [TestCase(EPlayerClass.Sorcerer, 2)]
        [TestCase(EPlayerClass.Sorcerer, 3)]
        [TestCase(EPlayerClass.Summoner, 3)]
        public void TryCast_PointOutsideRange_RejectsWithoutCharge(EPlayerClass playerClass, int slot)
        {
            CreateServer(playerClass);
            Assert.IsTrue(_catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill));
            Vector3 point = Vector3.forward * (skill.Area.CastRange + POINT_DISTANCE);
            AssertRejected(ESkillRequestKind.Regular, slot, point, START_TIME, true, ERejectReason.OutOfRange);
            Assert.AreEqual(0, _navigation.SampleCalls);
        }

        [Test]
        public void TryCast_PointHasNoNavigation_RejectsWithoutCharge()
        {
            CreateServer(EPlayerClass.Sorcerer);
            _navigation.SampleSucceeds = false;
            AssertRejected(ESkillRequestKind.Regular, FLAME_SLOT, Vector3.forward, START_TIME, true, ERejectReason.InvalidTarget);
            Assert.AreEqual(1, _navigation.SampleCalls);
        }

        [Test]
        public void TryCast_SampledPointMovesOutsideRange_RejectsWithoutCharge()
        {
            CreateServer(EPlayerClass.Sorcerer);
            _navigation.SampleOverride = Vector3.forward * _player.BaseMaxEnergy;
            AssertRejected(ESkillRequestKind.Regular, FLAME_SLOT, Vector3.forward, START_TIME, true, ERejectReason.OutOfRange);
        }

        [Test]
        public void TryCast_SampledPointBecomesNonFinite_RejectsWithoutCharge()
        {
            CreateServer(EPlayerClass.Sorcerer);
            _navigation.SampleOverride = new Vector3(float.NaN, 0f, 0f);
            AssertRejected(ESkillRequestKind.Regular, FLAME_SLOT, Vector3.forward, START_TIME, true, ERejectReason.InvalidTarget);
        }

        [Test]
        public void TryCast_SampledPointExceedsHeightLimit_RejectsWithoutCharge()
        {
            CreateServer(EPlayerClass.Sorcerer);
            _navigation.SampleOverride = Vector3.up * (Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE + POINT_DISTANCE);
            AssertRejected(ESkillRequestKind.Regular, FLAME_SLOT, Vector3.forward, START_TIME, true, ERejectReason.InvalidTarget);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void TryCast_NonFinitePoint_RejectsWithoutCharge(float coordinate)
        {
            CreateServer(EPlayerClass.Sorcerer);
            AssertRejected(ESkillRequestKind.Regular, FLAME_SLOT, new Vector3(coordinate, 0f, 0f), START_TIME,
                true, ERejectReason.InvalidTarget);
        }

        [TestCase(-1)]
        [TestCase(Constants.Player.SKILL_SLOT_COUNT)]
        public void TryCast_InvalidSlot_RejectsWithoutCharge(int slot)
        {
            CreateServer(EPlayerClass.Warrior);
            AssertRejected(ESkillRequestKind.Regular, slot, Vector3.zero, START_TIME, true, ERejectReason.InvalidTarget);
        }

        [TestCase(EPlayerClass.Warrior)]
        [TestCase(EPlayerClass.Sorcerer)]
        public void TryCast_NonSummonerAscension_RejectsWithoutCharge(EPlayerClass playerClass)
        {
            CreateServer(playerClass);
            AssertRejected(ESkillRequestKind.Ascension, 0, Vector3.zero, START_TIME, true, ERejectReason.InvalidTarget);
        }

        [Test]
        public void TryCast_UnknownRequestKind_RejectsWithoutCharge()
        {
            CreateServer(EPlayerClass.Summoner);
            AssertRejected((ESkillRequestKind)byte.MaxValue, 0, Vector3.zero, START_TIME, true, ERejectReason.InvalidTarget);
        }

        [TestCase(EPlayerClass.Warrior, 0, 2.2f)]
        [TestCase(EPlayerClass.Warrior, 2, 4f)]
        [TestCase(EPlayerClass.Sorcerer, 2, 1f)]
        [TestCase(EPlayerClass.Sorcerer, 3, 3.8f)]
        [TestCase(EPlayerClass.Summoner, 3, 0.7f)]
        public void Tick_InstantAreaSkill_HitsAtWindupAndOnlyInsideShape(EPlayerClass playerClass, int slot, float coefficient)
        {
            CreateServer(playerClass);
            Assert.IsTrue(_catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill));
            Vector3    point   = Vector3.forward * POINT_DISTANCE;
            Vector3    center  = skill.Area.AimMode == ESkillAimMode.Point ? point : Vector3.forward * skill.Area.CenterOffset;
            Vector3    inside  = skill.Area.Shape == ESkillShape.Cone ? Vector3.forward : center;
            FakeCaster hit     = Enemy(inside);
            FakeCaster outside = Enemy(center + (Vector3.right * (skill.Area.Radius + POINT_DISTANCE)));
            FakeCaster tooHigh = Enemy(inside + (Vector3.up * (Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE + POINT_DISTANCE)));
            FakeCaster rear    = skill.Area.Shape == ESkillShape.Cone ? Enemy(Vector3.back) : null;
            CastRegular(slot, point);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt - TIME_EPSILON);
            Assert.AreEqual(0, hit.DamageCalls);

            Tick(resolvesAt);
            Assert.AreEqual(ATTACK_POWER * coefficient, TARGET_HEALTH - hit.Health, ASSERT_EPSILON);
            Assert.AreEqual(1, hit.DamageCalls);
            Assert.AreEqual(0, outside.DamageCalls);
            Assert.AreEqual(0, tooHigh.DamageCalls);
            if (rear != null)
            {
                Assert.AreEqual(0, rear.DamageCalls);
            }
            Tick(resolvesAt);
            Assert.AreEqual(1, hit.DamageCalls);
            Assert.AreEqual(ESkillPhase.Recovery, _server.Phase);
            Tick(resolvesAt + skill.Timeline.Recovery + TIME_EPSILON);
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
            Assert.IsFalse(_server.BlocksMovement);
        }

        [Test]
        public void Tick_FlameField_EightDistinctTicksAndNoDuplicateAtSameTime()
        {
            CreateServer(EPlayerClass.Sorcerer);
            FakeCaster first  = Enemy(Vector3.forward * POINT_DISTANCE);
            FakeCaster second = Enemy(first.Position + Vector3.right);
            CastRegular(FLAME_SLOT, first.Position);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt);
            Assert.AreEqual(0, first.DamageCalls);

            for (int i = 1; i <= FIELD_TICKS; ++i)
            {
                double now = resolvesAt + (i * FLAME_INTERVAL);
                Tick(now);
                Tick(now);
                Assert.AreEqual(i, first.DamageCalls);
                Assert.AreEqual(i, second.DamageCalls);
            }

            Tick(resolvesAt + (FIELD_TICKS * FLAME_INTERVAL) + FLAME_INTERVAL);
            Assert.AreEqual(FIELD_TICKS, first.DamageCalls);
            Assert.AreEqual(ATTACK_POWER * FLAME_DAMAGE * FIELD_TICKS, TARGET_HEALTH - first.Health, ASSERT_EPSILON);
            Assert.AreEqual(0f, _server.Resonance);
        }

        [Test]
        public void Tick_LateFirstFieldActivation_CatchesUpToPlannedEndWithoutExtendingLifetime()
        {
            CreateServer(EPlayerClass.Sorcerer);
            FakeCaster target = Enemy(Vector3.forward * POINT_DISTANCE);
            CastRegular(FLAME_SLOT, target.Position);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt + (FIELD_TICKS * FLAME_INTERVAL));
            Assert.AreEqual(FIELD_TICKS, target.DamageCalls);
            Tick(resolvesAt + (FIELD_TICKS * FLAME_INTERVAL) + FLAME_INTERVAL);
            Assert.AreEqual(FIELD_TICKS, target.DamageCalls);
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
        }

        [Test]
        public void Cancel_FieldDuringWindup_PreventsCreationAndKeepsConfirmedCostAndCooldown()
        {
            CreateServer(EPlayerClass.Sorcerer);
            FakeCaster target = Enemy(Vector3.forward * POINT_DISTANCE);
            CastRegular(FLAME_SLOT, target.Position);
            double              resolvesAt = _server.ResolveAt;
            SkillCombatSnapshot spent      = _server.CaptureSnapshot();
            _server.Cancel();
            AssertSnapshotEqual(spent, _server.CaptureSnapshot());
            Tick(resolvesAt + (FIELD_TICKS * FLAME_INTERVAL));
            Assert.AreEqual(0, target.DamageCalls);
        }

        [Test]
        public void Tick_FlameFieldOwnerDies_ExistingFieldKeepsOriginalLifetime()
        {
            CreateServer(EPlayerClass.Sorcerer);
            FakeCaster target = Enemy(Vector3.forward * POINT_DISTANCE);
            CastRegular(FLAME_SLOT, target.Position);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt);
            _owner.LifeState = EParticipantLifeState.Dead;
            Tick(resolvesAt + (FIELD_TICKS * FLAME_INTERVAL));
            Assert.AreEqual(FIELD_TICKS, target.DamageCalls);
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
        }

        [Test]
        public void OnOwnerDeath_ActiveCompanionsAndBarrier_StopsCompanionsImmediatelyAndKeepsFieldAndResonance()
        {
            CreateServer(EPlayerClass.Summoner);
            double now = START_TIME;
            for (int slot = 0; slot < Constants.Combat.COMPANIONS_PER_PLAYER; ++slot)
            {
                CastRegular(slot, Vector3.zero, now);
                now = _server.ResolveAt;
                Tick(now, false);
                Assert.IsTrue(_server.GetCompanion(slot).IsActive);
                _server.Cancel();
            }
            FakeCaster target = Enemy(Vector3.forward);
            CastRegular(BARRIER_SLOT, target.Position, now);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt, false);
            Assert.AreEqual(SKILL_RESONANCE, _server.Resonance, ASSERT_EPSILON);
            SkillCombatSnapshot alive         = _server.CaptureSnapshot();
            int                 originalCount = _registry.Count;
            _owner.LifeState = EParticipantLifeState.Dead;

            _server.OnOwnerDeath();

            for (int slot = 0; slot < Constants.Combat.COMPANIONS_PER_PLAYER; ++slot)
            {
                Assert.IsFalse(_server.GetCompanion(slot).IsActive);
                Assert.IsFalse(_registry.TryGet(_server.GetCompanion(slot).CombatId, out _));
            }
            Assert.AreEqual(originalCount - Constants.Combat.COMPANIONS_PER_PLAYER, _registry.Count);
            AssertSnapshotEqual(alive, _server.CaptureSnapshot());
            Assert.AreEqual(0.2f, _server.GetCompanionDamageBonus(target), ASSERT_EPSILON);
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Summoner, BARRIER_SLOT,
                out SkillCatalogSO.SkillDefinition barrier));
            Tick(resolvesAt + barrier.Effect.Duration - TIME_EPSILON);
            Assert.AreEqual(0.2f, _server.GetCompanionDamageBonus(target), ASSERT_EPSILON);
            Tick(resolvesAt + barrier.Effect.Duration);
            Assert.AreEqual(0f, _server.GetCompanionDamageBonus(target));
            Assert.AreEqual(SKILL_RESONANCE, _server.Resonance, ASSERT_EPSILON);
            Assert.AreEqual(1, target.DamageCalls);
        }

        [Test]
        public void Tick_TargetBarrierMultipleTargets_GrantsResonanceOnceAndOwnCompanionBonus()
        {
            CreateServer(EPlayerClass.Summoner);
            FakeCaster first   = Enemy(Vector3.forward * POINT_DISTANCE);
            FakeCaster second  = Enemy(first.Position + Vector3.right);
            FakeCaster outside = Enemy(first.Position + (Vector3.right * POINT_DISTANCE));
            CastRegular(BARRIER_SLOT, first.Position);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt);
            Tick(resolvesAt);

            Assert.AreEqual(SKILL_RESONANCE, _server.Resonance);
            Assert.AreEqual(1, first.DamageCalls);
            Assert.AreEqual(1, second.DamageCalls);
            Assert.AreEqual(0.2f, _server.GetCompanionDamageBonus(first), ASSERT_EPSILON);
            Assert.AreEqual(0f, _server.GetCompanionDamageBonus(outside));
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Summoner, BARRIER_SLOT,
                out SkillCatalogSO.SkillDefinition barrier));
            Tick(resolvesAt + barrier.Effect.Duration + TIME_EPSILON);
            Assert.AreEqual(SKILL_RESONANCE, _server.Resonance);
            Assert.AreEqual(0f, _server.GetCompanionDamageBonus(first));
        }

        [Test]
        public void Apply_BasicAttackWithMultipleTargets_GrantsResonanceOnceThroughSkillResolver()
        {
            CreateServer(EPlayerClass.Summoner);
            FakeCaster    first   = Enemy(Vector3.forward);
            FakeCaster    second  = Enemy(Vector3.right);
            var           request = new DamageRequest(_owner.CombatId, first.CombatId, _resolver.IssueInstanceId(),
                _resolver.IssueInstanceId(), ENEMY_DAMAGE, EDamageFlags.None, _owner.Position,
                Constants.Combat.BASIC_ATTACK_SOURCE_ID);
            DamageRequest other   = request.WithTarget(second.CombatId);

            Assert.IsTrue(_resolver.Apply(in request).CountsAsHit);
            Assert.IsTrue(_resolver.Apply(in other).CountsAsHit);
            Assert.AreEqual(_catalog.ResonancePerBasicHit, _server.Resonance, ASSERT_EPSILON);
            Assert.AreEqual(EDamageOutcome.DuplicateHit, _resolver.Apply(in request).Outcome);
            Assert.AreEqual(_catalog.ResonancePerBasicHit, _server.Resonance, ASSERT_EPSILON);
        }

        [TestCase(EPlayerClass.Sorcerer, 0, 1.6f)]
        [TestCase(EPlayerClass.Summoner, 2, 1.8f)]
        public void Tick_Projectile_StopsAtFirstTargetAndDoesNotHitOutsideWidth(EPlayerClass playerClass, int slot,
            float coefficient)
        {
            CreateServer(playerClass);
            FakeCaster behind = Enemy(Vector3.forward * POINT_DISTANCE);
            FakeCaster first  = Enemy(Vector3.forward * (POINT_DISTANCE * FLAME_INTERVAL));
            FakeCaster beside = Enemy(first.Position + Vector3.right);
            CastRegular(slot, Vector3.zero);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt);
            Assert.AreEqual(0, first.DamageCalls);
            Tick(resolvesAt + FLAME_INTERVAL);
            Tick(resolvesAt + FLAME_INTERVAL);
            Tick(resolvesAt + POINT_DISTANCE);

            Assert.AreEqual(1, first.DamageCalls);
            Assert.AreEqual(ATTACK_POWER * coefficient, TARGET_HEALTH - first.Health, ASSERT_EPSILON);
            Assert.AreEqual(0, behind.DamageCalls);
            Assert.AreEqual(0, beside.DamageCalls);
            Assert.AreEqual(playerClass == EPlayerClass.Summoner ? SKILL_RESONANCE : 0f, _server.Resonance);
        }

        [Test]
        public void Tick_Dash_StopsBeforeFirstBodyAndNeverDamagesSecondTarget()
        {
            CreateServer(EPlayerClass.Warrior);
            FakeCaster behind = Enemy(Vector3.forward * POINT_DISTANCE);
            FakeCaster first  = Enemy(Vector3.forward * (POINT_DISTANCE * FLAME_INTERVAL));
            CastRegular(FLAME_SLOT, Vector3.zero);
            Tick(START_TIME);
            Tick(START_TIME + 0.3f);
            Vector3 stopped = _owner.Position;
            Tick(START_TIME + GUARD_SECONDS);

            Assert.AreEqual(1, first.DamageCalls);
            Assert.AreEqual(ATTACK_POWER * 1.8f, TARGET_HEALTH - first.Health, ASSERT_EPSILON);
            Assert.AreEqual(0, behind.DamageCalls);
            Assert.AreEqual(first.Position.z - Constants.Combat.SKILL_TARGET_BODY_RADIUS, stopped.z, ASSERT_EPSILON);
            Assert.AreEqual(stopped, _owner.Position);
        }

        [Test]
        public void Tick_DashNavigationBlocksMovement_StopsAtNavigationBoundary()
        {
            CreateServer(EPlayerClass.Warrior);
            _navigation.MoveLimit = Vector3.forward;
            FakeCaster target = Enemy(Vector3.forward * POINT_DISTANCE);
            CastRegular(FLAME_SLOT, Vector3.zero);
            Tick(START_TIME);
            Tick(START_TIME + 0.3f);
            Assert.AreEqual(Vector3.forward, _owner.Position);
            Assert.AreEqual(0, target.DamageCalls);
            Assert.Greater(_navigation.MoveCalls, 0);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void CompanionSlots_SummonResolveThenOwnerDeath_TrackerSpawnsAfterWindupAndDespawnsOnDeath(int slot)
        {
            CreateServer(EPlayerClass.Summoner);
            var host    = new FakeCompanionObjectHost();
            var tracker = new CompanionSpawnTracker(host, Constants.Combat.MAX_COMPANION_OBJECTS);
            CastRegular(slot, Vector3.zero);
            SyncCompanionObjects(tracker);

            // 예약된 자리는 사망 상태 대기 개체라 화면에 내보내지 않는다
            Assert.AreEqual(CombatIds.NONE, _server.GetActiveCompanionId(slot));
            Assert.AreEqual(0, host.Spawns.Count);

            Tick(_server.ResolveAt, false);
            SyncCompanionObjects(tracker);

            Assert.AreEqual(_server.GetCompanion(slot).CombatId, _server.GetActiveCompanionId(slot));
            Assert.AreEqual(1, host.Spawns.Count);
            Assert.AreEqual(_server.GetCompanion(slot).CombatId, host.Spawns[0].CompanionId);

            _owner.LifeState = EParticipantLifeState.Dead;
            _server.OnOwnerDeath();
            SyncCompanionObjects(tracker);

            Assert.AreEqual(CombatIds.NONE, _server.GetActiveCompanionId(slot));
            Assert.AreEqual(1, host.Despawns.Count);
            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void CompanionSlots_NonSummonerOrInvalidSlot_ReportsNoneAndRejectsBinding()
        {
            CreateServer(EPlayerClass.Warrior);

            Assert.AreEqual(Constants.Combat.COMPANIONS_PER_PLAYER, _server.CompanionSlotCount);
            Assert.AreEqual(PARTICIPANT_ID, _server.ParticipantId);
            Assert.AreEqual(CombatIds.NONE, _server.GetActiveCompanionId(-1));
            Assert.AreEqual(CombatIds.NONE, _server.GetActiveCompanionId(Constants.Combat.COMPANIONS_PER_PLAYER));
            Assert.IsFalse(_server.BindCompanionState(0, null));
            _server.UnbindCompanionState(Constants.Combat.COMPANIONS_PER_PLAYER);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Tick_Summon_OnlyAppearsAfterWindupAndCancelPreventsCreation(int slot)
        {
            CreateServer(EPlayerClass.Summoner);
            CastRegular(slot, Vector3.zero);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt - TIME_EPSILON);
            Assert.IsFalse(_server.GetCompanion(slot).IsActive);
            _server.Cancel();
            Tick(resolvesAt);
            Assert.IsFalse(_server.GetCompanion(slot).IsActive);

            var snapshot = new SkillCombatSnapshot(_player.BaseMaxEnergy, 0f, 0.0, 0.0, 0.0, 0.0, 0.0);
            _server.Restore(in snapshot);
            CastRegular(slot, Vector3.zero, resolvesAt);
            Tick(_server.ResolveAt);
            Assert.IsTrue(_server.GetCompanion(slot).IsActive);
            Assert.AreEqual(_owner.CombatId, _server.GetCompanion(slot).OwnerCombatId);
            Assert.AreEqual(0f, _server.Resonance);
        }

        [Test]
        public void Apply_CounterBlocksMultipleHits_CountersOnceAtWindowEndAndSharesDuplicateLedger()
        {
            CreateServer(EPlayerClass.Warrior);
            FakeCaster enemy = Enemy(Vector3.forward);
            CastRegular(COUNTER_SLOT, Vector3.zero);
            Tick(START_TIME);
            DamageRequest first  = Incoming(enemy, enemy.Position);
            DamageRequest second = Incoming(enemy, enemy.Position);
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in first).Outcome);
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in second).Outcome);
            Assert.AreEqual(EDamageOutcome.DuplicateHit, _resolver.Apply(in first).Outcome);
            Assert.AreEqual(0, _owner.DamageCalls);
            Assert.AreEqual(0, enemy.DamageCalls);

            Tick(START_TIME + GUARD_SECONDS - TIME_EPSILON);
            Assert.AreEqual(0, enemy.DamageCalls);
            Tick(START_TIME + GUARD_SECONDS);
            Tick(START_TIME + GUARD_SECONDS);
            Assert.AreEqual(1, enemy.DamageCalls);
            Assert.AreEqual(ATTACK_POWER * COUNTER_DAMAGE, TARGET_HEALTH - enemy.Health, ASSERT_EPSILON);
            Assert.AreEqual(EDamageOutcome.DuplicateHit, _resolver.Apply(in first).Outcome);
            DamageRequest afterWindow = Incoming(enemy, enemy.Position);
            Assert.AreEqual(EDamageOutcome.Applied, _resolver.Apply(in afterWindow).Outcome);
            Assert.AreEqual(1, _owner.DamageCalls);
        }

        [TestCase(59f, EDamageFlags.None, EDamageOutcome.Guarded)]
        [TestCase(61f, EDamageFlags.None, EDamageOutcome.Applied)]
        [TestCase(180f, EDamageFlags.None, EDamageOutcome.Applied)]
        [TestCase(0f, EDamageFlags.Unblockable, EDamageOutcome.Applied)]
        public void Apply_CounterDirectionsAndUnblockable_RespectsFront120Degrees(float angle, EDamageFlags flags,
            EDamageOutcome outcome)
        {
            CreateServer(EPlayerClass.Warrior);
            FakeCaster enemy = Enemy(Quaternion.Euler(0f, angle, 0f) * Vector3.forward);
            CastRegular(COUNTER_SLOT, Vector3.zero);
            Tick(START_TIME);
            DamageRequest request = Incoming(enemy, enemy.Position, flags);
            Assert.AreEqual(outcome, _resolver.Apply(in request).Outcome);
            Assert.AreEqual(outcome == EDamageOutcome.Applied ? 1 : 0, _owner.DamageCalls);
        }

        [Test]
        public void Cancel_SuccessfulCounter_RemovesRemainingGuardAndPendingCounter()
        {
            CreateServer(EPlayerClass.Warrior);
            FakeCaster enemy = Enemy(Vector3.forward);
            CastRegular(COUNTER_SLOT, Vector3.zero);
            Tick(START_TIME);
            DamageRequest guarded = Incoming(enemy, enemy.Position);
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in guarded).Outcome);
            SkillCombatSnapshot spent = _server.CaptureSnapshot();
            _server.Cancel();
            Assert.IsFalse(_server.IsGuardWindow);
            AssertSnapshotEqual(spent, _server.CaptureSnapshot());
            DamageRequest following = Incoming(enemy, enemy.Position);
            Assert.AreEqual(EDamageOutcome.Applied, _resolver.Apply(in following).Outcome);
            Assert.AreEqual(EDamageOutcome.DuplicateHit, _resolver.Apply(in guarded).Outcome);
            Tick(START_TIME + GUARD_SECONDS);
            Assert.AreEqual(0, enemy.DamageCalls);
        }

        [Test]
        public void Tick_UnsuccessfulCounter_DoesNotDamageAnyone()
        {
            CreateServer(EPlayerClass.Warrior);
            FakeCaster enemy = Enemy(Vector3.forward);
            CastRegular(COUNTER_SLOT, Vector3.zero);
            Tick(START_TIME);
            Tick(START_TIME + GUARD_SECONDS);
            Assert.AreEqual(0, enemy.DamageCalls);
        }

        [TestCase(0, 60f, 12f)]
        [TestCase(1, 80f, 7f)]
        [TestCase(2, 40f, 4f)]
        public void TryCast_Ascension_SpendsResonanceAtConfirmationAndStartsSharedCooldown(int slot, float cost,
            float coefficient)
        {
            CreateServer(EPlayerClass.Summoner);
            RestoreResonance(_catalog.ResonanceMax);
            FakeCaster target = Enemy(Vector3.forward * POINT_DISTANCE);
            float      energy = _server.Energy;
            CastAscension(slot, target.Position);
            Assert.AreEqual(_catalog.ResonanceMax - cost, _server.Resonance, ASSERT_EPSILON);
            Assert.AreEqual(energy, _server.Energy);
            Assert.AreEqual(START_TIME + _catalog.AscensionSharedCooldown, _server.AscensionReadyAt, TIME_EPSILON);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt - TIME_EPSILON);
            Assert.AreEqual(0, target.DamageCalls);
            Tick(resolvesAt);
            Assert.AreEqual(ATTACK_POWER * coefficient, TARGET_HEALTH - target.Health, ASSERT_EPSILON);
            Assert.AreEqual(_catalog.ResonanceMax - cost, _server.Resonance, ASSERT_EPSILON);
            _server.Cancel();
            RestoreResonance(_catalog.ResonanceMax);
            int nextSlot = (slot + 1) % _catalog.Ascensions.Count;
            AssertRejected(ESkillRequestKind.Ascension, nextSlot, target.Position, resolvesAt, true, ERejectReason.Cooldown);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TryCast_AscensionWithoutResonance_RejectsWithoutCooldown(int slot)
        {
            CreateServer(EPlayerClass.Summoner);
            AssertRejected(ESkillRequestKind.Ascension, slot, Vector3.zero, START_TIME, true, ERejectReason.InsufficientEnergy);
        }

        [Test]
        public void Tick_SingleAscension_ChoosesOnlyNearestValidTargetToCursor()
        {
            CreateServer(EPlayerClass.Summoner);
            RestoreResonance(_catalog.ResonanceMax);
            Vector3    point        = Vector3.forward * POINT_DISTANCE;
            FakeCaster farther      = Enemy(point + Vector3.right);
            FakeCaster invulnerable = Enemy(point);
            invulnerable.IsInvulnerable = true;
            FakeCaster nearest = Enemy(point + (Vector3.right * FLAME_INTERVAL));
            CastAscension(0, point);
            Tick(_server.ResolveAt);
            Assert.AreEqual(1, nearest.DamageCalls);
            Assert.AreEqual(0, farther.DamageCalls);
            Assert.AreEqual(0, invulnerable.DamageCalls);
        }

        [Test]
        public void Tick_ControlAscension_UsesTargetControlPermissionAndCapsBossRoot()
        {
            CreateServer(EPlayerClass.Summoner);
            RestoreResonance(_catalog.ResonanceMax);
            Vector3    point       = Vector3.forward * POINT_DISTANCE;
            FakeCaster normal      = Enemy(point);
            FakeCaster allowedBoss = Enemy(point + Vector3.right);
            allowedBoss.IsBoss = true;
            FakeCaster forbiddenBoss = Enemy(point + Vector3.left);
            forbiddenBoss.IsBoss     = true;
            forbiddenBoss.AllowsRoot = false;
            CastAscension(CONTROL_SLOT, point);
            Tick(_server.ResolveAt);

            Assert.AreEqual(2f, normal.RootDuration, ASSERT_EPSILON);
            Assert.AreEqual(Constants.Combat.BOSS_ROOT_MAX_SECONDS, allowedBoss.RootDuration, ASSERT_EPSILON);
            Assert.AreEqual(0, forbiddenBoss.RootCalls);
            Assert.AreEqual(1, forbiddenBoss.DamageCalls);
            Assert.AreEqual(point + Vector3.right, allowedBoss.Position);
            Assert.AreEqual(point + Vector3.left, forbiddenBoss.Position);
        }

        [Test]
        public void Tick_BindingField_UsesRootDurationScaleAndBossPermission()
        {
            CreateServer(EPlayerClass.Sorcerer);
            FakeCaster normal = Enemy(Vector3.forward * POINT_DISTANCE);
            FakeCaster elite  = Enemy(normal.Position + Vector3.right);
            elite.RootDurationScale = FLAME_INTERVAL;
            FakeCaster boss = Enemy(normal.Position + Vector3.left);
            boss.IsBoss     = true;
            boss.AllowsRoot = false;
            CastRegular(CONTROL_SLOT, normal.Position);
            Tick(_server.ResolveAt);
            Assert.AreEqual(2f, normal.RootDuration, ASSERT_EPSILON);
            Assert.AreEqual(1f, elite.RootDuration, ASSERT_EPSILON);
            Assert.AreEqual(0, boss.RootCalls);
        }

        [Test]
        public void Restore_SnapshotAfterSpendingAndDeath_DoesNotGrantFreeResourcesOrResetCooldowns()
        {
            CreateServer(EPlayerClass.Summoner);
            RestoreResonance(_catalog.ResonanceMax);
            CastRegular(BARRIER_SLOT, Vector3.zero);
            _server.Cancel();
            CastAscension(0, Vector3.zero);
            _server.Cancel();
            SkillCombatSnapshot beforeDeath = _server.CaptureSnapshot();
            _owner.LifeState = EParticipantLifeState.Dead;
            Tick(START_TIME + FLAME_INTERVAL);
            AssertSnapshotEqual(beforeDeath, _server.CaptureSnapshot());

            _owner.LifeState = EParticipantLifeState.Alive;
            CreateServer(EPlayerClass.Summoner, START_TIME + FLAME_INTERVAL);
            _server.Restore(in beforeDeath);
            AssertSnapshotEqual(beforeDeath, _server.CaptureSnapshot());
            Assert.Less(_server.Energy, _player.BaseMaxEnergy);
            Assert.Less(_server.Resonance, _catalog.ResonanceMax);
            AssertRejected(ESkillRequestKind.Regular, BARRIER_SLOT, Vector3.zero, START_TIME + FLAME_INTERVAL,
                true, ERejectReason.Cooldown);
            RestoreResonance(_catalog.ResonanceMax);
            AssertRejected(ESkillRequestKind.Ascension, CONTROL_SLOT, Vector3.zero, START_TIME + FLAME_INTERVAL,
                true, ERejectReason.Cooldown);
        }

        [Test]
        public void Detach_ActiveField_RebindPreservesEffectLifetimeAndCooldownWithoutFreeEnergy()
        {
            CreateServer(EPlayerClass.Sorcerer);
            FakeCaster target = Enemy(Vector3.forward * POINT_DISTANCE);
            CastRegular(FLAME_SLOT, target.Position);
            double resolvesAt = _server.ResolveAt;
            Tick(resolvesAt);
            _server.Detach();
            SkillCombatSnapshot detached = _server.CaptureSnapshot();
            _owner.LifeState = EParticipantLifeState.Dead;
            Tick(resolvesAt + FLAME_INTERVAL);
            Assert.AreEqual(1, target.DamageCalls);

            var rebound = new FakeCaster(_owner.CombatId, ECombatFaction.Player) { Skills = _server };
            Assert.IsTrue(_registry.Unregister(_owner.CombatId));
            Assert.IsTrue(_registry.Register(rebound));
            _owner = rebound;
            _server.Rebind(rebound);
            Assert.AreEqual(detached.GetReadyAt(FLAME_SLOT), _server.GetReadyAt(FLAME_SLOT), TIME_EPSILON);
            Assert.AreEqual(detached.Energy + (_player.EnergyRegenPerSecond * FLAME_INTERVAL), _server.Energy,
                ASSERT_EPSILON);
            AssertRejected(ESkillRequestKind.Regular, FLAME_SLOT, target.Position, resolvesAt + FLAME_INTERVAL,
                true, ERejectReason.Cooldown);
            Tick(resolvesAt + (FIELD_TICKS * FLAME_INTERVAL));
            Tick(resolvesAt + (FIELD_TICKS * FLAME_INTERVAL) + FLAME_INTERVAL);
            Assert.AreEqual(FIELD_TICKS, target.DamageCalls);
        }

        [Test]
        public void Detach_ActiveCompanion_RebindPreservesSameCompanionAndOriginalExpiry()
        {
            CreateServer(EPlayerClass.Summoner);
            CastRegular(0, Vector3.zero);
            Tick(_server.ResolveAt);
            CompanionServer companion = _server.GetCompanion(0);
            double          expiresAt = companion.ExpiresAt;
            int             combatId  = companion.CombatId;
            _server.Detach();
            _owner.LifeState = EParticipantLifeState.Dead;
            Tick(_server.ResolveAt + FLAME_INTERVAL);
            Assert.IsTrue(companion.IsActive);

            var rebound = new FakeCaster(_owner.CombatId, ECombatFaction.Player) { Skills = _server };
            _server.Rebind(rebound);
            Assert.AreSame(companion, _server.GetCompanion(0));
            Assert.AreEqual(expiresAt, companion.ExpiresAt, TIME_EPSILON);
            Assert.AreEqual(combatId, companion.CombatId);
            Tick(expiresAt);
            Assert.IsFalse(companion.IsActive);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static IEnumerable<TestCaseData> RegularSkills()
        {
            var classes = new[] { EPlayerClass.Warrior, EPlayerClass.Sorcerer, EPlayerClass.Summoner };
            for (int i = 0; i < classes.Length; ++i)
            {
                for (int slot = 0; slot < Constants.Player.SKILL_SLOT_COUNT; ++slot)
                {
                    yield return new TestCaseData(classes[i], slot);
                }
            }
        }

        private void CreateServer(EPlayerClass playerClass, double now = START_TIME)
        {
            _server?.Shutdown();
            _server       = new SkillServer(_owner, playerClass, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                new CombatLineOfSight(0, 0f), now, navigation: _navigation);
            _owner.Skills = _server;
            _lastTick     = now;
        }

        private FakeCaster Enemy(Vector3 position)
        {
            var target = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = position };
            Assert.IsTrue(_registry.Register(target));
            return target;
        }

        private void CastRegular(int slot, Vector3 point, double now = START_TIME)
        {
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Regular, slot, point, FacingRules.EncodeYaw(Vector3.forward),
                now, true, out ERejectReason reason), reason.ToString());
            Assert.AreEqual(ERejectReason.None, reason);
        }

        private void CastAscension(int slot, Vector3 point)
        {
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Ascension, slot, point, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason), reason.ToString());
            Assert.AreEqual(ERejectReason.None, reason);
        }

        private void AssertRejected(ESkillRequestKind kind, int slot, Vector3 point, double now, bool combatAllowed,
            ERejectReason expected)
        {
            SkillCombatSnapshot before = _server.CaptureSnapshot();
            Assert.IsFalse(_server.TryCast(kind, slot, point, 0, now, combatAllowed, out ERejectReason reason));
            Assert.AreEqual(expected, reason);
            AssertSnapshotEqual(before, _server.CaptureSnapshot());
        }

        private static void AssertSnapshotEqual(SkillCombatSnapshot expected, SkillCombatSnapshot actual)
        {
            Assert.AreEqual(expected.Energy, actual.Energy, ASSERT_EPSILON);
            Assert.AreEqual(expected.Resonance, actual.Resonance, ASSERT_EPSILON);
            Assert.AreEqual(expected.AscensionReadyAt, actual.AscensionReadyAt, TIME_EPSILON);
            for (int i = 0; i < Constants.Player.SKILL_SLOT_COUNT; ++i)
            {
                Assert.AreEqual(expected.GetReadyAt(i), actual.GetReadyAt(i), TIME_EPSILON);
            }
        }

        private void RestoreResonance(float resonance)
        {
            SkillCombatSnapshot before   = _server.CaptureSnapshot();
            var                 snapshot = new SkillCombatSnapshot(before.Energy, resonance, before.GetReadyAt(0), before.GetReadyAt(1),
                before.GetReadyAt(2), before.GetReadyAt(COUNTER_SLOT), before.AscensionReadyAt);
            _server.Restore(in snapshot);
        }

        private void SyncCompanionObjects(CompanionSpawnTracker tracker)
        {
            tracker.ReleaseStale();
            tracker.Track(_server);
        }

        private void Tick(double now, bool tickCompanions = true)
        {
            _core.BeginTick(now);
            _budget.BeginTick();
            _server.Tick(now, Mathf.Max(0f, (float)(now - _lastTick)), tickCompanions ? _budget : null);
            _lastTick = now;
        }

        private DamageRequest Incoming(FakeCaster enemy, Vector3 source, EDamageFlags flags = EDamageFlags.None)
        {
            return new DamageRequest(enemy.CombatId, _owner.CombatId, _resolver.IssueInstanceId(), _resolver.IssueInstanceId(),
                ENEMY_DAMAGE, flags, source, "test_enemy_hit");
        }

        private sealed class FakeNavigation : ISkillNavigation
        {
            public bool     SampleSucceeds { get; set; } = true;
            public Vector3? SampleOverride { get; set; }
            public Vector3? MoveLimit      { get; set; }
            public int      SampleCalls    { get; private set; }
            public int      MoveCalls      { get; private set; }

            public bool TrySample(Vector3 point, out Vector3 sampled)
            {
                ++SampleCalls;
                sampled = SampleOverride ?? point;
                return SampleSucceeds;
            }

            public Vector3 Move(Vector3 origin, Vector3 destination)
            {
                ++MoveCalls;
                return MoveLimit ?? destination;
            }
        }

        private sealed class FakeCaster : ISkillCombatant, ISkillControlTarget
        {
            public int                   CombatId          { get; }
            public ECombatFaction        Faction           { get; }
            public EParticipantLifeState LifeState         { get; set; }         = EParticipantLifeState.Alive;
            public bool                  IsSummon          => false;
            public int                   OwnerCombatId     => CombatIds.NONE;
            public Vector3               Position          { get; set; }
            public Vector3               Forward           { get; private set; } = Vector3.forward;
            public float                 BodyRadius        => 0f;
            public float                 Health            { get; private set; } = TARGET_HEALTH;
            public float                 MaxHealth         => TARGET_HEALTH;
            public float                 Defense           => 0f;
            public bool                  IsInvulnerable    { get; set; }
            public bool                  IsGuardingFront   => false;
            public float                 AttackPower       => ATTACK_POWER;
            public SkillServer           Skills            { get; set; }
            public int                   DamageCalls       { get; private set; }
            public bool                  IsBoss            { get; set; }
            public bool                  AllowsRoot        { get; set; }         = true;
            public float                 RootDurationScale { get; set; }         = 1f;
            public int                   RootCalls         { get; private set; }
            public float                 RootDuration      { get; private set; }

            public FakeCaster(int combatId, ECombatFaction faction)
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

            public void ApplyRoot(string sourceId, float duration, double serverTime)
            {
                ++RootCalls;
                RootDuration = duration;
            }
        }
    }
}
