// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 에셋에 들어간 월백 하드 P06이 이지 P06과 ID, 난이도, 직선 폭, 최근 사용 배율만 다르고, 이지 수집과 이지 검증,
    // 기존 이지 P06 값을 바꾸지 않는지 확인한다. 이 파일은 하드 1페이즈 여섯 개가 모두 갖춰진 상태도 함께 확인한다
    public class BossWolbaekHardP06DataTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string EASY_P06_ID = "wolbaek_p06";
        private const string HARD_P06_ID = "wolbaek_hard_p06";
        private const float  EASY_WIDTH  = 4f;
        private const float  HARD_WIDTH  = 4.5f;
        private const float  TEST_HREF   = 3541f; // 테스트가 명시적으로 주입하는 값이다

        private static readonly string[] G_EasyIds =
        {
            "wolbaek_p01", "wolbaek_p02", "wolbaek_p03", "wolbaek_p04", "wolbaek_p05", "wolbaek_p06"
        };

        private static readonly string[] G_HardIds =
        {
            "wolbaek_hard_p01", "wolbaek_hard_p02", "wolbaek_hard_p03",
            "wolbaek_hard_p04", "wolbaek_hard_p05", "wolbaek_hard_p06"
        };

        /****************************************
        *               Helpers
        ****************************************/

        // 팩토리 시험용 이동 창구, 이동 자체는 쓰지 않는다
        private sealed class FixedMovement : IBossMovement
        {
            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return maxDistance;
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                return to;
            }
        }

        /****************************************
        *        Hard P06 Matches Easy P06
        ****************************************/

        [Test]
        public void Asset_HardP06_HasUniqueIdAndHardDifficulty()
        {
            PatternDefinition hard = BossWolbaekHardDataKit.Get(BossWolbaekHardDataKit.LoadAsset(), HARD_P06_ID);

            Assert.AreEqual(HARD_P06_ID, BossPatternCatalogSO.WOLBAEK_HARD_P06_ID);
            Assert.AreEqual(HARD_P06_ID, WolbaekPatternFactory.HARD_P06_ID);
            BossWolbaekHardDataKit.AssertHardIdentity(hard, HARD_P06_ID, EASY_P06_ID);
        }

        [Test]
        public void Asset_HardP06_DiffersFromEasyP06OnlyInWidthAndRecentUse()
        {
            BossPatternCatalogSO catalog = BossWolbaekHardDataKit.LoadAsset();
            PatternDefinition easy = BossWolbaekHardDataKit.Get(catalog, EASY_P06_ID);
            PatternDefinition hard = BossWolbaekHardDataKit.Get(catalog, HARD_P06_ID);

            Assert.AreEqual(HARD_WIDTH, hard.Shape.Width);
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_HardRecent, hard.RecentUseMultipliers, "hard recent");

            // 길이는 이지와 같다. 제어 구간, 피해, 방어 불가도 이지와 같다
            Assert.AreEqual(easy.Shape.Range, hard.Shape.Range);
            BossWolbaekHardDataKit.AssertSameExceptTunedFields(easy, hard);
        }

        // 기존 이지 P06은 ID, 난이도, 폭, 최근 사용 배율, 실행 값이 그대로다
        [Test]
        public void Asset_EasyP06_KeepsIdValuesAndWidth()
        {
            PatternDefinition easy = BossWolbaekHardDataKit.Get(BossWolbaekHardDataKit.LoadAsset(), EASY_P06_ID);

            Assert.AreEqual(EBossDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(BossWolbaekHardDataKit.WOLBAEK_ID, easy.BossId);
            Assert.AreEqual(BossWolbaekHardDataKit.FIRST_PHASE, easy.Phase);
            Assert.AreEqual(8f, easy.BaseWeight);
            Assert.AreEqual(EEnemyAttackShape.Line, easy.Shape.Shape);
            Assert.AreEqual(14f, easy.Shape.Range);
            Assert.AreEqual(EASY_WIDTH, easy.Shape.Width);
            Assert.AreEqual(3.2f, easy.TelegraphSeconds);
            Assert.AreEqual(0.6f, easy.TrackingSeconds);
            Assert.AreEqual(90f, easy.TurnRate);
            Assert.AreEqual(0f, easy.ActiveSeconds);
            Assert.AreEqual(1f, easy.RecoverySeconds);
            Assert.AreEqual(0.45f, easy.DamageRef);
            Assert.IsFalse(easy.Guardable);
            Assert.AreEqual(EBossActionState.Telegraph, easy.ControlPhase);
            Assert.AreEqual(0.4f, easy.ControlWindowSeconds);
            Assert.IsTrue(easy.ControlCancelsPattern);
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_EasyRecent, easy.RecentUseMultipliers, "easy recent");
            BossWolbaekHardDataKit.AssertTable(new[] { 0.9f, 1.2f, 1f }, easy.DistanceMultipliers, "easy distance");
            BossWolbaekHardDataKit.AssertTable(new[] { 1.6f, 0.6f, 0.1f }, easy.DirectionMultipliers, "easy direction");
            BossWolbaekHardDataKit.AssertTable(new[] { 1f, 1.2f, 1.3f }, easy.NearbyMultipliers, "easy nearby");
        }

        /****************************************
        *      Easy And Hard Stay Separated
        ****************************************/

        [Test]
        public void Adapters_CollectOwnDifficultyOnly_HardPhaseOneHoldsP06()
        {
            BossWolbaekHardDataKit.AssertAdaptersSeparated(EASY_P06_ID, HARD_P06_ID);
        }

        // 이지 튜닝 결과와 부팅 검증은 하드 P06이 있어도 그대로다
        [Test]
        public void Tuning_EasyResults_AreUnchangedByHardP06()
        {
            BossWolbaekHardDataKit.AssertEasyResultsUnchanged();
        }

        /****************************************
        *      Hard Phase One Is Complete
        ****************************************/

        // 이지 수집은 이지 여섯 개만, 하드 수집은 하드 여섯 개만 돌려주고 서로 겹치지 않는다
        [Test]
        public void Adapters_HardPhaseOneHoldsExactlyHardP01ToP06_AndEasyHoldsExactlyEasy()
        {
            BossPatternCatalogSO catalog = BossWolbaekHardDataKit.LoadAsset();
            List<string> easyOne = BossWolbaekHardDataKit.Collect(new BossPatternCatalogSource(catalog, EBossDifficulty.Easy), BossWolbaekHardDataKit.FIRST_PHASE);
            List<string> hardOne = BossWolbaekHardDataKit.Collect(new BossPatternCatalogSource(catalog, EBossDifficulty.Hard), BossWolbaekHardDataKit.FIRST_PHASE);

            CollectionAssert.AreEquivalent(G_EasyIds, easyOne);
            CollectionAssert.AreEquivalent(G_HardIds, hardOne);
            CollectionAssert.IsEmpty(System.Linq.Enumerable.Intersect(easyOne, hardOne));
        }

        // 하드 여섯 개의 최근 사용 배율은 모두 0.25 / 0.3 / 0.7 / 1이고 난이도는 모두 하드, 보스와 페이즈는 월백 1페이즈다
        [Test]
        public void Asset_AllHardPhaseOnePatterns_ShareRecentUseMultipliersAndHardIdentity()
        {
            BossPatternCatalogSO catalog = BossWolbaekHardDataKit.LoadAsset();

            for (int i = 0; i < G_HardIds.Length; ++i)
            {
                PatternDefinition hard = BossWolbaekHardDataKit.Get(catalog, G_HardIds[i]);
                Assert.AreEqual(EBossDifficulty.Hard, hard.Difficulty, G_HardIds[i]);
                Assert.AreEqual(BossWolbaekHardDataKit.WOLBAEK_ID, hard.BossId, G_HardIds[i]);
                Assert.AreEqual(BossWolbaekHardDataKit.FIRST_PHASE, hard.Phase, G_HardIds[i]);
                BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_HardRecent, hard.RecentUseMultipliers, G_HardIds[i] + " recent");
            }
        }

        // 필요한 창구를 모두 줬을 때 하드 여섯 개가 모두 실행 객체가 되고, 창구가 없는 패턴은 CanCreate와 Create가 같은 답을 한다
        [Test]
        public void Factory_AllHardPhaseOnePatterns_CreateWithWindows_AndCanCreateMatchesCreate()
        {
            BossPatternCatalogSO catalog = BossWolbaekHardDataKit.LoadAsset();
            var query = new FakeTargetQuery();
            var full = new WolbaekPatternFactory(TEST_HREF, null, null, null, null, new FixedMovement(),
                new BossProjectileManager(query), null, new BossAreaManager(query));
            var bare = new WolbaekPatternFactory(TEST_HREF);

            for (int i = 0; i < G_HardIds.Length; ++i)
            {
                PatternDefinition hard = BossWolbaekHardDataKit.Get(catalog, G_HardIds[i]);

                Assert.IsTrue(full.CanCreate(hard), G_HardIds[i]);
                Assert.IsNotNull(full.Create(hard), G_HardIds[i]);
                Assert.AreEqual(bare.CanCreate(hard), bare.Create(hard) != null, G_HardIds[i]);
            }

            // 이동, 투사체, 장판 창구가 필요한 P01, P04, P05만 창구가 없으면 만들 수 없다
            Assert.IsFalse(bare.CanCreate(BossWolbaekHardDataKit.Get(catalog, G_HardIds[0])));
            Assert.IsTrue(bare.CanCreate(BossWolbaekHardDataKit.Get(catalog, G_HardIds[1])));
            Assert.IsTrue(bare.CanCreate(BossWolbaekHardDataKit.Get(catalog, G_HardIds[2])));
            Assert.IsFalse(bare.CanCreate(BossWolbaekHardDataKit.Get(catalog, G_HardIds[3])));
            Assert.IsFalse(bare.CanCreate(BossWolbaekHardDataKit.Get(catalog, G_HardIds[4])));
            Assert.IsTrue(bare.CanCreate(BossWolbaekHardDataKit.Get(catalog, G_HardIds[5])));
        }

        // 하드 1페이즈 여섯 개가 갖춰져 하드 1페이즈 튜닝과 IsTuned가 통과한다. 하드 2페이즈도 일곱 개가 갖춰져 통과한다
        [Test]
        public void Tuning_HardPhaseOneComplete_PassesAndPhaseTwoHasSeven()
        {
            BossPatternCatalogSO catalog = BossWolbaekHardDataKit.LoadAsset();
            var errors = new List<string>();
            catalog.ValidateTuning(BossWolbaekHardDataKit.WOLBAEK_ID, BossWolbaekHardDataKit.FIRST_PHASE, EBossDifficulty.Hard, errors);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(catalog.IsPhaseTuned(BossWolbaekHardDataKit.WOLBAEK_ID, BossWolbaekHardDataKit.FIRST_PHASE, EBossDifficulty.Hard));
            Assert.IsTrue(catalog.IsTuned(BossWolbaekHardDataKit.WOLBAEK_ID, EBossDifficulty.Hard));
            Assert.IsTrue(new BossPatternCatalogSource(catalog, EBossDifficulty.Hard).IsTuned(BossWolbaekHardDataKit.WOLBAEK_ID));

            var phaseTwoErrors = new List<string>();
            catalog.ValidateTuning(BossWolbaekHardDataKit.WOLBAEK_ID, BossWolbaekHardDataKit.SECOND_PHASE, EBossDifficulty.Hard, phaseTwoErrors);
            Assert.IsTrue(catalog.IsPhaseTuned(BossWolbaekHardDataKit.WOLBAEK_ID, BossWolbaekHardDataKit.SECOND_PHASE, EBossDifficulty.Hard),
                "하드 2페이즈 일곱 개가 갖춰졌다");
            Assert.IsEmpty(phaseTwoErrors, string.Join("\n", phaseTwoErrors));

            for (int i = 0; i < G_HardIds.Length; ++i)
            {
                Assert.IsTrue(catalog.IsPatternReady(BossWolbaekHardDataKit.WOLBAEK_ID, BossWolbaekHardDataKit.Get(catalog, G_HardIds[i])), G_HardIds[i]);
            }
        }
    }
}
