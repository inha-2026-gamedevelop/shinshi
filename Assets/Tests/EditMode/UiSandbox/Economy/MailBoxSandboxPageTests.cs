// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Editor;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 우편함 화면의 행 선택, 수령(전부 · 일부 · 만료 · 사라짐), 쪽 이동의 요청 시점 값 고정, 구독과 시험 배치 정리를 실제 MailBoxView로 확인한다 </summary>
    public sealed class MailBoxSandboxPageTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int ROW_MAIL_1 = 0;
        private const int ROW_MAIL_2 = 1;
        private const int ROW_MAIL_3 = 2;
        private const int ROW_COUNT  = 6;

        private EconomyTestHarness _harness;
        private MailBoxSandboxPage _page;
        private MailBoxView        _view;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _harness = new EconomyTestHarness();
            _page    = new MailBoxSandboxPage();
            _harness.AttachAndApply(_page);
            _view = _page.View;
            Assert.NotNull(_view, _page.FixtureNote);
        }

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
            _page    = null;
            _view    = null;
        }

        [Test]
        public void BuildEntries_DefaultScenario_ReturnsThreeEntriesInOrder()
        {
            InboxEntry[] entries = MailBoxSandboxPage.BuildEntries(Scenario());

            Assert.AreEqual(3, entries.Length);
            Assert.AreEqual("mail.1", entries[0].InboxId);
            Assert.AreEqual("mail.3", entries[2].InboxId);
            Assert.AreEqual(1, MailBoxSandboxPage.IndexOfId(Scenario(), "mail.2"));
            Assert.AreEqual(-1, MailBoxSandboxPage.IndexOfId(Scenario(), "mail.없음"));
        }

        [Test]
        public void Apply_DefaultScenario_ShowsThreeRowsOnOnePage()
        {
            ListRowView[] rows = Rows();

            Assert.AreEqual(ROW_COUNT, rows.Length);
            Assert.IsTrue(rows[ROW_MAIL_3].gameObject.activeSelf);
            Assert.IsFalse(rows[3].gameObject.activeSelf);
            Assert.AreEqual("1 / 1", Label("PageLabel"));
            Assert.IsFalse(Child<Button>("NextButton").interactable);
            Assert.IsFalse(Child<Button>("ClaimButton").interactable);
        }

        [Test]
        public void RowClick_FirstRow_SelectsMailAndEnablesClaim()
        {
            ClickRow(ROW_MAIL_1);

            Assert.AreEqual("mail.1", _page.SelectedId);
            Assert.IsTrue(Child<Button>("ClaimButton").interactable);
        }

        [Test]
        public void RowClick_RowBeyondList_DoesNotSelect()
        {
            ClickRow(5);

            Assert.IsNull(_page.SelectedId);
        }

        [Test]
        public void ClaimClick_WithoutSelection_SendsNothing()
        {
            EconomyTestHarness.Click(Child<Button>("ClaimButton"));

            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void Claim_FullReward_RemovesMailAfterSuccess()
        {
            ClickRow(ROW_MAIL_1);
            EconomyTestHarness.Click(Child<Button>("ClaimButton"));
            Assert.AreEqual(1, _harness.Context.History.Count);
            Assert.AreEqual(MailBoxSandboxPage.OP_CLAIM, _harness.LastRecord.Operation);
            Assert.AreEqual(3, MailCount());

            _harness.Tick();

            Assert.AreEqual(2, MailCount());
            Assert.AreEqual(-1, MailBoxSandboxPage.IndexOfId(Scenario(), "mail.1"));
            Assert.AreEqual("수령을 마쳤다", Label("StatusLabel"));
            StringAssert.Contains("우편 수령 성공", _harness.Context.Status);
            Assert.IsNull(_page.SelectedId);
        }

        [Test]
        public void Claim_Rejected_KeepsMailAndShowsReason()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected);
            ClickRow(ROW_MAIL_1);

            EconomyTestHarness.Click(Child<Button>("ClaimButton"));
            _harness.Tick();

            Assert.AreEqual(3, MailCount());
            Assert.AreEqual("요청이 거절되었습니다", Label("StatusLabel"));
            StringAssert.Contains("우편 수령 실패", _harness.Context.Status);
        }

        [Test]
        public void Claim_MailRemovedDuringDelay_ReportsStaleMail()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            ClickRow(ROW_MAIL_1);
            EconomyTestHarness.Click(Child<Button>("ClaimButton"));

            Assert.IsTrue(EconomyList.RemoveAt(Scenario(), MailBoxSandboxPage.MailSchema, 0));
            _harness.Advance(3.0);

            Assert.AreEqual("이미 목록에서 사라진 우편이다", Label("StatusLabel"));
            Assert.AreEqual(2, MailCount());
        }

        [Test]
        public void Claim_ExpiredMail_FailsAndKeepsMail()
        {
            ClickRow(ROW_MAIL_3);

            EconomyTestHarness.Click(Child<Button>("ClaimButton"));
            _harness.Tick();

            Assert.AreEqual("만료된 우편이다", Label("StatusLabel"));
            Assert.AreEqual(3, MailCount());
        }

        [Test]
        public void Claim_PartialReward_KeepsMailWithRemainingAmount()
        {
            ClickRow(ROW_MAIL_2);

            EconomyTestHarness.Click(Child<Button>("ClaimButton"));
            _harness.Tick();

            Assert.AreEqual(3, MailCount());
            Assert.AreEqual(300L, EconomyList.GetLong(Scenario(), MailBoxSandboxPage.MailSchema, 1, "qty2"));
            Assert.AreEqual(0L, EconomyList.GetLong(Scenario(), MailBoxSandboxPage.MailSchema, 1, "qty1"));
            StringAssert.Contains("(일부)", _harness.Context.Status);
            StringAssert.Contains("일부만 수령했다", Label("StatusLabel"));
            Assert.AreEqual("mail.2", _page.SelectedId);
        }

        [Test]
        public void Claim_PartialRewardRepeated_RemovesMailAfterLastRemainder()
        {
            ClickRow(ROW_MAIL_2);

            for (int i = 0; i < 2; ++i)
            {
                EconomyTestHarness.Click(Child<Button>("ClaimButton"));
                _harness.Tick();
                Assert.AreEqual(3, MailCount(), "일부 수령 " + i);
            }

            Assert.AreEqual(100L, EconomyList.GetLong(Scenario(), MailBoxSandboxPage.MailSchema, 1, "qty2"));

            EconomyTestHarness.Click(Child<Button>("ClaimButton"));
            _harness.Tick();

            Assert.AreEqual(-1, MailBoxSandboxPage.IndexOfId(Scenario(), "mail.2"));
            Assert.AreEqual(2, MailCount());
        }

        [Test]
        public void Claim_GrantZero_LeavesRewardQuantityUnchanged()
        {
            EconomyList.SetLong(Scenario(), MailBoxSandboxPage.MailSchema, 1, "grant2", 0L);
            ClickRow(ROW_MAIL_2);

            EconomyTestHarness.Click(Child<Button>("ClaimButton"));
            _harness.Tick();

            Assert.AreEqual(500L, EconomyList.GetLong(Scenario(), MailBoxSandboxPage.MailSchema, 1, "qty2"));
            Assert.AreEqual(3, MailCount());
        }

        [Test]
        public void Claim_NegativeGrantOnAllRewards_RemovesMail()
        {
            EconomyList.SetLong(Scenario(), MailBoxSandboxPage.MailSchema, 1, "grant2", -1L);
            ClickRow(ROW_MAIL_2);

            EconomyTestHarness.Click(Child<Button>("ClaimButton"));
            _harness.Tick();

            Assert.AreEqual(-1, MailBoxSandboxPage.IndexOfId(Scenario(), "mail.2"));
        }

        [Test]
        public void PageNext_TargetFixedAtRequestTime_IgnoresPageEditedDuringDelay()
        {
            EconomyList.SetCount(Scenario(), MailBoxSandboxPage.MailSchema, 7);
            _page.Apply(_harness.Context);
            Assert.AreEqual("1 / 2", Label("PageLabel"));
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);

            EconomyTestHarness.Click(Child<Button>("NextButton"));
            Assert.AreEqual(MailBoxSandboxPage.OP_PAGE, _harness.LastRecord.Operation);
            Scenario().SetInt(MailBoxSandboxPage.KEY_PAGE, 5);
            _harness.Advance(3.0);

            Assert.AreEqual(1, Scenario().GetInt(MailBoxSandboxPage.KEY_PAGE));
            Assert.AreEqual("2 / 2", Label("PageLabel"));
        }

        [Test]
        public void PagePrev_FromSecondPage_ReturnsToFirstPage()
        {
            EconomyList.SetCount(Scenario(), MailBoxSandboxPage.MailSchema, 7);
            Scenario().SetInt(MailBoxSandboxPage.KEY_PAGE, 1);
            _page.Apply(_harness.Context);
            Assert.AreEqual("2 / 2", Label("PageLabel"));

            EconomyTestHarness.Click(Child<Button>("PrevButton"));
            _harness.Tick();

            Assert.AreEqual(0, Scenario().GetInt(MailBoxSandboxPage.KEY_PAGE));
            Assert.AreEqual("1 / 2", Label("PageLabel"));
        }

        [Test]
        public void Page_NotPlaying_IsBlockedAndKeepsPage()
        {
            EconomyList.SetCount(Scenario(), MailBoxSandboxPage.MailSchema, 7);
            _page.Apply(_harness.Context);
            _harness.Playing = false;

            EconomyTestHarness.Click(Child<Button>("NextButton"));

            Assert.AreEqual(EUiSandboxRequestState.Blocked, _harness.LastRecord.State);
            Assert.AreEqual(0, Scenario().GetInt(MailBoxSandboxPage.KEY_PAGE));
        }

        [Test]
        public void Page_Disconnected_KeepsPageAndShowsReason()
        {
            EconomyList.SetCount(Scenario(), MailBoxSandboxPage.MailSchema, 7);
            _page.Apply(_harness.Context);
            _harness.SetMode(EUiSandboxResponseMode.Disconnected);

            EconomyTestHarness.Click(Child<Button>("NextButton"));
            _harness.Tick();

            Assert.AreEqual(0, Scenario().GetInt(MailBoxSandboxPage.KEY_PAGE));
            Assert.AreEqual("서버와의 연결이 끊겼습니다", Label("StatusLabel"));
        }

        [Test]
        public void Detach_AfterAttach_RemovesSubscribersAndDestroysFixture()
        {
            Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(_view, "RowSelected"));
            Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(_view, "ClaimRequested"));
            Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(_view, "PageDeltaRequested"));
            GameObject fixtureRoot = _view.transform.root.gameObject;

            _page.Detach();

            Assert.AreEqual(0, EconomyTestHarness.SubscriberCount(_view, "RowSelected"));
            Assert.AreEqual(0, EconomyTestHarness.SubscriberCount(_view, "ClaimRequested"));
            Assert.AreEqual(0, EconomyTestHarness.SubscriberCount(_view, "PageDeltaRequested"));
            Assert.IsTrue(fixtureRoot == null);
        }

        [Test]
        public void Reattach_AfterDetach_DoesNotDuplicateHandlers()
        {
            _page.Detach();
            _harness.AttachAndApply(_page);
            _view = _page.View;
            Assert.NotNull(_view, _page.FixtureNote);
            Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(_view, "ClaimRequested"));

            ClickRow(ROW_MAIL_1);
            EconomyTestHarness.Click(Child<Button>("ClaimButton"));

            Assert.AreEqual(1, _harness.Context.History.Count);
        }

        [Test]
        public void Detach_WithPendingClaim_CancelsAndKeepsMail()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            ClickRow(ROW_MAIL_1);
            EconomyTestHarness.Click(Child<Button>("ClaimButton"));

            _page.Detach();
            _harness.Advance(5.0);

            Assert.AreEqual(EUiSandboxRequestState.Canceled, _harness.LastRecord.State);
            Assert.AreEqual(3, MailCount());
        }

        private UiSandboxScenarioSO Scenario()
        {
            return _harness.Context.Scenario;
        }

        private int MailCount()
        {
            return EconomyList.Count(Scenario(), MailBoxSandboxPage.MailSchema);
        }

        private ListRowView[] Rows()
        {
            return _view.GetComponentsInChildren<ListRowView>(true);
        }

        private T Child<T>(string name) where T : Component
        {
            return EconomyTestHarness.Child<T>(_view, name);
        }

        private string Label(string name)
        {
            return Child<TMP_Text>(name).text;
        }

        private void ClickRow(int index)
        {
            EconomyTestHarness.Click(Rows()[index].GetComponentInChildren<Button>(true));
        }
    }
}
