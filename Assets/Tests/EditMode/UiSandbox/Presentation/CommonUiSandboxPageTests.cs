using System.Reflection;
using NUnit.Framework;
using Shinshi.Common;
using Shinshi.Editor;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary>
    /// 마우스 올림 · 누름 · 끌기와 커서 그림은 물리 입력이라 확인하지 않는다. 합성 이벤트 호출은 같은 공개 메서드가 시험 배치에 연결되어 있는지만 본다.
    /// UiButtonFeedback은 DOTween이라 에디터 모드에서 움직이지 않으므로 연결 여부만 본다
    /// </summary>
    public sealed class CommonUiSandboxPageTests
    {
        private PresentationTestHarness _harness;
        private CommonUiSandboxPage     _page;

        [SetUp]
        public void SetUp()
        {
            _harness = new PresentationTestHarness();
            _page    = _harness.Track(new CommonUiSandboxPage());
            _page.Attach(_harness.Context);
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private static object ReadField(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        /****************************************
        *            Attach / Detach
        ****************************************/

        [Test]
        public void Attach_NoSceneView_WiresEveryCommonViewAndPreparesTooltipAndDrag()
        {
            Assert.IsTrue(_page.UsesFixture);
            Assert.IsNotNull(_page.Tooltip);
            Assert.IsNotNull(_page.SaveView);
            Assert.IsNotNull(_page.FeedbackNormal);
            Assert.IsNotNull(_page.FeedbackDisabled);
            Assert.IsNotNull(_page.DragHandle);
            Assert.IsNotNull(ReadField(_page.Tooltip, "_area"));
            Assert.IsNotNull(ReadField(_page.DragHandle, "_area"));
            Assert.AreEqual(1, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void Detach_AfterAttach_ReleasesViewsAndCanvas()
        {
            _page.Detach();

            Assert.IsNull(_page.Tooltip);
            Assert.IsNull(_page.SaveView);
            Assert.IsNull(_page.DragHandle);
            Assert.IsNull(_page.CursorObject);
            Assert.IsFalse(_page.ShowTooltip());
            Assert.IsFalse(_page.SimulateDrag(0.5f, 0.5f));
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        /****************************************
        *                Tooltip
        ****************************************/

        [Test]
        public void ShowTooltip_LongBodyAtCorner_StaysInsideAreaAndHideHidesIt()
        {
            _harness.Scenario.SetString(CommonUiSandboxPage.KEY_TIP_BODY, CommonUiSandboxPage.LONG_BODY);
            _harness.Scenario.SetFloat(CommonUiSandboxPage.KEY_TIP_X, 1.0f);
            _harness.Scenario.SetFloat(CommonUiSandboxPage.KEY_TIP_Y, 0.0f);

            Assert.IsTrue(_page.ShowTooltip());
            Assert.IsTrue(_page.IsTooltipVisible);
            Assert.IsTrue(_page.IsTooltipInsideArea());

            Assert.IsTrue(_page.HideTooltip());
            Assert.IsFalse(_page.IsTooltipVisible);
        }

        /****************************************
        *               Save status
        ****************************************/

        [Test]
        public void StartSave_Succeeds_ShowsSavingThenSaved()
        {
            Assert.IsTrue(_page.StartSave(_harness.Context));
            Assert.AreEqual(ESaveStatus.Saving, _page.SaveView.Status);

            _harness.Tick();

            Assert.AreEqual(ESaveStatus.Saved, _page.SaveView.Status);
            Assert.AreEqual((int)ESaveStatus.Saved, _harness.Scenario.GetInt(CommonUiSandboxPage.KEY_SAVE_STATUS));
        }

        [Test]
        public void StartSave_Rejected_ShowsRetrying()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected, "저장 실패 (모의)");

            _page.StartSave(_harness.Context);
            _harness.Tick();

            Assert.AreEqual(ESaveStatus.Retrying, _page.SaveView.Status);
        }

        [Test]
        public void Detach_WhileSavePending_CancelsAndRestoresPreviousStatus()
        {
            _harness.Scenario.SetInt(CommonUiSandboxPage.KEY_SAVE_STATUS, (int)ESaveStatus.Retrying);
            _page.StartSave(_harness.Context);
            Assert.IsTrue(_harness.Context.IsBusy);

            _page.Detach();
            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual((int)ESaveStatus.Retrying, _harness.Scenario.GetInt(CommonUiSandboxPage.KEY_SAVE_STATUS));
        }

        /****************************************
        *               Drag window
        ****************************************/

        [Test]
        public void SimulateDrag_FarOutsideArea_ClampsWindowInsideArea()
        {
            Assert.IsTrue(_page.SimulateDrag(3.0f, -2.0f));

            Assert.IsTrue(_page.IsDragWindowInsideArea());
        }

        /****************************************
        *                Cursor
        ****************************************/

        [Test]
        public void Apply_CursorOnWhileNotPlaying_CreatesNoCursor()
        {
            _harness.Playing = false;
            _harness.Scenario.SetBool(CommonUiSandboxPage.KEY_CURSOR_ON, true);

            _page.Apply(_harness.Context);

            Assert.IsNull(_page.CursorObject);
        }

        [Test]
        public void Apply_CursorOnWhilePlaying_CreatesCursorAndDetachDestroysIt()
        {
            Assume.That(_page.IsCursorPrefabAvailable, "PF_UiCursor 프리팹이 없어 건너뜀");
            _harness.Scenario.SetBool(CommonUiSandboxPage.KEY_CURSOR_ON, true);

            _page.Apply(_harness.Context);
            Assert.IsNotNull(_page.CursorObject);

            _page.Detach();

            Assert.IsNull(_page.CursorObject);
        }
    }
}
