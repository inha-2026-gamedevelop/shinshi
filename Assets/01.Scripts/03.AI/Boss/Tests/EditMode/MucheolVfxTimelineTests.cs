// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 프리뷰에서 확정한 VFX 배정과, 서버 시각을 프로필 시각으로 바꾸는 규칙이 프리뷰의 변환과 같은지 확인한다.
    // 재생기 표시와 서버 사건 연결은 표현 프리팹 재생이나 실제 보스 연결에서 따로 확인해야 하며 여기서 다루지 않는다
    public class MucheolVfxTimelineTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double START     = 100.0;
        private const float  TOLERANCE = 0.0005f;
        private const float  TELEGRAPH = 1.6667f;  // 원본 01 프로필
        private const float  DASH_TELE = 0.9666666f; // 원본 06 프로필
        private const float  CHARGE    = 1.033333f;  // 원본 06 돌진 머리 이동

        /****************************************
        *               Tests
        ****************************************/

        // 프리뷰: 예고 구간은 시작부터 타격까지를 프로필 예고 길이로 늘인다
        [TestCase(0.0, 0f)]
        [TestCase(0.35, 0.5f)]
        [TestCase(0.7, 1f)]
        public void ToProfileTime_StrikeBeforeImpact_ScalesTelegraph(double elapsed, float ratio)
        {
            float time = MucheolVfxTimeline.ToProfileTime(START + elapsed, START, START + 0.7, START + 0.7, double.NaN, TELEGRAPH, 0f);

            Assert.That(time, Is.EqualTo(TELEGRAPH * ratio).Within(TOLERANCE));
        }

        // 서버 확정이 늦으면 예고 끝에서 멈춰 기다린다, 스스로 타격하지 않는다
        [Test]
        public void ToProfileTime_StrikePastImpactWithoutHit_HoldsAtTelegraphEnd()
        {
            float time = MucheolVfxTimeline.ToProfileTime(START + 1.0, START, START + 0.7, START + 0.7, double.NaN, TELEGRAPH, 0f);

            Assert.That(time, Is.EqualTo(TELEGRAPH).Within(TOLERANCE));
        }

        // 타격 뒤는 서버 확정 시각부터 실제 초로 흐른다
        [Test]
        public void ToProfileTime_AfterHit_FollowsServerHitTime()
        {
            float time = MucheolVfxTimeline.ToProfileTime(START + 1.0, START, START + 0.7, START + 0.7, START + 0.75, TELEGRAPH, 0f);

            Assert.That(time, Is.EqualTo(TELEGRAPH + 0.25f).Within(TOLERANCE));
        }

        // 프리뷰 1페이즈 P03: 예고 0~0.9초, 돌진 0.9~1.4초가 돌진 머리 이동 구간이다
        [TestCase(0.45, 0.5f, 0f)]
        [TestCase(0.9, 1f, 0f)]
        [TestCase(1.15, 1f, 0.5f)]
        [TestCase(1.4, 1f, 1f)]
        public void ToProfileTime_Dash_MapsTelegraphThenCharge(double elapsed, float teleRatio, float chargeRatio)
        {
            float time = MucheolVfxTimeline.ToProfileTime(START + elapsed, START, START + 0.9, START + 1.4, double.NaN, DASH_TELE, CHARGE);

            Assert.That(time, Is.EqualTo((DASH_TELE * teleRatio) + (CHARGE * chargeRatio)).Within(TOLERANCE));
        }

        // 돌진 시작 사건 전에는 예정 타격을 몰라 예고 끝에서 기다린다
        [Test]
        public void ToProfileTime_DashBeforeStartEvent_HoldsAtTelegraphEnd()
        {
            float time = MucheolVfxTimeline.ToProfileTime(START + 1.2, START, START + 0.9, double.NaN, double.NaN, DASH_TELE, CHARGE);

            Assert.That(time, Is.EqualTo(DASH_TELE).Within(TOLERANCE));
        }

        [TestCase(1.0, false)]
        [TestCase(1.4, true)]
        public void IsImpactOver_LateHit_ComparesWithTail(double sinceHit, bool expected)
        {
            Assert.That(MucheolVfxTimeline.IsImpactOver(START + sinceHit, START, 1.3f), Is.EqualTo(expected));
        }

        // 프리뷰 Patterns의 EffectCue에서 옮긴 원본, 크기, 각도
        [TestCase(MucheolMotionCatalog.P1_P01_ID, 0, EMucheolVfxTemplate.Fan01, 1f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P1_P02_ID, 0, EMucheolVfxTemplate.Fan01, 0.4043f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P1_P03_ID, 0, EMucheolVfxTemplate.DashPath06, 0.83333f, 0.44444f, 0f)]
        [TestCase(MucheolMotionCatalog.P1_P04_ID, 0, EMucheolVfxTemplate.Circle02, 1f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P1_P05_ID, 0, EMucheolVfxTemplate.Fan01, 1f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P1_P05_ID, 1, EMucheolVfxTemplate.Fan01, 0.4043f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P1_P06_ID, 0, EMucheolVfxTemplate.DashPath06, 1f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P2_P01_ID, 0, EMucheolVfxTemplate.Fan01, 0.33333f, 1f, -30f)]
        [TestCase(MucheolMotionCatalog.P2_P01_ID, 1, EMucheolVfxTemplate.Fan01, 0.33333f, 1f, 30f)]
        [TestCase(MucheolMotionCatalog.P2_P02_ID, 0, EMucheolVfxTemplate.Fan01, 0.4043f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P2_P02_ID, 1, EMucheolVfxTemplate.Line03, 1f, 0.33333f, 0f)]
        [TestCase(MucheolMotionCatalog.P2_P03_ID, 0, EMucheolVfxTemplate.DashPath06, 0.83333f, 0.44444f, 0f)]
        [TestCase(MucheolMotionCatalog.P2_P04_ID, 0, EMucheolVfxTemplate.SmallWave04, 1f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P2_P04_ID, 1, EMucheolVfxTemplate.LargeWave05, 1f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P2_P05_ID, 0, EMucheolVfxTemplate.Fan01, 0.33333f, 1f, -30f)]
        [TestCase(MucheolMotionCatalog.P2_P05_ID, 1, EMucheolVfxTemplate.Fan01, 0.33333f, 1f, 30f)]
        [TestCase(MucheolMotionCatalog.P2_P05_ID, 2, EMucheolVfxTemplate.Fan01, 0.4043f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P2_P06_ID, 0, EMucheolVfxTemplate.DashPath06, 1f, 1f, 0f)]
        [TestCase(MucheolMotionCatalog.P2_P07_ID, 0, EMucheolVfxTemplate.Battlefield07, 1f, 1f, 0f)]
        public void TryGetCue_MatchesPreviewCue(string patternId, int index, EMucheolVfxTemplate template, float scaleX, float scaleZ,
            float yaw)
        {
            Assert.That(MucheolVfxCatalog.TryGetCue(patternId, index, out MucheolVfxCue cue), Is.True);
            Assert.That(cue.Template, Is.EqualTo(template));
            Assert.That(cue.Scale.x, Is.EqualTo(scaleX).Within(TOLERANCE));
            Assert.That(cue.Scale.y, Is.EqualTo(1f).Within(TOLERANCE));
            Assert.That(cue.Scale.z, Is.EqualTo(scaleZ).Within(TOLERANCE));
            Assert.That(cue.YawDegrees, Is.EqualTo(yaw).Within(TOLERANCE));
        }

        // 강화 폭주 돌진(3회)은 배정의 마지막 돌진을 다시 쓰고, 순차 타격은 배정보다 많은 단계를 만들지 않는다
        [Test]
        public void TryGetCue_ExtraIndex_RepeatsOnlyForMultiDash()
        {
            Assert.That(MucheolVfxCatalog.TryGetCue(MucheolMotionCatalog.P2_P06_ID, 2, out MucheolVfxCue dash), Is.True);
            Assert.That(dash.Template, Is.EqualTo(EMucheolVfxTemplate.DashPath06));
            Assert.That(MucheolVfxCatalog.TryGetCue(MucheolMotionCatalog.P2_P05_ID, 3, out _), Is.False);
        }

        // 모션을 이식한 패턴은 모두 VFX도 배정돼 있다
        [Test]
        public void Handles_EveryMotionPattern()
        {
            string[] ids =
            {
                MucheolMotionCatalog.P1_P01_ID, MucheolMotionCatalog.P1_P02_ID, MucheolMotionCatalog.P1_P03_ID,
                MucheolMotionCatalog.P1_P04_ID, MucheolMotionCatalog.P1_P05_ID, MucheolMotionCatalog.P1_P06_ID,
                MucheolMotionCatalog.P2_P01_ID, MucheolMotionCatalog.P2_P02_ID, MucheolMotionCatalog.P2_P03_ID,
                MucheolMotionCatalog.P2_P04_ID, MucheolMotionCatalog.P2_P05_ID, MucheolMotionCatalog.P2_P06_ID,
                MucheolMotionCatalog.P2_P07_ID,
            };

            foreach (string id in ids)
            {
                Assert.That(MucheolVfxCatalog.Handles(id), Is.True, id);
                Assert.That(MucheolMotionCatalog.Handles(id), Is.True, id);
            }

            Assert.That(MucheolVfxCatalog.Handles("wolbaek_p02"), Is.False);
        }
    }
}
