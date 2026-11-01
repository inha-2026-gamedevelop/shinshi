// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class ArcherElementSkillServerTests
    {
        // 궁수 속성형 서버 판정. 속성 부여, 적중 스택(같은 속성 +1, 다른 속성 +2, 상한 10), R 속성 폭발, 화상, 둔화를 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME     = 10.0;
        private const float  TOLERANCE      = 0.01f;
        private const float  ATTACK_POWER   = 100f;
        private const float  TARGET_HEALTH  = 100000f;
        private const int    PARTICIPANT_ID = 1;
        private const int    LEDGER_SIZE    = 256;
        private const double LEDGER_AGE     = 30.0;
        private const double ACTION_AGE     = 10.0;
        private const int    PATH_BUDGET    = 4;
        private const string ENEMY_CATALOG  = "Assets/09.Data/AI/EnemyCatalog.asset";

        private const int SLOT_Q = Constants.Combat.SKILL_SLOT_Q;
        private const int SLOT_W = Constants.Combat.SKILL_SLOT_W;
        private const int SLOT_E = Constants.Combat.SKILL_SLOT_E;
        private const int SLOT_R = Constants.Combat.SKILL_SLOT_R;

        private SkillCatalogSO       _catalog;
        private PlayerConfigSO       _player;
        private CombatTargetRegistry _registry;
        private DamageResolver       _core;
        private SkillDamageResolver  _resolver;
        private FakeArcher           _owner;
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
            _player    = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _registry  = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(LEDGER_SIZE, LEDGER_AGE, ACTION_AGE);
            _core      = new DamageResolver(_registry, ledger, _player.DefenseReductionConstant, _player.DamageReductionCap);
            _resolver  = new SkillDamageResolver(_core, _registry, ledger);
            _owner     = new FakeArcher(_registry.IssueCombatId(), ECombatFaction.Player);
            Assert.IsTrue(_registry.Register(_owner));
            _budget    = new PathRequestBudget(PATH_BUDGET);
            _lastTick  = START_TIME;
            _core.BeginTick(START_TIME);
            _server    = new SkillServer(_owner, EPlayerClass.Archer, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                new CombatLineOfSight(0, 0f), START_TIME, null, new FakeNavigation());
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

        [Test]
        public void Catalog_Archer_KeepsIdsAndMapsElementsAndBurst()
        {
            AssertSlot(SLOT_Q, "skill_archer_rapid_shot", EArcherElement.Ice);
            AssertSlot(SLOT_W, "skill_archer_retreat_shot", EArcherElement.Wind);
            AssertSlot(SLOT_E, "skill_archer_scatter_shot", EArcherElement.Fire);

            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Archer, SLOT_R, out SkillCatalogSO.SkillDefinition burst));
            Assert.AreEqual("skill_archer_piercing_shot", burst.DefinitionId);
            Assert.IsTrue(burst.IsSignature);
            Assert.AreEqual(ESkillAimMode.Point, burst.Area.AimMode);
            Assert.AreEqual(ESkillShape.Circle, burst.Area.Shape);
            Assert.AreEqual(_catalog.ArcherStackMax, burst.Effect.RequiredStacks);

            var errors = new System.Collections.Generic.List<string>();
            _catalog.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [TestCase(SLOT_Q, EArcherElement.Ice)]
        [TestCase(SLOT_W, EArcherElement.Wind)]
        [TestCase(SLOT_E, EArcherElement.Fire)]
        public void ElementSkill_Resolves_ChangesElementAndSpendsEnergy(int slot, EArcherElement element)
        {
            Assert.AreEqual(EArcherElement.None, _server.ArcherElement);

            Cast(slot, _owner.Position);
            Assert.AreEqual(EArcherElement.None, _server.ArcherElement, "준비 중에는 아직 바뀌지 않는다");
            Assert.Less(_server.Energy, _player.BaseMaxEnergy);

            Tick(START_TIME + 0.15);
            Assert.AreEqual(element, _server.ArcherElement);
        }

        [Test]
        public void ElementSkill_CancelledInWindup_KeepsPreviousElement()
        {
            Cast(SLOT_E, _owner.Position);
            _server.Cancel();
            Tick(START_TIME + 0.5);

            Assert.AreEqual(EArcherElement.None, _server.ArcherElement);
        }

        [Test]
        public void Wind_ShortensBasicIntervalAndSpeedsArrow_OthersDoNot()
        {
            Assert.AreEqual(1f, _server.BasicAttackIntervalScale, TOLERANCE);
            Assert.AreEqual(1f, _server.BasicProjectileSpeedScale, TOLERANCE);

            SetElement(SLOT_W);
            Assert.AreEqual(_catalog.WindAttackIntervalScale, _server.BasicAttackIntervalScale, TOLERANCE);
            Assert.AreEqual(_catalog.WindProjectileSpeedScale, _server.BasicProjectileSpeedScale, TOLERANCE);
            Assert.Less(_server.BasicAttackIntervalScale, 1f);
            Assert.Greater(_server.BasicProjectileSpeedScale, 1f);

            SetElement(SLOT_Q, START_TIME + 10.0);
            Assert.AreEqual(1f, _server.BasicAttackIntervalScale, TOLERANCE);
        }

        [Test]
        public void ArrowHit_SameElementAddsOne_SwitchAddsTwo_NoneAddsNothing()
        {
            FakeArcher enemy = Enemy(Vector3.forward * 3f);
            DamageResult hit = DamageResult.Applied(10f, false, false);

            _server.OnArrowHit(EArcherElement.None, enemy, in hit, START_TIME);
            Assert.AreEqual(0, _server.ClassStacks, "속성 없는 화살은 쌓지 않는다");

            _server.OnArrowHit(EArcherElement.Ice, enemy, in hit, START_TIME);
            Assert.AreEqual(1, _server.ClassStacks, "처음 맞힌 속성 화살은 같은 속성으로 친다");

            _server.OnArrowHit(EArcherElement.Ice, enemy, in hit, START_TIME);
            Assert.AreEqual(2, _server.ClassStacks);

            _server.OnArrowHit(EArcherElement.Fire, enemy, in hit, START_TIME);
            Assert.AreEqual(4, _server.ClassStacks);

            _server.OnArrowHit(EArcherElement.Wind, enemy, in hit, START_TIME);
            Assert.AreEqual(6, _server.ClassStacks);

            _server.OnArrowHit(EArcherElement.Wind, enemy, in hit, START_TIME);
            Assert.AreEqual(7, _server.ClassStacks);
        }

        [Test]
        public void ArrowHit_Missed_AddsNothingAndKeepsLastElement()
        {
            FakeArcher enemy = Enemy(Vector3.forward * 3f);
            DamageResult hit  = DamageResult.Applied(10f, false, false);
            DamageResult miss = DamageResult.Ignored(EDamageOutcome.DuplicateHit);

            _server.OnArrowHit(EArcherElement.Ice, enemy, in hit, START_TIME);
            _server.OnArrowHit(EArcherElement.Fire, enemy, in miss, START_TIME);
            Assert.AreEqual(1, _server.ClassStacks);

            // 빗나간 불 화살은 직전 속성을 바꾸지 않으므로 다음 불 적중도 전환이다
            _server.OnArrowHit(EArcherElement.Fire, enemy, in hit, START_TIME);
            Assert.AreEqual(3, _server.ClassStacks);
        }

        [Test]
        public void ArrowHit_StacksStopAtMax_AndDeathClearsThem()
        {
            FakeArcher enemy = Enemy(Vector3.forward * 3f);
            DamageResult hit = DamageResult.Applied(10f, false, false);
            for (int i = 0; i < 30; ++i)
            {
                _server.OnArrowHit((i % 2 == 0) ? EArcherElement.Ice : EArcherElement.Wind, enemy, in hit, START_TIME);
            }

            Assert.AreEqual(_catalog.ArcherStackMax, _server.ClassStacks);

            _server.OnOwnerDeath();
            Assert.AreEqual(0, _server.ClassStacks);

            // 사망 뒤 첫 적중은 전환 보너스가 없다
            _server.OnArrowHit(EArcherElement.Fire, enemy, in hit, START_TIME);
            Assert.AreEqual(1, _server.ClassStacks);
        }

        [Test]
        public void Burst_WithoutFullStacks_RejectsAndSpendsNothing()
        {
            Enemy(Vector3.forward * 5f);
            float energy = _server.Energy;

            bool cast = _server.TryCast(ESkillRequestKind.Regular, SLOT_R, Vector3.forward * 5f, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason);

            Assert.IsFalse(cast);
            Assert.AreEqual(ERejectReason.InsufficientEnergy, reason);
            Assert.AreEqual(energy, _server.Energy, TOLERANCE);
            Assert.AreEqual(0.0, _server.GetReadyAt(SLOT_R), TOLERANCE);
        }

        [Test]
        public void Burst_FullStacks_HitsEveryEnemyInRadiusAtPointAndSpendsAllStacks()
        {
            FillStacks();
            Vector3 point = Vector3.forward * 8f;
            FakeArcher center = Enemy(point);
            FakeArcher edge   = Enemy(point + (Vector3.right * 4.5f));
            FakeArcher far    = Enemy(point + (Vector3.right * 6f));
            FakeArcher self   = Enemy(Vector3.back * 1f);

            Cast(SLOT_R, point);
            Assert.AreEqual(0, _server.ClassStacks, "시전 확정 때 스택을 모두 쓴다");
            Tick(START_TIME + 0.5);
            Assert.AreEqual(0, center.DamageCalls, "준비 0.7초 전에는 터지지 않는다");

            Tick(START_TIME + 0.75);
            float expected = ATTACK_POWER * 5.6f;
            Assert.AreEqual(1, center.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - expected, center.Health, TOLERANCE);
            Assert.AreEqual(1, edge.DamageCalls);
            Assert.AreEqual(0, far.DamageCalls);
            Assert.AreEqual(0, self.DamageCalls);
        }

        [Test]
        public void Burst_PointOutOfRange_RejectsAndKeepsStacks()
        {
            FillStacks();

            bool cast = _server.TryCast(ESkillRequestKind.Regular, SLOT_R, Vector3.forward * 13f, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason);

            Assert.IsFalse(cast);
            Assert.AreEqual(ERejectReason.OutOfRange, reason);
            Assert.AreEqual(_catalog.ArcherStackMax, _server.ClassStacks);
        }

        [Test]
        public void FireHit_BurnsSixTicksOverThreeSeconds_AndRefreshKeepsOneBurn()
        {
            FakeArcher enemy = Enemy(Vector3.forward * 3f);
            DamageResult hit = DamageResult.Applied(10f, false, false);
            float tick = ATTACK_POWER * _catalog.BurnTickCoefficient;

            _server.OnArrowHit(EArcherElement.Fire, enemy, in hit, START_TIME);
            for (double t = 0.25; t <= 3.01; t += 0.25)
            {
                Tick(START_TIME + t);
            }

            Assert.AreEqual(6, enemy.DamageCalls);
            Assert.AreEqual(TARGET_HEALTH - (tick * 6f), enemy.Health, TOLERANCE);

            // 다시 맞으면 끝 시각만 늘어나고 두 번 타지 않는다
            FakeArcher other = Enemy(Vector3.forward * 4f);
            _server.OnArrowHit(EArcherElement.Fire, other, in hit, START_TIME + 4.0);
            Tick(START_TIME + 5.0);
            _server.OnArrowHit(EArcherElement.Fire, other, in hit, START_TIME + 5.0);
            for (double t = 5.25; t <= 9.01; t += 0.25)
            {
                Tick(START_TIME + t);
            }

            Assert.AreEqual(8, other.DamageCalls, "4초에 걸고 5초에 갱신하면 4.5초부터 8초까지 0.5초마다 8회");
        }

        [Test]
        public void FireBurn_TargetDies_StopsTicking()
        {
            FakeArcher enemy = Enemy(Vector3.forward * 3f);
            DamageResult hit = DamageResult.Applied(10f, false, false);
            _server.OnArrowHit(EArcherElement.Fire, enemy, in hit, START_TIME);
            Tick(START_TIME + 0.5);
            Assert.AreEqual(1, enemy.DamageCalls);

            enemy.LifeState = EParticipantLifeState.Dead;
            Tick(START_TIME + 3.0);
            Assert.AreEqual(1, enemy.DamageCalls);
        }

        [Test]
        public void IceHit_SlowsSlowableEnemy_ButNotTargetWithoutSlow()
        {
            FakeArcher enemy = Enemy(Vector3.forward * 3f);
            FakeArcher boss  = Enemy(Vector3.forward * 4f);
            boss.AcceptsSlow = false;
            DamageResult hit = DamageResult.Applied(10f, false, false);

            _server.OnArrowHit(EArcherElement.Ice, enemy, in hit, START_TIME);
            _server.OnArrowHit(EArcherElement.Ice, boss, in hit, START_TIME);

            Assert.AreEqual(_catalog.ChillSlowRate, enemy.SlowRate, TOLERANCE);
            Assert.AreEqual(_catalog.ChillDuration, enemy.SlowDuration, TOLERANCE);
            Assert.AreEqual(0f, boss.SlowRate, TOLERANCE);
        }

        [Test]
        public void EnemyServer_Slow_ScalesMoveSpeedUntilExpiry()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalogSO>(ENEMY_CATALOG);
            Assert.IsNotNull(catalog);
            EnemyCatalogSO.EnemyDefinition definition = catalog.FindByRole(EEnemyRole.Melee);
            Assert.IsNotNull(definition);
            var enemy = new EnemyServer(definition, 99, Vector3.zero, Vector3.forward, ATTACK_POWER, 0.5f);

            Assert.AreEqual(1f, enemy.GetMoveSpeedScale(START_TIME), TOLERANCE);
            enemy.ApplySlow(0.4f, 1.5f, START_TIME);
            Assert.AreEqual(0.6f, enemy.GetMoveSpeedScale(START_TIME + 1.0), TOLERANCE);

            // 약한 둔화가 겹쳐도 강한 감소율과 늦은 끝 시각을 쓴다
            enemy.ApplySlow(0.2f, 1.5f, START_TIME + 1.0);
            Assert.AreEqual(0.6f, enemy.GetMoveSpeedScale(START_TIME + 2.0), TOLERANCE);
            Assert.AreEqual(1f, enemy.GetMoveSpeedScale(START_TIME + 2.6), TOLERANCE);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void AssertSlot(int slot, string id, EArcherElement element)
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Archer, slot, out SkillCatalogSO.SkillDefinition skill));
            Assert.AreEqual(id, skill.DefinitionId);
            Assert.AreEqual(ESkillShape.Buff, skill.Area.Shape);
            Assert.AreEqual(element, skill.Effect.ArcherElement);
            Assert.IsFalse(skill.IsSignature);
        }

        private void FillStacks()
        {
            FakeArcher dummy = Enemy(new Vector3(50f, 0f, 50f));
            DamageResult hit = DamageResult.Applied(10f, false, false);
            for (int i = 0; i < _catalog.ArcherStackMax; ++i)
            {
                _server.OnArrowHit(EArcherElement.Ice, dummy, in hit, START_TIME);
            }

            Assert.AreEqual(_catalog.ArcherStackMax, _server.ClassStacks);
        }

        private void SetElement(int slot, double at = START_TIME)
        {
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Regular, slot, _owner.Position, FacingRules.EncodeYaw(Vector3.forward),
                at, true, out ERejectReason reason), reason.ToString());
            Tick(at + 0.15);
            Tick(at + 0.5);
        }

        private FakeArcher Enemy(Vector3 position)
        {
            var target = new FakeArcher(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = position };
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

        private sealed class FakeArcher : ISkillCombatant, ISkillSlowTarget
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
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;
            public float                 AttackPower     => ATTACK_POWER;
            public SkillServer           Skills          { get; set; }
            public int                   DamageCalls     { get; private set; }
            public bool                  AcceptsSlow     { get; set; } = true;
            public float                 SlowRate        { get; private set; }
            public float                 SlowDuration    { get; private set; }

            public FakeArcher(int combatId, ECombatFaction faction)
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

            // 보스처럼 둔화를 받지 않는 대상을 흉내 낸다
            public void ApplySlow(float rate, float duration, double serverTime)
            {
                if (!AcceptsSlow)
                {
                    return;
                }

                SlowRate     = rate;
                SlowDuration = duration;
            }
        }
    }
}
