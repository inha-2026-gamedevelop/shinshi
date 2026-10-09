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

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class ThreeClassSkillServerTests
    {
        // 검사, 마검사, 궁수의 서버 판정. 특히 궁수 관통 계열의 계수, 대상 수, 벽 우선, 순차 발사 취소를 시험한다

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
        private const int    LAYER_MASK     = 1;
        private const float  LOS_HEIGHT     = 0.5f;

        private const int SLOT_Q = Constants.Combat.SKILL_SLOT_Q;
        private const int SLOT_W = Constants.Combat.SKILL_SLOT_W;
        private const int SLOT_E = Constants.Combat.SKILL_SLOT_E;
        private const int SLOT_R = Constants.Combat.SKILL_SLOT_R;

        // 물리 시험은 다른 개체와 겹치지 않게 멀리 떨어뜨린 자리에서 한다
        private static readonly Vector3 FAR_BASE = new Vector3(1000f, 0f, 1000f);

        private readonly List<GameObject> _objects = new List<GameObject>();

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
        private CombatLineOfSight    _lineOfSight;
        private double               _lastTick;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog     = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _talismans   = ScriptableObject.CreateInstance<TalismanCatalogSO>();
            _player      = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _registry    = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger   = new HitLedger(LEDGER_SIZE, LEDGER_AGE, ACTION_AGE);
            _core        = new DamageResolver(_registry, ledger, _player.DefenseReductionConstant, _player.DamageReductionCap);
            _resolver    = new SkillDamageResolver(_core, _registry, ledger);
            _owner       = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Player);
            Assert.IsTrue(_registry.Register(_owner));
            _modifiers   = new FixedModifiers();
            _budget      = new PathRequestBudget(PATH_BUDGET);
            _lineOfSight = new CombatLineOfSight(0, 0f);
            _lastTick    = START_TIME;
            _core.BeginTick(START_TIME);
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Shutdown();
            for (int i = 0; i < _objects.Count; ++i)
            {
                Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_talismans);
            Object.DestroyImmediate(_player);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(EPlayerClass.Swordsman)]
        [TestCase(EPlayerClass.Mageknight)]
        [TestCase(EPlayerClass.Archer)]
        public void Catalog_NewClass_HasFourSkillsInSlotOrder(EPlayerClass playerClass)
        {
            for (int slot = 0; slot < Constants.Combat.SKILLS_PER_CLASS; ++slot)
            {
                Assert.IsTrue(_catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill),
                    $"{playerClass} 슬롯 {slot}");
                Assert.AreEqual(playerClass, skill.PlayerClass);
                Assert.AreEqual(slot, skill.Slot);
            }
        }

        [TestCase(EPlayerClass.Swordsman, SLOT_Q)]
        [TestCase(EPlayerClass.Swordsman, SLOT_W)]
        [TestCase(EPlayerClass.Swordsman, SLOT_E)]
        [TestCase(EPlayerClass.Swordsman, SLOT_R)]
        [TestCase(EPlayerClass.Mageknight, SLOT_Q)]
        [TestCase(EPlayerClass.Mageknight, SLOT_W)]
        [TestCase(EPlayerClass.Mageknight, SLOT_E)]
        [TestCase(EPlayerClass.Mageknight, SLOT_R)]
        [TestCase(EPlayerClass.Archer, SLOT_Q)]
        [TestCase(EPlayerClass.Archer, SLOT_W)]
        [TestCase(EPlayerClass.Archer, SLOT_E)]
        [TestCase(EPlayerClass.Archer, SLOT_R)]
        public void TryCast_PlainNewClassSkill_SpendsEnergyAndStartsCooldown(EPlayerClass playerClass, int slot)
        {
            CreatePlain(playerClass);
            Enemy(Vector3.forward * 2f);

            Cast(slot, Vector3.forward * 2f);

            Assert.Less(_server.Energy, _player.BaseMaxEnergy);
            Assert.Greater(_server.GetReadyAt(slot), START_TIME + 1.0);
        }

        [Test]
        public void BasicAttack_NewClasses_MatchDesignedSpecs()
        {
            PlayerConfigSO.BasicAttackSpec sword = _player.GetBasicAttack(EPlayerClass.Swordsman);
            Assert.AreEqual(1.0f, sword.DamageCoefficient, TOLERANCE);
            Assert.AreEqual(0.9f, sword.Interval, TOLERANCE);
            Assert.AreEqual(3f, sword.Range, TOLERANCE);
            Assert.AreEqual(90f, sword.ArcAngle, TOLERANCE);
            Assert.AreEqual(0.2f, sword.Windup, TOLERANCE);

            PlayerConfigSO.BasicAttackSpec mage = _player.GetBasicAttack(EPlayerClass.Mageknight);
            Assert.AreEqual(0.9f, mage.DamageCoefficient, TOLERANCE);
            Assert.AreEqual(0.85f, mage.Interval, TOLERANCE);
            Assert.AreEqual(2.5f, mage.Range, TOLERANCE);
            Assert.AreEqual(90f, mage.ArcAngle, TOLERANCE);

            PlayerConfigSO.BasicAttackSpec bow = _player.GetBasicAttack(EPlayerClass.Archer);
            Assert.AreEqual(0.9f, bow.DamageCoefficient, TOLERANCE);
            Assert.AreEqual(0.95f, bow.Interval, TOLERANCE);
            Assert.AreEqual(10f, bow.Range, TOLERANCE);
            Assert.AreEqual(0f, bow.ArcAngle, TOLERANCE);
            Assert.AreEqual(24f, bow.ProjectileSpeed, TOLERANCE);
            Assert.AreEqual(0.3f, bow.ProjectileWidth, TOLERANCE);
            Assert.AreEqual(0.35f, bow.Windup, TOLERANCE);
            Assert.IsTrue(bow.CancelOnMove);
        }

        [Test]
        public void BasicAttack_LegacyClasses_AreUnchanged()
        {
            Assert.AreEqual(1.0f, _player.GetBasicAttack(EPlayerClass.Warrior).DamageCoefficient, TOLERANCE);
            Assert.AreEqual(0.7f, _player.GetBasicAttack(EPlayerClass.Sorcerer).DamageCoefficient, TOLERANCE);
            Assert.AreEqual(0.6f, _player.GetBasicAttack(EPlayerClass.Summoner).DamageCoefficient, TOLERANCE);
        }

        [Test]
        public void PiercingShot_Default_EveryHitDealsFullCoefficientUpToFourTargets()
        {
            CreatePlain(EPlayerClass.Archer);
            FakeCaster first  = Enemy(Vector3.forward * 3f);
            FakeCaster second = Enemy(Vector3.forward * 5f);
            FakeCaster third  = Enemy(Vector3.forward * 7f);
            FakeCaster fourth = Enemy(Vector3.forward * 9f);
            FakeCaster fifth  = Enemy(Vector3.forward * 11f);

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.9);
            Tick(START_TIME + 1.4);

            AssertHitOnce(first, 320f);
            AssertHitOnce(second, 320f);
            AssertHitOnce(third, 320f);
            AssertHitOnce(fourth, 320f);
            Assert.AreEqual(0, fifth.DamageCalls);
        }

        [Test]
        public void PiercingShot_PierceExtend_HitsSixTargetsWithSameDamage()
        {
            Create(EPlayerClass.Archer, SLOT_E, "T23-3A");
            var enemies = new FakeCaster[7];
            for (int i = 0; i < 6; ++i)
            {
                enemies[i] = Enemy(Vector3.forward * (2f + (i * 2f)));
            }

            enemies[6] = Enemy(Vector3.forward * 13.5f);

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.9);
            Tick(START_TIME + 1.4);

            float expected = TARGET_HEALTH - enemies[0].Health;
            Assert.Greater(expected, 0f);
            for (int i = 0; i < 6; ++i)
            {
                Assert.AreEqual(1, enemies[i].DamageCalls, $"대상 {i}");
                Assert.AreEqual(expected, TARGET_HEALTH - enemies[i].Health, TOLERANCE, $"대상 {i}");
            }

            Assert.AreEqual(0, enemies[6].DamageCalls);
        }

        [Test]
        public void PiercingShot_SideBySideTargets_DoNotBurnPierceSlots()
        {
            CreatePlain(EPlayerClass.Archer);
            FakeCaster a = Enemy(new Vector3(-0.1f, 0f, 4f));
            FakeCaster b = Enemy(new Vector3(0.1f, 0f, 4f));
            FakeCaster c = Enemy(Vector3.forward * 6f);
            FakeCaster d = Enemy(Vector3.forward * 8f);
            FakeCaster e = Enemy(Vector3.forward * 10f);

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.9);
            Tick(START_TIME + 1.4);

            AssertHitOnce(a, 320f);
            AssertHitOnce(b, 320f);
            AssertHitOnce(c, 320f);
            AssertHitOnce(d, 320f);
            Assert.AreEqual(0, e.DamageCalls);
        }

        [Test]
        public void PiercingShot_FollowUpArrow_HitsSameEnemyAgain()
        {
            Create(EPlayerClass.Archer, SLOT_E, "T23-3B");
            FakeCaster enemy = Enemy(Vector3.forward * 3f);

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.9);
            Tick(START_TIME + 1.6);

            Assert.AreEqual(2, enemy.DamageCalls);
        }

        [Test]
        public void PiercingShot_CancelAfterRelease_KeepsFlownArrowButDropsFollowUp()
        {
            Create(EPlayerClass.Archer, SLOT_E, "T23-3B");
            FakeCaster enemy = Enemy(Vector3.forward * 3f);

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.9);
            Tick(START_TIME + 0.95);
            _server.Cancel();
            Tick(START_TIME + 2.0);

            Assert.AreEqual(1, enemy.DamageCalls);
        }

        [Test]
        public void ScatterShot_AdjacentArrowsOnSameEnemy_DealOneHit()
        {
            CreatePlain(EPlayerClass.Archer);
            FakeCaster close = Enemy(Vector3.forward * 0.6f);

            Cast(SLOT_R, Vector3.zero);
            Tick(START_TIME + 0.45);
            Tick(START_TIME + 1.0);

            AssertHitOnce(close, 160f);
        }

        [Test]
        public void ScatterShot_PierceExtend_EachArrowPiercesTwoTargetsAndNoMore()
        {
            Create(EPlayerClass.Archer, SLOT_R, "T24-3A");
            FakeCaster close = Enemy(Vector3.forward * 0.6f);
            FakeCaster mid   = Enemy(Vector3.forward * 3f);
            FakeCaster far   = Enemy(Vector3.forward * 5f);

            Cast(SLOT_R, Vector3.zero);
            Tick(START_TIME + 0.45);
            Tick(START_TIME + 1.0);

            Assert.AreEqual(1, close.DamageCalls);
            Assert.AreEqual(1, mid.DamageCalls);
            Assert.AreEqual(0, far.DamageCalls);
        }

        [Test]
        public void PiercingShot_WallInFront_BlocksTargetsAtAndBeyondWall()
        {
            UseWall(FAR_BASE + (Vector3.forward * 5.1f), new Vector3(10f, 2f, 0.2f));
            CreatePlain(EPlayerClass.Archer);
            _owner.Position = FAR_BASE;
            FakeCaster before = Enemy(FAR_BASE + (Vector3.forward * 4.9f));
            FakeCaster atWall = Enemy(FAR_BASE + (Vector3.forward * 5.0f));
            FakeCaster behind = Enemy(FAR_BASE + (Vector3.forward * 7f));

            Cast(SLOT_E, FAR_BASE);
            Tick(START_TIME + 0.9);
            Tick(START_TIME + 1.4);

            Assert.AreEqual(1, before.DamageCalls);
            Assert.AreEqual(0, atWall.DamageCalls);
            Assert.AreEqual(0, behind.DamageCalls);
        }

        [Test]
        public void RapidShot_Default_FiresThreeSequentialHits()
        {
            CreatePlain(EPlayerClass.Archer);
            FakeCaster enemy = Enemy(Vector3.forward * 3f);

            Cast(SLOT_Q, Vector3.zero);
            Tick(START_TIME + 0.35);
            Tick(START_TIME + 0.5);
            Tick(START_TIME + 0.75);
            Tick(START_TIME + 1.0);
            Tick(START_TIME + 1.3);

            Assert.AreEqual(3, enemy.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - 240f, enemy.Health, TOLERANCE);
        }

        [Test]
        public void RapidShot_CancelBetweenShots_DropsRemainingVolley()
        {
            CreatePlain(EPlayerClass.Archer);
            FakeCaster enemy = Enemy(Vector3.forward * 3f);

            Cast(SLOT_Q, Vector3.zero);
            Tick(START_TIME + 0.35);
            Tick(START_TIME + 0.5);
            Assert.AreEqual(1, enemy.DamageCalls);

            _server.Cancel();
            Tick(START_TIME + 1.5);

            Assert.AreEqual(1, enemy.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - 80f, enemy.Health, TOLERANCE);
        }

        [Test]
        public void RapidShot_FollowUpArrow_FiresAfterLastShotWhenNotCanceled()
        {
            Create(EPlayerClass.Archer, SLOT_Q, "T21-3A");
            FakeCaster enemy = Enemy(Vector3.forward * 3f);

            Cast(SLOT_Q, Vector3.zero);
            for (double t = 0.35; t <= 2.0; t += 0.15)
            {
                Tick(START_TIME + t);
            }

            Assert.AreEqual(4, enemy.DamageCalls);
        }

        [Test]
        public void RapidShot_FollowUpArrow_IsDroppedWhenOwnerDies()
        {
            Create(EPlayerClass.Archer, SLOT_Q, "T21-3A");
            FakeCaster enemy = Enemy(Vector3.forward * 3f);

            Cast(SLOT_Q, Vector3.zero);
            Tick(START_TIME + 0.35);
            Tick(START_TIME + 0.5);
            _owner.LifeState = EParticipantLifeState.Dead;
            Tick(START_TIME + 2.0);

            Assert.AreEqual(1, enemy.DamageCalls);
        }

        [Test]
        public void RetreatShot_MovesOwnerThreeMetersBackward()
        {
            CreatePlain(EPlayerClass.Archer);
            Enemy(Vector3.forward * 4f);

            Cast(SLOT_W, Vector3.zero);
            Tick(START_TIME + 0.15);
            Tick(START_TIME + 0.6);

            Assert.AreEqual(-3f, _owner.Position.z, TOLERANCE);
            Assert.AreEqual(0f, _owner.Position.x, TOLERANCE);
        }

        [Test]
        public void RetreatShot_EnemyBehind_StopsBeforeBody()
        {
            CreatePlain(EPlayerClass.Archer);
            Enemy(new Vector3(0f, 0f, -1.5f));

            Cast(SLOT_W, Vector3.zero);
            Tick(START_TIME + 0.15);
            Tick(START_TIME + 0.6);

            Assert.Greater(_owner.Position.z, -1.0f);
        }

        [Test]
        public void RetreatShot_WallBehind_ClampsRetreat()
        {
            UseWall(FAR_BASE + (Vector3.back * 1.6f), new Vector3(10f, 2f, 0.2f));
            CreatePlain(EPlayerClass.Archer);
            _owner.Position = FAR_BASE;

            Cast(SLOT_W, FAR_BASE);
            Tick(START_TIME + 0.15);
            Tick(START_TIME + 0.6);

            float retreated = FAR_BASE.z - _owner.Position.z;
            Assert.Greater(retreated, 1.0f);
            Assert.LessOrEqual(retreated, 1.51f);
        }

        [Test]
        public void SealSlash_Boss_RootIsCappedToOneSecondWhileNormalEnemyGetsTwo()
        {
            CreatePlain(EPlayerClass.Mageknight);
            FakeCaster normal = Enemy(new Vector3(-0.5f, 0f, 2f));
            FakeCaster boss   = Enemy(new Vector3(0.5f, 0f, 2f));
            boss.IsBoss = true;

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.31);

            Assert.AreEqual(2f, normal.RootDuration, TOLERANCE);
            Assert.AreEqual(Constants.Combat.BOSS_ROOT_MAX_SECONDS, boss.RootDuration, TOLERANCE);
        }

        [Test]
        public void SealSlash_PullRoot_PullsNormalEnemyButNotBoss()
        {
            Create(EPlayerClass.Mageknight, SLOT_E, "T19-3A");
            FakeCaster normal = Enemy(new Vector3(-0.3f, 0f, 2.9f));
            FakeCaster boss   = Enemy(new Vector3(0.3f, 0f, 2.9f));
            boss.IsBoss = true;

            Cast(SLOT_E, Vector3.zero);
            Tick(START_TIME + 0.31);

            Assert.Less(normal.Position.z, 2.9f - 0.5f);
            Assert.AreEqual(2.9f, boss.Position.z, TOLERANCE);
            Assert.Greater(boss.RootDuration, 0f);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void AssertHitOnce(FakeCaster target, float damage)
        {
            Assert.AreEqual(1, target.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - damage, target.Health, TOLERANCE);
        }

        private void UseWall(Vector3 center, Vector3 size)
        {
            var wall = new GameObject("TestWall");
            _objects.Add(wall);
            wall.layer = 0;
            wall.transform.position = center + (Vector3.up * LOS_HEIGHT);
            wall.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
            _lineOfSight = new CombatLineOfSight(LAYER_MASK, LOS_HEIGHT);
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
            _server = new SkillServer(_owner, playerClass, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                _lineOfSight, START_TIME, modifiers, new FakeNavigation());
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
            public bool                  IsBoss            { get; set; }
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
