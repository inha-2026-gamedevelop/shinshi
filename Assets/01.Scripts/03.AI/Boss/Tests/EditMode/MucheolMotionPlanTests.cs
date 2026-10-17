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

namespace Shinshi.Tests
{
    // 사용자가 무철 모션 프리뷰에서 확정한 시간 키가 서버 사건 시각으로 옮겨도 같은 클립 시간을 내는지 확인한다
    // Animator 재생과 서버 사건 연결은 PlayMode나 실제 보스 연결에서 따로 확인해야 하며 여기서 다루지 않는다
    public class MucheolMotionPlanTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CONTROLLER_PATH = "Assets/01.Scripts/03.AI/Boss/Animation/AC_Mucheol.controller";
        private const string CLIP_FOLDER     = "Assets/99.ThirdParty/Animation/Gient_Golem_AnimSet/Animation/Humanoid/Inplace/";
        private const double START           = 100.0;
        private const float  TOLERANCE       = 0.001f;

        /****************************************
        *               Tests
        ****************************************/

        // 1페이즈 P05: 예고 0.8초, 둘째 타 0.5초 뒤. 프리뷰 키 0->0, 0.8->0.84, 1.3->2.4, 2.5->끝
        [Test]
        public void Resolve_PhaseOneCombo_MatchesConfirmedTimeKeys()
        {
            MucheolMotionTrack track = ResolveStrike(MucheolMotionCatalog.P1_P05_ID, 0.8f, 0f, 0.5f);

            AssertKeys(track, MucheolMotionCatalog.COMBO_CLIP,
                new[] { 0.0, 0.8, 1.3, 2.5 }, new[] { 0f, 0.84f, 2.4f, 3.3333335f });
        }

        // 2페이즈 P05: 예고 0.9초, 타 지연 0, 0.45, 1.0. 프리뷰 키 0->0, 0.9->0.84, 1.35->1.2, 1.9->2.4, 3.4->끝
        [Test]
        public void Resolve_PhaseTwoCombo_MatchesConfirmedTimeKeys()
        {
            MucheolMotionTrack track = ResolveStrike(MucheolMotionCatalog.P2_P05_ID, 0.9f, 0f, 0.45f, 1f);

            AssertKeys(track, MucheolMotionCatalog.COMBO_CLIP,
                new[] { 0.0, 0.9, 1.35, 1.9, 3.4 }, new[] { 0f, 0.84f, 1.2f, 2.4f, 3.3333335f });
        }

        // 1페이즈 P01은 attack01이고 0.7초 타격에 클립 1.78초가 온다
        [Test]
        public void Resolve_PhaseOneSwing_HitsConfirmedClipAtStrike()
        {
            MucheolMotionTrack track = ResolveStrike(MucheolMotionCatalog.P1_P01_ID, 0.7f);

            Assert.That(track.ClipName, Is.EqualTo(MucheolMotionCatalog.SWING_CLIP));
            MucheolMotionPlan.Sample(track, START + 0.7, out float clip, out _);
            Assert.That(clip, Is.EqualTo(1.78f).Within(TOLERANCE));
        }

        // 서버 타 시각이 프리뷰와 달라져도 타격 키는 서버 시각에 붙는다
        [Test]
        public void Resolve_ShiftedServerStrike_FollowsServerTime()
        {
            MucheolMotionTrack track = ResolveStrike(MucheolMotionCatalog.P1_P05_ID, 0.8f, 0f, 0.7f);

            MucheolMotionPlan.Sample(track, START + 1.5, out float clip, out _);
            Assert.That(clip, Is.EqualTo(2.4f).Within(TOLERANCE));
        }

        // P05는 타마다 다시 시작하지 않고 한 클립 한 구간이다
        [TestCase(MucheolMotionCatalog.P1_P05_ID)]
        [TestCase(MucheolMotionCatalog.P2_P05_ID)]
        public void TryGetStrike_Combo_IsSingleAttack02Segment(string patternId)
        {
            Assert.That(MucheolMotionCatalog.TryGetStrike(patternId, out MucheolMotionSegment[] segments), Is.True);
            Assert.That(segments.Length, Is.EqualTo(1));
            Assert.That(segments[0].ClipName, Is.EqualTo(MucheolMotionCatalog.COMBO_CLIP));
        }

        [TestCase(MucheolMotionCatalog.P1_P01_ID, true)]
        [TestCase(MucheolMotionCatalog.P1_P03_ID, true)]
        [TestCase(MucheolMotionCatalog.P1_P06_ID, true)]
        [TestCase(MucheolMotionCatalog.P2_P06_ID, true)]
        [TestCase(MucheolMotionCatalog.P2_P07_ID, true)]
        [TestCase("wolbaek_p02", false)]
        [TestCase(null, false)]
        public void Handles_PatternId_ReturnsExpected(string patternId, bool expected)
        {
            Assert.That(MucheolMotionCatalog.Handles(patternId), Is.EqualTo(expected));
        }

        // 돌진 예고는 예고 끝에서 0.75초에 멈춰 돌진 시작을 기다린다
        [Test]
        public void Sample_DashWindupAfterTelegraph_HoldsWithZeroSpeed()
        {
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.CastStart    = START;
            anchors.TelegraphEnd = START + 1.6;
            MucheolMotionTrack track = MucheolMotionPlan.Resolve(MucheolMotionCatalog.DashWindup, in anchors);

            MucheolMotionPlan.Sample(track, START + 2.0, out float clip, out float speed);

            Assert.That(clip, Is.EqualTo(0.75f).Within(TOLERANCE));
            Assert.That(speed, Is.EqualTo(0f));
        }

        // 그로기 내려앉기는 0.87초부터 시작해 끝 자세에서 속도 0으로 유지한다
        [Test]
        public void Resolve_Groggy_SkipsRiseAndHoldsLastPose()
        {
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.Event     = START;
            anchors.GroggyEnd = START + 5.0;
            MucheolMotionTrack down = MucheolMotionPlan.Resolve(MucheolMotionCatalog.GroggyDown, in anchors);
            MucheolMotionTrack up   = MucheolMotionPlan.Resolve(MucheolMotionCatalog.GroggyUp, in anchors);

            Assert.That(down.StartClip, Is.EqualTo(0.87f).Within(TOLERANCE));
            MucheolMotionPlan.Sample(down, START + 2.0, out float holdClip, out float holdSpeed);
            Assert.That(holdSpeed, Is.EqualTo(0f));
            Assert.That(holdClip, Is.EqualTo(down.EndClip).Within(TOLERANCE));
            Assert.That(up.StartTime, Is.EqualTo(down.EndTime).Within(TOLERANCE));
            Assert.That(up.EndTime, Is.EqualTo(START + 5.0).Within(TOLERANCE));
        }

        // 그로기 끝을 모를 때는 끝 시각 키 없이 내려앉아 끝 자세에서 멈춘다
        [Test]
        public void Resolve_GroggyDownWithoutEnd_HoldsLastPose()
        {
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.Event = START;

            Assert.That(MucheolMotionPlan.Resolve(MucheolMotionCatalog.GroggyDown, in anchors), Is.Null);
            MucheolMotionTrack open = MucheolMotionPlan.Resolve(MucheolMotionCatalog.GroggyDownOpen, in anchors);
            Assert.That(open, Is.Not.Null);
            MucheolMotionPlan.Sample(open, START + 10.0, out float clip, out float speed);
            Assert.That(clip, Is.EqualTo(open.EndClip).Within(TOLERANCE));
            Assert.That(speed, Is.EqualTo(0f));
        }

        // 그로기가 내려앉기보다 짧아도 시각이 거꾸로 가지 않는다
        [Test]
        public void Resolve_ShortGroggy_KeepsTimesMonotonic()
        {
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.Event     = START;
            anchors.GroggyEnd = START + 0.5;
            MucheolMotionTrack down = MucheolMotionPlan.Resolve(MucheolMotionCatalog.GroggyDown, in anchors);

            for (int i = 1; i < down.KeyCount; ++i)
            {
                Assert.That(down.GetTime(i), Is.GreaterThanOrEqualTo(down.GetTime(i - 1)));
            }
        }

        // 모르는 기준 시각에 기대는 구간은 만들지 않는다
        [Test]
        public void Resolve_UnknownAnchor_ReturnsNull()
        {
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.CastStart = START;

            Assert.That(MucheolMotionPlan.Resolve(MucheolMotionCatalog.DashRun, in anchors), Is.Null);
        }

        // 돌진이 일찍 멈추면 들이받는 구간을 그 시각에서 끊고, 끊은 지점의 클립 시간에서 끝 자세로 잇는다
        [Test]
        public void TruncateAt_DashRun_EndsAtStopClipAndJoinsStopSpan()
        {
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.DashStart = START;
            anchors.DashEnd   = START + 0.5;
            MucheolMotionTrack run = MucheolMotionPlan.Resolve(MucheolMotionCatalog.DashRun, in anchors);

            MucheolMotionTrack cut = MucheolMotionPlan.TruncateAt(run, START + 0.3125);
            MucheolMotionTrack stop = MucheolMotionPlan.Span(MucheolMotionCatalog.DASH_CLIP, START + 0.3125, cut.EndClip,
                MucheolMotionCatalog.DashRunEndClip, MucheolMotionCatalog.DASH_STOP_SECONDS);

            Assert.That(cut.EndTime, Is.EqualTo(START + 0.3125).Within(TOLERANCE));
            Assert.That(cut.EndClip, Is.EqualTo(0.75f + (0.65f * 0.625f)).Within(TOLERANCE));
            Assert.That(MucheolMotionPlan.IsContinuous(cut, stop, TOLERANCE), Is.True);
            Assert.That(stop.EndTime, Is.EqualTo(START + 0.3125 + MucheolMotionCatalog.DASH_STOP_SECONDS).Within(TOLERANCE));
            Assert.That(stop.EndClip, Is.EqualTo(1.4f).Within(TOLERANCE));
        }

        // 구간 밖의 시각으로는 끊지 않는다(예정 끝 이후의 정지는 정상 끝과 같다)
        [TestCase(-0.1)]
        [TestCase(0.5)]
        [TestCase(0.7)]
        public void TruncateAt_OutsideTrack_ReturnsNull(double offset)
        {
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.DashStart = START;
            anchors.DashEnd   = START + 0.5;
            MucheolMotionTrack run = MucheolMotionPlan.Resolve(MucheolMotionCatalog.DashRun, in anchors);

            Assert.That(MucheolMotionPlan.TruncateAt(run, START + offset), Is.Null);
        }

        // 컨트롤러에 Idle과 모든 동작 클립의 두 상태가 있고 클립은 원본 Inplace 폴더의 것이다
        [Test]
        public void Controller_HasEveryActionStateFromOriginalClips()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
            Assert.That(controller, Is.Not.Null);

            var states = new Dictionary<string, Motion>();
            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                states[child.state.name] = child.state.motion;
            }

            Assert.That(states.ContainsKey(MucheolMotionCatalog.IDLE_CLIP), Is.True);
            foreach (string clip in MucheolMotionCatalog.ActionClips)
            {
                AssertState(states, clip, clip);
                AssertState(states, clip + MucheolMotionCatalog.ALTERNATE_SUFFIX, clip);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private static MucheolMotionTrack ResolveStrike(string patternId, float telegraphSeconds, params float[] delays)
        {
            Assert.That(MucheolMotionCatalog.TryGetStrike(patternId, out MucheolMotionSegment[] segments), Is.True);

            var strikes = new List<double>();
            for (int i = 0; i < delays.Length; ++i)
            {
                strikes.Add(START + telegraphSeconds + delays[i]);
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.CastStart    = START;
            anchors.TelegraphEnd = START + telegraphSeconds;
            anchors.Strikes      = strikes;

            MucheolMotionTrack track = MucheolMotionPlan.Resolve(in segments[0], in anchors);
            Assert.That(track, Is.Not.Null);
            return track;
        }

        private static void AssertKeys(MucheolMotionTrack track, string clipName, double[] offsets, float[] clips)
        {
            Assert.That(track.ClipName, Is.EqualTo(clipName));
            Assert.That(track.KeyCount, Is.EqualTo(offsets.Length));
            for (int i = 0; i < offsets.Length; ++i)
            {
                Assert.That(track.GetTime(i), Is.EqualTo(START + offsets[i]).Within(TOLERANCE));
                Assert.That(track.GetClip(i), Is.EqualTo(clips[i]).Within(TOLERANCE));
            }
        }

        private static void AssertState(Dictionary<string, Motion> states, string stateName, string clipName)
        {
            Assert.That(states.TryGetValue(stateName, out Motion motion), Is.True, stateName);
            Assert.That(motion, Is.Not.Null, stateName);
            Assert.That(motion.name, Is.EqualTo(clipName), stateName);
            Assert.That(AssetDatabase.GetAssetPath(motion), Does.StartWith(CLIP_FOLDER), stateName);
        }
    }
}
