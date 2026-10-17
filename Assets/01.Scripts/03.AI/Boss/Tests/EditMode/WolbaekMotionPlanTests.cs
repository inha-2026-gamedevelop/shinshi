// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Data;

namespace Shinshi.Tests
{
    // 시험 씬에서 사용자가 확정한 월백 모션 값이 정식 이식 코드에서 같은 지연과 속도와 시간표로 나오는지 확인한다
    // 서버 사건과 Animator 재생은 PlayMode나 실제 보스 연결에서 따로 확인해야 하며 여기서 다루지 않는다
    public class WolbaekMotionPlanTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CONTROLLER_PATH = "Assets/01.Scripts/03.AI/Boss/Animation/AC_Wolbaek.controller";
        private const float  TOLERANCE       = 0.001f;

        /****************************************
        *               Tests
        ****************************************/

        // 판정 시각, 힘 순간으로 확정한 지연과 속도를 그대로 낸다
        [TestCase("wolbaek_p02", 1.0f, 1f, 0.39f)]
        [TestCase("wolbaek_p03", 1.2f, 1f, 0.27f)]
        [TestCase("wolbaek_p04", 0.9f, 1f, 0.46f)]
        [TestCase("wolbaek_p05", 1.0f, 1f, 0.53f)]
        [TestCase("wolbaek_p06", 1.6f, 1.4375f, 0f)]
        [TestCase("wolbaek_ph2_p04", 1.0f, 1f, 0.53f)]
        [TestCase("wolbaek_hard_ph2_p04", 4.0f, 1f, 3.53f)]
        [TestCase("wolbaek_ph2_p06", 1.2f, 1f, 0.27f)]
        [TestCase("wolbaek_hard_ph2_p06", 2.0f, 1f, 1.07f)]
        [TestCase("wolbaek_ph2_p07", 1.5f, 1.7f, 0f)]
        [TestCase("wolbaek_hard_ph2_p07", 1.5f, 1.7f, 0f)]
        public void ResolveStart_ConfirmedPattern_ReturnsConfirmedSpeedAndDelay(string patternId, float strikeSeconds, float speed, float delay)
        {
            Assert.That(WolbaekMotionCatalog.TryGet(patternId, out WolbaekMotionEntry entry), Is.True);

            WolbaekMotionPlan.ResolveStart(strikeSeconds, in entry, out float resolvedSpeed, out float resolvedDelay);

            Assert.That(resolvedSpeed, Is.EqualTo(speed).Within(TOLERANCE));
            Assert.That(resolvedDelay, Is.EqualTo(delay).Within(TOLERANCE));
        }

        // 이지 2P P05 3연 투사체는 0.49배속으로 고정하고 던지는 순간이 첫 발에 오게 한다
        [Test]
        public void ResolveStart_ThreeShotProjectile_UsesForcedSpeed()
        {
            WolbaekMotionCatalog.TryGet(BossPatternCatalogSO.WOLBAEK_P2_P05_ID, out WolbaekMotionEntry entry);

            WolbaekMotionPlan.ResolveStart(0.9f, in entry, out float speed, out float delay);

            Assert.That(speed, Is.EqualTo(0.49f).Within(TOLERANCE));
            Assert.That(delay, Is.EqualTo(0.9f - (0.44f / 0.49f)).Within(TOLERANCE));
        }

        [Test]
        public void ResolveSpeed_SlowSegment_AppliesOnlyInsideTheClipRange()
        {
            var entry = new WolbaekMotionEntry("state", 0.4f, 0f, 0.4f, 2.2f, 0.3f);

            Assert.That(WolbaekMotionPlan.ResolveSpeed(in entry, 1f, 0.2f), Is.EqualTo(1f));
            Assert.That(WolbaekMotionPlan.ResolveSpeed(in entry, 1f, 1.0f), Is.EqualTo(0.3f));
            Assert.That(WolbaekMotionPlan.ResolveSpeed(in entry, 1f, 2.5f), Is.EqualTo(1f));
        }

        [Test]
        public void ResolveSpeed_WithoutSlowSegment_ReturnsBaseSpeed()
        {
            WolbaekMotionCatalog.TryGet("wolbaek_p02", out WolbaekMotionEntry entry);

            Assert.That(WolbaekMotionPlan.ResolveSpeed(in entry, 1.2f, 0.5f), Is.EqualTo(1.2f));
        }

        // 돌진 배속은 클립 길이 / 돌진 시간이고 상한 4배다 (8m는 0.45초, 5m는 약 0.28초)
        [TestCase(0.45f, 2.962f)]
        [TestCase(0.28f, 4f)]
        [TestCase(0f, 4f)]
        public void ResolveDashSpeed_DashSeconds_IsCappedAtMaximum(float dashSeconds, float expected)
        {
            Assert.That(WolbaekMotionPlan.ResolveDashSpeed(dashSeconds), Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void ResolveDashRecoverSpeed_EasesFromDashSpeedToOne()
        {
            Assert.That(WolbaekMotionPlan.ResolveDashRecoverSpeed(3f, 0f), Is.EqualTo(3f).Within(TOLERANCE));
            Assert.That(WolbaekMotionPlan.ResolveDashRecoverSpeed(3f, WolbaekMotionPlan.DASH_RECOVER_SECONDS), Is.EqualTo(1f).Within(TOLERANCE));
            Assert.That(WolbaekMotionPlan.ResolveDashBlendSeconds(3f), Is.EqualTo(1f).Within(TOLERANCE));
        }

        // P07 시간표: 분해 1.0, 재결합 1.15, 모델 표시 1.4 (확정, 바꾸지 않는다)
        [TestCase(0.0, WolbaekMotionPlan.EWarpStage.Somersault)]
        [TestCase(0.999, WolbaekMotionPlan.EWarpStage.Somersault)]
        [TestCase(1.0, WolbaekMotionPlan.EWarpStage.Hidden)]
        [TestCase(1.149, WolbaekMotionPlan.EWarpStage.Hidden)]
        [TestCase(1.15, WolbaekMotionPlan.EWarpStage.Reforming)]
        [TestCase(1.399, WolbaekMotionPlan.EWarpStage.Reforming)]
        [TestCase(1.4, WolbaekMotionPlan.EWarpStage.Shown)]
        [TestCase(2.5, WolbaekMotionPlan.EWarpStage.Shown)]
        public void ResolveWarpStage_Elapsed_FollowsConfirmedTimetable(double elapsed, WolbaekMotionPlan.EWarpStage expected)
        {
            Assert.That(WolbaekMotionPlan.ResolveWarpStage(elapsed), Is.EqualTo(expected));
        }

        [Test]
        public void Catalog_IdsWithoutPatternNumberInEvents_AreNotHandled()
        {
            // 빔, 별, 격자, 반월 연쇄, 난사는 서버 사건에 패턴 번호가 없어 아직 다루지 않는다
            Assert.That(WolbaekMotionDirector.Handles(BossPatternCatalogSO.WOLBAEK_P2_P01_ID), Is.False);
            Assert.That(WolbaekMotionDirector.Handles(BossPatternCatalogSO.WOLBAEK_P2_P02_ID), Is.False);
            Assert.That(WolbaekMotionDirector.Handles(BossPatternCatalogSO.WOLBAEK_P2_P03_ID), Is.False);
            Assert.That(WolbaekMotionDirector.Handles(BossPatternCatalogSO.WOLBAEK_HARD_P2_P05_ID), Is.False);
            Assert.That(WolbaekMotionDirector.Handles(null), Is.False);
        }

        [Test]
        public void Catalog_DashAndWarpPatterns_AreRecognized()
        {
            Assert.That(WolbaekMotionCatalog.IsDash("wolbaek_p01"), Is.True);
            Assert.That(WolbaekMotionCatalog.IsDash(BossPatternCatalogSO.WOLBAEK_HARD_P01_ID), Is.True);
            Assert.That(WolbaekMotionCatalog.IsWarp(BossPatternCatalogSO.WOLBAEK_P2_P07_ID), Is.True);
            Assert.That(WolbaekMotionCatalog.IsWarp(BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID), Is.True);
            Assert.That(WolbaekMotionCatalog.IsWarp("wolbaek_p02"), Is.False);
        }

        // 카탈로그와 처치, 등장, 돌진이 쓰는 상태 이름이 정식 컨트롤러에 모두 있어야 한다. 이름이 어긋나면 모션이 조용히 재생되지 않는다
        [Test]
        public void Controller_ContainsEveryStateTheCatalogUses()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
            Assert.That(controller, Is.Not.Null);

            var names = new HashSet<string>();
            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                names.Add(child.state.name);
            }

            var wanted = new List<string>
            {
                WolbaekMotionCatalog.IDLE_STATE, WolbaekMotionCatalog.APPEAR_STATE, WolbaekMotionCatalog.DEATH_STATE,
                WolbaekMotionCatalog.DASH_STATE, "dead01_hold"
            };

            string[] patternIds =
            {
                "wolbaek_p02", "wolbaek_p03", "wolbaek_p04", "wolbaek_p05", "wolbaek_p06",
                BossPatternCatalogSO.WOLBAEK_P2_P04_ID, BossPatternCatalogSO.WOLBAEK_P2_P05_ID, BossPatternCatalogSO.WOLBAEK_P2_P06_ID,
                BossPatternCatalogSO.WOLBAEK_P2_P07_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P04_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID,
                BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID
            };

            for (int i = 0; i < patternIds.Length; ++i)
            {
                Assert.That(WolbaekMotionCatalog.TryGet(patternIds[i], out WolbaekMotionEntry entry), Is.True, patternIds[i]);
                wanted.Add(entry.StateName);
            }

            for (int i = 0; i < wanted.Count; ++i)
            {
                Assert.That(names.Contains(wanted[i]), Is.True, "정식 컨트롤러에 상태가 없다: " + wanted[i]);
            }
        }
    }
}
