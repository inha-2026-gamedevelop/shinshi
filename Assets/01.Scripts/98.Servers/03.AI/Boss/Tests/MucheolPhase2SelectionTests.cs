// System
using System;
using System.Collections.Generic;

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

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 무철 2페이즈 팩토리, 개발 카탈로그, 두뇌 선택, 1페이즈 회귀 검증. 개발 카탈로그의 수치는 플레이 테스트 전 시작안이라
    // 밸런스 값이 아니며, 여기서는 그 값이 두뇌 흐름(전환, 후보, 강화 구간 경계)에 올바르게 이어지는지만 본다
    public class MucheolPhase2SelectionTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  TEST_HREF    = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  ARENA        = 44f;
        private const float  TOLERANCE    = 0.001f;
        private const float  BODY_RADIUS  = 0.5f;
        private const float  HP_FULL      = 1f;
        private const float  HP_PHASE_TWO = 0.5f;
        private const float  HP_ENHANCED_EDGE = 0.25f;
        private const float  HP_JUST_ABOVE    = 0.2501f;
        private const float  HP_JUST_BELOW    = 0.2499f;
        private const float  HP_TRANSITION    = 0.55f;
        private const float  HP_ABOVE_TRANSITION = 0.5501f;
        private const double STEP         = 0.05;
        private const double RUN_SECONDS  = 400.0;
        private const int    SEED         = 12345;
        private const string PHASE_TWO_PREFIX = "mucheol_ph2_";

        private const int NEAR = 0;
        private const int MID  = 1;
        private const int FAR  = 2;
        private const int FRONT = 0;
        private const int ONE   = 0;
        private const int RECENT_NONE = 3;

        private static readonly string[] PhaseTwoIds =
        {
            MucheolPhase2PatternFactory.P01_ID, MucheolPhase2PatternFactory.P02_ID, MucheolPhase2PatternFactory.P03_ID,
            MucheolPhase2PatternFactory.P04_ID, MucheolPhase2PatternFactory.P05_ID, MucheolPhase2PatternFactory.P06_ID
        };

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition Find(MucheolDevCatalog catalog, string id)
        {
            PatternDefinition definition = catalog.Find(id);
            Assert.IsNotNull(definition, id);
            return definition;
        }

        private static BossDefinition Boss(MucheolDevCatalog catalog)
        {
            Assert.IsTrue(catalog.TryGetBoss(MucheolPatternFactory.BOSS_ID, out BossDefinition boss));
            return boss;
        }

        private static MucheolPatternFactory MakePhaseOneFactory(Phase2Movement movement)
        {
            return new MucheolPatternFactory(TEST_HREF, null, movement, null, null,
                new MucheolComboSpec(new MucheolComboHit(5f, 120f, 0.14f, 0f)), null, new MucheolChargeSpec(2.5f));
        }

        private static int PlannedDashCount(MucheolPhase2PatternFactory factory, PatternDefinition definition)
        {
            IBossPattern pattern = factory.Create(definition);
            Assert.IsInstanceOf<MucheolMultiDashPattern>(pattern);
            return ((MucheolMultiDashPattern)pattern).PlannedDashCount;
        }

        // 두뇌에 실제 개발 카탈로그와 1, 2페이즈 팩토리 묶음을 올린 구성. 보스는 원점에서 +z를 보고 대상은 spot에 선다
        private sealed class BrainRig
        {
            public FakeCombatTarget    Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, BodyRadius = BODY_RADIUS };
            public FakeCombatTarget    Player   { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player };
            public Phase2Query         Query    { get; } = new Phase2Query();
            public FakeDamageResolver  Resolver { get; } = new FakeDamageResolver();
            public FakeBossVitals      Vitals   { get; } = new FakeBossVitals();
            public Phase2Movement      Movement { get; } = new Phase2Movement();
            public MucheolDevCatalog   Catalog  { get; }
            public MucheolPhase2PatternFactory PhaseTwoFactory { get; }
            public BossBrain           Brain    { get; }
            public Vector3             Spot     { get; set; }
            public List<string>        Started  { get; } = new List<string>();

            public BrainRig(Vector3 spot, bool includePhaseTwo = true)
            {
                Spot            = spot;
                Player.Position = spot;
                Query.Players.Add(Player);
                Movement.Self   = Self;
                Catalog         = new MucheolDevCatalog(ARENA, includePhaseTwo);
                PhaseTwoFactory = new MucheolPhase2PatternFactory(TEST_HREF, MucheolDevCatalog.CreatePhaseTwoSpec(), Vitals, Catalog, Movement);

                Brain = new BossBrain(MucheolPatternFactory.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = Catalog,
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Resolver,
                    Patterns = new MucheolPatternFactoryChain(MakePhaseOneFactory(Movement), PhaseTwoFactory),
                    Effects  = new BossEffectComposite(),
                    Control  = new FakeControlSource(),
                    Random   = new SeededBossRandom(SEED)
                });
            }

            // 시험 몸체가 전장 끝으로 흘러가지 않게 시전이 끝날 때마다 보스를 중심에 되돌리고 대상을 제자리에 세운다
            public void Run(double from, double seconds)
            {
                EBossAttackState previous = Brain.AttackState;
                for (double t = from; t <= (from + seconds); t += STEP)
                {
                    Brain.Tick(t);

                    EBossAttackState now = Brain.AttackState;
                    if ((previous == EBossAttackState.Ready) && (now != EBossAttackState.Ready))
                    {
                        Started.Add(Brain.ActivePatternId);
                    }

                    if (now == EBossAttackState.Ready)
                    {
                        Self.Position   = Vector3.zero;
                        Player.Position = Spot;
                    }

                    previous = now;
                }
            }
        }

        /****************************************
        *               Factory
        ****************************************/

        [Test]
        public void Create_AllSixIds_ReturnTheirExecutionTypes()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            MucheolPhase2PatternFactory factory = rig.PhaseTwoFactory;

            foreach (string id in new[] { PhaseTwoIds[0], PhaseTwoIds[1], PhaseTwoIds[3], PhaseTwoIds[4] })
            {
                Assert.IsTrue(factory.CanCreate(Find(rig.Catalog, id)), id);
                Assert.IsInstanceOf<MucheolSequencePattern>(factory.Create(Find(rig.Catalog, id)), id);
            }

            foreach (string id in new[] { PhaseTwoIds[2], PhaseTwoIds[5] })
            {
                Assert.IsTrue(factory.CanCreate(Find(rig.Catalog, id)), id);
                Assert.IsInstanceOf<MucheolMultiDashPattern>(factory.Create(Find(rig.Catalog, id)), id);
            }
        }

        [Test]
        public void Create_WithoutMovement_DashPatternsCannotBeCreatedAndReturnNull()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            var noMovement = new MucheolPhase2PatternFactory(TEST_HREF, MucheolDevCatalog.CreatePhaseTwoSpec(), rig.Vitals, rig.Catalog);

            foreach (string id in new[] { PhaseTwoIds[2], PhaseTwoIds[5] })
            {
                Assert.IsFalse(noMovement.CanCreate(Find(rig.Catalog, id)), id);
                Assert.IsNull(noMovement.Create(Find(rig.Catalog, id)), id);
            }

            Assert.IsTrue(noMovement.CanCreate(Find(rig.Catalog, PhaseTwoIds[0])), "순차 타격은 이동 창구가 필요 없다");
        }

        [Test]
        public void Create_MissingSpecEntry_CannotCreateThatPattern()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            MucheolPhase2Spec full = MucheolDevCatalog.CreatePhaseTwoSpec();
            var partial = new MucheolPhase2PatternFactory(TEST_HREF, new MucheolPhase2Spec(null, full.P02, full.P03, null, full.P05, null),
                rig.Vitals, rig.Catalog, rig.Movement);

            Assert.IsFalse(partial.CanCreate(Find(rig.Catalog, PhaseTwoIds[0])));
            Assert.IsNull(partial.Create(Find(rig.Catalog, PhaseTwoIds[0])));
            Assert.IsTrue(partial.CanCreate(Find(rig.Catalog, PhaseTwoIds[1])));
            Assert.IsFalse(partial.CanCreate(Find(rig.Catalog, PhaseTwoIds[3])));
            Assert.IsTrue(partial.CanCreate(Find(rig.Catalog, PhaseTwoIds[2])));
            Assert.IsFalse(partial.CanCreate(Find(rig.Catalog, PhaseTwoIds[5])));
        }

        [Test]
        public void Create_UnknownOrPhaseOneIdsAndNull_CannotCreate()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            PatternDefinition phaseOne = Find(rig.Catalog, MucheolPatternFactory.P01_ID);

            Assert.IsFalse(rig.PhaseTwoFactory.CanCreate(null));
            Assert.IsFalse(rig.PhaseTwoFactory.CanCreate(phaseOne));
            Assert.IsNull(rig.PhaseTwoFactory.Create(phaseOne));
            Assert.IsNull(rig.PhaseTwoFactory.Create(null));
        }

        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            MucheolPhase2Spec spec = MucheolDevCatalog.CreatePhaseTwoSpec();

            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolPhase2PatternFactory(0f, spec, rig.Vitals, rig.Catalog));
            Assert.Throws<ArgumentNullException>(() => new MucheolPhase2PatternFactory(TEST_HREF, null, rig.Vitals, rig.Catalog));
            Assert.Throws<ArgumentNullException>(() => new MucheolPhase2PatternFactory(TEST_HREF, spec, null, rig.Catalog));
            Assert.Throws<ArgumentNullException>(() => new MucheolPhase2PatternFactory(TEST_HREF, spec, rig.Vitals, null));
        }

        /****************************************
        *          25% Boundary (Dash Count)
        ****************************************/

        [TestCase(HP_FULL, 2)]
        [TestCase(HP_JUST_ABOVE, 2)]
        [TestCase(HP_ENHANCED_EDGE, 3)]
        [TestCase(HP_JUST_BELOW, 3)]
        [TestCase(0.01f, 3)]
        public void Create_P06_DashCountFollowsTheEnhancedBoundaryInclusive(float healthRate, int expected)
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            rig.Vitals.HealthRate = healthRate;

            Assert.AreEqual(expected, PlannedDashCount(rig.PhaseTwoFactory, Find(rig.Catalog, PhaseTwoIds[5])));
        }

        [TestCase(HP_FULL)]
        [TestCase(HP_ENHANCED_EDGE)]
        [TestCase(0.01f)]
        public void Create_P03_AlwaysTwoDashesRegardlessOfHealth(float healthRate)
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            rig.Vitals.HealthRate = healthRate;

            Assert.AreEqual(2, PlannedDashCount(rig.PhaseTwoFactory, Find(rig.Catalog, PhaseTwoIds[2])));
        }

        [Test]
        public void Create_BossWithoutEnhancedRate_UsesTheNormalCount()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            var catalog = new MucheolDevCatalog(ARENA, false);  // 1페이즈 시험용 정의는 강화 기준이 미지정이다
            var factory = new MucheolPhase2PatternFactory(TEST_HREF, MucheolDevCatalog.CreatePhaseTwoSpec(), rig.Vitals, catalog, rig.Movement);
            rig.Vitals.HealthRate = 0.01f;

            Assert.AreEqual(2, PlannedDashCount(factory, Find(rig.Catalog, PhaseTwoIds[5])));
        }

        // 선택 가중치의 강화 구간과 같은 경계다: 25%는 강화(기본 10 -> 강화 14), 25% 초과는 기본
        [Test]
        public void SecondPhaseWeight_P06_UsesEnhancedBaseWeightAtTheBoundaryInclusive()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            PatternDefinition p06 = Find(rig.Catalog, PhaseTwoIds[5]);
            BossDefinition boss = Boss(rig.Catalog);

            float above = BossPatternSelector.ComputeSecondPhaseWeight(p06, boss, HP_JUST_ABOVE, EPatternGroup.None, FAR, FRONT, ONE, RECENT_NONE);
            float edge  = BossPatternSelector.ComputeSecondPhaseWeight(p06, boss, HP_ENHANCED_EDGE, EPatternGroup.None, FAR, FRONT, ONE, RECENT_NONE);
            float below = BossPatternSelector.ComputeSecondPhaseWeight(p06, boss, HP_JUST_BELOW, EPatternGroup.None, FAR, FRONT, ONE, RECENT_NONE);

            Assert.AreEqual(10f * 1.8f * 1.5f, above, TOLERANCE);
            Assert.AreEqual(14f * 1.8f * 1.5f, edge, TOLERANCE);
            Assert.AreEqual(edge, below, TOLERANCE);
        }

        [Test]
        public void SecondPhaseWeight_FlowMultiplierSwitchesAtTheSameBoundary()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            PatternDefinition p06 = Find(rig.Catalog, PhaseTwoIds[5]);
            BossDefinition boss = Boss(rig.Catalog);

            // 직전 Large -> 다음 Large: 강화 아님 0.25, 강화 0.45
            float above = BossPatternSelector.ComputeSecondPhaseWeight(p06, boss, HP_JUST_ABOVE, EPatternGroup.Large, FAR, FRONT, ONE, RECENT_NONE);
            float edge  = BossPatternSelector.ComputeSecondPhaseWeight(p06, boss, HP_ENHANCED_EDGE, EPatternGroup.Large, FAR, FRONT, ONE, RECENT_NONE);

            Assert.AreEqual(10f * 1.8f * 1.5f * 0.25f, above, TOLERANCE);
            Assert.AreEqual(14f * 1.8f * 1.5f * 0.45f, edge, TOLERANCE);
        }

        /****************************************
        *            Dev Catalog Data
        ****************************************/

        [Test]
        public void DevCatalog_PhaseTwoDisabled_IsExactlyThePhaseOneSetup()
        {
            MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA);
            BossDefinition boss = Boss(catalog);
            List<PatternDefinition> second = new List<PatternDefinition>();
            List<PatternDefinition> first = new List<PatternDefinition>();

            catalog.CollectPatterns(MucheolPatternFactory.BOSS_ID, 2, second);
            catalog.CollectPatterns(MucheolPatternFactory.BOSS_ID, 1, first);

            Assert.AreEqual(0f, boss.PhaseTransitionHealthRate, "전환 비율 0은 2페이즈가 없다는 뜻이다");
            Assert.AreEqual(0, second.Count);
            Assert.AreEqual(6, first.Count);
            for (int i = 0; i < PhaseTwoIds.Length; ++i)
            {
                Assert.IsNull(catalog.Find(PhaseTwoIds[i]), PhaseTwoIds[i]);
            }
        }

        [Test]
        public void DevCatalog_PhaseTwoEnabled_HasSixPhaseTwoPatternsAndTransitionAt55Percent()
        {
            MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA, true);
            BossDefinition boss = Boss(catalog);
            List<PatternDefinition> second = new List<PatternDefinition>();
            List<PatternDefinition> first = new List<PatternDefinition>();

            catalog.CollectPatterns(MucheolPatternFactory.BOSS_ID, 2, second);
            catalog.CollectPatterns(MucheolPatternFactory.BOSS_ID, 1, first);

            Assert.AreEqual(HP_TRANSITION, boss.PhaseTransitionHealthRate, TOLERANCE);
            Assert.AreEqual(HP_ENHANCED_EDGE, boss.EnhancedHealthRate, TOLERANCE);
            Assert.AreEqual(6, first.Count, "1페이즈 후보는 그대로다");
            Assert.AreEqual(6, second.Count);
            for (int i = 0; i < second.Count; ++i)
            {
                Assert.AreEqual(2, second[i].Phase);
                Assert.IsTrue(second[i].DefinitionId.StartsWith(PHASE_TWO_PREFIX), second[i].DefinitionId);
                Assert.AreNotEqual(EPatternGroup.None, second[i].Group, second[i].DefinitionId);
                Assert.Greater(second[i].EnhancedBaseWeight, 0f, second[i].DefinitionId);
            }

            for (int i = 0; i < first.Count; ++i)
            {
                Assert.AreEqual(1, first[i].Phase);
                Assert.AreEqual(EPatternGroup.None, first[i].Group, first[i].DefinitionId);
            }
        }

        // 개발 데이터는 정의의 실행 시간과 패턴별 데이터가 서로 맞아야 한다
        [Test]
        public void DevCatalog_ExecuteSecondsMatchThePatternData()
        {
            MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA, true);
            MucheolPhase2Spec spec = MucheolDevCatalog.CreatePhaseTwoSpec();
            var sequences = new Dictionary<string, MucheolSequenceSpec>
            {
                { PhaseTwoIds[0], spec.P01 }, { PhaseTwoIds[1], spec.P02 }, { PhaseTwoIds[3], spec.P04 }, { PhaseTwoIds[4], spec.P05 }
            };

            foreach (KeyValuePair<string, MucheolSequenceSpec> pair in sequences)
            {
                PatternDefinition definition = Find(catalog, pair.Key);
                float lastDelay = pair.Value.Steps[pair.Value.StepCount - 1].DelaySeconds;
                Assert.LessOrEqual(lastDelay, definition.ActiveSeconds + definition.RecoverySeconds, pair.Key + " 마지막 타가 회복 안에 든다");
            }

            Assert.AreEqual(spec.P03.GetExecuteSeconds(spec.P03.DashCount), Find(catalog, PhaseTwoIds[2]).ActiveSeconds, TOLERANCE);
            Assert.AreEqual(spec.P03.GetExecuteSeconds(spec.P03.EnhancedDashCount), Find(catalog, PhaseTwoIds[2]).ActiveSeconds, TOLERANCE);
        }

        // 공용 FSM이 실행 시간을 하나로 고정해서 개발 데이터는 P06 정의를 세 번(최대) 길이에 맞춘다. 이 의존이 바뀌면 이 시험이 알린다
        [Test]
        public void DevCatalog_P06DefinitionIsSizedForTheMaximumDashCountBecauseTheSharedFsmFixesExecuteTime()
        {
            MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA, true);
            MucheolPhase2Spec spec = MucheolDevCatalog.CreatePhaseTwoSpec();
            PatternDefinition p06 = Find(catalog, PhaseTwoIds[5]);

            Assert.AreEqual(spec.P06.GetExecuteSeconds(spec.P06.EnhancedDashCount), p06.ActiveSeconds, TOLERANCE);
            Assert.Greater(p06.ActiveSeconds, spec.P06.GetExecuteSeconds(spec.P06.DashCount));
        }

        /****************************************
        *           Brain: Phase Two Flow
        ****************************************/

        [Test]
        public void Brain_PhaseOne_NeverPicksPhaseTwoPatternsAboveTheTransition()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            rig.Vitals.HealthRate = HP_ABOVE_TRANSITION;
            rig.Brain.Begin(0.0);

            rig.Run(0.0, RUN_SECONDS);

            Assert.AreEqual(1, rig.Brain.Phase);
            Assert.Greater(rig.Started.Count, 10);
            for (int i = 0; i < rig.Started.Count; ++i)
            {
                Assert.IsFalse(rig.Started[i].StartsWith(PHASE_TWO_PREFIX), rig.Started[i]);
            }
        }

        [Test]
        public void Brain_ExactlyAtTheTransitionRate_BeginsPhaseTwoAfterHandoff()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            rig.Brain.Begin(0.0);
            rig.Run(0.0, 2.0);
            Assert.AreEqual(1, rig.Brain.Phase);

            rig.Vitals.HealthRate = HP_TRANSITION;
            rig.Run(2.0 + STEP, 1.0);

            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, rig.Brain.PhaseState, "인계 전에는 새 패턴을 고르지 않는다");
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.AreEqual(2, rig.Brain.Phase);

            int before = rig.Started.Count;
            rig.Run(3.1, 5.0);
            Assert.AreEqual(before, rig.Started.Count, "CompleteHandoff 전에는 선택이 없다");

            Assert.IsTrue(rig.Brain.CompleteHandoff());
            Assert.AreEqual(EBossPhaseState.Active, rig.Brain.PhaseState);
        }

        [Test]
        public void Brain_PhaseTwoNear_OnlyPhaseTwoPatternsAreChosen()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            rig.Brain.Begin(0.0);
            rig.Run(0.0, 2.0);
            rig.Vitals.HealthRate = HP_PHASE_TWO;
            rig.Run(2.0 + STEP, 1.0);
            Assert.IsTrue(rig.Brain.CompleteHandoff());
            rig.Started.Clear();

            rig.Run(3.1, RUN_SECONDS);

            Assert.Greater(rig.Started.Count, 10);
            HashSet<string> distinct = new HashSet<string>(rig.Started);
            foreach (string id in distinct)
            {
                Assert.IsTrue(id.StartsWith(PHASE_TWO_PREFIX), id);
            }

            Assert.GreaterOrEqual(distinct.Count, 3, "근거리 후보는 여럿이고 가중치로 고르게 뽑힌다");
            Assert.IsTrue(distinct.Contains(PhaseTwoIds[0]) || distinct.Contains(PhaseTwoIds[1]) || distinct.Contains(PhaseTwoIds[4]));
        }

        [Test]
        public void Brain_PhaseTwoFar_OnlyTheTwoDashPatternsAreCandidates()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 15f));
            rig.Brain.Begin(0.0);
            rig.Run(0.0, 2.0);
            rig.Vitals.HealthRate = HP_PHASE_TWO;
            rig.Run(2.0 + STEP, 1.0);
            Assert.IsTrue(rig.Brain.CompleteHandoff());
            rig.Started.Clear();

            rig.Run(3.1, RUN_SECONDS);

            Assert.Greater(rig.Started.Count, 10);
            foreach (string id in new HashSet<string>(rig.Started))
            {
                Assert.IsTrue((id == PhaseTwoIds[2]) || (id == PhaseTwoIds[5]), id);
            }
        }

        [Test]
        public void Brain_PhaseTwoAtLowHealth_StillRunsPhaseTwoAndThreeDashesAreChosenByTheFactory()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 15f));
            rig.Brain.Begin(0.0);
            rig.Run(0.0, 2.0);
            rig.Vitals.HealthRate = HP_PHASE_TWO;
            rig.Run(2.0 + STEP, 1.0);
            Assert.IsTrue(rig.Brain.CompleteHandoff());

            rig.Vitals.HealthRate = HP_ENHANCED_EDGE;
            rig.Started.Clear();
            rig.Run(3.1, RUN_SECONDS);

            Assert.AreEqual(2, rig.Brain.Phase);
            Assert.Greater(rig.Started.Count, 10);
            Assert.AreEqual(3, PlannedDashCount(rig.PhaseTwoFactory, Find(rig.Catalog, PhaseTwoIds[5])));
        }

        /****************************************
        *            Phase One Regression
        ****************************************/

        [Test]
        public void PhaseOneFactory_CannotCreatePhaseTwoIds()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            MucheolPatternFactory phaseOne = MakePhaseOneFactory(rig.Movement);

            for (int i = 0; i < PhaseTwoIds.Length; ++i)
            {
                PatternDefinition definition = Find(rig.Catalog, PhaseTwoIds[i]);
                Assert.IsFalse(phaseOne.CanCreate(definition), PhaseTwoIds[i]);
                Assert.IsNull(phaseOne.Create(definition), PhaseTwoIds[i]);
            }
        }

        [Test]
        public void Chain_RoutesByDefinitionAndKeepsPhaseOneTypes()
        {
            BrainRig rig = new BrainRig(new Vector3(0f, 0f, 3f));
            var chain = new MucheolPatternFactoryChain(MakePhaseOneFactory(rig.Movement), rig.PhaseTwoFactory);

            Assert.IsInstanceOf<BossFanBlastPattern>(chain.Create(Find(rig.Catalog, MucheolPatternFactory.P01_ID)));
            Assert.IsInstanceOf<BossDashPattern>(chain.Create(Find(rig.Catalog, MucheolPatternFactory.P03_ID)));
            Assert.IsInstanceOf<BossCircleBlastPattern>(chain.Create(Find(rig.Catalog, MucheolPatternFactory.P04_ID)));
            Assert.IsInstanceOf<MucheolComboPattern>(chain.Create(Find(rig.Catalog, MucheolPatternFactory.P05_ID)));
            Assert.IsInstanceOf<MucheolChargePattern>(chain.Create(Find(rig.Catalog, MucheolPatternFactory.P06_ID)));
            Assert.IsInstanceOf<MucheolSequencePattern>(chain.Create(Find(rig.Catalog, PhaseTwoIds[0])));
            Assert.IsInstanceOf<MucheolMultiDashPattern>(chain.Create(Find(rig.Catalog, PhaseTwoIds[5])));

            PatternDefinition unknown = new PatternDefinition("mucheol_unknown", MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);
            Assert.IsFalse(chain.CanCreate(unknown));
            Assert.IsNull(chain.Create(unknown));
        }

        [Test]
        public void Asset_ProductionCatalog_HasNoPhaseTwoMucheolPatterns()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);

            for (int i = 0; i < PhaseTwoIds.Length; ++i)
            {
                Assert.IsFalse(catalog.TryGetPattern(PhaseTwoIds[i], out PatternDefinition _), PhaseTwoIds[i]);
            }
        }
    }
}
