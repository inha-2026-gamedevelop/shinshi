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

        /****************************************
        *                Helpers
        ****************************************/

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
