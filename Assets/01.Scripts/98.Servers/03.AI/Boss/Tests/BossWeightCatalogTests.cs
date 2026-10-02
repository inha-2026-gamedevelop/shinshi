// System
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
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 에셋에 입력한 월백 1페이즈 선택 가중치가 원본 기획안(구미호보스_1페이즈_가중치_기획안.txt)과 같은지, 기획서의 계산 예시와
    // 실제 선택 코드의 계산이 같은지 확인한다. 아래 표는 기획안에서 그대로 옮긴 값이고 에셋과 코드에서 가져오지 않는다
    public class BossWeightCatalogTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const float  TOLERANCE    = 0.001f;

        private const int NEAR   = 0;
        private const int MID    = 1;
        private const int FAR    = 2;
        private const int FRONT  = 0;
        private const int SIDE   = 1;
        private const int BACK   = 2;
        private const int ONE    = 0;
        private const int TWO    = 1;
        private const int THREE  = 2;
        private const int RECENT_NONE = 3;

        private static readonly string[] Ids = { "wolbaek_p01", "wolbaek_p02", "wolbaek_p03", "wolbaek_p04", "wolbaek_p05", "wolbaek_p06" };

        // 기획안 5~8장 표, 행 순서는 P01~P06
        private static readonly float[]   Base      = { 20f, 15f, 10f, 20f, 15f, 8f };
        private static readonly float[][] Distance  =
        {
            new[] { 0.4f, 1.3f, 1.8f }, new[] { 1.8f, 0.8f, 0.1f }, new[] { 1.6f, 0.5f, 0f },
            new[] { 0.5f, 1.4f, 1.7f }, new[] { 0.8f, 1.3f, 1.5f }, new[] { 0.9f, 1.2f, 1.0f }
        };
        private static readonly float[][] Direction =
        {
            new[] { 1.2f, 1.0f, 0.8f }, new[] { 1.8f, 0.7f, 0.2f }, new[] { 1.0f, 1.0f, 1.0f },
            new[] { 1.3f, 1.0f, 0.6f }, new[] { 1.0f, 1.0f, 1.0f }, new[] { 1.6f, 0.6f, 0.1f }
        };
        private static readonly float[][] Nearby    =
        {
            new[] { 1.0f, 0.9f, 0.7f }, new[] { 1.1f, 1.3f, 1.4f }, new[] { 0.6f, 1.5f, 2.2f },
            new[] { 1.2f, 0.9f, 0.6f }, new[] { 1.0f, 1.2f, 1.4f }, new[] { 1.0f, 1.2f, 1.3f }
        };

        // 9장 일반 패턴과 10장 돌진 예외, 직전 / 2회 전 / 3회 전 / 최근 3회 안에 없음
        private static readonly float[] RecentNormal = { 0f, 0.3f, 0.7f, 1f };
        private static readonly float[] RecentDash   = { 0.25f, 0.5f, 0.8f, 1f };

        /****************************************
        *               Helpers
        ****************************************/

        private static BossPatternCatalogSO LoadCatalog()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        private static PatternDefinition Pattern(BossPatternCatalogSO catalog, int index)
        {
            Assert.IsTrue(catalog.TryGetPattern(Ids[index], out PatternDefinition pattern));
            return pattern;
        }

        private static void AssertTable(float[] expected, IReadOnlyList<float> actual, string label)
        {
            Assert.AreEqual(expected.Length, actual.Count, label);
            for (int i = 0; i < expected.Length; ++i)
            {
                Assert.AreEqual(expected[i], actual[i], $"{label}[{i}]");
            }
        }

        private static float Weight(BossPatternCatalogSO catalog, int index, int distance, int direction, int nearby, int recent)
        {
            return BossPatternSelector.ComputeWeight(Pattern(catalog, index), distance, direction, nearby, recent);
        }

        /****************************************
        *        Asset Values Equal The Plan
        ****************************************/

        [Test]
        public void Asset_BaseAndMultiplierTables_EqualPlanTables()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            for (int i = 0; i < Ids.Length; ++i)
            {
                PatternDefinition pattern = Pattern(catalog, i);
                Assert.AreEqual(1, pattern.Phase, Ids[i]);
                Assert.AreEqual(WOLBAEK_ID, pattern.BossId, Ids[i]);
                Assert.AreEqual(Base[i], pattern.BaseWeight, Ids[i] + " base");
                AssertTable(Distance[i], pattern.DistanceMultipliers, Ids[i] + " distance");
                AssertTable(Direction[i], pattern.DirectionMultipliers, Ids[i] + " direction");
                AssertTable(Nearby[i], pattern.NearbyMultipliers, Ids[i] + " nearby");
            }
        }

        // 돌진만 예외표를 쓰고 나머지 다섯은 일반표를 쓴다
        [Test]
        public void Asset_RecentUseTables_DashExceptionKeptAndOthersUseNormalTable()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            AssertTable(RecentDash, Pattern(catalog, 0).RecentUseMultipliers, "p01 recent");
            for (int i = 1; i < Ids.Length; ++i)
            {
                AssertTable(RecentNormal, Pattern(catalog, i).RecentUseMultipliers, Ids[i] + " recent");
            }
        }

        [Test]
        public void Asset_NoDensityColumnAndNoExtraPatterns()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            List<PatternDefinition> collected = new List<PatternDefinition>();

            catalog.CollectPatterns(WOLBAEK_ID, 1, collected);

            Assert.AreEqual(6, collected.Count);
            for (int i = 0; i < collected.Count; ++i)
            {
                Assert.AreEqual(3, collected[i].DistanceMultipliers.Count);
                Assert.AreEqual(3, collected[i].DirectionMultipliers.Count);
                Assert.AreEqual(3, collected[i].NearbyMultipliers.Count);
                Assert.AreEqual(4, collected[i].RecentUseMultipliers.Count);
            }
        }

        /****************************************
        *          Boundaries And Bands
        ****************************************/

        [Test]
        public void Asset_BossSelectionBoundaries_EqualPlanAndSupplementRules()
        {
            Assert.IsTrue(LoadCatalog().TryGetBoss(WOLBAEK_ID, out BossDefinition boss));

            Assert.AreEqual(5f, boss.NearDistanceMax);
            Assert.AreEqual(12f, boss.MidDistanceMax);
            Assert.AreEqual(60f, boss.FrontAngleMax);
            Assert.AreEqual(120f, boss.SideAngleMax);
            Assert.AreEqual(5f, boss.NearbyRadius);
            Assert.AreEqual(0.25f, boss.EmptyWeightRetrySeconds);
        }

        // 보완 규칙: 근거리 0 이상 5m 미만, 중거리 5 이상 12m 미만, 원거리 12m 이상
        [TestCase(0f, NEAR)]
        [TestCase(4.999f, NEAR)]
        [TestCase(5f, MID)]
        [TestCase(11.999f, MID)]
        [TestCase(12f, FAR)]
        [TestCase(40f, FAR)]
        public void DistanceBand_UsesAssetBoundaries(float distance, int expected)
        {
            Assert.IsTrue(LoadCatalog().TryGetBoss(WOLBAEK_ID, out BossDefinition boss));

            Assert.AreEqual(expected, BossPatternSelector.GetDistanceBand(distance, boss));
        }

        // 보완 규칙: 정면 대비 0~60도 포함이 전방, 60 초과~120 포함이 측면, 120 초과가 후방
        [TestCase(0f, FRONT)]
        [TestCase(60f, FRONT)]
        [TestCase(60.01f, SIDE)]
        [TestCase(120f, SIDE)]
        [TestCase(120.01f, BACK)]
        [TestCase(180f, BACK)]
        public void DirectionBand_UsesAssetBoundaries(float angle, int expected)
        {
            Assert.IsTrue(LoadCatalog().TryGetBoss(WOLBAEK_ID, out BossDefinition boss));

            Assert.AreEqual(expected, BossPatternSelector.GetDirectionBand(angle, boss));
        }

        // 보완 규칙: 0명은 1명 열, 3명과 4명은 3명 열
        [TestCase(0, ONE)]
        [TestCase(1, ONE)]
        [TestCase(2, TWO)]
        [TestCase(3, THREE)]
        [TestCase(4, THREE)]
        [TestCase(8, THREE)]
        public void NearbyBand_ZeroUsesOneAndFourUsesThree(int count, int expected)
        {
            Assert.AreEqual(expected, BossPatternSelector.GetNearbyBand(count));
        }

        /****************************************
        *          Plan Calculation Examples
        ****************************************/

        // 예시 A: 15m, 전방, 주변 1명, 최근 기록 없음
        [Test]
        public void ExampleA_FarFrontOnePlayer_MatchesPlanNumbers()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.AreEqual(43.2f, Weight(catalog, 0, FAR, FRONT, ONE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(53.04f, Weight(catalog, 3, FAR, FRONT, ONE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(2.97f, Weight(catalog, 1, FAR, FRONT, ONE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(0f, Weight(catalog, 2, FAR, FRONT, ONE, RECENT_NONE), "원거리 원형은 0");
        }

        // 예시 B: 근거리, 방향 영향 없음(투사체 측면 1.0), 주변 3명
        [Test]
        public void ExampleB_NearThreeClose_MatchesPlanNumbers()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.AreEqual(35.2f, Weight(catalog, 2, NEAR, SIDE, THREE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(6f, Weight(catalog, 3, NEAR, SIDE, THREE, RECENT_NONE), TOLERANCE);
        }

        // 예시 C: 직전에 쓴 부채꼴은 어떤 상황이어도 0
        [Test]
        public void ExampleC_JustUsedFan_IsZeroInEveryContext()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            for (int d = NEAR; d <= FAR; ++d)
            {
                for (int r = FRONT; r <= BACK; ++r)
                {
                    for (int n = ONE; n <= THREE; ++n)
                    {
                        Assert.AreEqual(0f, Weight(catalog, 1, d, r, n, 0));
                    }
                }
            }
        }

        // 12장 합산 예시: 30 + 20 + 15 + 25 + 10 + 5 = 105 에서 0~105 사이 값으로 고른다
        [Test]
        public void Pick_PlanSumExample_SelectsByWeightRatio()
        {
            float[] weights = { 30f, 20f, 15f, 25f, 10f, 5f };

            Assert.AreEqual(0, BossPatternSelector.Pick(weights, 0f));
            Assert.AreEqual(0, BossPatternSelector.Pick(weights, 29.9f / 105f));
            Assert.AreEqual(1, BossPatternSelector.Pick(weights, 30.1f / 105f));
            Assert.AreEqual(3, BossPatternSelector.Pick(weights, 65.1f / 105f));
            Assert.AreEqual(5, BossPatternSelector.Pick(weights, 100.1f / 105f));
            Assert.AreEqual(5, BossPatternSelector.Pick(weights, 0.9999f));
        }

        /****************************************
        *      Real Tree With The Real Asset
        ****************************************/

        private sealed class TreeRig
        {
            public FakeCombatTarget Self   { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeTargetQuery  Query  { get; } = new FakeTargetQuery();
            public BossBlackboard   Board  { get; } = new BossBlackboard();
            public BossSelectionTree Tree  { get; } = new BossSelectionTree();

            public TreeRig(BossPatternCatalogSO catalog, Vector3 playerPosition, int nearbyCount, int seed)
            {
                Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossDefinition boss));

                Query.Players.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = playerPosition });
                Query.NearbyCount = nearbyCount;

                Board.Self    = Self;
                Board.Boss    = boss;
                Board.Phase   = 1;
                Board.Targets = Query;
                Board.Effects = new FakeEffectLifetime();
                Board.Catalog = new BossPatternCatalogSource(catalog);
                Board.Random  = new SeededBossRandom(seed);
            }

            public string SelectOnce()
            {
                Assert.IsTrue(Tree.Evaluate(Board));
                return Board.SelectedPattern.DefinitionId;
            }
        }

        // 예시 A 상황에서 실제 BT와 에셋으로 뽑은 빈도가 최종 가중치 비율과 같고, 최고값만 고르지 않는다
        [Test]
        public void Tree_ExampleAContext_SelectionFrequencyFollowsWeightRatio()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            TreeRig rig = new TreeRig(catalog, new Vector3(0f, 0f, 15f), 1, 12345);
            rig.Self.Forward = Vector3.forward;

            // 같은 상황을 반복하려고 최근 기록은 매번 비운 상태로 본다
            float[] expected = { 43.2f, 2.97f, 0f, 53.04f, 22.5f, 12.8f };
            float total = 0f;
            for (int i = 0; i < expected.Length; ++i)
            {
                total += expected[i];
            }

            const int SAMPLES = 60000;
            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int i = 0; i < SAMPLES; ++i)
            {
                string id = rig.SelectOnce();
                counts[id] = counts.TryGetValue(id, out int value) ? (value + 1) : 1;
            }

            for (int i = 0; i < Ids.Length; ++i)
            {
                counts.TryGetValue(Ids[i], out int count);
                Assert.AreEqual(expected[i] / total, (float)count / SAMPLES, 0.01f, Ids[i]);
            }

            Assert.IsFalse(counts.ContainsKey("wolbaek_p03"), "원거리 원형은 뽑히지 않는다");
            Assert.Greater(counts["wolbaek_p01"], 0, "최고 가중치(P04)만 고르는 방식이 아니다");
        }

        // 직전에 쓴 일반 패턴은 다음 선택에서 뽑히지 않고 돌진은 직전에 써도 뽑힐 수 있다
        [Test]
        public void Tree_RecentUse_NormalPatternExcludedAndDashStillPossible()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            TreeRig rig = new TreeRig(catalog, new Vector3(0f, 0f, 15f), 1, 777);
            rig.Self.Forward = Vector3.forward;

            rig.Board.RecordUse("wolbaek_p04");
            rig.Board.RecordUse("wolbaek_p01");
            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int i = 0; i < 20000; ++i)
            {
                string id = rig.SelectOnce();
                counts[id] = counts.TryGetValue(id, out int value) ? (value + 1) : 1;
            }

            // 직전 P01(x0.25), 2회 전 P04(x0.3)
            float p01 = 43.2f * 0.25f;
            float p04 = 53.04f * 0.3f;
            float p05 = 22.5f;
            float p06 = 12.8f;
            float p02 = 2.97f;
            float total = p01 + p04 + p05 + p06 + p02;
            Assert.AreEqual(p01 / total, (float)counts["wolbaek_p01"] / 20000f, 0.015f);
            Assert.AreEqual(p04 / total, (float)counts["wolbaek_p04"] / 20000f, 0.015f);

            rig.Board.RecordUse("wolbaek_p02");
            for (int i = 0; i < 2000; ++i)
            {
                Assert.AreNotEqual("wolbaek_p02", rig.SelectOnce(), "직전에 쓴 부채꼴은 제외된다");
            }
        }

        // 예시 B 상황에서는 원형이 가장 자주 나온다
        [Test]
        public void Tree_ExampleBContext_RoundBlastDominatesAsPlanned()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            // 근거리 3m, 보스 정면에서 90도(측면), 주변 3명
            TreeRig rig = new TreeRig(catalog, new Vector3(3f, 0f, 0f), 3, 99);
            rig.Self.Forward = Vector3.forward;

            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int i = 0; i < 20000; ++i)
            {
                string id = rig.SelectOnce();
                counts[id] = counts.TryGetValue(id, out int value) ? (value + 1) : 1;
            }

            Assert.Greater(counts["wolbaek_p03"], counts["wolbaek_p04"] * 5, "원형 35.2 대 투사체 6");
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Key != "wolbaek_p03")
                {
                    Assert.Less(pair.Value, counts["wolbaek_p03"]);
                }
            }
        }

        /****************************************
        *         Executability And Tuning
        ****************************************/

        // 가중치가 입력되어도 서버 몸체 연결(이동 창구, 투사체 발사 창구, 장판 관리자)이 없으면 팩토리가 만들지 않아 실행할 수 없다
        [TestCase("wolbaek_p01")]
        [TestCase("wolbaek_p04")]
        [TestCase("wolbaek_p05")]
        public void PatternsNeedingServerConnections_HaveWeightsButFactoryCannotCreateThemWithoutThem(string patternId)
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGetPattern(patternId, out PatternDefinition pattern));
            WolbaekPatternFactory factory = new WolbaekPatternFactory(3541f);

            Assert.Greater(pattern.BaseWeight, 0f);
            Assert.IsNull(factory.Create(pattern));
        }

        // 선택 경계와 월백 보스 기본값이 모두 입력돼 전투를 연다
        [Test]
        public void IsTuned_True_AndHasNoTuningErrors()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            List<string> errors = new List<string>();

            catalog.ValidateTuning(WOLBAEK_ID, errors);

            string all = string.Join("\n", errors);
            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));
            Assert.AreEqual(0, errors.Count, all);
        }
    }
}
