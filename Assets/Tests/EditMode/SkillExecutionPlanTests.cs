// System
using System;
using System.Collections.Generic;
using System.Reflection;

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
    public class SkillExecutionPlanTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME     = 10.0;
        private const float  TOLERANCE      = 0.0001f;
        private const float  ATTACK_POWER   = 100f;
        private const float  TARGET_HEALTH  = 10000f;
        private const int    PARTICIPANT_ID = 1;
        private const int    LEDGER_SIZE    = 256;
        private const double LEDGER_AGE     = 30.0;
        private const double ACTION_AGE     = 10.0;
        private const int    BOLT_SLOT      = 0;
        private const int    CONE_SLOT      = 0;
        private const float  INTERVAL       = 0.2f;
        private const int    ALLOCATION_ITERATIONS = 1000;

        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        private SkillCatalogSO       _catalog;
        private PlayerConfigSO       _player;
        private CombatTargetRegistry _registry;
        private DamageResolver       _core;
        private SkillDamageResolver  _resolver;
        private FakeCaster           _owner;
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
            _owner    = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Player);
            Assert.IsTrue(_registry.Register(_owner));
            _core.BeginTick(START_TIME);
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Shutdown();
            UnityEngine.Object.DestroyImmediate(_catalog);
            UnityEngine.Object.DestroyImmediate(_player);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Constructor_DefaultProjectile_SingleShotOneSlotNoTail()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Sorcerer, BOLT_SLOT);

            var plan = new SkillExecutionPlan(skill, SkillModifiers.Identity);

            Assert.IsTrue(plan.IsProjectile);
            Assert.AreEqual(1, plan.ProjectileCount);
            Assert.AreEqual(1, plan.VolleyCount);
            Assert.AreEqual(1, plan.SlotCount);
            Assert.AreEqual(0f, plan.ScheduledTail, TOLERANCE);
        }

        [Test]
        public void Constructor_RepeatedBranchedProjectile_SlotsAreBranchesTimesShotsAndTailIsLastShot()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Sorcerer, BOLT_SLOT);
            SetEffect(skill, "_projectileCount", 2);
            SetEffect(skill, "_repeatCount", 3);
            SetEffect(skill, "_repeatInterval", INTERVAL);

            var plan = new SkillExecutionPlan(skill, SkillModifiers.Identity);

            Assert.AreEqual(6, plan.SlotCount);
            Assert.AreEqual(2 * INTERVAL, plan.ScheduledTail, TOLERANCE);
            Assert.AreEqual(0f, plan.GetShotOffset(0), TOLERANCE);
            Assert.AreEqual(2 * INTERVAL, plan.GetShotOffset(2), TOLERANCE);
        }

        [Test]
        public void Constructor_ProjectileWithFollowUpProjectile_AddsSlotAndTailAfterLastShot()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Sorcerer, BOLT_SLOT);
            SetEffect(skill, "_repeatCount", 3);
            SetEffect(skill, "_repeatInterval", INTERVAL);
            SkillModifiers modifiers = WithCore(ETalismanEffectKind.FollowUpProjectile, 0.5f);

            var plan = new SkillExecutionPlan(skill, in modifiers);

            Assert.AreEqual(4, plan.SlotCount);
            Assert.IsTrue(plan.HasFollowUpProjectile);
            Assert.AreEqual(2 * INTERVAL, plan.FollowUpProjectileBase, TOLERANCE);
            Assert.AreEqual((2 * INTERVAL) + 0.5f, plan.ScheduledTail, TOLERANCE);
        }

        [Test]
        public void Constructor_AreaWithSecondHit_ReservesOneSlotAndTailIsSecondHitDelay()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Warrior, CONE_SLOT);
            SetEffect(skill, "_damageCoefficient", 1f);
            SetEffect(skill, "_secondHitCoefficient", 1.5f);
            SetEffect(skill, "_secondHitDelay", 0.3f);
            SetEffect(skill, "_duration", 0f);

            var plan = new SkillExecutionPlan(skill, SkillModifiers.Identity);

            Assert.IsFalse(plan.IsProjectile);
            Assert.IsTrue(plan.EmitsSecondHit);
            Assert.AreEqual(1, plan.SlotCount);
            Assert.AreEqual(0.3f, plan.ScheduledTail, TOLERANCE);
            Assert.AreEqual(0.3f, plan.FollowUpProjectileBase, TOLERANCE);
        }

        [TestCase(ETalismanEffectKind.FollowUpStrike)]
        [TestCase(ETalismanEffectKind.Shockwave)]
        [TestCase(ETalismanEffectKind.FollowUpProjectile)]
        public void Constructor_AreaWithCoreFollowUp_SharesOneExtraSlotWithSecondHitSeparate(ETalismanEffectKind kind)
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Warrior, CONE_SLOT);
            SetEffect(skill, "_damageCoefficient", 1f);
            SetEffect(skill, "_secondHitCoefficient", 1.5f);
            SetEffect(skill, "_secondHitDelay", 0.3f);
            SetEffect(skill, "_duration", 0f);
            SkillModifiers modifiers = WithCore(kind, 0.5f);

            var plan = new SkillExecutionPlan(skill, in modifiers);

            Assert.AreEqual(2, plan.SlotCount);
        }

        [Test]
        public void Constructor_AreaFollowUpStrike_TailIsLaterOfSecondHitAndCoreDelay()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Warrior, CONE_SLOT);
            SetEffect(skill, "_secondHitCoefficient", 1.5f);
            SetEffect(skill, "_secondHitDelay", 0.3f);
            SkillModifiers shorter = WithCore(ETalismanEffectKind.FollowUpStrike, 0.1f);
            SkillModifiers longer  = WithCore(ETalismanEffectKind.FollowUpStrike, 0.7f);

            Assert.AreEqual(0.3f, new SkillExecutionPlan(skill, in shorter).ScheduledTail, TOLERANCE);
            Assert.AreEqual(0.7f, new SkillExecutionPlan(skill, in longer).ScheduledTail, TOLERANCE);
        }

        [Test]
        public void Constructor_AreaWithDuration_ReservesFieldSlot()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Warrior, CONE_SLOT);
            SetEffect(skill, "_damageCoefficient", 1f);
            SetEffect(skill, "_secondHitCoefficient", 0f);
            SetEffect(skill, "_duration", 2f);

            var plan = new SkillExecutionPlan(skill, SkillModifiers.Identity);

            Assert.IsTrue(plan.HasDurationEffect);
            Assert.AreEqual(1, plan.SlotCount);
        }

        [TestCase(ESkillShape.Summon)]
        [TestCase(ESkillShape.Line)]
        public void Constructor_SummonOrLine_ReservesNoSlotEvenWithRepeat(ESkillShape shape)
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Warrior, CONE_SLOT);
            typeof(SkillCatalogSO.SkillArea).GetField("_shape", PRIVATE_INSTANCE).SetValue(skill.Area, shape);
            SetEffect(skill, "_repeatCount", 4);
            SetEffect(skill, "_duration", 3f);

            var plan = new SkillExecutionPlan(skill, SkillModifiers.Identity);

            Assert.AreEqual(0, plan.SlotCount);
        }

        [Test]
        public void Constructor_SecondHitWithoutPrimaryDamage_KeepsLegacyTailButEmitsNothing()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Warrior, CONE_SLOT);
            SetEffect(skill, "_damageCoefficient", 0f);
            SetEffect(skill, "_secondHitCoefficient", 1.5f);
            SetEffect(skill, "_secondHitDelay", 0.3f);
            SetEffect(skill, "_duration", 0f);

            var plan = new SkillExecutionPlan(skill, SkillModifiers.Identity);

            Assert.IsFalse(plan.EmitsSecondHit);
            Assert.AreEqual(0, plan.SlotCount);
            Assert.AreEqual(0.3f, plan.ScheduledTail, TOLERANCE);
        }

        [Test]
        public void TryCast_ProjectileVolley_ActivatesExactlyPlannedSlotsWithPerShotIds()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Sorcerer, BOLT_SLOT);
            SetEffect(skill, "_projectileCount", 2);
            SetEffect(skill, "_repeatCount", 3);
            SetEffect(skill, "_repeatInterval", INTERVAL);
            CreateServer(EPlayerClass.Sorcerer);
            var plan = new SkillExecutionPlan(skill, SkillModifiers.Identity);

            Cast(BOLT_SLOT);
            int actionId = _server.ActionInstanceId;
            Tick(_server.ResolveAt);

            List<int> actionIds = ActiveEffectActionIds();
            Assert.AreEqual(plan.SlotCount, actionIds.Count);
            Assert.AreEqual(3, new HashSet<int>(actionIds).Count, "분기는 발사 순번마다 ID를 공유한다");
            Assert.AreEqual(2, CountOf(actionIds, actionId), "첫 발사 순번만 동작 ID를 쓴다");
        }

        [Test]
        public void TryCast_PlannedSlotsExceedFreeEffects_RejectsWithoutChargeOrPhaseChange()
        {
            SkillCatalogSO.SkillDefinition skill = GetSkill(EPlayerClass.Sorcerer, BOLT_SLOT);
            SetEffect(skill, "_projectileCount", 2);
            SetEffect(skill, "_repeatCount", 7);
            SetEffect(skill, "_repeatInterval", INTERVAL);
            CreateServer(EPlayerClass.Sorcerer);
            float       energy   = _server.Energy;
            int         castId   = _server.CastInstanceId;
            int         actionId = _server.ActionInstanceId;

            bool accepted = _server.TryCast(ESkillRequestKind.Regular, BOLT_SLOT, Vector3.forward,
                FacingRules.EncodeYaw(Vector3.forward), START_TIME, true, out ERejectReason reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(ERejectReason.NoCapacity, reason);
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
            Assert.AreEqual(energy, _server.Energy, TOLERANCE);
            Assert.AreEqual(castId, _server.CastInstanceId);
            Assert.AreEqual(actionId, _server.ActionInstanceId);
        }

        // 실전 성능을 잰 것이 아니라 워밍업 뒤 계획 계산이 힙 할당을 새로 만들지 않는지만 본다
        [Test]
        public void Constructor_RepeatedAfterWarmup_AllocatesZero()
        {
            SkillCatalogSO.SkillDefinition projectile = GetSkill(EPlayerClass.Sorcerer, BOLT_SLOT);
            SkillCatalogSO.SkillDefinition area       = GetSkill(EPlayerClass.Warrior, CONE_SLOT);
            SkillModifiers                 followUp   = WithCore(ETalismanEffectKind.FollowUpProjectile, 0.5f);
            float                          checksum   = ReadPlan(projectile, SkillModifiers.Identity) + ReadPlan(area, in followUp);

            GC.GetAllocatedBytesForCurrentThread();
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[256];
            long controlAllocated = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
            GC.KeepAlive(control);
            if (controlAllocated == 0)
            {
                Assert.Ignore("현재 런타임의 GC.GetAllocatedBytesForCurrentThread가 기지 256바이트 할당도 0으로 반환한다. 계획 계산의 0 할당은 실행 검증할 수 없어 이 시험을 제외한다");
            }

            Assert.GreaterOrEqual(controlAllocated, control.Length, "기지 할당을 세지 못하는 런타임에서는 0 할당 판정을 내릴 수 없다");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < ALLOCATION_ITERATIONS; ++i)
            {
                checksum += ReadPlan(projectile, SkillModifiers.Identity);
                checksum += ReadPlan(area, in followUp);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.Greater(checksum, 0f, "계산 결과를 읽어 반복이 생략되지 않았음을 확인한다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static float ReadPlan(SkillCatalogSO.SkillDefinition skill, in SkillModifiers modifiers)
        {
            var plan = new SkillExecutionPlan(skill, in modifiers);
            return plan.SlotCount + plan.ScheduledTail + plan.FollowUpProjectileBase + plan.GetShotOffset(1);
        }

        private SkillCatalogSO.SkillDefinition GetSkill(EPlayerClass playerClass, int slot)
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill));
            return skill;
        }

        private static void SetEffect(SkillCatalogSO.SkillDefinition skill, string field, object value)
        {
            FieldInfo info = typeof(SkillCatalogSO.SkillEffectSpec).GetField(field, PRIVATE_INSTANCE);
            Assert.IsNotNull(info, field);
            info.SetValue(skill.Effect, value);
        }

        private static SkillModifiers WithCore(ETalismanEffectKind kind, float delay)
        {
            var core = new SkillCoreVariant(kind, 0f, 0f, delay, 0f, 0f, 0f, 0, 0f, 0, 0f);
            return new SkillModifiers(1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, in core);
        }

        private void CreateServer(EPlayerClass playerClass)
        {
            _server = new SkillServer(_owner, playerClass, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                new CombatLineOfSight(0, 0f), START_TIME);
        }

        private void Cast(int slot)
        {
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Regular, slot, Vector3.forward,
                FacingRules.EncodeYaw(Vector3.forward), START_TIME, true, out ERejectReason reason), reason.ToString());
        }

        private void Tick(double now)
        {
            _core.BeginTick(now);
            _server.Tick(now, 0f, null);
        }

        private List<int> ActiveEffectActionIds()
        {
            var ids = new List<int>();
            var effects = (Array)typeof(SkillServer).GetField("_effects", PRIVATE_INSTANCE).GetValue(_server);
            for (int i = 0; i < effects.Length; ++i)
            {
                object effect = effects.GetValue(i);
                Type   type   = effect.GetType();
                if ((bool)type.GetField("Active").GetValue(effect))
                {
                    ids.Add((int)type.GetField("ActionId").GetValue(effect));
                }
            }
            return ids;
        }

        private static int CountOf(List<int> values, int target)
        {
            int count = 0;
            for (int i = 0; i < values.Count; ++i)
            {
                if (values[i] == target)
                {
                    ++count;
                }
            }
            return count;
        }

        private sealed class FakeCaster : ISkillCaster
        {
            public int                   CombatId        { get; }
            public ECombatFaction        Faction         { get; }
            public EParticipantLifeState LifeState       { get; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; private set; }
            public Vector3               Forward         { get; private set; } = Vector3.forward;
            public float                 BodyRadius      => 0f;
            public float                 Health          => TARGET_HEALTH;
            public float                 MaxHealth       => TARGET_HEALTH;
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;
            public float                 AttackPower     => ATTACK_POWER;

            public FakeCaster(int combatId, ECombatFaction faction)
            {
                CombatId = combatId;
                Faction  = faction;
            }

            public void SetSkillPosition(Vector3 position) { Position = position; }
            public void SetSkillFacing(Vector3 direction) { Forward = direction; }
            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime) { }
        }
    }
}
