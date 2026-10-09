using NUnit.Framework;
using Shinshi.Common;
using Shinshi.Editor;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class SortiePrepPageTests
    {
        private PresentationTestHarness _harness;
        private SortiePrepPage          _page;

        [SetUp]
        public void SetUp()
        {
            _harness = new PresentationTestHarness();
            _page    = _harness.Track(new SortiePrepPage());
            _page.Attach(_harness.Context);
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
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
        *            Attach / Detach
        ****************************************/

        [Test]
        public void Attach_NoSceneView_BuildsLabeledFixture()
        {
            Assert.IsNotNull(_page.View);
            Assert.IsTrue(_page.UsesFixture);
            Assert.AreEqual(1, _page.Fixtures.Count);
            Assert.AreEqual("SortiePrepView", _page.Fixtures[0].View.GetType().Name);
            Assert.IsTrue(_page.IsAttached);
        }

        [Test]
        public void Attach_UserValueExists_DoesNotOverwriteSeed()
        {
            _harness.Scenario.SetString(SortiePrepPage.KEY_DUNGEON_NAME, "사용자 던전");
            _harness.Scenario.SetString(SortiePrepPage.Key(0, "name"), "사용자");

            _page.Attach(_harness.Context);

            Assert.AreEqual("사용자 던전", _harness.Scenario.GetString(SortiePrepPage.KEY_DUNGEON_NAME));
            Assert.AreEqual("사용자", _harness.Scenario.GetString(SortiePrepPage.Key(0, "name")));
            Assert.IsTrue(_harness.Scenario.Contains(SortiePrepPage.Key(3, "level")));
        }

        [Test]
        public void Attach_Default_SubscribesEachEventExactlyOnce()
        {
            SortiePrepView view = _page.View;

            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(view, "ReadyClicked"));
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(view, "StartClicked"));
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(view, "LeaveClicked"));
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(view, "DifficultyChanged"));
        }

        [Test]
        public void Detach_AfterAttach_UnsubscribesAndDestroysFixture()
        {
            SortiePrepView view = _page.View;

            _page.Detach();

            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(view, "ReadyClicked"));
            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(view, "StartClicked"));
            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(view, "LeaveClicked"));
            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(view, "DifficultyChanged"));
            Assert.IsNull(_page.View);
            Assert.IsFalse(_page.IsAttached);
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void Attach_Twice_KeepsSingleFixtureAndSubscription()
        {
            _page.Attach(_harness.Context);

            Assert.AreEqual(1, _page.Fixtures.Count);
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(_page.View, "ReadyClicked"));
            Assert.AreEqual(1, PresentationTestHarness.CountFixtureCanvases());
        }

        /****************************************
        *               Requests
        ****************************************/

        [Test]
        public void ReadyClicked_Playing_StartsPendingRequestAndTogglesReadyOnSuccess()
        {
            bool before = _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY);

            PresentationTestHarness.RaiseEvent(_page.View, "ReadyClicked");

            Assert.IsTrue(_harness.Context.IsBusy);
            Assert.AreEqual(SortiePrepPage.OP_READY, _harness.Context.PendingOperation);

            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(!before, _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY));
            StringAssert.Contains("성공", _page.LastResponse);
        }

        [Test]
        public void DifficultyChanged_Playing_AppliesChosenDifficultyAndClearsReady()
        {
            PresentationTestHarness.RaiseEvent(_page.View, "DifficultyChanged", EBossDifficulty.Hard);
            Assert.AreEqual(SortiePrepPage.OP_DIFFICULTY, _harness.Context.PendingOperation);

            _harness.Tick();

            Assert.AreEqual((int)EBossDifficulty.Hard, _harness.Scenario.GetInt(SortiePrepPage.KEY_DIFFICULTY));
            Assert.IsFalse(_harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY));
        }

        [Test]
        public void StartClicked_Rejected_ShowsFailureMessageAsNotice()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected, "출정 조건 미달 (모의)");

            PresentationTestHarness.RaiseEvent(_page.View, "StartClicked");
            _harness.Tick();

            Assert.AreEqual("출정 조건 미달 (모의)", _harness.Scenario.GetString(SortiePrepPage.KEY_NOTICE_TEXT));
            StringAssert.Contains("실패", _page.LastResponse);
        }

        [Test]
        public void ReadyClicked_NotPlaying_RecordsBlockedAndKeepsState()
        {
            _harness.Playing = false;
            bool before = _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY);

            PresentationTestHarness.RaiseEvent(_page.View, "ReadyClicked");

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Blocked));
            Assert.AreEqual(before, _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY));
            StringAssert.Contains("시작되지 않음", _page.LastResponse);
        }

        [Test]
        public void LeaveClicked_WhileAnotherPending_BlocksSecondRequest()
        {
            PresentationTestHarness.RaiseEvent(_page.View, "ReadyClicked");
            PresentationTestHarness.RaiseEvent(_page.View, "LeaveClicked");

            Assert.AreEqual(SortiePrepPage.OP_READY, _harness.Context.PendingOperation);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Blocked));

            _harness.Tick();

            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
        }

        /****************************************
        *             Cancellation
        ****************************************/

        [Test]
        public void Detach_WhileRequestPending_CancelsRequestAndDropsLateResponse()
        {
            bool before = _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY);
            PresentationTestHarness.RaiseEvent(_page.View, "ReadyClicked");
            Assert.IsTrue(_harness.Context.IsBusy);

            _page.Detach();
            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Canceled));
            Assert.AreEqual(0, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
            Assert.AreEqual(before, _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY));
            StringAssert.DoesNotContain("성공", _page.LastResponse);
        }

        [Test]
        public void Attach_WhileRequestPending_CancelsOldRequestWithoutApplyingIt()
        {
            bool before = _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY);
            PresentationTestHarness.RaiseEvent(_page.View, "ReadyClicked");

            _page.Attach(_harness.Context);
            _harness.Tick();

            Assert.AreEqual(0, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Canceled));
            Assert.AreEqual(before, _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY));
        }

        [Test]
        public void CancelPending_ExternalCancel_SkipsCallbacksAndKeepsScenario()
        {
            bool before = _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY);
            PresentationTestHarness.RaiseEvent(_page.View, "ReadyClicked");

            bool canceled = _harness.Context.CancelPending("테스트 취소");
            _harness.Tick();

            Assert.IsTrue(canceled);
            Assert.AreEqual(before, _harness.Scenario.GetBool(SortiePrepPage.KEY_LOCAL_READY));
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Canceled));
            Assert.AreEqual(0, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
        }
    }
}
