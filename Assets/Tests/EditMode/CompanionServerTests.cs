// System
using System.Collections.Generic;
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
    public class CompanionServerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  OWNER_HEALTH    = 1500f;
        private const float  OWNER_DEFENSE   = 100f;
        private const float  OWNER_ATTACK    = 100f;
        private const float  ENEMY_HEALTH    = 1000f;
        private const float  TOLERANCE       = 0.0001f;
        private const float  MELEE_INTERVAL  = 0.75f;
        private const float  RANGED_INTERVAL = 1f;
        private const float  RECALL_DISTANCE = 12f;
        private const double START_TIME      = 0.0;

        private SkillCatalogSO       _catalog;
        private CombatTargetRegistry _registry;
        private FakeCaster           _owner;
        private FakeResolver         _resolver;
        private FakeContext          _context;
        private CompanionServer      _companion;
        private PathRequestBudget    _budget;

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog  = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            _owner    = new FakeCaster(_registry.IssueCombatId());
            _registry.Register(_owner);
            _resolver  = new FakeResolver(_registry);
            _context   = new FakeContext();
            _budget    = new PathRequestBudget(Constants.Player.PATH_REQUESTS_PER_TICK);
            _companion = CreateCompanion(_registry, _resolver);
        }

        [TearDown]
        public void TearDown()
        {
            _companion.Stop();
            Object.DestroyImmediate(_catalog);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryReserve_FullRegistry_RejectsBeforeSpawn()
        {
            var registry = new CombatTargetRegistry(1);
            registry.IssueCombatId();
            registry.Register(_owner);
            var             resolver  = new FakeResolver(registry);
            CompanionServer companion = CreateCompanion(registry, resolver);

            LogAssert.Expect(LogType.Error, new Regex("상한"));
            Assert.IsFalse(companion.TryReserve());

            Assert.AreEqual(1, registry.Count);
            Assert.IsFalse(companion.IsActive);
        }

        [Test]
        public void TryReserve_InactiveSummon_RegistersNonLivingPlaceholderUntilStart()
        {
            Assert.IsTrue(_companion.TryReserve());
            int reservedId = _companion.CombatId;

            Assert.AreEqual(2, _registry.Count);
            Assert.IsFalse(_companion.IsActive);
            Assert.AreEqual(EParticipantLifeState.Dead, _companion.LifeState);
            Assert.AreEqual(1, _registry.CountLivingInRadius(Vector3.zero, _catalog.CompanionSearchRadius,
                ECombatFaction.Player, true));
            Assert.IsTrue(_registry.TryGet(reservedId, out ICombatTarget placeholder));
            Assert.AreSame(_companion, placeholder);
            Assert.IsTrue(_companion.TryReserve());
            Assert.AreEqual(2, _registry.Count);

            StartCompanion(0);

            Assert.IsTrue(_companion.IsActive);
            Assert.AreEqual(reservedId, _companion.CombatId);
            Assert.AreEqual(2, _registry.Count);
        }

        [Test]
        public void CancelReservation_NewSummon_ReleasesSlotWithoutSpawn()
        {
            Assert.IsTrue(_companion.TryReserve());
            int reservedId = _companion.CombatId;

            _companion.CancelReservation();
            _companion.CancelReservation();

            Assert.IsFalse(_companion.IsActive);
            Assert.IsFalse(_registry.TryGet(reservedId, out _));
            Assert.AreEqual(1, _registry.Count);
            Assert.IsTrue(_companion.TryReserve());
            Assert.AreNotEqual(reservedId, _companion.CombatId);
        }

        [Test]
        public void CancelReservation_ActiveReplacement_KeepsExistingCompanion()
        {
            StartCompanion(0);
            int    previousId     = _companion.CombatId;
            double previousExpiry = _companion.ExpiresAt;
            Assert.IsTrue(_companion.TryReserve());

            _companion.CancelReservation();

            Assert.IsTrue(_companion.IsActive);
            Assert.AreEqual(previousId, _companion.CombatId);
            Assert.AreEqual(previousExpiry, _companion.ExpiresAt);
            Assert.AreEqual(2, _registry.Count);
        }

        [Test]
        public void Stop_ReservedInactiveSummon_ReleasesSlot()
        {
            Assert.IsTrue(_companion.TryReserve());
            int reservedId = _companion.CombatId;

            _companion.Stop();

            Assert.IsFalse(_registry.TryGet(reservedId, out _));
            Assert.AreEqual(1, _registry.Count);
        }

        [Test]
        public void Start_HurtOwner_SnapshotsMaximumHealthAndDefenseWithoutHealingOwner()
        {
            _owner.Health = OWNER_HEALTH / 2f;
            StartCompanion(0);

            Assert.AreEqual(OWNER_HEALTH * _catalog.CompanionHealthRate, _companion.MaxHealth, TOLERANCE);
            Assert.AreEqual(_companion.MaxHealth, _companion.Health, TOLERANCE);
            Assert.AreEqual(OWNER_DEFENSE * _catalog.CompanionDefenseRate, _companion.Defense, TOLERANCE);
            Assert.AreEqual(OWNER_HEALTH / 2f, _owner.Health, TOLERANCE);
            Assert.AreEqual(_owner.CombatId, _companion.OwnerCombatId);
            Assert.AreEqual(_owner.Faction, _companion.Faction);
            Assert.IsTrue(_companion.IsSummon);
            Assert.AreEqual(START_TIME + GetSummon(0).Effect.Duration, _companion.ExpiresAt);
        }

        [Test]
        public void Start_FullRegistry_RejectsInactiveSummon()
        {
            var registry = new CombatTargetRegistry(1);
            registry.IssueCombatId();
            registry.Register(_owner);
            var             resolver  = new FakeResolver(registry);
            CompanionServer companion = CreateCompanion(registry, resolver);

            LogAssert.Expect(LogType.Error, new Regex("상한"));
            bool accepted = companion.Start(GetSummon(0), resolver.IssueInstanceId(), START_TIME);

            Assert.IsFalse(accepted);
            Assert.IsFalse(companion.IsActive);
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void Start_ActiveSummonAtRegistryCapacity_ReplacesOneEntityAndSnapshotsAgain()
        {
            var registry = new CombatTargetRegistry(2);
            registry.IssueCombatId();
            registry.Register(_owner);
            var             resolver  = new FakeResolver(registry);
            CompanionServer companion = CreateCompanion(registry, resolver);
            Assert.IsTrue(companion.Start(GetSummon(0), resolver.IssueInstanceId(), START_TIME));
            int previousId = companion.CombatId;
            companion.ApplyDamage(OWNER_ATTACK, false, START_TIME);
            _owner.MaxHealth = OWNER_HEALTH * 2f;

            Assert.IsTrue(companion.Start(GetSummon(0), resolver.IssueInstanceId(), MELEE_INTERVAL));

            Assert.AreEqual(2, registry.Count);
            Assert.AreNotEqual(previousId, companion.CombatId);
            Assert.IsFalse(registry.TryGet(previousId, out _));
            Assert.IsTrue(registry.TryGet(companion.CombatId, out ICombatTarget registered));
            Assert.AreSame(companion, registered);
            Assert.AreEqual(_owner.MaxHealth * _catalog.CompanionHealthRate, companion.Health, TOLERANCE);
            Assert.AreEqual(MELEE_INTERVAL + GetSummon(0).Effect.Duration, companion.ExpiresAt);
            companion.Stop();
        }

        [Test]
        public void Start_InvalidReplacement_KeepsExistingSummon()
        {
            StartCompanion(0);
            int    previousId     = _companion.CombatId;
            double previousExpiry = _companion.ExpiresAt;
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Warrior, 0, out SkillCatalogSO.SkillDefinition warriorSkill));

            Assert.IsFalse(_companion.Start(warriorSkill, _resolver.IssueInstanceId(), MELEE_INTERVAL));

            Assert.IsTrue(_companion.IsActive);
            Assert.AreEqual(previousId, _companion.CombatId);
            Assert.AreEqual(previousExpiry, _companion.ExpiresAt);
            Assert.IsTrue(_registry.TryGet(previousId, out _));
        }

        [Test]
        public void Tick_AtExpiry_UnregistersWithoutAttack()
        {
            StartCompanion(0);
            int combatId = _companion.CombatId;
            CreateEnemy(Vector3.forward);

            Tick(_companion.ExpiresAt);

            Assert.IsFalse(_companion.IsActive);
            Assert.IsFalse(_registry.TryGet(combatId, out _));
            Assert.AreEqual(0, _resolver.ApplyCalls);
            Assert.AreEqual(0, _context.HitCalls);
        }

        [Test]
        public void Tick_OwnerDead_RecallsWithoutChangingOwnerHealth()
        {
            StartCompanion(0);
            CreateEnemy(Vector3.forward);
            _owner.LifeState = EParticipantLifeState.Dead;
            _owner.Health    = 0f;

            Tick(MELEE_INTERVAL);

            Assert.IsFalse(_companion.IsActive);
            Assert.AreEqual(EParticipantLifeState.Dead, _companion.LifeState);
            Assert.AreEqual(0f, _owner.Health);
            Assert.AreEqual(0, _resolver.ApplyCalls);
        }

        [Test]
        public void ApplyDamage_Lethal_RemovesOnlyCompanion()
        {
            StartCompanion(0);
            int combatId = _companion.CombatId;

            _companion.ApplyDamage(_companion.Health, true, MELEE_INTERVAL);

            Assert.IsFalse(_companion.IsActive);
            Assert.AreEqual(0f, _companion.Health);
            Assert.AreEqual(OWNER_HEALTH, _owner.Health);
            Assert.AreEqual(EParticipantLifeState.Alive, _owner.LifeState);
            Assert.IsFalse(_registry.TryGet(combatId, out _));
        }

        [Test]
        public void Tick_MarkedEnemy_PrioritizesMarkAndAppliesItsBonus()
        {
            CreateEnemy(Vector3.forward / 2f);
            FakeDamageable marked = CreateEnemy(Vector3.forward);
            _context.MarkedTarget = marked;
            _context.MarkedBonus  = GetSummonBarrierBonus();
            StartCompanion(0);

            Tick(MELEE_INTERVAL);

            Assert.AreEqual(marked.CombatId, _companion.TargetId);
            Assert.AreEqual(marked.CombatId, _resolver.LastRequest.HitTargetId);
            Assert.AreEqual(OWNER_ATTACK * GetSummon(0).Effect.CompanionAttackCoefficient * (1f + _context.MarkedBonus),
                _resolver.LastRequest.RawDamage, TOLERANCE);
            Assert.AreEqual(1, _context.HitCalls);
        }

        [Test]
        public void Tick_DeadMarkedEnemy_SelectsLivingNearestEnemy()
        {
            FakeDamageable marked  = CreateEnemy(Vector3.forward / 2f);
            FakeDamageable nearest = CreateEnemy(Vector3.forward);
            marked.LifeState      = EParticipantLifeState.Dead;
            _context.MarkedTarget = marked;
            StartCompanion(0);

            Tick(MELEE_INTERVAL);

            Assert.AreEqual(nearest.CombatId, _companion.TargetId);
            Assert.AreEqual(nearest.CombatId, _resolver.LastRequest.HitTargetId);
        }

        [Test]
        public void Tick_EnemyOutsideOwnerSearchRadius_DoesNotAcquireOrAttack()
        {
            CreateEnemy(Vector3.forward * (_catalog.CompanionSearchRadius + RANGED_INTERVAL));
            StartCompanion(0);

            Tick(MELEE_INTERVAL);

            Assert.AreEqual(CombatIds.NONE, _companion.TargetId);
            Assert.AreEqual(0, _resolver.ApplyCalls);
        }

        [Test]
        public void Tick_OwnerAtRecallDistance_PrioritizesReturnAndSharesPathBudget()
        {
            CreateEnemy(Vector3.forward);
            StartCompanion(0);
            _owner.Position = Vector3.right * RECALL_DISTANCE;

            // 아직 BeginTick하지 않은 예산은 0이므로 내비 메시를 조회하지 않고 이동을 다음 틱으로 미룬다
            _companion.Tick(MELEE_INTERVAL, MELEE_INTERVAL, _budget);

            Assert.IsTrue(_companion.IsActive);
            Assert.AreEqual(CombatIds.NONE, _companion.TargetId);
            Assert.AreEqual(Vector3.zero, _companion.Position);
            Assert.AreEqual(0, _resolver.ApplyCalls);
            Assert.AreEqual(0, _budget.Remaining);
        }

        [Test]
        public void Tick_RangedMarkedEnemyBehindAnotherEnemy_HitsFirstEnemyOnly()
        {
            FakeDamageable blocker = CreateEnemy(Vector3.forward * 3f);
            FakeDamageable marked  = CreateEnemy(Vector3.forward * 7f);
            _context.MarkedTarget = marked;
            StartCompanion(1);

            Tick(RANGED_INTERVAL);

            Assert.AreEqual(0, _resolver.ApplyCalls);
            Tick(RANGED_INTERVAL + (3f / Constants.Combat.COMPANION_PROJECTILE_SPEED));

            Assert.AreEqual(marked.CombatId, _companion.TargetId);
            Assert.AreEqual(blocker.CombatId, _resolver.LastRequest.HitTargetId);
            Assert.AreEqual(1, blocker.DamageCalls);
            Assert.AreEqual(0, marked.DamageCalls);
            Assert.AreEqual(1, _context.HitCalls);
        }

        [Test]
        public void Tick_ProjectileInFlight_DefersDamageAndReadsAttackAtImpact()
        {
            FakeDamageable enemy = CreateEnemy(Vector3.forward * 3f);
            StartCompanion(1);
            Tick(RANGED_INTERVAL);
            Tick(RANGED_INTERVAL + (1f / Constants.Combat.COMPANION_PROJECTILE_SPEED));

            Assert.AreEqual(0, _resolver.ApplyCalls);
            Assert.AreEqual(ENEMY_HEALTH, enemy.Health);
            _owner.AttackPower = OWNER_ATTACK * 2f;
            int newerAttackId = _resolver.IssueInstanceId();

            Tick(RANGED_INTERVAL + (3f / Constants.Combat.COMPANION_PROJECTILE_SPEED));

            Assert.AreEqual(1, _resolver.ApplyCalls);
            Assert.AreEqual(_owner.AttackPower * GetSummon(1).Effect.CompanionAttackCoefficient,
                _resolver.LastRequest.RawDamage, TOLERANCE);
            Assert.Greater(_resolver.LastRequest.CastInstanceId, newerAttackId);
            Assert.Less(_resolver.LastRequest.ActionInstanceId, newerAttackId);
            Assert.AreEqual(1, _context.HitCalls);
        }

        [Test]
        public void Tick_OwnerDiesDuringProjectileFlight_RecallsWithoutImpact()
        {
            CreateEnemy(Vector3.forward * 3f);
            StartCompanion(1);
            Tick(RANGED_INTERVAL);
            _owner.LifeState = EParticipantLifeState.Dead;

            Tick(RANGED_INTERVAL + (3f / Constants.Combat.COMPANION_PROJECTILE_SPEED));

            Assert.IsFalse(_companion.IsActive);
            Assert.AreEqual(0, _resolver.ApplyCalls);
            Assert.AreEqual(0, _context.HitCalls);
        }

        [Test]
        public void Tick_OwnerAttackChanges_UsesCurrentAttackAndSummonSnapshotDefense()
        {
            CreateEnemy(Vector3.forward);
            StartCompanion(0);
            _owner.AttackPower = OWNER_ATTACK * 2f;
            _owner.Defense     = 0f;
            _owner.MaxHealth   = OWNER_HEALTH * 2f;

            Tick(MELEE_INTERVAL);

            Assert.AreEqual(_owner.AttackPower * GetSummon(0).Effect.CompanionAttackCoefficient,
                _resolver.LastRequest.RawDamage, TOLERANCE);
            Assert.AreEqual(OWNER_DEFENSE * _catalog.CompanionDefenseRate, _companion.Defense, TOLERANCE);
            Assert.AreEqual(OWNER_HEALTH * _catalog.CompanionHealthRate, _companion.MaxHealth, TOLERANCE);
        }

        [Test]
        public void Tick_RepeatedTimestampAndNextAttack_ReportsOncePerNewAction()
        {
            CreateEnemy(Vector3.forward);
            StartCompanion(0);

            Tick(MELEE_INTERVAL);
            int firstActionId = _resolver.LastRequest.ActionInstanceId;
            int firstCastId   = _resolver.LastRequest.CastInstanceId;
            int summonCastId  = _companion.SummonCastId;
            Tick(MELEE_INTERVAL);
            Assert.AreEqual(1, _resolver.ApplyCalls);
            Assert.AreEqual(1, _context.HitCalls);

            Tick(MELEE_INTERVAL * 2f);

            Assert.AreEqual(2, _resolver.ApplyCalls);
            Assert.AreEqual(2, _context.HitCalls);
            Assert.AreNotEqual(firstActionId, _resolver.LastRequest.ActionInstanceId);
            Assert.AreNotEqual(firstCastId, _resolver.LastRequest.CastInstanceId);
            Assert.AreEqual(summonCastId, _companion.SummonCastId);
            Assert.AreEqual(_companion.CombatId, _resolver.LastRequest.AttackerId);
        }

        [Test]
        public void Tick_IgnoredDamage_DoesNotReportResonanceHit()
        {
            CreateEnemy(Vector3.forward);
            StartCompanion(0);
            _resolver.IgnoreHits = true;

            Tick(MELEE_INTERVAL);

            Assert.AreEqual(1, _resolver.ApplyCalls);
            Assert.AreEqual(0, _context.HitCalls);
        }

        [Test]
        public void GetLivingPlayers_ActiveCompanion_ExcludesSummonFromPlayerCount()
        {
            StartCompanion(0);
            var players = new List<ICombatTarget>();

            Assert.AreEqual(1, _registry.GetLivingPlayers(players));
            Assert.AreSame(_owner, players[0]);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private CompanionServer CreateCompanion(CombatTargetRegistry registry, FakeResolver resolver)
        {
            return new CompanionServer(_owner, _catalog, registry, resolver,
                new CombatLineOfSight(0, Constants.Combat.LINE_OF_SIGHT_HEIGHT), _context);
        }

        private SkillCatalogSO.SkillDefinition GetSummon(int slot)
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Summoner, slot, out SkillCatalogSO.SkillDefinition skill));
            return skill;
        }

        private float GetSummonBarrierBonus()
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Summoner, 3, out SkillCatalogSO.SkillDefinition skill));
            return skill.Effect.AllyDamageBonus;
        }

        private void StartCompanion(int slot)
        {
            Assert.IsTrue(_companion.Start(GetSummon(slot), _resolver.IssueInstanceId(), START_TIME));
        }

        private FakeDamageable CreateEnemy(Vector3 position)
        {
            var enemy = new FakeDamageable(_registry.IssueCombatId(), ECombatFaction.Enemy, ENEMY_HEALTH, 0f);
            enemy.Position = position;
            Assert.IsTrue(_registry.Register(enemy));
            return enemy;
        }

        private void Tick(double now)
        {
            _budget.BeginTick();
            _companion.Tick(0f, now, _budget);
        }

        private sealed class FakeCaster : ISkillCaster
        {
            public int                   CombatId        { get; }
            public ECombatFaction        Faction         => ECombatFaction.Player;
            public EParticipantLifeState LifeState       { get; set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; set; } = Vector3.zero;
            public Vector3               Forward         { get; set; } = Vector3.forward;
            public float                 BodyRadius      => 0f;
            public float                 Health          { get; set; } = OWNER_HEALTH;
            public float                 MaxHealth       { get; set; } = OWNER_HEALTH;
            public float                 Defense         { get; set; } = OWNER_DEFENSE;
            public float                 AttackPower     { get; set; } = OWNER_ATTACK;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;

            public FakeCaster(int combatId)
            {
                CombatId = combatId;
            }

            public void SetSkillPosition(Vector3 position) { Position = position; }
            public void SetSkillFacing(Vector3 direction) { Forward = direction; }

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
                Health = Mathf.Max(0f, Health - appliedAmount);
                if (isLethal)
                {
                    LifeState = EParticipantLifeState.Dead;
                }
            }
        }

        private sealed class FakeResolver : IDamageResolver
        {
            private readonly CombatTargetRegistry _registry;
            private int _lastInstanceId = CombatIds.NONE;

            public bool          IgnoreHits  { get; set; }
            public int           ApplyCalls  { get; private set; }
            public DamageRequest LastRequest { get; private set; }

            public FakeResolver(CombatTargetRegistry registry)
            {
                _registry = registry;
            }

            public int IssueInstanceId()
            {
                ++_lastInstanceId;
                return _lastInstanceId;
            }

            public DamageResult Apply(in DamageRequest request)
            {
                ++ApplyCalls;
                LastRequest = request;
                if ((IgnoreHits) || (!_registry.TryGetDamageable(request.HitTargetId, out IDamageable target)))
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
            public int           HitCalls     { get; private set; }

            public bool TryGetMarkedTarget(Vector3 origin, out ICombatTarget target)
            {
                target = MarkedTarget;
                return target != null;
            }

            public float GetCompanionDamageBonus(ICombatTarget target)
            {
                return ReferenceEquals(target, MarkedTarget) ? MarkedBonus : 0f;
            }

            public void OnCompanionHit(in DamageRequest request, in DamageResult result)
            {
                ++HitCalls;
            }
        }
    }
}
