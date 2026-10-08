using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Shinshi.Tests
{
    public sealed class PresentationPageBaseTests
    {
        private const string PROBE_OP = "probe.op";

        private PresentationTestHarness _harness;

        [TearDown]
        public void TearDown()
        {
            if (_harness != null)
            {
                _harness.Dispose();
                _harness = null;
            }
        }

        /// <summary> 기반 클래스의 보호 멤버를 시험하기 위한 최소 구현. 실제 View를 만들지 않는다 </summary>
        private sealed class ProbePage : PresentationPageBase
        {
            public bool ViewRequired = false;
            public bool Reattach;
            public bool ThrowOnAttach;
            public bool ThrowOnApply;
            public int  AttachCount;
            public int  ApplyCount;
            public int  DetachCount;
            public int  SeedCount;

            public override string Id              { get { return "probe.page"; } }
            public override string Title           { get { return "프로브 화면"; } }
            public override string SourceScenePath { get { return string.Empty; } }
            public override string Requirement     { get { return "시험용"; } }

            public override bool HasView
            {
                get { return ViewRequired; }
            }

            public bool Run(UiSandboxContext context, Action onSuccess, Action<string> onFailure = null)
            {
                return RunRequest(context, PROBE_OP, onSuccess, onFailure);
            }

            protected override bool NeedsReattach(UiSandboxContext context)
            {
                return Reattach;
            }

            protected override void Seed(UiSandboxScenarioSO scenario)
            {
                ++SeedCount;
            }

            protected override void OnAttach(UiSandboxContext context)
            {
                ++AttachCount;
                if (ThrowOnAttach)
                {
                    throw new InvalidOperationException("probe 연결 예외");
                }
            }

            protected override void OnApply(UiSandboxContext context)
            {
                ++ApplyCount;
                if (ThrowOnApply)
                {
                    throw new InvalidOperationException("probe 적용 예외");
                }
            }

            protected override void OnDrawControls(UiSandboxContext context) { }

            protected override void OnDetach()
            {
                ++DetachCount;
            }
        }

        private static int CountOutcome(UiSandboxContext context, EUiSandboxRequestState state)
        {
            int count = 0;
            for (int i = 0; i < context.History.Count; ++i)
            {
                if (context.History[i].State == state)
                {
                    ++count;
                }
            }

            return count;
        }

        /****************************************
        *             Attach / Apply
        ****************************************/

        [Test]
        public void Attach_NullContext_Throws()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());

            Assert.Throws<ArgumentNullException>(() => page.Attach(null));
        }

        [Test]
        public void Attach_NoViewPage_SeedsAndAttachesWithoutTestScope()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());

            page.Attach(_harness.Context);

            Assert.IsTrue(page.IsAttached);
            Assert.IsFalse(page.IsRefused);
            Assert.AreEqual(1, page.SeedCount);
            Assert.AreEqual(1, page.AttachCount);
        }

        [Test]
        public void Attach_ViewPageWithoutTestScope_RefusesAndSkipsOnAttach()
        {
            _harness = new PresentationTestHarness(false);
            string reason;
            Assume.That(PresentationFixtureFactory.CanCreate(out reason), Is.False, "활성 씬이 샌드박스 씬이라 거절 경로를 확인할 수 없어 건너뜀");

            ProbePage page = _harness.Track(new ProbePage { ViewRequired = true });
            page.Attach(_harness.Context);
            page.Apply(_harness.Context);

            Assert.IsTrue(page.IsRefused);
            Assert.AreEqual(PresentationFixtureFactory.REFUSE_REASON, page.RefusalReason);
            Assert.AreEqual(0, page.AttachCount);
            Assert.AreEqual(0, page.ApplyCount);
            StringAssert.Contains(page.Title, _harness.Context.Status);
        }

        [Test]
        public void Attach_ViewPageWithTestScope_DoesNotRefuse()
        {
            _harness = new PresentationTestHarness();
            ProbePage page = _harness.Track(new ProbePage { ViewRequired = true });

            page.Attach(_harness.Context);

            Assert.IsFalse(page.IsRefused);
            Assert.AreEqual(1, page.AttachCount);
        }

        [Test]
        public void Attach_OnAttachThrows_RefusesWithExceptionReason()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage { ThrowOnAttach = true });
            LogAssert.Expect(LogType.Exception, new Regex("probe 연결 예외"));

            page.Attach(_harness.Context);

            Assert.IsTrue(page.IsRefused);
            StringAssert.Contains("InvalidOperationException", page.RefusalReason);
        }

        [Test]
        public void Apply_OnApplyThrows_ReportsStatusInsteadOfThrowing()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage { ThrowOnApply = true });
            page.Attach(_harness.Context);
            LogAssert.Expect(LogType.Exception, new Regex("probe 적용 예외"));

            Assert.DoesNotThrow(() => page.Apply(_harness.Context));

            StringAssert.Contains("적용 중 예외", _harness.Context.Status);
        }

        [Test]
        public void Apply_SameContext_DoesNotReattach()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);

            page.Apply(_harness.Context);
            page.Apply(_harness.Context);

            Assert.AreEqual(1, page.AttachCount);
            Assert.AreEqual(2, page.ApplyCount);
        }

        [Test]
        public void Apply_DifferentContext_ReattachesToNewContext()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            int detachBefore = page.DetachCount;
            UiSandboxContext other = _harness.CreateContext(null);

            try
            {
                page.Apply(other);

                Assert.AreEqual(2, page.AttachCount);
                Assert.AreEqual(detachBefore + 1, page.DetachCount);
            }
            finally
            {
                page.Detach();
                other.Dispose();
            }
        }

        [Test]
        public void Apply_NeedsReattachTrue_ReattachesEvenOnSameContext()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            page.Reattach = true;

            page.Apply(_harness.Context);

            Assert.AreEqual(2, page.AttachCount);
        }

        [Test]
        public void Apply_NullContext_DoesNothing()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);

            page.Apply(null);

            Assert.AreEqual(0, page.ApplyCount);
            Assert.IsTrue(page.IsAttached);
        }

        [Test]
        public void Apply_FixtureDestroyedExternally_RebuildsFixture()
        {
            _harness = new PresentationTestHarness();
            HudPartyPage page = _harness.Track(new HudPartyPage());
            page.Attach(_harness.Context);
            PresentationFixture first = page.Fixtures[0];
            first.Destroy();
            Assert.IsFalse(first.IsAlive);

            page.Apply(_harness.Context);

            Assert.AreEqual(1, page.Fixtures.Count);
            Assert.AreNotSame(first, page.Fixtures[0]);
            Assert.IsTrue(page.Fixtures[0].IsAlive);
            Assert.IsNotNull(page.PartyView);
        }

        /****************************************
        *               Requests
        ****************************************/

        [Test]
        public void RunRequest_Playing_ShowsPendingThenSuccessText()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            bool succeeded = false;

            bool started = page.Run(_harness.Context, () => succeeded = true);

            Assert.IsTrue(started);
            Assert.AreEqual(PROBE_OP + ": 요청 중", page.LastResponse);

            _harness.Tick();

            Assert.IsTrue(succeeded);
            Assert.AreEqual(PROBE_OP + ": 성공 (모의 응답)", page.LastResponse);
        }

        [Test]
        public void RunRequest_Rejected_ShowsFailureMessageAndCallsFailure()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            _harness.SetMode(EUiSandboxResponseMode.Rejected, "거절 사유 (모의)");
            string failure = null;

            page.Run(_harness.Context, () => { }, message => failure = message);
            _harness.Tick();

            Assert.AreEqual("거절 사유 (모의)", failure);
            StringAssert.Contains("실패", page.LastResponse);
            StringAssert.Contains("거절 사유 (모의)", page.LastResponse);
        }

        [Test]
        public void RunRequest_NotPlaying_ReturnsFalseWithNotStartedText()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            _harness.Playing = false;
            bool invoked = false;

            bool started = page.Run(_harness.Context, () => invoked = true);
            _harness.Tick();

            Assert.IsFalse(started);
            Assert.IsFalse(invoked);
            Assert.IsFalse(_harness.Context.IsBusy);
            StringAssert.StartsWith(PROBE_OP, page.LastResponse);
            StringAssert.Contains("시작되지 않음", page.LastResponse);
        }

        [Test]
        public void RunRequest_AnotherPending_ReturnsFalseWithNotStartedText()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            page.Run(_harness.Context, () => { });

            bool second = page.Run(_harness.Context, () => { });

            Assert.IsFalse(second);
            StringAssert.Contains("시작되지 않음", page.LastResponse);
            Assert.IsTrue(_harness.Context.IsBusy);
        }

        /****************************************
        *               Detach
        ****************************************/

        [Test]
        public void Detach_WhileRequestPending_CancelsRequestAndNeverCallsBack()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            int detachBefore = page.DetachCount;
            bool invoked = false;
            page.Run(_harness.Context, () => invoked = true);
            Assert.IsTrue(_harness.Context.IsBusy);

            page.Detach();
            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.IsFalse(invoked);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Canceled));
            Assert.AreEqual(0, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
            Assert.IsFalse(page.IsAttached);
            Assert.AreEqual(detachBefore + 1, page.DetachCount);
        }

        [Test]
        public void Detach_StaleResponseFromOtherContext_IsDroppedByGeneration()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            UiSandboxContext other = _harness.CreateContext(null);
            bool invoked = false;

            try
            {
                Assert.IsTrue(page.Run(other, () => invoked = true));

                page.Detach();
                other.Tick(PresentationTestHarness.START_TIME + 1000.0);

                Assert.IsFalse(invoked);
                StringAssert.DoesNotContain("성공", page.LastResponse);
            }
            finally
            {
                other.Dispose();
            }
        }

        [Test]
        public void Attach_Again_ResetsLastResponseAndReattaches()
        {
            _harness = new PresentationTestHarness(false);
            ProbePage page = _harness.Track(new ProbePage());
            page.Attach(_harness.Context);
            int detachBefore = page.DetachCount;
            page.Run(_harness.Context, () => { });
            _harness.Tick();
            StringAssert.Contains("성공", page.LastResponse);

            page.Attach(_harness.Context);

            Assert.AreEqual("없음", page.LastResponse);
            Assert.AreEqual(2, page.AttachCount);
            Assert.AreEqual(detachBefore + 1, page.DetachCount);
        }

        [Test]
        public void Detach_FixturePage_DestroysFixtureAndReleasesCanvas()
        {
            _harness = new PresentationTestHarness();
            HudPartyPage page = _harness.Track(new HudPartyPage());
            page.Attach(_harness.Context);
            Assert.IsTrue(page.UsesFixture);

            page.Detach();

            Assert.IsFalse(page.UsesFixture);
            Assert.AreEqual(0, page.Fixtures.Count);
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }
    }
}
