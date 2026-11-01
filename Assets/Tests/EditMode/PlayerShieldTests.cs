// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.UI;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class PlayerShieldTests
    {
        // 마검사 Q 보호막의 부여, 피해 흡수 순서(보호막 먼저, 남은 피해는 체력), 만료, 덮어쓰기와 체력바 비율을 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private const double NOW            = 50.0;
        private const double SHIELD_SECONDS = 3.0;
        private const float  TOLERANCE      = 0.01f;
        private const float  HUGE_DAMAGE    = 1000000f;
        private const int    PATH_BUDGET    = 4;
        private const int    PARTICIPANT_ID = 1;

        // 등록하지 않은 공격자 번호, 판정기는 떠난 공격자로 보고 진영 검사를 건너뛴다
        private const int ATTACKER_ID = 9999;

        // 기술 서버 시험
        private const double SKILL_START   = 10.0;
        private const float  OWNER_HEALTH  = 2000f;
        private const float  ATTACK_POWER  = 100f;
        private const int    LEDGER_SIZE   = 256;
        private const double LEDGER_AGE    = 30.0;
        private const double ACTION_AGE    = 10.0;

        private PlayerServerTestRig _rig;
        private PlayerServer        _player;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _rig    = new PlayerServerTestRig();
            _player = _rig.Add(PARTICIPANT_ID, Vector3.zero, EPlayerClass.Mageknight);
            Assert.IsTrue(_rig.Registry.Register(_player));
            _rig.Resolver.BeginTick(NOW);
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /****************************************
        *              Rules Tests
        ****************************************/

        [Test]
        public void Absorb_DamageBelowShield_AbsorbsAllAndKeepsRest()
        {
            float passed = PlayerShieldRules.Absorb(50f, 30f, out float remaining);

            Assert.AreEqual(0f, passed, TOLERANCE);
            Assert.AreEqual(20f, remaining, TOLERANCE);
        }

        [Test]
        public void Absorb_DamageAboveShield_PassesOverflowAndEmptiesShield()
        {
            float passed = PlayerShieldRules.Absorb(50f, 80f, out float remaining);

            Assert.AreEqual(30f, passed, TOLERANCE);
            Assert.AreEqual(0f, remaining, TOLERANCE);
        }

        [Test]
        public void Absorb_DamageEqualsShield_PassesNothingAndEmptiesShield()
        {
            float passed = PlayerShieldRules.Absorb(50f, 50f, out float remaining);

            Assert.AreEqual(0f, passed, TOLERANCE);
            Assert.AreEqual(0f, remaining, TOLERANCE);
        }

        [Test]
        public void Absorb_NoOrInvalidShield_PassesDamageUnchanged()
        {
            Assert.AreEqual(40f, PlayerShieldRules.Absorb(0f, 40f, out float empty), TOLERANCE);
            Assert.AreEqual(0f, empty);
            Assert.AreEqual(40f, PlayerShieldRules.Absorb(float.NaN, 40f, out float nan), TOLERANCE);
            Assert.AreEqual(0f, nan);
            Assert.AreEqual(40f, PlayerShieldRules.Absorb(float.PositiveInfinity, 40f, out float infinite), TOLERANCE);
            Assert.AreEqual(0f, infinite);
        }

        [Test]
        public void GetGrantAmount_MaxHealth_IsFivePercent()
        {
            Assert.AreEqual(50f, PlayerShieldRules.GetGrantAmount(1000f), TOLERANCE);
            Assert.AreEqual(0f, PlayerShieldRules.GetGrantAmount(0f));
            Assert.AreEqual(0f, PlayerShieldRules.GetGrantAmount(float.NaN));
        }

        [Test]
        public void GetDuration_NoBarrierDuration_UsesDefault()
        {
            Assert.AreEqual(3f, PlayerShieldRules.GetDuration(3f), TOLERANCE);
            Assert.AreEqual(PlayerShieldRules.DEFAULT_SHIELD_DURATION, PlayerShieldRules.GetDuration(0f), TOLERANCE);
            Assert.AreEqual(PlayerShieldRules.DEFAULT_SHIELD_DURATION, PlayerShieldRules.GetDuration(float.NaN), TOLERANCE);
        }

        [Test]
        public void GrantsShield_OnlyMageknightQ_IsTrue()
        {
            Assert.IsTrue(PlayerShieldRules.GrantsShield(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_Q));
            Assert.IsFalse(PlayerShieldRules.GrantsShield(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_W));
            Assert.IsFalse(PlayerShieldRules.GrantsShield(EPlayerClass.Archer, Constants.Combat.SKILL_SLOT_Q));
            Assert.IsFalse(PlayerShieldRules.GrantsShield(EPlayerClass.Warrior, Constants.Combat.SKILL_SLOT_Q));
        }

        /****************************************
        *           Damage Resolver Tests
        ****************************************/

        [Test]
        public void Apply_DamageAboveShield_ShieldFirstThenHealth()
        {
            float shield = GrantFivePercent(NOW + SHIELD_SECONDS);
            float raw    = RawFor(shield * 3f);

            DamageResult result = _rig.Resolver.Apply(Request(1, raw));

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.AreEqual(shield * 2f, result.AppliedAmount, TOLERANCE);
            Assert.AreEqual(_player.MaxHealth - (shield * 2f), _player.Health, TOLERANCE);
            Assert.AreEqual(0f, _player.Shield);
            Assert.AreEqual(0f, _rig.Combats[0].Shield);
        }

        [Test]
        public void Apply_DamageBelowShield_KeepsHealthAndCountsAsAppliedZero()
        {
            float shield = GrantFivePercent(NOW + SHIELD_SECONDS);
            float raw    = RawFor(shield * 0.5f);

            DamageResult result = _rig.Resolver.Apply(Request(1, raw));

            Assert.AreEqual(EDamageOutcome.Applied, result.Outcome);
            Assert.AreEqual(0f, result.AppliedAmount);
            Assert.IsFalse(result.IsLethal);
            Assert.IsFalse(result.CountsAsHit);
            Assert.AreEqual(_player.MaxHealth, _player.Health, TOLERANCE);
            Assert.AreEqual(shield * 0.5f, _player.Shield, TOLERANCE);
            Assert.AreEqual(shield * 0.5f, _rig.Combats[0].Shield, TOLERANCE);
        }

        [Test]
        public void Apply_AfterShieldExpires_DamagesHealthOnly()
        {
            float shield = GrantFivePercent(NOW + SHIELD_SECONDS);
            float raw    = RawFor(shield);
            _rig.Resolver.BeginTick(NOW + SHIELD_SECONDS + 1.0);

            DamageResult result = _rig.Resolver.Apply(Request(1, raw));

            Assert.AreEqual(shield, result.AppliedAmount, TOLERANCE);
            Assert.AreEqual(_player.MaxHealth - shield, _player.Health, TOLERANCE);
            Assert.AreEqual(0f, _player.Shield);
            Assert.AreEqual(0f, _rig.Combats[0].Shield);
        }

        [Test]
        public void Apply_LethalThroughShield_KillsAndClearsShield()
        {
            GrantFivePercent(NOW + SHIELD_SECONDS);

            DamageResult result = _rig.Resolver.Apply(Request(1, HUGE_DAMAGE));

            Assert.IsTrue(result.IsLethal);
            Assert.AreEqual(EParticipantLifeState.Dead, _player.LifeState);
            Assert.AreEqual(0f, _player.Shield);
            Assert.AreEqual(0f, _rig.Combats[0].Shield);
        }

        [Test]
        public void GrantShield_Twice_OverwritesInsteadOfStacking()
        {
            float shield = GrantFivePercent(NOW + SHIELD_SECONDS);
            _player.GrantShield(shield, NOW + SHIELD_SECONDS + 1.0);

            Assert.AreEqual(shield, _player.Shield, TOLERANCE);
            Assert.AreEqual(shield, _rig.Combats[0].Shield, TOLERANCE);
        }

        [Test]
        public void GrantShield_WhenDead_IsIgnored()
        {
            Assert.IsTrue(_player.KillByServer(NOW));

            _player.GrantShield(PlayerShieldRules.GetGrantAmount(_player.MaxHealth), NOW + SHIELD_SECONDS);

            Assert.AreEqual(0f, _player.Shield);
        }

        [Test]
        public void TickSkills_AfterExpiry_ClearsReplicatedShield()
        {
            GrantFivePercent(NOW + SHIELD_SECONDS);

            _player.TickSkills(0f, NOW + SHIELD_SECONDS, new PathRequestBudget(PATH_BUDGET));

            Assert.AreEqual(0f, _player.Shield);
            Assert.AreEqual(0f, _rig.Combats[0].Shield);
        }

        /****************************************
        *            Skill Server Tests
        ****************************************/

        [Test]
        public void MageknightQ_Resolve_GrantsFivePercentShieldForBarrierDuration()
        {
            SkillCatalogSO catalog = ScriptableObject.CreateInstance<SkillCatalogSO>();
            PlayerConfigSO config  = ScriptableObject.CreateInstance<PlayerConfigSO>();
            try
            {
                ShieldCaster owner = CastAndResolveQ(catalog, config, EPlayerClass.Mageknight, out SkillServer server);
                Assert.IsTrue(catalog.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_Q,
                    out SkillCatalogSO.SkillDefinition q));

                Assert.AreEqual(OWNER_HEALTH * PlayerShieldRules.MAGEKNIGHT_Q_SHIELD_RATE, owner.ShieldAmount, TOLERANCE);
                Assert.AreEqual(SKILL_START + q.Timeline.Windup + q.Effect.HitImmunityDuration, owner.ShieldUntil, TOLERANCE);
                server.Shutdown();
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ArcherQ_Resolve_GrantsNoShield()
        {
            SkillCatalogSO catalog = ScriptableObject.CreateInstance<SkillCatalogSO>();
            PlayerConfigSO config  = ScriptableObject.CreateInstance<PlayerConfigSO>();
            try
            {
                ShieldCaster owner = CastAndResolveQ(catalog, config, EPlayerClass.Archer, out SkillServer server);

                Assert.AreEqual(0f, owner.ShieldAmount);
                server.Shutdown();
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(config);
            }
        }

        /****************************************
        *              HUD Bar Tests
        ****************************************/

        [Test]
        public void Compute_HealthPlusShieldWithinMax_AppendsAfterHealth()
        {
            HudShieldBarRules.Compute(500, 50, 1000, out float health, out float shieldEnd);

            Assert.AreEqual(0.5f, health, TOLERANCE);
            Assert.AreEqual(0.55f, shieldEnd, TOLERANCE);
        }

        [Test]
        public void Compute_HealthPlusShieldOverMax_NormalizesBySum()
        {
            HudShieldBarRules.Compute(1000, 50, 1000, out float health, out float shieldEnd);

            Assert.AreEqual(1000f / 1050f, health, TOLERANCE);
            Assert.AreEqual(1f, shieldEnd, TOLERANCE);
        }

        [Test]
        public void Compute_NoMaxOrNegativeShield_StaysInBar()
        {
            HudShieldBarRules.Compute(10, 5, 0, out float noMaxHealth, out float noMaxShield);
            HudShieldBarRules.Compute(300, -20, 1000, out float health, out float shieldEnd);

            Assert.AreEqual(0f, noMaxHealth);
            Assert.AreEqual(0f, noMaxShield);
            Assert.AreEqual(0.3f, health, TOLERANCE);
            Assert.AreEqual(0.3f, shieldEnd, TOLERANCE);
        }

        [Test]
        public void SegmentApply_WithShield_CreatesWhiteSiblingAndHidesWhenEmpty()
        {
            var track = new GameObject("ShieldTrack", typeof(RectTransform));
            try
            {
                var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                var fill = (RectTransform)fillObject.transform;
                fill.SetParent(track.transform, false);
                RectTransform segment = null;

                HudShieldSegment.Apply(ref segment, fill, 0.5f, 0.55f);

                Assert.IsNotNull(segment);
                Assert.AreEqual(track.transform, segment.parent);
                Assert.AreEqual(fill.GetSiblingIndex() + 1, segment.GetSiblingIndex());
                Assert.IsTrue(segment.gameObject.activeSelf);
                Assert.AreEqual(0.5f, segment.anchorMin.x, TOLERANCE);
                Assert.AreEqual(0.55f, segment.anchorMax.x, TOLERANCE);
                Assert.AreEqual(Color.white, segment.GetComponent<Image>().color);

                HudShieldSegment.Apply(ref segment, fill, 0.5f, 0.5f);

                Assert.IsFalse(segment.gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(track);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private float GrantFivePercent(double until)
        {
            float shield = PlayerShieldRules.GetGrantAmount(_player.MaxHealth);
            Assert.Greater(shield, 0f);
            _player.GrantShield(shield, until);
            Assert.AreEqual(shield, _player.Shield, TOLERANCE);
            Assert.AreEqual(shield, _rig.Combats[0].Shield, TOLERANCE);
            return shield;
        }

        // 방어 감소를 거친 뒤 net만큼이 되는 원 피해, 방어 공식이 선형이라 비율로 되돌린다
        private float RawFor(float net)
        {
            PlayerConfigSO config = _rig.Config;
            float unit = DamageRules.ApplyDefense(1f, _player.Defense, config.DefenseReductionConstant, config.DamageReductionCap);
            Assert.Greater(unit, 0f);
            return net / unit;
        }

        private DamageRequest Request(int actionId, float rawDamage)
        {
            return new DamageRequest(ATTACKER_ID, _player.CombatId, actionId, actionId, rawDamage, EDamageFlags.None,
                Vector3.forward, Constants.Combat.BASIC_ATTACK_SOURCE_ID);
        }

        private static ShieldCaster CastAndResolveQ(SkillCatalogSO catalog, PlayerConfigSO config, EPlayerClass playerClass,
            out SkillServer server)
        {
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger   = new HitLedger(LEDGER_SIZE, LEDGER_AGE, ACTION_AGE);
            var core     = new DamageResolver(registry, ledger, config.DefenseReductionConstant, config.DamageReductionCap);
            var resolver = new SkillDamageResolver(core, registry, ledger);
            var owner    = new ShieldCaster(registry.IssueCombatId());
            Assert.IsTrue(registry.Register(owner));
            core.BeginTick(SKILL_START);

            server = new SkillServer(owner, playerClass, PARTICIPANT_ID, catalog, config, registry, resolver,
                new CombatLineOfSight(0, 0f), SKILL_START, null, new FakeNavigation());
            owner.Skills = server;

            Assert.IsTrue(catalog.TryGetSkillBySlot(playerClass, Constants.Combat.SKILL_SLOT_Q, out SkillCatalogSO.SkillDefinition q));
            Assert.IsTrue(server.TryCast(ESkillRequestKind.Regular, Constants.Combat.SKILL_SLOT_Q, Vector3.zero,
                FacingRules.EncodeYaw(Vector3.forward), SKILL_START, true, out ERejectReason reason), reason.ToString());
            Assert.AreEqual(0f, owner.ShieldAmount);

            double resolveAt = SKILL_START + q.Timeline.Windup;
            var budget = new PathRequestBudget(PATH_BUDGET);
            core.BeginTick(resolveAt);
            budget.BeginTick();
            server.Tick(resolveAt, q.Timeline.Windup, budget);
            return owner;
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

        private sealed class ShieldCaster : ISkillCombatant, IShieldTarget
        {
            public int                   CombatId        { get; }
            public ECombatFaction        Faction         => ECombatFaction.Player;
            public EParticipantLifeState LifeState       => EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; private set; }
            public Vector3               Forward         { get; private set; } = Vector3.forward;
            public float                 BodyRadius      => 0f;
            public float                 Health          => OWNER_HEALTH;
            public float                 MaxHealth       => OWNER_HEALTH;
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;
            public float                 AttackPower     => ATTACK_POWER;
            public SkillServer           Skills          { get; set; }
            public float                 ShieldAmount    { get; private set; }
            public double                ShieldUntil     { get; private set; }

            public ShieldCaster(int combatId) { CombatId = combatId; }

            public void SetSkillPosition(Vector3 position) { Position = position; }
            public void SetSkillFacing(Vector3 direction) { Forward = direction; }
            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime) { }

            public void GrantShield(float amount, double untilServerTime)
            {
                ShieldAmount = amount;
                ShieldUntil  = untilServerTime;
            }
        }
    }
}
