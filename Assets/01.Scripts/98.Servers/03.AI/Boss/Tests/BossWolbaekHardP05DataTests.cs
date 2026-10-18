// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 에셋에 들어간 월백 하드 P05가 이지 P05와 ID, 난이도, 장판 반경, 최근 사용 배율만 다르고, 이지 수집과 이지 검증,
    // 기존 이지 P05 값을 바꾸지 않는지 확인한다
    public class BossWolbaekHardP05DataTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string EASY_P05_ID = "wolbaek_p05";
        private const string HARD_P05_ID = "wolbaek_hard_p05";
        private const float  EASY_RADIUS = 3f;
        private const float  HARD_RADIUS = 3.25f;

        /****************************************
        *        Hard P05 Matches Easy P05
        ****************************************/

        [Test]
        public void Asset_HardP05_HasUniqueIdAndHardDifficulty()
        {
            PatternDefinition hard = BossWolbaekHardDataKit.Get(BossWolbaekHardDataKit.LoadAsset(), HARD_P05_ID);

            Assert.AreEqual(HARD_P05_ID, BossPatternCatalogSO.WOLBAEK_HARD_P05_ID);
            Assert.AreEqual(HARD_P05_ID, WolbaekPatternFactory.HARD_P05_ID);
            BossWolbaekHardDataKit.AssertHardIdentity(hard, HARD_P05_ID, EASY_P05_ID);
        }

        [Test]
        public void Asset_HardP05_DiffersFromEasyP05OnlyInRadiusAndRecentUse()
        {
            BossPatternCatalogSO catalog = BossWolbaekHardDataKit.LoadAsset();
            PatternDefinition easy = BossWolbaekHardDataKit.Get(catalog, EASY_P05_ID);
            PatternDefinition hard = BossWolbaekHardDataKit.Get(catalog, HARD_P05_ID);

            Assert.AreEqual(HARD_RADIUS, hard.Shape.Range);
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_HardRecent, hard.RecentUseMultipliers, "hard recent");

            // 장판 도형은 반경만 쓰므로 폭은 이지와 같다. 지속, 틱, 동시 장판 수, 놓친 회차 처리도 이지와 같다
            Assert.AreEqual(easy.Shape.Width, hard.Shape.Width);
            BossWolbaekHardDataKit.AssertSameExceptTunedFields(easy, hard);
        }

        // 기존 이지 P05는 ID, 난이도, 반경, 최근 사용 배율, 실행 값이 그대로다
        [Test]
        public void Asset_EasyP05_KeepsIdValuesAndRadius()
        {
            PatternDefinition easy = BossWolbaekHardDataKit.Get(BossWolbaekHardDataKit.LoadAsset(), EASY_P05_ID);

            Assert.AreEqual(EBossDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(BossWolbaekHardDataKit.WOLBAEK_ID, easy.BossId);
            Assert.AreEqual(BossWolbaekHardDataKit.FIRST_PHASE, easy.Phase);
            Assert.AreEqual(15f, easy.BaseWeight);
            Assert.AreEqual(EEnemyAttackShape.TargetArea, easy.Shape.Shape);
            Assert.AreEqual(EASY_RADIUS, easy.Shape.Range);
            Assert.AreEqual(2f, easy.TelegraphSeconds);
            Assert.AreEqual(0f, easy.TrackingSeconds);
            Assert.AreEqual(0f, easy.TurnRate);
            Assert.AreEqual(0f, easy.ActiveSeconds);
            Assert.AreEqual(1f, easy.RecoverySeconds);
            Assert.AreEqual(0.08f, easy.DamageRef);
            Assert.IsFalse(easy.Guardable);
            Assert.IsFalse(easy.HasControlWindow);
            Assert.AreEqual(6f, easy.AreaDurationSeconds);
            Assert.AreEqual(1f, easy.AreaTickInterval);
            Assert.AreEqual(3, easy.MaxConcurrentAreas);
            Assert.IsTrue(easy.AreaSkipsMissedRounds);
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_EasyRecent, easy.RecentUseMultipliers, "easy recent");
            BossWolbaekHardDataKit.AssertTable(new[] { 0.8f, 1.3f, 1.5f }, easy.DistanceMultipliers, "easy distance");
            BossWolbaekHardDataKit.AssertTable(new[] { 1f, 1f, 1f }, easy.DirectionMultipliers, "easy direction");
            BossWolbaekHardDataKit.AssertTable(new[] { 1f, 1.2f, 1.4f }, easy.NearbyMultipliers, "easy nearby");
        }

        /****************************************
        *      Easy And Hard Stay Separated
        ****************************************/

        [Test]
        public void Adapters_CollectOwnDifficultyOnly_HardPhaseOneHoldsP05()
        {
            BossWolbaekHardDataKit.AssertAdaptersSeparated(EASY_P05_ID, HARD_P05_ID);
        }

        // 이지 튜닝 결과와 부팅 검증은 하드 P05가 있어도 그대로다
        [Test]
        public void Tuning_EasyResults_AreUnchangedByHardP05()
        {
            BossWolbaekHardDataKit.AssertEasyResultsUnchanged();
        }
    }
}
