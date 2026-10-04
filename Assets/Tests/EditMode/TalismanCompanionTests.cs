// System
using System.Collections.Generic;

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
    public class TalismanCompanionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  OWNER_HEALTH  = 1500f;
        private const float  OWNER_ATTACK  = 100f;
        private const float  ENEMY_HEALTH  = 1000f;
        private const float  TOLERANCE     = 0.001f;
        private const double START_TIME    = 0.0;
        private const int    LEVEL_ONE     = 1;
        private const int    MELEE_SLOT    = 0;
        private const int    RANGED_SLOT   = 1;
        private const float  BARRIER_BONUS = 0.2f;

        private SkillCatalogSO       _catalog;
        private TalismanCatalogSO    _talismans;
        private CombatTargetRegistry _registry;
        private FakeOwner            _owner;
        private RecordingResolver    _resolver;
        private FakeContext          _context;
        private CompanionServer      _companion;
        private PathRequestBudget    _budget;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog   = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _talismans = ScriptableObject.CreateInstance<TalismanCatalogSO>();
            _registry  = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            _owner     = new FakeOwner(_registry.IssueCombatId());
            _registry.Register(_owner);
            _resolver  = new RecordingResolver(_registry);
            _context   = new FakeContext();
            _budget    = new PathRequestBudget(Constants.Player.PATH_REQUESTS_PER_TICK);
            _companion = new CompanionServer(_owner, _catalog, _registry, _resolver,
                new CombatLineOfSight(0, Constants.Combat.LINE_OF_SIGHT_HEIGHT), _context);
        }

        [TearDown]
        public void TearDown()
        {
            _companion.Stop();
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_talismans);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Start_DurationTalisman_ExtendsExpiry()
        {
            Start(MELEE_SLOT, "T09-1A");

            Assert.AreEqual(START_TIME + 24.0, _companion.ExpiresAt, TOLERANCE);
        }

        [Test]
        public void Melee_FollowUpStrike_HitsSameTargetAgainWithSameCast()
        {
            FakeDamageable enemy = Enemy(Vector3.forward);
            Start(MELEE_SLOT, "T09-3A");

            Tick(0.75);
            Tick(0.88);

            Assert.AreEqual(2, enemy.DamageCalls);
            Assert.AreEqual(ENEMY_HEALTH - 35f - 15f, enemy.Health, TOLERANCE);
            Assert.AreEqual(_resolver.Requests[0].CastInstanceId, _resolver.Requests[1].CastInstanceId);
            Assert.AreNotEqual(_resolver.Requests[0].ActionInstanceId, _resolver.Requests[1].ActionInstanceId);
        }

        [Test]
        public void Melee_CircleSweep_HitsAllNearbyEnemiesInOneAction()
        {
            FakeDamageable front  = Enemy(Vector3.forward);
            FakeDamageable behind = Enemy(Vector3.back * 2f);
            Start(MELEE_SLOT, "T09-3B");

            Tick(0.75);

            Assert.AreEqual(ENEMY_HEALTH - 30f, front.Health, TOLERANCE);
            Assert.AreEqual(ENEMY_HEALTH - 30f, behind.Health, TOLERANCE);
            Assert.AreEqual(_resolver.Requests[0].ActionInstanceId, _resolver.Requests[1].ActionInstanceId);
            Assert.AreEqual(_resolver.Requests[0].CastInstanceId, _resolver.Requests[1].CastInstanceId);
        }

        [Test]
        public void Melee_MarkedTargetTalisman_AddsToBarrierBonusOnly()
        {
            FakeDamageable enemy = Enemy(Vector3.forward);
            _context.MarkedTarget = enemy;
            _context.MarkedBonus  = BARRIER_BONUS;
            Start(MELEE_SLOT, "T09-2B");

            Tick(0.75);

            Assert.AreEqual(OWNER_ATTACK * 0.35f * (1f + BARRIER_BONUS + 0.1f), _resolver.Requests[0].RawDamage, TOLERANCE);
        }

        [Test]
        public void Melee_AttackIntervalTalisman_AttacksSooner()
        {
            Enemy(Vector3.forward);
            Start(MELEE_SLOT, "T09-2A");

            Tick(0.68);

            Assert.AreEqual(1, _resolver.Requests.Count);
        }

        [Test]
        public void Ranged_SplitTargets_ShootsTwoDifferentEnemiesWithOneCast()
        {
            FakeDamageable near = Enemy(Vector3.forward * 3f);
            FakeDamageable far  = Enemy(Vector3.right * 4f);
            Start(RANGED_SLOT, "T10-3A");

            Tick(1.0);
            Tick(2.0);

            Assert.AreEqual(ENEMY_HEALTH - 35f, near.Health, TOLERANCE);
            Assert.AreEqual(ENEMY_HEALTH - 35f, far.Health, TOLERANCE);
            Assert.AreEqual(_resolver.Requests[0].CastInstanceId, _resolver.Requests[1].CastInstanceId);
        }

        [Test]
        public void Ranged_SplitTargetsSingleEnemy_ShootsOnce()
        {
            FakeDamageable only = Enemy(Vector3.forward * 3f);
            Start(RANGED_SLOT, "T10-3A");

            Tick(1.0);
            Tick(2.0);

            Assert.AreEqual(1, only.DamageCalls);
            Assert.AreEqual(1, _resolver.Requests.Count);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Start(int slot, string effectId)
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Summoner, slot, out SkillCatalogSO.SkillDefinition skill));
            Assert.IsTrue(_talismans.TryGetEffect(effectId, out TalismanCatalogSO.TalismanEffectDefinition effect));
            var entries = new[] { new CombatTalismanEntry(effect.SkillId, (int)effect.Tier, effectId, LEVEL_ONE) };
            Assert.IsTrue(TalismanModifierRules.TryCompile(_talismans, skill, entries, out SkillModifiers modifiers, out string error),
                error);
            Assert.IsTrue(_companion.Start(skill, _resolver.IssueInstanceId(), START_TIME, in modifiers));
        }

        private FakeDamageable Enemy(Vector3 position)
        {
            var enemy = new FakeDamageable(_registry.IssueCombatId(), ECombatFaction.Enemy, ENEMY_HEALTH, 0f) { Position = position };
            Assert.IsTrue(_registry.Register(enemy));
            return enemy;
        }

        private void Tick(double now)
        {
            _budget.BeginTick();
            _companion.Tick(0f, now, _budget);
        }

        private sealed class FakeOwner : ISkillCaster
        {
            public int                   CombatId        { get; }
            public ECombatFaction        Faction         => ECombatFaction.Player;
            public EParticipantLifeState LifeState       => EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        => Vector3.zero;
            public Vector3               Forward         => Vector3.forward;
            public float                 BodyRadius      => 0f;
            public float                 Health          => OWNER_HEALTH;
            public float                 MaxHealth       => OWNER_HEALTH;
            public float                 Defense         => 0f;
            public float                 AttackPower     => OWNER_ATTACK;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;

            public FakeOwner(int combatId) { CombatId = combatId; }

            public void SetSkillPosition(Vector3 position) { }
            public void SetSkillFacing(Vector3 direction) { }
            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime) { }
        }

        private sealed class RecordingResolver : IDamageResolver
        {
            private readonly CombatTargetRegistry _registry;
            private int _lastInstanceId = CombatIds.NONE;

            public List<DamageRequest> Requests { get; } = new List<DamageRequest>();

            public RecordingResolver(CombatTargetRegistry registry) { _registry = registry; }

            public int IssueInstanceId()
            {
                ++_lastInstanceId;
                return _lastInstanceId;
            }

            public DamageResult Apply(in DamageRequest request)
            {
                Requests.Add(request);
                if (!_registry.TryGetDamageable(request.HitTargetId, out IDamageable target))
                {
                    return DamageResult.Ignored(EDamageOutcome.TargetNotFound);
                }

                float amount = Mathf.Min(target.Health, request.RawDamage);
                bool  lethal = amount >= target.Health;
                target.ApplyDamage(amount, lethal, START_TIME);
                return DamageResult.Applied(amount, lethal, false);
            }
        }

        private sealed class FakeContext : ICompanionContext
        {
            public ICombatTarget MarkedTarget { get; set; }
            public float         MarkedBonus  { get; set; }

            public bool TryGetMarkedTarget(Vector3 origin, out ICombatTarget target)
            {
                target = MarkedTarget;
                return target != null;
            }

            public float GetCompanionDamageBonus(ICombatTarget target)
            {
                return ReferenceEquals(target, MarkedTarget) ? MarkedBonus : 0f;
            }

            public void OnCompanionHit(in DamageRequest request, in DamageResult result) { }
        }
    }
}
