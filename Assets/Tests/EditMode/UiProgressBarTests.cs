// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 진행 막대의 시간 채움 계산, 남은 시간 글자, 무한 대기 띠 위치와 상태 전환 </summary>
    public sealed class UiProgressBarTests
    {
        private const float TOLERANCE = 0.0001f;

        private GameObject  _root;
        private UiProgressBar _bar;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("UiProgressBarTest");
            _bar  = _root.AddComponent<UiProgressBar>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void ClampRatio_OutOfRangeAndNaN_StaysBetweenZeroAndOne()
        {
            Assert.AreEqual(0f, ProgressBarRules.ClampRatio(-0.5f), TOLERANCE);
            Assert.AreEqual(1f, ProgressBarRules.ClampRatio(3f), TOLERANCE);
            Assert.AreEqual(0f, ProgressBarRules.ClampRatio(float.NaN), TOLERANCE);
            Assert.AreEqual(0.4f, ProgressBarRules.ClampRatio(0.4f), TOLERANCE);
        }

        [Test]
        public void TimedRatio_FillAndDrain_MoveInOppositeDirections()
        {
            Assert.AreEqual(0.25f, ProgressBarRules.TimedRatio(0.75f, 3f, false), TOLERANCE);
            Assert.AreEqual(0.75f, ProgressBarRules.TimedRatio(0.75f, 3f, true), TOLERANCE);
            Assert.AreEqual(1f, ProgressBarRules.TimedRatio(9f, 3f, false), TOLERANCE);
            Assert.AreEqual(0f, ProgressBarRules.TimedRatio(9f, 3f, true), TOLERANCE);
        }

        [Test]
        public void TimedRatio_ZeroDuration_IsAlreadyFinished()
        {
            Assert.AreEqual(1f, ProgressBarRules.TimedRatio(0f, 0f, false), TOLERANCE);
            Assert.AreEqual(0f, ProgressBarRules.TimedRatio(0f, 0f, true), TOLERANCE);
        }

        [Test]
        public void RemainingTenths_RoundsUpToTenthOfSecond()
        {
            Assert.AreEqual(30, ProgressBarRules.RemainingTenths(3f));
            Assert.AreEqual(26, ProgressBarRules.RemainingTenths(2.51f));
            Assert.AreEqual(1, ProgressBarRules.RemainingTenths(0.01f));
            Assert.AreEqual(0, ProgressBarRules.RemainingTenths(-1f));
        }

        [Test]
        public void SweepAnchors_PhaseZeroAndOne_TouchBothEnds()
        {
            ProgressBarRules.SweepAnchors(0f, 0.25f, out float min, out float max);
            Assert.AreEqual(0f, min, TOLERANCE);
            Assert.AreEqual(0.25f, max, TOLERANCE);

            ProgressBarRules.SweepAnchors(1f, 0.25f, out min, out max);
            Assert.AreEqual(0.75f, min, TOLERANCE);
            Assert.AreEqual(1f, max, TOLERANCE);
        }

        [Test]
        public void Begin_FromIdle_BecomesActiveAtZero()
        {
            _bar.Begin("로그인");

            Assert.AreEqual(EProgressBarState.Active, _bar.State);
            Assert.IsTrue(_bar.IsRunning);
            Assert.AreEqual(0f, _bar.Ratio, TOLERANCE);
        }

        [Test]
        public void SetStep_WhileActive_KeepsClampedRatio()
        {
            _bar.Begin("로그인");

            _bar.SetStep(0.4f, "2/5");
            Assert.AreEqual(0.4f, _bar.Ratio, TOLERANCE);

            _bar.SetStep(5f, "9/5");
            Assert.AreEqual(1f, _bar.Ratio, TOLERANCE);
        }

        [Test]
        public void SetStep_AfterComplete_IsIgnored()
        {
            _bar.Begin("로그인");
            _bar.SetStep(0.5f, null);
            _bar.Complete(null);

            _bar.SetStep(0.1f, null);

            Assert.AreEqual(EProgressBarState.Completed, _bar.State);
            Assert.AreEqual(0.5f, _bar.Ratio, TOLERANCE);
        }

        [Test]
        public void Complete_WhileActive_RaisesCompletedOnce()
        {
            int count = 0;
            _bar.Completed += () => ++count;
            _bar.Begin("로그인");

            _bar.Complete(null);
            _bar.Complete(null);

            Assert.AreEqual(EProgressBarState.Completed, _bar.State);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Fail_WhileActive_RaisesFailedAndBlocksComplete()
        {
            int failed    = 0;
            int completed = 0;
            _bar.Failed    += () => ++failed;
            _bar.Completed += () => ++completed;
            _bar.Begin("채집");

            _bar.Fail("취소됨");
            _bar.Complete(null);

            Assert.AreEqual(EProgressBarState.Failed, _bar.State);
            Assert.AreEqual(1, failed);
            Assert.AreEqual(0, completed);
        }

        [Test]
        public void StartTimed_Advance_FillsThenCompletes()
        {
            int completed = 0;
            _bar.Completed += () => ++completed;
            _bar.StartTimed("채집", 2f, false);

            _bar.Advance(1f);
            Assert.AreEqual(0.5f, _bar.Ratio, TOLERANCE);
            Assert.IsTrue(_bar.IsRunning);

            _bar.Advance(1.5f);
            Assert.AreEqual(1f, _bar.Ratio, TOLERANCE);
            Assert.AreEqual(EProgressBarState.Completed, _bar.State);
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void StartTimed_Drain_StartsFullAndEmptiesToCompletion()
        {
            _bar.StartTimed("소환 유지", 2f, true);
            Assert.AreEqual(1f, _bar.Ratio, TOLERANCE);

            _bar.Advance(0.5f);
            Assert.AreEqual(0.75f, _bar.Ratio, TOLERANCE);

            _bar.Advance(2f);
            Assert.AreEqual(0f, _bar.Ratio, TOLERANCE);
            Assert.AreEqual(EProgressBarState.Completed, _bar.State);
        }

        [Test]
        public void StartTimed_ZeroSeconds_CompletesImmediately()
        {
            _bar.StartTimed("즉시", 0f, false);

            Assert.AreEqual(EProgressBarState.Completed, _bar.State);
        }

        [Test]
        public void Advance_AfterFail_DoesNothing()
        {
            _bar.StartTimed("채집", 2f, false);
            _bar.Advance(0.5f);
            _bar.Fail("취소됨");

            _bar.Advance(5f);

            Assert.AreEqual(EProgressBarState.Failed, _bar.State);
            Assert.AreEqual(0.25f, _bar.Ratio, TOLERANCE);
        }

        [Test]
        public void Begin_AfterFailure_RestartsFromZero()
        {
            _bar.StartTimed("채집", 2f, false);
            _bar.Advance(1f);
            _bar.Fail("취소됨");

            _bar.Begin("다시");

            Assert.AreEqual(EProgressBarState.Active, _bar.State);
            Assert.AreEqual(0f, _bar.Ratio, TOLERANCE);
        }
    }
}
