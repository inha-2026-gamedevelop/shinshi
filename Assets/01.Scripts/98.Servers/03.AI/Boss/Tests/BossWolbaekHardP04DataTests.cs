// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 에셋에 들어간 월백 하드 P04가 이지 P04와 ID, 난이도, 투사체 폭, 최근 사용 배율만 다르고, 이지 수집과 이지 검증,
    // 기존 이지 P04 값을 바꾸지 않는지 확인한다
    public class BossWolbaekHardP04DataTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string EASY_P04_ID = "wolbaek_p04";
        private const string HARD_P04_ID = "wolbaek_hard_p04";
        private const float  EASY_WIDTH  = 1f;
        private const float  HARD_WIDTH  = 1.2f;

        /****************************************
        *        Hard P04 Matches Easy P04
        ****************************************/

        [Test]
        public void Asset_HardP04_HasUniqueIdAndHardDifficulty()
        {
            PatternDefinition hard = BossWolbaekHardDataKit.Get(BossWolbaekHardDataKit.LoadAsset(), HARD_P04_ID);

            Assert.AreEqual(HARD_P04_ID, BossPatternCatalogSO.WOLBAEK_HARD_P04_ID);
            Assert.AreEqual(HARD_P04_ID, WolbaekPatternFactory.HARD_P04_ID);
            BossWolbaekHardDataKit.AssertHardIdentity(hard, HARD_P04_ID, EASY_P04_ID);
        }

        [Test]
        public void Asset_HardP04_DiffersFromEasyP04OnlyInWidthAndRecentUse()
        {
            BossPatternCatalogSO catalog = BossWolbaekHardDataKit.LoadAsset();
            PatternDefinition easy = BossWolbaekHardDataKit.Get(catalog, EASY_P04_ID);
            PatternDefinition hard = BossWolbaekHardDataKit.Get(catalog, HARD_P04_ID);

            Assert.AreEqual(HARD_WIDTH, hard.Shape.Width);
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_HardRecent, hard.RecentUseMultipliers, "hard recent");

            // 사거리는 이지와 같다. 폭 말고 도형 값이 바뀌지 않았다
            Assert.AreEqual(easy.Shape.Range, hard.Shape.Range);
            BossWolbaekHardDataKit.AssertSameExceptTunedFields(easy, hard);
        }

        // 기존 이지 P04는 ID, 난이도, 폭, 최근 사용 배율, 실행 값이 그대로다
        [Test]
        public void Asset_EasyP04_KeepsIdValuesAndWidth()
        {
            PatternDefinition easy = BossWolbaekHardDataKit.Get(BossWolbaekHardDataKit.LoadAsset(), EASY_P04_ID);

            Assert.AreEqual(EBossDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(BossWolbaekHardDataKit.WOLBAEK_ID, easy.BossId);
            Assert.AreEqual(BossWolbaekHardDataKit.FIRST_PHASE, easy.Phase);
            Assert.AreEqual(20f, easy.BaseWeight);
            Assert.AreEqual(EEnemyAttackShape.Projectile, easy.Shape.Shape);
            Assert.AreEqual(35f, easy.Shape.Range);
            Assert.AreEqual(EASY_WIDTH, easy.Shape.Width);
            Assert.AreEqual(10f, easy.Shape.ProjectileSpeed);
            Assert.AreEqual(0.9f, easy.TelegraphSeconds);
            Assert.AreEqual(0.45f, easy.TrackingSeconds);
            Assert.AreEqual(120f, easy.TurnRate);
            Assert.AreEqual(0f, easy.ActiveSeconds);
            Assert.AreEqual(0.5f, easy.RecoverySeconds);
            Assert.AreEqual(0.18f, easy.DamageRef);
            Assert.IsTrue(easy.Guardable);
            Assert.AreEqual(EBossActionState.Recover, easy.ControlPhase);
            Assert.AreEqual(0.5f, easy.ControlWindowSeconds);
            Assert.IsFalse(easy.ControlCancelsPattern);
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_EasyRecent, easy.RecentUseMultipliers, "easy recent");
            BossWolbaekHardDataKit.AssertTable(new[] { 0.5f, 1.4f, 1.7f }, easy.DistanceMultipliers, "easy distance");
            BossWolbaekHardDataKit.AssertTable(new[] { 1.3f, 1f, 0.6f }, easy.DirectionMultipliers, "easy direction");
            BossWolbaekHardDataKit.AssertTable(new[] { 1.2f, 0.9f, 0.6f }, easy.NearbyMultipliers, "easy nearby");
        }

        /****************************************
        *      Easy And Hard Stay Separated
        ****************************************/

        [Test]
        public void Adapters_CollectOwnDifficultyOnly_HardPhaseOneHoldsP04()
        {
            BossWolbaekHardDataKit.AssertAdaptersSeparated(EASY_P04_ID, HARD_P04_ID);
        }

        // 이지 튜닝 결과와 부팅 검증은 하드 P04가 있어도 그대로다
        [Test]
        public void Tuning_EasyResults_AreUnchangedByHardP04()
        {
            BossWolbaekHardDataKit.AssertEasyResultsUnchanged();
        }
    }
}
