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
    // 기술 투사체가 대상 몸체 반경을 보고, 틱 사이에 지나친 다가오는 적을 되짚어 맞히는지 시험한다
    public class SkillProjectileBodyTests
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
        private const float  BOSS_RADIUS    = 2f;
        private const double TICK_STEP      = 0.05;

        private const int SLOT_Q = Constants.Combat.SKILL_SLOT_Q;
        private const int SLOT_E = Constants.Combat.SKILL_SLOT_E;

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

        // 비행선에서 옆으로 2m 떨어진 중심은 반폭 0.25m 밖이지만 반경 2m 몸체는 닿는다
        [TestCase(BOSS_RADIUS, 1)]
        [TestCase(0f, 0)]
        public void Tick_Projectile_HitsBodyEdgeBesidePath(float bodyRadius, int expectedCalls)
        {
            CreateServer(EPlayerClass.Sorcerer, null);
            FakeCaster boss = Enemy(new Vector3(2f, 0f, 5f), bodyRadius);

            Cast(SLOT_Q);
            TickUntil(_server.ResolveAt + 1.0);

            Assert.AreEqual(expectedCalls, boss.DamageCalls);
            Assert.AreEqual(ATTACK_POWER * 1.6f * expectedCalls, TARGET_HEALTH - boss.Health, TOLERANCE);
        }

        // 투사체 끝(4m) 바로 앞 5m에 있던 적이 다음 틱 전에 3.5m로 달려와 끝을 지나쳐도 맞는다
        [Test]
        public void Tick_Projectile_HitsEnemyThatRanPastTipBetweenTicks()
        {
            CreateServer(EPlayerClass.Sorcerer, null);
            FakeCaster runner = Enemy(Vector3.forward * 5f, 0f);

            Cast(SLOT_Q);
            double launch = _server.ResolveAt;
            Tick(launch);
            Tick(launch + 0.25);
            Assert.AreEqual(0, runner.DamageCalls);

            runner.Position = Vector3.forward * 3.5f;
            Tick(launch + 0.35);

            Assert.AreEqual(1, runner.DamageCalls);
            Assert.AreEqual(ATTACK_POWER * 1.6f, TARGET_HEALTH - runner.Health, TOLERANCE);
        }

        // 쏜 자리 뒤는 되짚지 않는다
        [Test]
        public void Tick_Projectile_DoesNotLookBehindLaunchPoint()
        {
            CreateServer(EPlayerClass.Sorcerer, null);
            FakeCaster behind = Enemy(Vector3.forward * 9.5f, 0f);

            Cast(SLOT_Q);
            double launch = _server.ResolveAt;
            Tick(launch);
            Tick(launch + 0.05);
            behind.Position = Vector3.back * 0.5f;
            Tick(launch + 0.15);

            Assert.AreEqual(0, behind.DamageCalls);
        }

        // 관통 투사체가 큰 몸체를 여러 틱에 걸쳐 지나도 한 번만 치고, 관통 수만큼만 다음 적으로 이어 간다
        [Test]
        public void Tick_PierceShot_HitsLargeBodyOnceAndKeepsPierceCount()
        {
            Create(EPlayerClass.Summoner, SLOT_E, "T11-3A");
            FakeCaster first  = Enemy(Vector3.forward * 3f, 0f);
            FakeCaster boss   = Enemy(new Vector3(2f, 0f, 5f), BOSS_RADIUS);
            FakeCaster third  = Enemy(Vector3.forward * 7f, 0f);
            FakeCaster fourth = Enemy(Vector3.forward * 8.5f, 0f);

            Cast(SLOT_E);
            TickUntil(_server.ResolveAt + 1.0);

            Assert.AreEqual(1, first.DamageCalls);
            Assert.AreEqual(1, boss.DamageCalls);
            Assert.AreEqual(1, third.DamageCalls);
            Assert.AreEqual(0, fourth.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - 180f, first.Health, TOLERANCE);
            Assert.AreEqual(TARGET_HEALTH - 120f, boss.Health, TOLERANCE);
            Assert.AreEqual(TARGET_HEALTH - 120f, third.Health, TOLERANCE);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Create(EPlayerClass playerClass, int slot, string effectId)
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill));
            Assert.IsTrue(_talismans.TryGetEffect(effectId, out TalismanCatalogSO.TalismanEffectDefinition effect));
            var entries = new[] { new CombatTalismanEntry(effect.SkillId, (int)effect.Tier, effect.DefinitionId, LEVEL_ONE) };

            Assert.IsTrue(TalismanModifierRules.TryCompile(_talismans, skill, entries, out SkillModifiers modifiers, out string error),
                error);
            _modifiers.Skill = skill;
            _modifiers.Value = modifiers;
            CreateServer(playerClass, _modifiers);
        }

        private void CreateServer(EPlayerClass playerClass, ITalismanModifierResolver modifiers)
        {
            _server       = new SkillServer(_owner, playerClass, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                new CombatLineOfSight(0, 0f), START_TIME, modifiers, new FakeNavigation());
            _owner.Skills = _server;
        }

        private FakeCaster Enemy(Vector3 position, float bodyRadius)
        {
            var target = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = position, BodyRadius = bodyRadius };
            Assert.IsTrue(_registry.Register(target));
            return target;
        }

        private void Cast(int slot)
        {
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Regular, slot, Vector3.zero, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason), reason.ToString());
        }

        private void TickUntil(double end)
        {
            for (double now = START_TIME + TICK_STEP; now <= end; now += TICK_STEP)
            {
                Tick(now);
            }
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

        private sealed class FakeCaster : ISkillCombatant, ISkillControlTarget
        {
            public int                   CombatId          { get; }
            public ECombatFaction        Faction           { get; }
            public EParticipantLifeState LifeState         { get; set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon          => false;
            public int                   OwnerCombatId     => CombatIds.NONE;
            public Vector3               Position          { get; set; }
            public Vector3               Forward           { get; private set; } = Vector3.forward;
            public float                 BodyRadius        { get; set; }
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

            public void ApplyRoot(string sourceId, float duration, double serverTime) { }
        }
    }
}
