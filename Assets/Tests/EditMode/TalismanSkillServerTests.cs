// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class TalismanSkillServerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME     = 10.0;
        private const float  TOLERANCE      = 0.01f;
        private const float  ATTACK_POWER   = 100f;
        private const float  TARGET_HEALTH  = 10000f;
        private const int    PARTICIPANT_ID = 1;
        private const int    LEDGER_SIZE    = 256;
        private const double LEDGER_AGE     = 30.0;
        private const double ACTION_AGE     = 10.0;
        private const int    PATH_BUDGET    = 4;
        private const int    LEVEL_ONE      = 1;
        private const float  BOOSTED_ENERGY = 140f;
        private const float  SKILL_GAIN     = 4f;

        // 시전 확정 직후 효과 칸을 읽으려고 시전 시각에서 더하는 여유(초)
        private const double ACTIVATION_MARGIN = 0.01;

        // SkillServer의 효과 종류 열거 순서(투사체, 장판, 지연 타격)를 반사로 읽은 값과 맞추는 번호
        private const int EFFECT_KIND_PROJECTILE = 0;
        private const int EFFECT_KIND_FIELD      = 1;

        private const int SLOT_Q = Constants.Combat.SKILL_SLOT_Q;
        private const int SLOT_W = Constants.Combat.SKILL_SLOT_W;
        private const int SLOT_E = Constants.Combat.SKILL_SLOT_E;
        private const int SLOT_R = Constants.Combat.SKILL_SLOT_R;

        private SkillCatalogSO       _catalog;
        private TalismanCatalogSO    _talismans;
        private PlayerConfigSO       _player;
        private CombatTargetRegistry _registry;
        private DamageResolver       _core;
        private SkillDamageResolver  _resolver;
        private FakeCaster           _owner;
        private FixedModifiers       _modifiers;
        private PathRequestBudget    _budget;
        private SkillServer          _server;
        private double               _lastTick;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog   = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _talismans = ScriptableObject.CreateInstance<TalismanCatalogSO>();
            _player    = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _registry  = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(LEDGER_SIZE, LEDGER_AGE, ACTION_AGE);
            _core      = new DamageResolver(_registry, ledger, _player.DefenseReductionConstant, _player.DamageReductionCap);
            _resolver  = new SkillDamageResolver(_core, _registry, ledger);
            _owner     = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Player);
            Assert.IsTrue(_registry.Register(_owner));
            _modifiers = new FixedModifiers();
            _budget    = new PathRequestBudget(PATH_BUDGET);
            _lastTick  = START_TIME;
            _core.BeginTick(START_TIME);
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Shutdown();
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_talismans);
            Object.DestroyImmediate(_player);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryCast_CooldownTalisman_ShortensOriginalCooldown()
        {
            Create(EPlayerClass.Warrior, SLOT_Q, "T01-1A");

            Cast(SLOT_Q, Vector3.zero);

            Assert.AreEqual(_player.BaseMaxEnergy - 20f, _server.Energy, TOLERANCE);
            Assert.AreEqual(START_TIME + 5.4, _server.GetReadyAt(SLOT_Q), TOLERANCE);
        }

        [Test]
        public void TryCast_EnergyTalisman_ReducesOriginalCost()
        {
            Create(EPlayerClass.Warrior, SLOT_Q, "T01-1B");

            Cast(SLOT_Q, Vector3.zero);

            Assert.AreEqual(_player.BaseMaxEnergy - 16f, _server.Energy, TOLERANCE);
            Assert.AreEqual(START_TIME + 6.0, _server.GetReadyAt(SLOT_Q), TOLERANCE);
        }

        [Test]
        public void Constructor_StatsMaxEnergy_ReplacesBaseMaxEnergy()
        {
            _server = new SkillServer(_owner, EPlayerClass.Warrior, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                new CombatLineOfSight(0, 0f), START_TIME, null, new FakeNavigation(), BOOSTED_ENERGY);

            Assert.AreEqual(BOOSTED_ENERGY, _server.MaxEnergy);
            Assert.AreEqual(BOOSTED_ENERGY, _server.Energy);
        }

        [Test]
        public void WideSlash_FollowUpStrike_HitsSameEnemyTwice()
        {
            Create(EPlayerClass.Warrior, SLOT_Q, "T01-3A");
            FakeCaster enemy = Enemy(new Vector3(0f, 0f, 2f));

            Cast(SLOT_Q, Vector3.zero);
            Tick(START_TIME + 0.21);
            Assert.AreEqual(TARGET_HEALTH - 220f, enemy.Health, TOLERANCE);

            Tick(START_TIME + 0.36);
            Assert.AreEqual(2, enemy.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - 280f, enemy.Health, TOLERANCE);
        }

        [Test]
        public void WideSlash_CircleSweep_HitsBehindCaster()
        {
            Create(EPlayerClass.Warrior, SLOT_Q, "T01-3B");
            FakeCaster behind = Enemy(new Vector3(0f, 0f, -2f));

            Cast(SLOT_Q, Vector3.zero);
            Tick(START_TIME + 0.21);

            Assert.AreEqual(TARGET_HEALTH - 240f, behind.Health, TOLERANCE);
        }

        [Test]
        public void WideSlash_WithoutTalisman_DoesNotHitBehindCaster()
        {
            CreatePlain(EPlayerClass.Warrior);
            FakeCaster behind = Enemy(new Vector3(0f, 0f, -2f));

            Cast(SLOT_Q, Vector3.zero);
            Tick(START_TIME + 0.21);

            Assert.AreEqual(0, behind.DamageCalls);
        }

        [Test]
        public void GroundSlam_Shockwave_HitsOutsideFirstRadiusAfterDelay()
        {
            Create(EPlayerClass.Warrior, SLOT_E, "T03-3A");
            FakeCaster outer = Enemy(new Vector3(0f, 0f, 5.5f));

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.61);
            Assert.AreEqual(0, outer.DamageCalls);

            Tick(START_TIME + 0.82);
            Assert.AreEqual(TARGET_HEALTH - 120f, outer.Health, TOLERANCE);
        }

        [Test]
        public void SpellBolt_SpreadShot_HitsEachBranchTargetOnce()
        {
            Create(EPlayerClass.Sorcerer, SLOT_Q, "T05-3A");
            FakeCaster left   = Enemy(Quaternion.AngleAxis(-20f, Vector3.up) * Vector3.forward * 5f);
            FakeCaster center = Enemy(Vector3.forward * 5f);
            FakeCaster right  = Enemy(Quaternion.AngleAxis(20f, Vector3.up) * Vector3.forward * 5f);

            Cast(SLOT_Q, Vector3.zero);
            Tick(START_TIME + 0.16);
            Tick(START_TIME + 0.8);

            Assert.AreEqual(1, left.DamageCalls);
            Assert.AreEqual(1, center.DamageCalls);
            Assert.AreEqual(1, right.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - 140f, center.Health, TOLERANCE);
        }

        [Test]
        public void SpiritStrike_PierceShot_HitsTwoMoreEnemiesWithResonanceOnce()
        {
            Create(EPlayerClass.Summoner, SLOT_E, "T11-3A");
            FakeCaster first  = Enemy(Vector3.forward * 3f);
            FakeCaster second = Enemy(Vector3.forward * 5f);
            FakeCaster third  = Enemy(Vector3.forward * 7f);
            FakeCaster fourth = Enemy(Vector3.forward * 8.5f);

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.21);
            Tick(START_TIME + 0.9);

            Assert.AreEqual(TARGET_HEALTH - 180f, first.Health, TOLERANCE);
            Assert.AreEqual(TARGET_HEALTH - 120f, second.Health, TOLERANCE);
            Assert.AreEqual(TARGET_HEALTH - 120f, third.Health, TOLERANCE);
            Assert.AreEqual(0, fourth.DamageCalls);
            Assert.AreEqual(SKILL_GAIN, _server.Resonance, TOLERANCE);
        }

        [Test]
        public void FlameField_Detonate_ReplacesTicksWithSingleExplosion()
        {
            Create(EPlayerClass.Sorcerer, SLOT_W, "T06-3A");
            Vector3    point = Vector3.forward * 4f;
            FakeCaster enemy = Enemy(point);

            Cast(SLOT_W, point);
            Tick(START_TIME + 0.31);
            Tick(START_TIME + 1.0);
            Assert.AreEqual(0, enemy.DamageCalls);

            Tick(START_TIME + 1.11);
            Tick(START_TIME + 6.0);
            Assert.AreEqual(1, enemy.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - 550f, enemy.Health, TOLERANCE);
        }

        [Test]
        public void FlameField_EndExplosion_AddsFinalBlastAfterTicks()
        {
            Create(EPlayerClass.Sorcerer, SLOT_W, "T06-3B");
            Vector3    point = Vector3.forward * 4f;
            FakeCaster enemy = Enemy(point);

            Cast(SLOT_W, point);
            for (int i = 1; i <= 50; ++i)
            {
                Tick(START_TIME + (0.1 * i));
            }

            Assert.AreEqual(9, enemy.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - ((8f * 56.25f) + 150f), enemy.Health, TOLERANCE);
        }

        [Test]
        public void Counter_GuardDurationTalisman_ExtendsGuardWindow()
        {
            Create(EPlayerClass.Warrior, SLOT_R, "T04-2A");

            Cast(SLOT_R, Vector3.zero);
            Tick(START_TIME + 0.55);

            Assert.IsTrue(_server.IsGuardWindow);
        }

        [Test]
        public void Counter_CounterWave_HitsAlongForwardLine()
        {
            Create(EPlayerClass.Warrior, SLOT_R, "T04-3A");
            FakeCaster attacker = Enemy(Vector3.forward * 2f);
            FakeCaster far      = Enemy(Vector3.forward * 7f);

            Cast(SLOT_R, Vector3.zero);
            Tick(START_TIME + 0.1);
            var incoming = new DamageRequest(attacker.CombatId, _owner.CombatId, _resolver.IssueInstanceId(),
                _resolver.IssueInstanceId(), 10f, EDamageFlags.None, attacker.Position, "test_enemy_hit");
            Assert.AreEqual(EDamageOutcome.Guarded, _resolver.Apply(in incoming).Outcome);
            Tick(START_TIME + 0.51);

            Assert.AreEqual(TARGET_HEALTH - 400f, far.Health, TOLERANCE);
        }

        [Test]
        public void Dash_PierceDash_HitsEveryEnemyOnPathWithoutStopping()
        {
            Create(EPlayerClass.Warrior, SLOT_W, "T02-3A");
            FakeCaster near = Enemy(Vector3.forward * 1.5f);
            FakeCaster mid  = Enemy(Vector3.forward * 3f);

            Cast(SLOT_W, Vector3.zero);
            for (int i = 1; i <= 4; ++i)
            {
                Tick(START_TIME + (0.1 * i));
            }

            Assert.AreEqual(TARGET_HEALTH - 200f, near.Health, TOLERANCE);
            Assert.AreEqual(TARGET_HEALTH - 200f, mid.Health, TOLERANCE);
            Assert.AreEqual(4f, _owner.Position.z, TOLERANCE);
        }

        [Test]
        public void BindingField_PullRoot_PullsToCenterAndAddsRoot()
        {
            Create(EPlayerClass.Sorcerer, SLOT_E, "T07-3A");
            Vector3    point = Vector3.forward * 4f;
            FakeCaster enemy = Enemy(point + Vector3.right * 2f);

            Cast(SLOT_E, point);
            Tick(START_TIME + 0.31);

            Assert.AreEqual(point.x, enemy.Position.x, TOLERANCE);
            Assert.AreEqual(point.z, enemy.Position.z, TOLERANCE);
            Assert.AreEqual(2.2f, enemy.RootDuration, TOLERANCE);
        }

        [Test]
        public void Lightning_FollowUpWithRadius_SecondStrikeUsesWiderRadius()
        {
            Create(EPlayerClass.Sorcerer, SLOT_R, "T08-3A", "T08-2A");
            Vector3    point = Vector3.forward * 4f;
            FakeCaster edge  = Enemy(point + Vector3.right * 2.4f);

            Cast(SLOT_R, point);
            Tick(START_TIME + 0.71);
            Tick(START_TIME + 1.07);

            Assert.AreEqual(2, edge.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - 580f, edge.Health, TOLERANCE);
        }

        // 아래 Activate_ 시험은 같은 기술과 부적 변형으로 만든 SkillExecutionPlan이 예약한 칸 수, 마지막 일정을
        // 시전 확정 직후 SkillServer가 실제로 만든 효과 칸과 비교한다. 대상을 두지 않아 효과가 중간에 사라지지 않는다.
        // 기대 숫자는 카탈로그 값에서 나오므로 데이터가 바뀌면 같이 봐야 한다

        [Test]
        public void Activate_FollowUpStrike_SlotCountAndLastScheduleMatchCreatedStrike()
        {
            Create(EPlayerClass.Warrior, SLOT_Q, "T01-3A");
            SkillExecutionPlan plan = CurrentPlan();

            double resolveAt = CastAndActivate(SLOT_Q, Vector3.zero);

            List<ScheduledEffect> effects = ActiveEffects();
            Assert.AreEqual(1, plan.SlotCount);
            Assert.AreEqual(plan.SlotCount, effects.Count);
            Assert.AreEqual(resolveAt + plan.ScheduledTail, LatestAt(effects), TOLERANCE);
        }

        [Test]
        public void Activate_Shockwave_ReservesOneSlotButScheduledTailDoesNotCoverShockwave()
        {
            Create(EPlayerClass.Warrior, SLOT_E, "T03-3A");
            SkillExecutionPlan plan = CurrentPlan();

            double resolveAt = CastAndActivate(SLOT_E, Vector3.zero);

            List<ScheduledEffect> effects = ActiveEffects();
            Assert.AreEqual(1, plan.SlotCount);
            Assert.AreEqual(plan.SlotCount, effects.Count);
            Assert.AreEqual(resolveAt + plan.CoreDelay, LatestAt(effects), TOLERANCE);

            // 기존 동작 보존: 충격파는 마지막 일정 꼬리에 들어가지 않아 실제 타격이 꼬리보다 늦다
            Assert.AreEqual(0f, plan.ScheduledTail, TOLERANCE);
            Assert.Greater(LatestAt(effects), resolveAt + plan.ScheduledTail + TOLERANCE);
        }

        [Test]
        public void Activate_Detonate_DurationSlotHoldsSingleExplosionAndTailDoesNotCoverIt()
        {
            Create(EPlayerClass.Sorcerer, SLOT_W, "T06-3A");
            SkillExecutionPlan plan = CurrentPlan();

            double resolveAt = CastAndActivate(SLOT_W, Vector3.forward * 4f);

            // 지속 칸 하나를 장판 대신 폭발 한 번이 쓴다
            List<ScheduledEffect> effects = ActiveEffects();
            Assert.IsTrue(plan.HasDurationEffect);
            Assert.AreEqual(1, plan.SlotCount);
            Assert.AreEqual(plan.SlotCount, effects.Count);
            Assert.AreNotEqual(EFFECT_KIND_FIELD, effects[0].Kind, "장판이 아니라 지연 타격이다");
            Assert.AreEqual(resolveAt + plan.CoreDelay, LatestAt(effects), TOLERANCE);

            // 기존 동작 보존: 폭발 지연도 꼬리에 넣지 않았다
            Assert.AreEqual(0f, plan.ScheduledTail, TOLERANCE);
        }

        [Test]
        public void Activate_EndExplosion_FieldSlotLivesForFullDurationAndTailDoesNotCoverBlast()
        {
            Create(EPlayerClass.Sorcerer, SLOT_W, "T06-3B");
            SkillExecutionPlan plan     = CurrentPlan();
            SkillModifiers     modifiers = _modifiers.Value;
            float              duration  = TalismanModifierRules.GetDuration(_modifiers.Skill, in modifiers);

            double resolveAt = CastAndActivate(SLOT_W, Vector3.forward * 4f);

            // 마지막 폭발은 장판 칸이 끝날 때 나가므로 칸 하나가 지속 시간 내내 산다
            List<ScheduledEffect> effects = ActiveEffects();
            Assert.AreEqual(1, plan.SlotCount);
            Assert.AreEqual(plan.SlotCount, effects.Count);
            Assert.AreEqual(EFFECT_KIND_FIELD, effects[0].Kind);
            Assert.AreEqual(resolveAt + duration, effects[0].At, TOLERANCE);

            // 기존 동작 보존: 장판 종료 폭발도 꼬리에 넣지 않았다
            Assert.AreEqual(0f, plan.ScheduledTail, TOLERANCE);
        }

        [Test]
        public void Activate_SecondHitWithFollowUpProjectile_TwoSlotsAndTailIsLaunchAfterSecondHit()
        {
            Create(EPlayerClass.Swordsman, SLOT_R, "T16-3A");
            SkillExecutionPlan plan = CurrentPlan();

            double resolveAt = CastAndActivate(SLOT_R, Vector3.zero);

            // 두 번째 타격 칸 하나와 후속 투사체 칸 하나, 후속 투사체는 두 번째 타격 시각을 기준으로 발사된다
            List<ScheduledEffect> effects = ActiveEffects();
            Assert.IsTrue(plan.EmitsSecondHit);
            Assert.IsTrue(plan.HasFollowUpProjectile);
            Assert.AreEqual(2, plan.SlotCount);
            Assert.AreEqual(plan.SlotCount, effects.Count);
            Assert.AreEqual(plan.SecondHitDelay, plan.FollowUpProjectileBase, TOLERANCE);
            Assert.IsTrue(HasEffectAt(effects, resolveAt + plan.SecondHitDelay), "두 번째 타격");
            Assert.IsTrue(HasEffectAt(effects, resolveAt + plan.SecondHitDelay + plan.CoreDelay), "후속 투사체 발사");
            Assert.AreEqual(resolveAt + plan.ScheduledTail, LatestAt(effects), TOLERANCE);
        }

        [Test]
        public void Activate_ProjectileFollowUp_TwoSlotsAndFollowUpLaunchesAfterFirstShot()
        {
            Create(EPlayerClass.Mageknight, SLOT_W, "T20-3A");
            SkillExecutionPlan plan = CurrentPlan();

            double resolveAt = CastAndActivate(SLOT_W, Vector3.zero);

            // 투사체형은 발사 순번이 하나뿐이라 후속 투사체가 첫 발사 기준으로 나간다
            List<ScheduledEffect> effects = ActiveEffects();
            Assert.IsTrue(plan.IsProjectile);
            Assert.AreEqual(2, plan.SlotCount);
            Assert.AreEqual(plan.SlotCount, effects.Count);
            Assert.AreEqual(0f, plan.FollowUpProjectileBase, TOLERANCE);
            Assert.IsTrue(HasEffectAt(effects, resolveAt), "첫 발사");
            Assert.IsTrue(HasEffectAt(effects, resolveAt + plan.CoreDelay), "후속 투사체 발사");
            Assert.AreEqual(resolveAt + plan.ScheduledTail, LatestAt(effects), TOLERANCE);
        }

        [Test]
        public void Activate_SpreadShot_BranchCountMatchesCreatedProjectilesSharingOneActionId()
        {
            Create(EPlayerClass.Sorcerer, SLOT_Q, "T05-3A");
            SkillExecutionPlan plan = CurrentPlan();

            double resolveAt = CastAndActivate(SLOT_Q, Vector3.zero);

            List<ScheduledEffect> effects = ActiveEffects();
            Assert.AreEqual(3, plan.ProjectileCount);
            Assert.AreEqual(plan.ProjectileCount * plan.VolleyCount, plan.SlotCount);
            Assert.AreEqual(plan.SlotCount, effects.Count);
            Assert.AreEqual(resolveAt + plan.ScheduledTail, LatestAt(effects), TOLERANCE);
            for (int i = 1; i < effects.Count; ++i)
            {
                Assert.AreEqual(effects[0].ActionId, effects[i].ActionId, "한 발사의 갈래는 동작 번호를 나눠 쓴다");
            }
        }

        [Test]
        public void Activate_CircleSweepShapeChange_ReservesNoSlotAndCreatesNoEffect()
        {
            Create(EPlayerClass.Warrior, SLOT_Q, "T01-3B");
            SkillExecutionPlan plan = CurrentPlan();

            CastAndActivate(SLOT_Q, Vector3.zero);

            // 원형 교체는 첫 타격 한 번으로 끝나는 즉시 판정이라 예약할 칸도 만들 효과도 없다
            Assert.AreEqual(0, plan.SlotCount);
            Assert.AreEqual(0, ActiveEffects().Count);
            Assert.AreEqual(0f, plan.ScheduledTail, TOLERANCE);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private SkillExecutionPlan CurrentPlan()
        {
            SkillModifiers modifiers = _modifiers.Value;
            return new SkillExecutionPlan(_modifiers.Skill, in modifiers);
        }

        private double CastAndActivate(int slot, Vector3 point)
        {
            Cast(slot, point);
            double resolveAt = _server.ResolveAt;
            Tick(resolveAt + ACTIVATION_MARGIN);
            return resolveAt;
        }

        // 켜져 있는 효과 칸을 반사로 읽는다. 투사체는 발사 시각, 지연 타격은 판정 시각, 장판은 종료 시각을 일정으로 본다
        private List<ScheduledEffect> ActiveEffects()
        {
            FieldInfo effectsField = typeof(SkillServer).GetField("_effects", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(effectsField, "SkillServer._effects");
            var slots   = (System.Array)effectsField.GetValue(_server);
            var effects = new List<ScheduledEffect>();
            for (int i = 0; i < slots.Length; ++i)
            {
                object slot = slots.GetValue(i);
                System.Type type = slot.GetType();
                if (!(bool)type.GetField("Active").GetValue(slot))
                {
                    continue;
                }

                int kind = System.Convert.ToInt32(type.GetField("Kind").GetValue(slot));
                effects.Add(new ScheduledEffect(kind, (int)type.GetField("ActionId").GetValue(slot),
                    (double)type.GetField(kind == EFFECT_KIND_PROJECTILE ? "StartAt" : "EndsAt").GetValue(slot)));
            }
            return effects;
        }

        private static double LatestAt(List<ScheduledEffect> effects)
        {
            double latest = double.MinValue;
            for (int i = 0; i < effects.Count; ++i)
            {
                latest = System.Math.Max(latest, effects[i].At);
            }
            return latest;
        }

        private static bool HasEffectAt(List<ScheduledEffect> effects, double at)
        {
            for (int i = 0; i < effects.Count; ++i)
            {
                if (System.Math.Abs(effects[i].At - at) <= TOLERANCE)
                {
                    return true;
                }
            }
            return false;
        }

        private void Create(EPlayerClass playerClass, int slot, params string[] effectIds)
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill));
            var entries = new CombatTalismanEntry[effectIds.Length];
            for (int i = 0; i < effectIds.Length; ++i)
            {
                Assert.IsTrue(_talismans.TryGetEffect(effectIds[i], out TalismanCatalogSO.TalismanEffectDefinition effect));
                entries[i] = new CombatTalismanEntry(effect.SkillId, (int)effect.Tier, effect.DefinitionId, LEVEL_ONE);
            }

            Assert.IsTrue(TalismanModifierRules.TryCompile(_talismans, skill, entries, out SkillModifiers modifiers, out string error),
                error);
            _modifiers.Skill = skill;
            _modifiers.Value = modifiers;
            CreateServer(playerClass, _modifiers);
        }

        private void CreatePlain(EPlayerClass playerClass)
        {
            CreateServer(playerClass, null);
        }

        private void CreateServer(EPlayerClass playerClass, ITalismanModifierResolver modifiers)
        {
            _server       = new SkillServer(_owner, playerClass, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                new CombatLineOfSight(0, 0f), START_TIME, modifiers, new FakeNavigation());
            _owner.Skills = _server;
        }

        private FakeCaster Enemy(Vector3 position)
        {
            var target = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = position };
            Assert.IsTrue(_registry.Register(target));
            return target;
        }

        private void Cast(int slot, Vector3 point)
        {
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Regular, slot, point, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason), reason.ToString());
        }

        private void Tick(double now)
        {
            _core.BeginTick(now);
            _budget.BeginTick();
            _server.Tick(now, Mathf.Max(0f, (float)(now - _lastTick)), _budget);
            _lastTick = now;
        }

        private readonly struct ScheduledEffect
        {
            public int    Kind     { get; }
            public int    ActionId { get; }
            public double At       { get; }

            public ScheduledEffect(int kind, int actionId, double at)
            {
                Kind     = kind;
                ActionId = actionId;
                At       = at;
            }
        }

        private sealed class FixedModifiers : ITalismanModifierResolver
        {
            public SkillCatalogSO.SkillDefinition Skill { get; set; }
            public SkillModifiers                 Value { get; set; } = SkillModifiers.Identity;

            public SkillModifiers Resolve(int participantId, SkillCatalogSO.SkillDefinition skill)
            {
                return ReferenceEquals(skill, Skill) ? Value : SkillModifiers.Identity;
            }
        }

        private sealed class FakeNavigation : ISkillNavigation
        {
            public bool TrySample(Vector3 point, out Vector3 sampled)
            {
                sampled = point;
                return true;
            }

            public Vector3 Move(Vector3 origin, Vector3 destination)
            {
                return destination;
            }
        }

        private sealed class FakeCaster : ISkillCombatant, ISkillControlTarget, ISkillPullTarget
        {
            public int                   CombatId          { get; }
            public ECombatFaction        Faction           { get; }
            public EParticipantLifeState LifeState         { get; set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon          => false;
            public int                   OwnerCombatId     => CombatIds.NONE;
            public Vector3               Position          { get; set; }
            public Vector3               Forward           { get; private set; } = Vector3.forward;
            public float                 BodyRadius        => 0f;
            public float                 Health            { get; private set; } = TARGET_HEALTH;
            public float                 MaxHealth         => TARGET_HEALTH;
            public float                 Defense           => 0f;
            public bool                  IsInvulnerable    => false;
            public bool                  IsGuardingFront   => false;
            public float                 AttackPower       => ATTACK_POWER;
            public SkillServer           Skills            { get; set; }
            public int                   DamageCalls       { get; private set; }
            public bool                  IsBoss            => false;
            public bool                  AllowsRoot        => true;
            public float                 RootDurationScale => 1f;
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
                    LifeState = EParticipantLifeState.Dead;
                }
            }

            public void ApplyRoot(string sourceId, float duration, double serverTime) { RootDuration = duration; }
            public void PullTo(Vector3 point, double serverTime) { Position = point; }
        }
    }
}
