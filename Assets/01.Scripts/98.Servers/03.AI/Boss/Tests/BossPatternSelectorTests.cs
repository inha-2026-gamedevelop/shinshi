// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Data;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    public class BossPatternSelectorTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private BossDefinition _boss;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _boss = BossTestFixtures.MakeBoss();
        }

        /****************************************
        *                Bands
        ****************************************/

        [TestCase(0f, 0)]
        [TestCase(4.99f, 0)]
        [TestCase(5f, 1)]
        [TestCase(11.99f, 1)]
        [TestCase(12f, 2)]
        [TestCase(40f, 2)]
        public void GetDistanceBand_Boundaries_FollowNearMidFar(float distance, int expected)
        {
            Assert.AreEqual(expected, BossPatternSelector.GetDistanceBand(distance, _boss));
        }

        [TestCase(0f, 0)]
        [TestCase(60f, 0)]
        [TestCase(60.1f, 1)]
        [TestCase(120f, 1)]
        [TestCase(120.1f, 2)]
        [TestCase(180f, 2)]
        public void GetDirectionBand_Boundaries_IncludeEdgeInEarlierBand(float angle, int expected)
        {
            Assert.AreEqual(expected, BossPatternSelector.GetDirectionBand(angle, _boss));
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        public void GetNearbyBand_ZeroUsesFirstColumnAndThreeOrMoreUsesLast(int count, int expected)
        {
            Assert.AreEqual(expected, BossPatternSelector.GetNearbyBand(count));
        }

        /****************************************
        *               Weights
        ****************************************/

        [Test]
        public void ComputeWeight_MultipliesBaseAndAllFourTables()
        {
            PatternDefinition pattern = new PatternDefinition("p", BossTestFixtures.BOSS_ID, 1, 20f,
                new[] { 0.4f, 1.3f, 1.8f }, new[] { 1.2f, 1f, 0.8f }, new[] { 1f, 0.9f, 0.7f }, new[] { 0f, 0.3f, 0.7f, 1f },
                1f, 0f, 0f, 0f, 0f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 1f, true);

            // 중거리, 후방, 주변 1명, 최근 3회 안에 없음
            float weight = BossPatternSelector.ComputeWeight(pattern, 1, 2, 0, BossBlackboard.RecentSlotNone);

            Assert.AreEqual(20f * 1.3f * 0.8f * 1f * 1f, weight, 0.0001f);
        }

        [Test]
        public void ComputeWeight_EmptyMultiplierTable_IsZero()
        {
            PatternDefinition pattern = new PatternDefinition("p", BossTestFixtures.BOSS_ID, 1, 20f,
                new float[0], new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                1f, 0f, 0f, 0f, 0f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 1f, true);

            Assert.AreEqual(0f, BossPatternSelector.ComputeWeight(pattern, 0, 0, 0, 0));
        }

        /****************************************
        *                 Pick
        ****************************************/

        [TestCase(0f, 0)]
        [TestCase(0.2499f, 0)]
        [TestCase(0.25f, 1)]
        [TestCase(0.9999f, 1)]
        public void Pick_RollBoundaries_SplitByCumulativeWeight(float roll, int expected)
        {
            List<float> weights = new List<float> { 10f, 30f };

            Assert.AreEqual(expected, BossPatternSelector.Pick(weights, roll));
        }

        [Test]
        public void Pick_ZeroWeights_AreNeverSelected()
        {
            List<float> weights = new List<float> { 0f, 5f, 0f };

            Assert.AreEqual(1, BossPatternSelector.Pick(weights, 0f));
            Assert.AreEqual(1, BossPatternSelector.Pick(weights, 0.9999f));
        }

        [Test]
        public void Pick_TotalZeroOrEmpty_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, BossPatternSelector.Pick(new List<float> { 0f, 0f }, 0.5f));
            Assert.AreEqual(-1, BossPatternSelector.Pick(new List<float>(), 0.5f));
        }

        /****************************************
        *            Recent History
        ****************************************/

        [Test]
        public void GetRecentSlot_TracksLastThreeUses()
        {
            BossBlackboard blackboard = new BossBlackboard();

            Assert.AreEqual(BossBlackboard.RecentSlotNone, blackboard.GetRecentSlot("a"));

            blackboard.RecordUse("a");
            Assert.AreEqual(0, blackboard.GetRecentSlot("a"));

            blackboard.RecordUse("b");
            blackboard.RecordUse("c");
            Assert.AreEqual(0, blackboard.GetRecentSlot("c"));
            Assert.AreEqual(1, blackboard.GetRecentSlot("b"));
            Assert.AreEqual(2, blackboard.GetRecentSlot("a"));

            blackboard.RecordUse("d");
            Assert.AreEqual(BossBlackboard.RecentSlotNone, blackboard.GetRecentSlot("a"));
        }

        [Test]
        public void GetRecentSlot_SameIdTwice_UsesMostRecent()
        {
            BossBlackboard blackboard = new BossBlackboard();
            blackboard.RecordUse("a");
            blackboard.RecordUse("b");
            blackboard.RecordUse("a");

            Assert.AreEqual(0, blackboard.GetRecentSlot("a"));
        }
    }
}
