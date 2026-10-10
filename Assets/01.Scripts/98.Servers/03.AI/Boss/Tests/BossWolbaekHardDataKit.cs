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
    /// <summary> 월백 하드 P04~P06 데이터 테스트가 함께 쓰는 에셋 읽기와 이지 대조 도우미 </summary>
    internal static class BossWolbaekHardDataKit
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        public const string WOLBAEK_ID   = "boss_wolbaek";
        public const int    FIRST_PHASE  = 1;
        public const int    SECOND_PHASE = 2;
        public const int    PHASE_ONE_COUNT = 6;

        // 하드 최근 사용 배율은 하드 P01~P03과 같다
        public static readonly float[] G_EasyRecent = { 0f, 0.3f, 0.7f, 1f };
        public static readonly float[] G_HardRecent = { 0.25f, 0.3f, 0.7f, 1f };

        /****************************************
        *                Methods
        ****************************************/

        public static BossPatternCatalogSO LoadAsset()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        public static PatternDefinition Get(BossPatternCatalogSO catalog, string id)
        {
            Assert.IsTrue(catalog.TryGetPattern(id, out PatternDefinition pattern), id);
            return pattern;
        }

        public static void AssertTable(float[] expected, IReadOnlyList<float> actual, string label)
        {
            Assert.AreEqual(expected.Length, actual.Count, label);
            for (int i = 0; i < expected.Length; ++i)
            {
                Assert.AreEqual(expected[i], actual[i], label + "[" + i + "]");
            }
        }

        public static void AssertSameTable(IReadOnlyList<float> expected, IReadOnlyList<float> actual, string label)
        {
            Assert.AreEqual(expected.Count, actual.Count, label);
            for (int i = 0; i < expected.Count; ++i)
            {
                Assert.AreEqual(expected[i], actual[i], label + "[" + i + "]");
            }
        }

        public static List<string> Collect(IBossPatternCatalog catalog, int phase)
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

        // 하드가 이지와 달라도 되는 항목(ID, 표시 키, 난이도, 최근 사용 배율, 도형의 사거리와 폭)을 뺀 나머지 필드가 모두 같은가
        // 도형의 사거리와 폭은 패턴마다 바뀌는 쪽이 달라 호출한 테스트가 따로 확인한다
        public static void AssertSameExceptTunedFields(PatternDefinition easy, PatternDefinition hard)
        {
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
            Assert.AreEqual(easy.Shape.ArcAngle, hard.Shape.ArcAngle);
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

        // 하드 정의의 ID, 표시 키, 난이도, 보스, 페이즈가 계약대로인가. 이지 ID와 겹치지 않는다
        public static void AssertHardIdentity(PatternDefinition hard, string hardId, string easyId)
        {
            Assert.AreEqual(hardId, hard.DefinitionId);
            Assert.AreEqual(EBossDifficulty.Hard, hard.Difficulty);
            Assert.AreEqual(WOLBAEK_ID, hard.BossId);
            Assert.AreEqual(FIRST_PHASE, hard.Phase);
            Assert.AreEqual(hardId + ".name", hard.DisplayKey);
            Assert.AreNotEqual(easyId, hard.DefinitionId);
        }

        // 이지와 하드 수집이 서로의 정의를 섞지 않는다
        public static void AssertAdaptersSeparated(string easyId, string hardId)
        {
            BossPatternCatalogSO catalog = LoadAsset();
            var easy   = new BossPatternCatalogSource(catalog, EBossDifficulty.Easy);
            var hard   = new BossPatternCatalogSource(catalog, EBossDifficulty.Hard);
            var legacy = new BossPatternCatalogSource(catalog);

            List<string> easyOne = Collect(easy, FIRST_PHASE);
            Assert.AreEqual(PHASE_ONE_COUNT, easyOne.Count);
            CollectionAssert.Contains(easyOne, easyId);
            CollectionAssert.DoesNotContain(easyOne, hardId);
            CollectionAssert.AreEqual(easyOne, Collect(legacy, FIRST_PHASE));

            List<string> hardOne = Collect(hard, FIRST_PHASE);
            CollectionAssert.Contains(hardOne, hardId);
            CollectionAssert.DoesNotContain(hardOne, easyId);
            Assert.IsEmpty(Collect(hard, SECOND_PHASE));
            CollectionAssert.DoesNotContain(Collect(easy, SECOND_PHASE), hardId);
        }

        // 이지 튜닝 결과와 부팅 검증은 하드 정의가 있어도 그대로다
        public static void AssertEasyResultsUnchanged()
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
