using System.Reflection;
using NUnit.Framework;
using Shinshi.Common;
using Shinshi.Editor;
using Shinshi.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.Tests
{
    /// <summary>
    /// 에디터 모드에서는 View의 Awake · OnEnable을 유니티가 부르지 않으므로 리플렉션으로 생명주기를 직접 호출한다. 이는 물리 입력이 아니다.
    /// 버튼은 onClick.Invoke로만 누르며, Esc와 바깥 클릭은 View가 Update에서 Input을 읽으므로 여기서 확인하지 않는다 (실제 Play에서 부모가 확인)
    /// </summary>
    public sealed class HudMenuPageTests
    {
        private PresentationTestHarness _harness;
        private HudMenuPage             _page;

        [SetUp]
        public void SetUp()
        {
            _harness = new PresentationTestHarness();
            _page    = _harness.Track(new HudMenuPage());
            _page.Attach(_harness.Context);
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private static FieldInfo MenuField(string name)
        {
            return typeof(HudMenuDropdown).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private void StartLifecycle()
        {
            HudMenuDropdown view = _page.MenuView;
            Assume.That(MenuField("_wasActive").GetValue(view), Is.Null, "유니티가 이미 Awake를 불러 수동 생명주기를 쓸 수 없어 건너뜀");

            typeof(ClientView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            typeof(HudMenuDropdown).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);

            Assume.That(_page.IsHideTargetActive(0) && _page.IsHideTargetActive(1), Is.True, "숨김 대상이 켜진 채 시작하지 않아 건너뜀");
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
        public void Attach_NoSceneView_BuildsFixtureWithItemsAndHideTargets()
        {
            Assert.IsNotNull(_page.MenuView);
            Assert.IsTrue(_page.UsesFixture);
            Assert.AreEqual(HudMenuPage.ITEM_COUNT, _page.ItemCount);
            Assert.AreEqual(HudMenuPage.HIDE_COUNT, _page.HideTargetCount);
        }

        [Test]
        public void Attach_Default_LeavesHotkeyRouterUnwired()
        {
            Assert.IsNull(MenuField("_router").GetValue(_page.MenuView));
        }

        [Test]
        public void Attach_Default_SeedsLabelsAndNoLastItem()
        {
            Assert.AreEqual("메뉴 항목 1 (모의)", _harness.Scenario.GetString(HudMenuPage.ItemKey(0)));
            Assert.AreEqual(-1, _harness.Scenario.GetInt(HudMenuPage.KEY_LAST_ITEM));
            Assert.AreEqual(string.Empty, _harness.Scenario.GetString(HudMenuPage.KEY_OPENED_WINDOW));
        }

        [Test]
        public void Attach_UserLabelExists_DoesNotOverwriteSeed()
        {
            _harness.Scenario.SetString(HudMenuPage.ItemKey(1), "사용자 항목");

            _page.Attach(_harness.Context);

            Assert.AreEqual("사용자 항목", _harness.Scenario.GetString(HudMenuPage.ItemKey(1)));
        }

        [Test]
        public void Attach_Twice_KeepsSingleSetOfItems()
        {
            _page.Attach(_harness.Context);

            Assert.AreEqual(HudMenuPage.ITEM_COUNT, _page.ItemCount);
            Assert.AreEqual(1, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void Apply_LabelChanged_UpdatesItemButtonText()
        {
            RectTransform panel = MenuField("_panel").GetValue(_page.MenuView) as RectTransform;
            Assume.That(panel != null, "시험 배치에 패널이 연결되지 않아 건너뜀");
            Button[] buttons = panel.GetComponentsInChildren<Button>(true);
            TMP_Text text    = (buttons.Length > 1) ? buttons[1].GetComponentInChildren<TMP_Text>(true) : null;
            Assume.That(text != null, "항목 버튼에 글자 컴포넌트가 없어 건너뜀");

            _harness.Scenario.SetString(HudMenuPage.ItemKey(1), "바뀐 이름");
            _page.Apply(_harness.Context);

            Assert.AreEqual("바뀐 이름", text.text);
        }

        [Test]
        public void Detach_AfterAttach_ReleasesViewItemsAndFixture()
        {
            _page.Detach();

            Assert.IsNull(_page.MenuView);
            Assert.AreEqual(0, _page.ItemCount);
            Assert.AreEqual(0, _page.HideTargetCount);
            Assert.IsFalse(_page.IsPanelOpen);
            Assert.IsFalse(_page.ClickMenuButton());
            Assert.IsFalse(_page.ClickItem(0));
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void ClickItem_OutOfRange_ReturnsFalse()
        {
            Assert.IsFalse(_page.ClickItem(-1));
            Assert.IsFalse(_page.ClickItem(HudMenuPage.ITEM_COUNT));
            Assert.IsFalse(_harness.Context.IsBusy);
        }

        /****************************************
        *       Open / close (manual lifecycle)
        ****************************************/

        [Test]
        public void ClickMenuButton_AfterLifecycle_OpensPanelAndHidesTargets()
        {
            StartLifecycle();
            Assert.IsFalse(_page.IsPanelOpen);

            bool clicked = _page.ClickMenuButton();

            Assert.IsTrue(clicked);
            Assert.IsTrue(_page.IsPanelOpen);
            Assert.IsFalse(_page.IsHideTargetActive(0));
            Assert.IsFalse(_page.IsHideTargetActive(1));
        }

        [Test]
        public void ClickMenuButton_Twice_ClosesPanelAndRestoresTargets()
        {
            StartLifecycle();

            _page.ClickMenuButton();
            _page.ClickMenuButton();

            Assert.IsFalse(_page.IsPanelOpen);
            Assert.IsTrue(_page.IsHideTargetActive(0));
            Assert.IsTrue(_page.IsHideTargetActive(1));
        }

        [Test]
        public void ClickMenuButton_TargetAlreadyHidden_RestoresOnlyWhatWasActive()
        {
            StartLifecycle();
            Assert.IsTrue(_page.SetHideTargetActive(1, false));

            _page.ClickMenuButton();
            _page.ClickMenuButton();

            Assert.IsTrue(_page.IsHideTargetActive(0));
            Assert.IsFalse(_page.IsHideTargetActive(1));
        }

        [Test]
        public void SetHideTargetActive_OutOfRange_ReturnsFalse()
        {
            Assert.IsFalse(_page.SetHideTargetActive(-1, true));
            Assert.IsFalse(_page.SetHideTargetActive(HudMenuPage.HIDE_COUNT, true));
            Assert.IsFalse(_page.IsHideTargetActive(HudMenuPage.HIDE_COUNT));
        }

        /****************************************
        *               Item click
        ****************************************/

        [Test]
        public void ClickItem_PanelOpen_ClosesPanelAndRestoresTargets()
        {
            StartLifecycle();
            _page.ClickMenuButton();
            Assert.IsTrue(_page.IsPanelOpen);

            bool clicked = _page.ClickItem(0);

            Assert.IsTrue(clicked);
            Assert.IsFalse(_page.IsPanelOpen);
            Assert.IsTrue(_page.IsHideTargetActive(0));
            Assert.IsTrue(_page.IsHideTargetActive(1));
        }

        [Test]
        public void ClickItem_Playing_RecordsLastItemAndOpensWindowAfterTick()
        {
            StartLifecycle();
            _page.ClickMenuButton();

            _page.ClickItem(2);

            Assert.AreEqual(2, _harness.Scenario.GetInt(HudMenuPage.KEY_LAST_ITEM));
            Assert.AreEqual(HudMenuPage.OP_OPEN_WINDOW, _harness.Context.PendingOperation);
            Assert.AreEqual(string.Empty, _harness.Scenario.GetString(HudMenuPage.KEY_OPENED_WINDOW));

            _harness.Tick();

            Assert.AreEqual("메뉴 항목 3 (모의)", _harness.Scenario.GetString(HudMenuPage.KEY_OPENED_WINDOW));
            StringAssert.Contains("성공", _page.LastResponse);
        }

        [Test]
        public void ClickItem_Rejected_ShowsNoticeAndKeepsOpenedWindowEmpty()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected, "창 열기 실패 (모의)");

            _page.ClickItem(1);
            _harness.Tick();

            Assert.AreEqual("창 열기 실패 (모의)", _harness.Scenario.GetString(HudMenuPage.KEY_NOTICE));
            Assert.AreEqual(string.Empty, _harness.Scenario.GetString(HudMenuPage.KEY_OPENED_WINDOW));
        }

        [Test]
        public void ClickItem_NotPlaying_BlocksRequestAndOpensNoWindow()
        {
            _harness.Playing = false;

            _page.ClickItem(1);
            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Blocked));
            Assert.AreEqual(string.Empty, _harness.Scenario.GetString(HudMenuPage.KEY_OPENED_WINDOW));
            StringAssert.Contains("시작되지 않음", _page.LastResponse);
        }

        [Test]
        public void Detach_WhileOpenWindowRequestPending_CancelsAndNeverOpensWindow()
        {
            _page.ClickItem(0);
            Assert.IsTrue(_harness.Context.IsBusy);

            _page.Detach();
            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Canceled));
            Assert.AreEqual(string.Empty, _harness.Scenario.GetString(HudMenuPage.KEY_OPENED_WINDOW));
        }
    }
}
