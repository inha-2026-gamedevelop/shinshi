// System
using System.Collections.Generic;

// Unity
using UnityEditor;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 에셋에 들어간 월백 하드 P02가 이지 P02와 ID, 난이도, 사거리, 각도, 최근 사용 배율만 다르고, 이지 수집과 이지 검증,
    // 기존 이지 P02 값을 바꾸지 않는지 확인한다
    public class BossWolbaekHardP02DataTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string EASY_P02_ID  = "wolbaek_p02";
        private const string HARD_P02_ID  = "wolbaek_hard_p02";
        private const string HARD_P01_ID  = "wolbaek_hard_p01";
        private const float  EASY_RANGE   = 6f;
        private const float  EASY_ARC     = 100f;
        private const float  HARD_RANGE   = 7f;
        private const float  HARD_ARC     = 110f;
        private const int    FIRST_PHASE  = 1;
        private const int    SECOND_PHASE = 2;
        private const int    EASY_PHASE_ONE_COUNT = 6;

        private static readonly float[] G_EasyRecent = { 0f, 0.3f, 0.7f, 1f };
        private static readonly float[] G_HardRecent = { 0.25f, 0.3f, 0.7f, 1f };

        /****************************************
        *               Helpers
        ****************************************/

        private static BossPatternCatalogSO LoadAsset()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        private static PatternDefinition Get(BossPatternCatalogSO catalog, string id)
        {
            Assert.IsTrue(catalog.TryGetPattern(id, out PatternDefinition pattern), id);
            return pattern;
        }

        private static void AssertTable(float[] expected, IReadOnlyList<float> actual, string label)
        {
            Assert.AreEqual(expected.Length, actual.Count, label);
            for (int i = 0; i < expected.Length; ++i)
            {
                Assert.AreEqual(expected[i], actual[i], label + "[" + i + "]");
            }
        }

        private static void AssertSameTable(IReadOnlyList<float> expected, IReadOnlyList<float> actual, string label)
        {
            Assert.AreEqual(expected.Count, actual.Count, label);
            for (int i = 0; i < expected.Count; ++i)
            {
                Assert.AreEqual(expected[i], actual[i], label + "[" + i + "]");
            }
        }

        private static List<string> Collect(IBossPatternCatalog catalog, int phase)
        {
            var results = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, phase, results);

            var ids = new List<string>();
            for (int i = 0; i < results.Count; ++i)
            {
                ids.Add(results[i].DefinitionId);
            }

            return ids;
        }

        /****************************************
        *        Hard P02 Matches Easy P02
        ****************************************/

        [Test]
        public void Asset_HardP02_HasUniqueIdAndHardDifficulty()
        {
            BossPatternCatalogSO catalog = LoadAsset();
            PatternDefinition hard = Get(catalog, HARD_P02_ID);

            Assert.AreEqual(HARD_P02_ID, BossPatternCatalogSO.WOLBAEK_HARD_P02_ID);
            Assert.AreEqual(HARD_P02_ID, WolbaekPatternFactory.HARD_P02_ID);
            Assert.AreEqual(EBossDifficulty.Hard, hard.Difficulty);
            Assert.AreEqual(WOLBAEK_ID, hard.BossId);
            Assert.AreEqual(FIRST_PHASE, hard.Phase);
            Assert.AreEqual(HARD_P02_ID + ".name", hard.DisplayKey);
            Assert.AreNotEqual(EASY_P02_ID, hard.DefinitionId);
        }

        [Test]
        public void Asset_HardP02_DiffersFromEasyP02OnlyInRangeArcAndRecentUse()
        {
            BossPatternCatalogSO catalog = LoadAsset();
            PatternDefinition easy = Get(catalog, EASY_P02_ID);
            PatternDefinition hard = Get(catalog, HARD_P02_ID);

            Assert.AreEqual(HARD_RANGE, hard.Shape.Range);
            Assert.AreEqual(HARD_ARC, hard.Shape.ArcAngle);
            AssertTable(G_HardRecent, hard.RecentUseMultipliers, "hard recent");

            Assert.AreEqual(easy.BossId, hard.BossId);
            Assert.AreEqual(easy.Phase, hard.Phase);
            Assert.AreEqual(easy.BaseWeight, hard.BaseWeight);
            AssertSameTable(easy.DistanceMultipliers, hard.DistanceMultipliers, "distance");
            AssertSameTable(easy.DirectionMultipliers, hard.DirectionMultipliers, "direction");
            AssertSameTable(easy.NearbyMultipliers, hard.NearbyMultipliers, "nearby");
            Assert.AreEqual(easy.TelegraphSeconds, hard.TelegraphSeconds);
            Assert.AreEqual(easy.TrackingSeconds, hard.TrackingSeconds);
            Assert.AreEqual(easy.TurnRate, hard.TurnRate);
            Assert.AreEqual(easy.ActiveSeconds, hard.ActiveSeconds);
            Assert.AreEqual(easy.RecoverySeconds, hard.RecoverySeconds);
            Assert.AreEqual(easy.Shape.Shape, hard.Shape.Shape);
            Assert.AreEqual(easy.Shape.Width, hard.Shape.Width);
            Assert.AreEqual(easy.Shape.ProjectileSpeed, hard.Shape.ProjectileSpeed);
            Assert.AreEqual(easy.DamageRef, hard.DamageRef);
            Assert.AreEqual(easy.Guardable, hard.Guardable);
            Assert.AreEqual(easy.ControlPhase, hard.ControlPhase);
            Assert.AreEqual(easy.ControlWindowSeconds, hard.ControlWindowSeconds);
            Assert.AreEqual(easy.ControlCancelsPattern, hard.ControlCancelsPattern);
            Assert.AreEqual(easy.AreaDurationSeconds, hard.AreaDurationSeconds);
            Assert.AreEqual(easy.AreaTickInterval, hard.AreaTickInterval);
            Assert.AreEqual(easy.MaxConcurrentAreas, hard.MaxConcurrentAreas);
            Assert.AreEqual(easy.Group, hard.Group);
            Assert.AreEqual(easy.EnhancedBaseWeight, hard.EnhancedBaseWeight);
            Assert.AreEqual(easy.RawProjectileCount, hard.RawProjectileCount);
            Assert.AreEqual(easy.ProjectileIntervalSeconds, hard.ProjectileIntervalSeconds);
            Assert.AreEqual(easy.RawAreaTargetCount, hard.RawAreaTargetCount);
            Assert.AreEqual(easy.AreaSkipsMissedRounds, hard.AreaSkipsMissedRounds);
            Assert.AreEqual(easy.RawSweepZoneCount, hard.RawSweepZoneCount);
            Assert.AreEqual(easy.SweepIntervalSeconds, hard.SweepIntervalSeconds);
            Assert.AreEqual(easy.RawBeamCount, hard.RawBeamCount);
            Assert.AreEqual(easy.BeamLateralOffset, hard.BeamLateralOffset);
            Assert.AreEqual(easy.BeamTickInterval, hard.BeamTickInterval);
            Assert.AreEqual(easy.StarVertexCount, hard.StarVertexCount);
            Assert.AreEqual(easy.StarRingRadius, hard.StarRingRadius);
            Assert.AreEqual(easy.StarConnectStep, hard.StarConnectStep);
            Assert.AreEqual(easy.StarRotationDegrees, hard.StarRotationDegrees);
        }

        // 기존 이지 P02는 ID, 난이도, 사거리, 각도, 최근 사용 배율, 실행 값이 그대로다
        [Test]
        public void Asset_EasyP02_KeepsIdValuesRangeAndArc()
        {
            PatternDefinition easy = Get(LoadAsset(), EASY_P02_ID);

            Assert.AreEqual(EBossDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(WOLBAEK_ID, easy.BossId);
            Assert.AreEqual(FIRST_PHASE, easy.Phase);
            Assert.AreEqual(15f, easy.BaseWeight);
            Assert.AreEqual(EEnemyAttackShape.Fan, easy.Shape.Shape);
            Assert.AreEqual(EASY_RANGE, easy.Shape.Range);
            Assert.AreEqual(EASY_ARC, easy.Shape.ArcAngle);
            Assert.AreEqual(1f, easy.TelegraphSeconds);
            Assert.AreEqual(0.5f, easy.TrackingSeconds);
            Assert.AreEqual(120f, easy.TurnRate);
            Assert.AreEqual(0f, easy.ActiveSeconds);
            Assert.AreEqual(0.5f, easy.RecoverySeconds);
            Assert.AreEqual(0.24f, easy.DamageRef);
            Assert.IsTrue(easy.Guardable);
            AssertTable(G_EasyRecent, easy.RecentUseMultipliers, "easy recent");
            AssertTable(new[] { 1.8f, 0.8f, 0.1f }, easy.DistanceMultipliers, "easy distance");
            AssertTable(new[] { 1.8f, 0.7f, 0.2f }, easy.DirectionMultipliers, "easy direction");
            AssertTable(new[] { 1.1f, 1.3f, 1.4f }, easy.NearbyMultipliers, "easy nearby");
        }

        /****************************************
        *      Easy And Hard Stay Separated
        ****************************************/

        [Test]
        public void Adapters_CollectOwnDifficultyOnly_HardPhaseOneHoldsP02()
        {
            BossPatternCatalogSO catalog = LoadAsset();
            var easy   = new BossPatternCatalogSource(catalog, EBossDifficulty.Easy);
            var hard   = new BossPatternCatalogSource(catalog, EBossDifficulty.Hard);
            var legacy = new BossPatternCatalogSource(catalog);

            List<string> easyOne = Collect(easy, FIRST_PHASE);
            Assert.AreEqual(EASY_PHASE_ONE_COUNT, easyOne.Count);
            CollectionAssert.Contains(easyOne, EASY_P02_ID);
            CollectionAssert.DoesNotContain(easyOne, HARD_P02_ID);
            CollectionAssert.AreEqual(easyOne, Collect(legacy, FIRST_PHASE));

            List<string> hardOne = Collect(hard, FIRST_PHASE);
            CollectionAssert.Contains(hardOne, HARD_P02_ID);
            CollectionAssert.Contains(hardOne, HARD_P01_ID);
            CollectionAssert.DoesNotContain(hardOne, EASY_P02_ID);
            CollectionAssert.AreEqual(new[] { BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P02_ID,
                BossPatternCatalogSO.WOLBAEK_HARD_P2_P03_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P04_ID,
                BossPatternCatalogSO.WOLBAEK_HARD_P2_P05_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID,
                BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID },
                Collect(hard, SECOND_PHASE), "하드 2페이즈는 P01~P07 일곱 개다");
            CollectionAssert.DoesNotContain(Collect(easy, SECOND_PHASE), HARD_P02_ID);
        }

        // 하드 1페이즈 여섯 개가 모두 들어와 하드 1페이즈 튜닝이 통과한다. 하드 2페이즈도 일곱 개가 갖춰져 통과한다
        [Test]
        public void Tuning_HardPhaseOneHasSixPatterns_PassesAndPhaseTwoHasSeven()
        {
            BossPatternCatalogSO catalog = LoadAsset();
            var errors = new List<string>();
            catalog.ValidateTuning(WOLBAEK_ID, FIRST_PHASE, EBossDifficulty.Hard, errors);

            Assert.AreEqual(EASY_PHASE_ONE_COUNT, Collect(new BossPatternCatalogSource(catalog, EBossDifficulty.Hard), FIRST_PHASE).Count);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID, EBossDifficulty.Hard));
            Assert.IsTrue(new BossPatternCatalogSource(catalog, EBossDifficulty.Hard).IsTuned(WOLBAEK_ID));
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, SECOND_PHASE, EBossDifficulty.Hard), "하드 2페이즈 일곱 개가 갖춰졌다");
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, Get(catalog, HARD_P02_ID)));
        }

        // 이지 튜닝 결과와 부팅 검증은 하드 P02가 있어도 그대로다
        [Test]
        public void Tuning_EasyResults_AreUnchangedByHardP02()
        {
            BossPatternCatalogSO catalog = LoadAsset();

            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));
            Assert.AreEqual(catalog.IsTuned(WOLBAEK_ID), new BossPatternCatalogSource(catalog).IsTuned(WOLBAEK_ID));
            Assert.AreEqual(catalog.IsTuned(WOLBAEK_ID), new BossPatternCatalogSource(catalog, EBossDifficulty.Easy).IsTuned(WOLBAEK_ID));
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, SECOND_PHASE));

            var errors = new List<string>();
            catalog.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }
    }
}
