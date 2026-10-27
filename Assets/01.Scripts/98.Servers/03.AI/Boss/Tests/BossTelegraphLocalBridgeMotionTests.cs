// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 개발 씬 연결부(BossTelegraphLocalBridge)가 반월, 평행 빔, 회전 별을 시작하면 네트워크 연결부와 같은 종류와 시각으로 몸 모션을 계획하고,
    // 취소하면 그 시전의 모션을 끝내는지 본다. 실제 BossView의 애니메이터 재생과 다인 접속은 다루지 않는다
    public class BossTelegraphLocalBridgeMotionTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    CAST_ID      = 21;
        private const int    NEXT_CAST_ID = 22;
        private const int    FIRST_ID     = 31;
        private const int    SECOND_ID    = 32;
        private const double CLOCK        = 40.0;
        private const float  TELEGRAPH    = 1.5f;
        private const float  DURATION     = 4f;
        private const float  TICK         = 0.5f;
        private const float  ARENA_RADIUS = 20f;
        private const float  WIDTH        = 2f;
        private const double TIME_EPSILON = 0.000001;

        private const EBossInterruptReason CANCEL_REASON = EBossInterruptReason.PhaseTransition;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject             _root;
        private BossTelegraphPresenter _presenter;
        private MotionRecorder         _motion;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root      = new GameObject("TestPresenter");
            _presenter = _root.AddComponent<BossTelegraphPresenter>();
            _motion    = new MotionRecorder();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private BossTelegraphLocalBridge MakeBridge(IBossEventMotionSink motion)
        {
            var body = new BossTestRigBody(1, Vector3.zero, Vector3.forward, 100f, 0f, 1f, Vector3.zero, ARENA_RADIUS, null);
            return new BossTelegraphLocalBridge(_presenter, body, () => CLOCK, null, motion);
        }

        private static BossBeamShape Beam(float offset)
        {
            return new BossBeamShape(Vector3.zero, ARENA_RADIUS, Vector3.forward, offset, WIDTH);
        }

        private static BossStarShape Star()
        {
            return new BossStarShape(Vector3.zero, ARENA_RADIUS, Vector3.forward, 8f, 5, 2, WIDTH, 30f);
        }

        private void AssertBegin(int index, int castId, EWolbaekEventMotion kind)
        {
            MotionCall call = _motion.Calls[index];
            Assert.IsTrue(call.IsBegin, "모션 시작 호출이다");
            Assert.AreEqual(castId, call.CastId);
            Assert.AreEqual(kind, call.Kind);
            Assert.AreEqual(CLOCK, call.StartServerTime, TIME_EPSILON, "예고 시작 시각을 시전 시작 시각으로 쓴다");
            Assert.AreEqual(TELEGRAPH, call.StrikeSeconds, "힘 순간을 예고 끝에 맞춘다");
        }

        private void AssertEnd(int index, int castId)
        {
            MotionCall call = _motion.Calls[index];
            Assert.IsFalse(call.IsBegin, "모션 종료 호출이다");
            Assert.AreEqual(castId, call.CastId);
            Assert.IsTrue(call.IsCancelled, "취소로 끝낸다");
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Beam_TwoLinesOfOneCast_PlansBeamPairOnce_CancelEndsCast()
        {
            IBossBeamTelegraphListener beams = MakeBridge(_motion);
            BossBeamShape left  = Beam(-2f);
            BossBeamShape right = Beam(2f);

            beams.OnTelegraphStarted(CAST_ID, FIRST_ID, in left, TELEGRAPH, DURATION, TICK);
            beams.OnTelegraphStarted(CAST_ID, SECOND_ID, in right, TELEGRAPH, DURATION, TICK);

            Assert.AreEqual(1, _motion.Calls.Count, "줄마다 시작 알림이 와도 시전마다 한 번만 계획한다");
            AssertBegin(0, CAST_ID, EWolbaekEventMotion.BeamPair);

            beams.OnCancelled(CAST_ID, FIRST_ID, CANCEL_REASON);

            Assert.AreEqual(2, _motion.Calls.Count);
            AssertEnd(1, CAST_ID);
        }

        [Test]
        public void Star_TwoStructuresOfOneCast_PlansStarRotatingOnce_CancelEndsCast()
        {
            IBossStarTelegraphListener stars = MakeBridge(_motion);
            BossStarShape shape = Star();

            stars.OnTelegraphStarted(CAST_ID, FIRST_ID, in shape, TELEGRAPH, DURATION, TICK);
            stars.OnTelegraphStarted(CAST_ID, SECOND_ID, in shape, TELEGRAPH, DURATION, TICK);

            Assert.AreEqual(1, _motion.Calls.Count);
            AssertBegin(0, CAST_ID, EWolbaekEventMotion.StarRotating);

            stars.OnCancelled(CAST_ID, FIRST_ID, CANCEL_REASON);

            Assert.AreEqual(2, _motion.Calls.Count);
            AssertEnd(1, CAST_ID);
        }

        [Test]
        public void Sweep_Started_PlansHalfMoon_CancelEndsCast()
        {
            IBossSweepTelegraphListener sweep = MakeBridge(_motion);

            sweep.OnSweepStarted(CAST_ID, Vector3.zero, Vector3.forward, true, 10f, 180f, 5, TELEGRAPH, 1f);

            Assert.AreEqual(1, _motion.Calls.Count);
            AssertBegin(0, CAST_ID, EWolbaekEventMotion.SweepHalfMoon);

            sweep.OnCancelled(CAST_ID, CANCEL_REASON);

            Assert.AreEqual(2, _motion.Calls.Count);
            AssertEnd(1, CAST_ID);
        }

        [Test]
        public void Beam_NextCast_PlansAgain()
        {
            IBossBeamTelegraphListener beams = MakeBridge(_motion);
            BossBeamShape shape = Beam(0f);

            beams.OnTelegraphStarted(CAST_ID, FIRST_ID, in shape, TELEGRAPH, DURATION, TICK);
            beams.OnTelegraphStarted(NEXT_CAST_ID, SECOND_ID, in shape, TELEGRAPH, DURATION, TICK);

            Assert.AreEqual(2, _motion.Calls.Count, "다음 시전은 새로 계획한다");
            AssertBegin(1, NEXT_CAST_ID, EWolbaekEventMotion.BeamPair);
        }

        [Test]
        public void NoMotionSink_StartAndCancel_DoNotThrow()
        {
            IBossBeamTelegraphListener beams = MakeBridge(null);
            BossBeamShape shape = Beam(0f);

            Assert.DoesNotThrow(() =>
            {
                beams.OnTelegraphStarted(CAST_ID, FIRST_ID, in shape, TELEGRAPH, DURATION, TICK);
                beams.OnCancelled(CAST_ID, FIRST_ID, CANCEL_REASON);
            }, "무철 시험처럼 몸 모션이 없는 연결부도 그대로 쓴다");
        }

        /****************************************
        *            Test Doubles
        ****************************************/

        private struct MotionCall
        {
            public bool                IsBegin;
            public int                 CastId;
            public EWolbaekEventMotion Kind;
            public double              StartServerTime;
            public float               StrikeSeconds;
            public bool                IsCancelled;
        }

        private sealed class MotionRecorder : IBossEventMotionSink
        {
            public readonly List<MotionCall> Calls = new List<MotionCall>();

            public void BeginEventMotion(int castId, EWolbaekEventMotion kind, double castStartServerTime, float strikeSeconds)
            {
                Calls.Add(new MotionCall
                {
                    IsBegin = true, CastId = castId, Kind = kind, StartServerTime = castStartServerTime, StrikeSeconds = strikeSeconds
                });
            }

            public void EndEventMotion(int castId, bool isCancelled)
            {
                Calls.Add(new MotionCall { IsBegin = false, CastId = castId, IsCancelled = isCancelled });
            }
        }
    }
}
