// System
using System.Collections.Generic;

// Unity
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
    // 무철 하드 P07이 시험 스위치와 페이즈에 따라 후보가 되는지, 두뇌가 예고와 회복 시간을 지키며 반복해서 고르는지 본다.
    // 수치는 시험 전 임시안이라 실제 사용률이나 밸런스는 증명하지 않는다. 같은 난수 씨앗으로 돌려 결과가 매번 같다
    public class MucheolBattlefieldSelectionTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TEST_HREF    = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  ARENA        = 44f;
        private const float  TOLERANCE    = 0.001f;
        private const float  HP_PHASE_TWO = 0.5f;
        private const float  HP_ABOVE_TRANSITION = 0.5501f;
        private const float  HP_ENHANCED  = 0.2499f;
        private const double STEP         = 0.05;
        private const double RUN_SECONDS  = 3000.0;
        private const int    SEED         = 12345;
        private const int    MIN_GAP      = 4;     // 같은 패턴이 다시 나오기까지 최소 시전 수, 최근 세 번 안에는 고르지 않는다
        private const string P07_ID       = MucheolPhase2PatternFactory.P07_ID;

        private const int NEAR = 0;
        private const int MID  = 1;
        private const int FAR  = 2;
        private const int FRONT = 0;
        private const int ONE   = 0;

        /****************************************
        *               Helpers
        ****************************************/

        private static MucheolPatternFactory MakePhaseOneFactory(Phase2Movement movement)
        {
            return new MucheolPatternFactory(TEST_HREF, null, movement, null, null,
                new MucheolComboSpec(new MucheolComboHit(5f, 120f, 0.14f, 0f)), null, new MucheolChargeSpec(2.5f));
        }

        // 선택 규칙은 전투를 열기 전에만 더할 수 있어서, 먼저 꺼진 채로 더해 두고 2페이즈에 들어간 뒤 켠다
        private sealed class ForceP07Rule : IBossSelectionRule
        {
            private readonly MucheolDevCatalog _catalog;

            public bool IsEnabled;

            public ForceP07Rule(MucheolDevCatalog catalog)
            {
                _catalog = catalog;
            }

            public bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern)
            {
                pattern = IsEnabled ? _catalog.Find(P07_ID) : null;
                return pattern != null;
            }
        }

        private sealed class NullDisplacer : IBossHitDisplacer
        {
            public int Calls;

            public float TryPush(int combatId, Vector3 direction, float maxDistance)
            {
                ++Calls;
                return 0f;
            }
        }

        // 두뇌에 개발 카탈로그와 1, 2페이즈 팩토리 묶음을 올린 구성. 보스는 원점에서 +z를 보고 대상은 spot에 선다
        private sealed class Rig
        {
            public FakeCombatTarget    Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, BodyRadius = 2f };
            public FakeCombatTarget    Player   { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player };
            public Phase2Query         Query    { get; } = new Phase2Query();
            public FakeDamageResolver  Resolver { get; } = new FakeDamageResolver();
            public FakeBossVitals      Vitals   { get; } = new FakeBossVitals();
            public Phase2Movement      Movement { get; } = new Phase2Movement();
            public NullDisplacer       Displacer { get; } = new NullDisplacer();
            public MucheolDevCatalog   Catalog  { get; }
            public MucheolPhase2PatternFactory PhaseTwoFactory { get; }
            public BossBrain           Brain    { get; }
            public Vector3             Spot     { get; set; }
            public List<string>        Started  { get; } = new List<string>();

            public Rig(Vector3 spot, bool includePhaseTwo, bool hard, bool giveDisplacer = true)
            {
                Spot            = spot;
                Player.Position = spot;
                Query.Players.Add(Player);
                Movement.Self   = Self;
                Catalog         = new MucheolDevCatalog(ARENA, includePhaseTwo, hard);
                PhaseTwoFactory = new MucheolPhase2PatternFactory(TEST_HREF, MucheolDevCatalog.CreatePhaseTwoSpec(), Vitals, Catalog,
                    Movement, null, null, giveDisplacer ? Displacer : null);

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

            // 1페이즈에서 시작해 55%로 전환하고 인계를 마친다. 끝난 시각 이후부터 이어서 돌리면 된다
            public double GoToPhaseTwo()
            {
                Brain.Begin(0.0);
                Run(0.0, 2.0);
                Vitals.HealthRate = HP_PHASE_TWO;
                Run(2.0 + STEP, 1.0);
                Assert.IsTrue(Brain.CompleteHandoff());
                Started.Clear();
                return 3.1;
            }
        }

        private static List<int> IndicesOf(List<string> started, string id)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < started.Count; ++i)
            {
                if (started[i] == id)
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        /****************************************
        *            Dev Catalog Switch
        ****************************************/

        [Test]
        public void Catalog_SwitchOff_HasNoP07AndKeepsTheSixPhaseTwoPatterns()
        {
            MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA, true, false);
            List<PatternDefinition> second = new List<PatternDefinition>();

            catalog.CollectPatterns(MucheolPatternFactory.BOSS_ID, 2, second);

            Assert.IsFalse(catalog.IncludesHardP07);
            Assert.IsNull(catalog.Find(P07_ID));
            Assert.AreEqual(6, second.Count);
        }

        [Test]
        public void Catalog_TwoArgumentConstructor_IsTheSameAsSwitchOff()
        {
            MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA, true);

            Assert.IsFalse(catalog.IncludesHardP07);
            Assert.IsNull(catalog.Find(P07_ID));
        }

        [Test]
        public void Catalog_SwitchOn_AddsP07ToPhaseTwoOnly()
        {
            MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA, true, true);
            List<PatternDefinition> first = new List<PatternDefinition>();
            List<PatternDefinition> second = new List<PatternDefinition>();

            catalog.CollectPatterns(MucheolPatternFactory.BOSS_ID, 1, first);
            catalog.CollectPatterns(MucheolPatternFactory.BOSS_ID, 2, second);

            Assert.IsTrue(catalog.IncludesHardP07);
            Assert.AreEqual(6, first.Count);
            Assert.AreEqual(7, second.Count);
            Assert.IsTrue(second.Contains(catalog.Find(P07_ID)));
            Assert.IsFalse(first.Contains(catalog.Find(P07_ID)));
        }

        [Test]
        public void Catalog_SwitchOnWithoutPhaseTwo_HasNoP07()
        {
            MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA, false, true);

            Assert.IsFalse(catalog.IncludesHardP07);
            Assert.IsNull(catalog.Find(P07_ID));
        }

        /****************************************
        *            Selection Weight
        ****************************************/

        [TestCase(NEAR)]
        [TestCase(MID)]
        [TestCase(FAR)]
        public void Weight_P07_IsThreeInEveryDistanceBandAndNotRepeatableWithinThreeCasts(int band)
        {
            Rig rig = new Rig(new Vector3(0f, 0f, 3f), true, true);
            PatternDefinition p07 = rig.Catalog.Find(P07_ID);
            rig.Catalog.TryGetBoss(MucheolPatternFactory.BOSS_ID, out BossDefinition boss);

            for (int recent = 0; recent < 3; ++recent)
            {
                Assert.AreEqual(0f, BossPatternSelector.ComputeSecondPhaseWeight(p07, boss, HP_PHASE_TWO, EPatternGroup.None, band, FRONT, ONE, recent), TOLERANCE, $"최근 {recent}번째");
            }

            Assert.AreEqual(3f, BossPatternSelector.ComputeSecondPhaseWeight(p07, boss, HP_PHASE_TWO, EPatternGroup.None, band, FRONT, ONE, 3), TOLERANCE);
            Assert.AreEqual(3f, BossPatternSelector.ComputeSecondPhaseWeight(p07, boss, HP_ENHANCED, EPatternGroup.None, band, FRONT, ONE, 3), TOLERANCE, "강화 구간도 같은 기본값");
        }

        /****************************************
        *            Brain Selection
        ****************************************/

        [Test]
        public void Brain_SwitchOff_PhaseTwo_NeverPicksP07()
        {
            Rig rig = new Rig(new Vector3(0f, 0f, 3f), true, false);
            double next = rig.GoToPhaseTwo();

            rig.Run(next, RUN_SECONDS);

            Assert.Greater(rig.Started.Count, 100);
            CollectionAssert.DoesNotContain(rig.Started, P07_ID);
        }

        [Test]
        public void Brain_SwitchOn_PhaseOne_NeverPicksP07()
        {
            Rig rig = new Rig(new Vector3(0f, 0f, 3f), true, true);
            rig.Vitals.HealthRate = HP_ABOVE_TRANSITION;
            rig.Brain.Begin(0.0);

            rig.Run(0.0, RUN_SECONDS);

            Assert.AreEqual(1, rig.Brain.Phase);
            Assert.Greater(rig.Started.Count, 100);
            CollectionAssert.DoesNotContain(rig.Started, P07_ID);
        }

        [Test]
        public void Brain_SwitchOn_PhaseTwo_PicksP07RepeatedlyButNeverWithinThreeCasts()
        {
            Rig rig = new Rig(new Vector3(0f, 0f, 3f), true, true);
            double next = rig.GoToPhaseTwo();

            rig.Run(next, RUN_SECONDS);

            List<int> indices = IndicesOf(rig.Started, P07_ID);
            Assert.GreaterOrEqual(indices.Count, 3, "낮은 빈도여도 반복해서 고른다");
            for (int i = 1; i < indices.Count; ++i)
            {
                Assert.GreaterOrEqual(indices[i] - indices[i - 1], MIN_GAP, "최근 세 번 안에는 다시 고르지 않는다");
            }

            Assert.Less(indices.Count, rig.Started.Count / 4, "다른 패턴이 대부분이다");
        }

        [TestCase(3f)]
        [TestCase(8f)]
        [TestCase(15f)]
        public void Brain_SwitchOn_PhaseTwo_P07IsACandidateAtEveryDistance(float distance)
        {
            Rig rig = new Rig(new Vector3(0f, 0f, distance), true, true);
            double next = rig.GoToPhaseTwo();

            rig.Run(next, RUN_SECONDS);

            Assert.GreaterOrEqual(IndicesOf(rig.Started, P07_ID).Count, 1, $"거리 {distance}m");
        }

        [Test]
        public void Brain_SwitchOnWithoutDisplacer_NeverPicksP07()
        {
            Rig rig = new Rig(new Vector3(0f, 0f, 3f), true, true, giveDisplacer: false);
            double next = rig.GoToPhaseTwo();

            rig.Run(next, RUN_SECONDS);

            Assert.Greater(rig.Started.Count, 100);
            CollectionAssert.DoesNotContain(rig.Started, P07_ID, "밀림 창구가 없으면 후보에서 빠진다");
        }

        /****************************************
        *        Forced Cast Timing And Repeat
        ****************************************/

        [Test]
        public void Brain_ForcedP07_HitsAtFourSecondsAndRecoversForOnePointEightSeconds()
        {
            Rig rig = new Rig(new Vector3(0f, 0f, 8f), true, true);
            var rule = new ForceP07Rule(rig.Catalog);
            Assert.IsTrue(rig.Brain.AddSelectionRule(rule));
            double next = rig.GoToPhaseTwo();
            rule.IsEnabled = true;

            double startTime = -1.0;
            double hitTime   = -1.0;
            double endTime   = -1.0;
            int    before    = 0;
            for (double t = next; t < (next + 30.0); t += STEP)
            {
                rig.Brain.Tick(t);

                if ((startTime < 0.0) && (rig.Brain.AttackState != EBossAttackState.Ready) && (rig.Brain.ActivePatternId == P07_ID))
                {
                    startTime = t;
                    before    = rig.Resolver.Requests.Count;
                }
                else if ((startTime >= 0.0) && (hitTime < 0.0) && (rig.Resolver.Requests.Count > before))
                {
                    hitTime = t;
                }
                else if ((hitTime >= 0.0) && (rig.Brain.AttackState == EBossAttackState.Ready))
                {
                    endTime = t;
                    break;
                }
            }

            Assert.GreaterOrEqual(startTime, 0.0, "강제 선택으로 시전이 시작된다");
            Assert.AreEqual(4.0, hitTime - startTime, STEP + TOLERANCE, "예고 4초 뒤 판정");
            Assert.AreEqual(5.8, endTime - startTime, STEP + TOLERANCE, "판정 뒤 회복 1.8초");
            Assert.AreEqual(1, rig.Resolver.Requests.Count - before, "대상 한 명에게 한 번만 피해 요청");
        }

        [Test]
        public void Brain_ForcedP07_RepeatsEveryCast()
        {
            Rig rig = new Rig(new Vector3(0f, 0f, 8f), true, true);
            var rule = new ForceP07Rule(rig.Catalog);
            Assert.IsTrue(rig.Brain.AddSelectionRule(rule));
            double next = rig.GoToPhaseTwo();
            rule.IsEnabled = true;

            rig.Run(next, 40.0);

            Assert.GreaterOrEqual(rig.Started.Count, 5);
            foreach (string id in rig.Started)
            {
                Assert.AreEqual(P07_ID, id);
            }
        }
    }
}
