// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.Editor;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 1:1 거래 화면의 실제 단추 · 줄 이벤트가 가짜 응답(성공 · 거절 · 지연)으로 이어지는 흐름과 제안 8줄, 정리를 실제 TradeView로 확인한다 </summary>
    public sealed class TradeSandboxPageTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int OFFER_ROWS = 8;

        private static readonly string[] G_EventNames =
        {
            "InventoryRowClicked", "MyLineClicked", "CurrencyEdited", "LockToggleRequested", "AcceptRequested", "CancelRequested"
        };

        private EconomyTestHarness _harness;
        private TradeSandboxPage   _page;
        private TradeView         _view;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _harness = new EconomyTestHarness();
            _page    = new TradeSandboxPage();
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
        public void Fixture_AllThreeLists_HaveEightRows()
        {
            Assert.AreEqual(OFFER_ROWS, Rows("_myRows").Length);
            Assert.AreEqual(OFFER_ROWS, Rows("_partnerRows").Length);
            Assert.AreEqual(OFFER_ROWS, Rows("_inventoryRows").Length);
        }

        [Test]
        public void Apply_EightOfferLinesEachSide_ShowsEveryRow()
        {
            FillNamed(TradeSandboxPage.MyOfferSchema, OFFER_ROWS, "내것");
            FillNamed(TradeSandboxPage.PartnerOfferSchema, OFFER_ROWS, "상대것");

            _page.Apply(_harness.Context);

            for (int i = 0; i < OFFER_ROWS; ++i)
            {
                Assert.IsTrue(Rows("_myRows")[i].gameObject.activeSelf, "내 제안 " + i);
                Assert.IsTrue(Rows("_partnerRows")[i].gameObject.activeSelf, "상대 제안 " + i);
            }
        }

        [Test]
        public void InventoryRowClick_EighthRow_MovesOneToMyOfferAfterResponse()
        {
            FillNamed(TradeSandboxPage.InventorySchema, OFFER_ROWS, "재료");
            _page.Apply(_harness.Context);

            ClickRow("_inventoryRows", 7);

            Assert.AreEqual(TradeSandboxPage.OP_ADD, _harness.LastRecord.Operation);
            Assert.AreEqual(1, EconomyList.Count(Scenario(), TradeSandboxPage.MyOfferSchema));
            _harness.Tick();

            Assert.AreEqual(2, EconomyList.Count(Scenario(), TradeSandboxPage.MyOfferSchema));
            Assert.AreEqual("재료 8", EconomyList.GetText(Scenario(), TradeSandboxPage.MyOfferSchema, 1, "name"));
            Assert.AreEqual(1, EconomyList.GetInt(Scenario(), TradeSandboxPage.MyOfferSchema, 1, "qty"));
            Assert.AreEqual(7, EconomyList.Count(Scenario(), TradeSandboxPage.InventorySchema));
            Assert.IsTrue(Rows("_myRows")[1].gameObject.activeSelf);
        }

        [Test]
        public void MyRowClick_EighthOfferLine_ReturnsOneToInventory()
        {
            FillNamed(TradeSandboxPage.MyOfferSchema, OFFER_ROWS, "내것");
            _page.Apply(_harness.Context);

            ClickRow("_myRows", 7);
            _harness.Tick();

            Assert.AreEqual(TradeSandboxPage.OP_REMOVE, _harness.LastRecord.Operation);
            Assert.AreEqual(7, EconomyList.Count(Scenario(), TradeSandboxPage.MyOfferSchema));
            Assert.AreEqual(4, EconomyList.Count(Scenario(), TradeSandboxPage.InventorySchema));
            Assert.AreEqual("내것 8", EconomyList.GetText(Scenario(), TradeSandboxPage.InventorySchema, 3, "name"));
        }

        [Test]
        public void InventoryRowClick_Rejected_KeepsBothLists()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected);

            ClickRow("_inventoryRows", 0);
            _harness.Tick();

            Assert.AreEqual(1, EconomyList.Count(Scenario(), TradeSandboxPage.MyOfferSchema));
            Assert.AreEqual(12, EconomyList.GetInt(Scenario(), TradeSandboxPage.InventorySchema, 0, "qty"));
            StringAssert.Contains("요청이 거절되었습니다", _harness.Context.Status);
        }

        [Test]
        public void InventoryRowClick_LineRenamedDuringDelay_FailsWithoutMoving()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            ClickRow("_inventoryRows", 0);

            EconomyList.SetText(Scenario(), TradeSandboxPage.InventorySchema, 0, "name", "바뀐 품목");
            _harness.Advance(3.0);

            StringAssert.Contains("요청한 뒤 줄이 바뀌었다", _harness.Context.Status);
            Assert.AreEqual(1, EconomyList.Count(Scenario(), TradeSandboxPage.MyOfferSchema));
            Assert.AreEqual(12, EconomyList.GetInt(Scenario(), TradeSandboxPage.InventorySchema, 0, "qty"));
        }

        [Test]
        public void InventoryRowClick_AfterTradeDone_SendsNothing()
        {
            Scenario().SetString(TradeSandboxPage.KEY_STATE, nameof(ETradeState.Done));
            _page.Apply(_harness.Context);

            ClickRow("_inventoryRows", 0);

            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void MyRowClick_WhileLocked_ReturnsLineAndReleasesLocks()
        {
            Scenario().SetBool(TradeSandboxPage.KEY_MY_LOCKED, true);
            Scenario().SetBool(TradeSandboxPage.KEY_PARTNER_LOCKED, true);
            Scenario().SetString(TradeSandboxPage.KEY_STATE, nameof(ETradeState.Locked));
            _page.Apply(_harness.Context);

            ClickRow("_myRows", 0);
            _harness.Tick();

            Assert.IsFalse(Scenario().GetBool(TradeSandboxPage.KEY_MY_LOCKED, true));
            Assert.IsFalse(Scenario().GetBool(TradeSandboxPage.KEY_PARTNER_LOCKED, true));
            Assert.AreEqual(nameof(ETradeState.Editing), Scenario().GetString(TradeSandboxPage.KEY_STATE, string.Empty));
        }

        [Test]
        public void CurrencyEdit_ValidText_SavesAfterResponse()
        {
            Child<TMP_InputField>("MyCurrency").onEndEdit.Invoke("700");

            Assert.AreEqual(TradeSandboxPage.OP_CURRENCY, _harness.LastRecord.Operation);
            Assert.AreEqual(500L, Scenario().GetLong(TradeSandboxPage.KEY_MY_CURRENCY, 0L));
            _harness.Tick();

            Assert.AreEqual(700L, Scenario().GetLong(TradeSandboxPage.KEY_MY_CURRENCY, 0L));
        }

        [Test]
        public void CurrencyEdit_NotANumber_SendsNothing()
        {
            Child<TMP_InputField>("MyCurrency").onEndEdit.Invoke("abc");

            Assert.AreEqual(0, _harness.Context.History.Count);
            Assert.AreEqual(500L, Scenario().GetLong(TradeSandboxPage.KEY_MY_CURRENCY, 0L));
        }

        [TestCase(3, nameof(ETradeState.Countdown))]
        [TestCase(0, nameof(ETradeState.Accepting))]
        public void LockButton_PartnerAlreadyLocked_MovesToCountdownOrAccepting(int countdown, string expectedState)
        {
            Scenario().SetBool(TradeSandboxPage.KEY_PARTNER_LOCKED, true);
            Scenario().SetInt(TradeSandboxPage.KEY_COUNTDOWN, countdown);

            EconomyTestHarness.Click(Child<Button>("LockButton"));
            Assert.AreEqual(TradeSandboxPage.OP_LOCK, _harness.LastRecord.Operation);
            _harness.Tick();

            Assert.IsTrue(Scenario().GetBool(TradeSandboxPage.KEY_MY_LOCKED, false));
            Assert.AreEqual(expectedState, Scenario().GetString(TradeSandboxPage.KEY_STATE, string.Empty));
        }

        [Test]
        public void LockButton_PartnerNotLocked_MovesToLocked()
        {
            EconomyTestHarness.Click(Child<Button>("LockButton"));
            _harness.Tick();

            Assert.AreEqual(nameof(ETradeState.Locked), Scenario().GetString(TradeSandboxPage.KEY_STATE, string.Empty));
        }

        [Test]
        public void LockButton_Rejected_KeepsEditing()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected);

            EconomyTestHarness.Click(Child<Button>("LockButton"));
            _harness.Tick();

            Assert.IsFalse(Scenario().GetBool(TradeSandboxPage.KEY_MY_LOCKED, true));
            Assert.AreEqual(nameof(ETradeState.Editing), Scenario().GetString(TradeSandboxPage.KEY_STATE, string.Empty));
        }

        [Test]
        public void AcceptButton_PartnerAlreadyAccepted_FinishesTrade()
        {
            Scenario().SetBool(TradeSandboxPage.KEY_MY_LOCKED, true);
            Scenario().SetBool(TradeSandboxPage.KEY_PARTNER_LOCKED, true);
            Scenario().SetBool(TradeSandboxPage.KEY_PARTNER_ACCEPTED, true);
            Scenario().SetString(TradeSandboxPage.KEY_STATE, nameof(ETradeState.Accepting));
            _page.Apply(_harness.Context);

            EconomyTestHarness.Click(Child<Button>("AcceptButton"));
            _harness.Tick();

            Assert.AreEqual(TradeSandboxPage.OP_ACCEPT, _harness.LastRecord.Operation);
            Assert.IsTrue(Scenario().GetBool(TradeSandboxPage.KEY_MY_ACCEPTED, false));
            Assert.AreEqual(nameof(ETradeState.Done), Scenario().GetString(TradeSandboxPage.KEY_STATE, string.Empty));
        }

        [Test]
        public void CancelButton_InProgress_MovesToCancelled()
        {
            EconomyTestHarness.Click(Child<Button>("CancelButton"));
            Assert.AreEqual(TradeSandboxPage.OP_CANCEL, _harness.LastRecord.Operation);
            _harness.Tick();

            Assert.AreEqual(nameof(ETradeState.Cancelled), Scenario().GetString(TradeSandboxPage.KEY_STATE, string.Empty));
        }

        [Test]
        public void CancelButton_AfterTradeFinished_ClosesWindowWithoutRequest()
        {
            Scenario().SetString(TradeSandboxPage.KEY_STATE, nameof(ETradeState.Done));
            _page.Apply(_harness.Context);

            EconomyTestHarness.Click(Child<Button>("CancelButton"));

            Assert.AreEqual(0, _harness.Context.History.Count);
            Assert.AreEqual(1, _page.ClosedCount);
            Assert.IsFalse(_view.IsOpen);
        }

        [Test]
        public void Detach_AfterAttach_RemovesAllSixSubscribersAndDestroysFixture()
        {
            TradeView view = _view;
            GameObject fixtureRoot = view.transform.root.gameObject;
            for (int i = 0; i < G_EventNames.Length; ++i)
            {
                Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(view, G_EventNames[i]), G_EventNames[i]);
            }

            _page.Detach();

            for (int i = 0; i < G_EventNames.Length; ++i)
            {
                Assert.AreEqual(0, EconomyTestHarness.SubscriberCount(view, G_EventNames[i]), G_EventNames[i]);
            }

            Assert.IsTrue(fixtureRoot == null);
        }

        [Test]
        public void Reattach_AfterDetach_DoesNotDuplicateHandlers()
        {
            _page.Detach();
            _harness.AttachAndApply(_page);
            _view = _page.View;
            Assert.NotNull(_view, _page.FixtureNote);

            ClickRow("_inventoryRows", 0);

            Assert.AreEqual(1, _harness.Context.History.Count);
            Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(_view, "InventoryRowClicked"));
        }

        [Test]
        public void Detach_WithPendingLock_CancelsAndKeepsEditing()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            EconomyTestHarness.Click(Child<Button>("LockButton"));

            _page.Detach();
            _harness.Advance(5.0);

            Assert.AreEqual(EUiSandboxRequestState.Canceled, _harness.LastRecord.State);
            Assert.IsFalse(Scenario().GetBool(TradeSandboxPage.KEY_MY_LOCKED, true));
        }

        private UiSandboxScenarioSO Scenario()
        {
            return _harness.Context.Scenario;
        }

        private ListRowView[] Rows(string field)
        {
            return EconomyUiBuilder.GetRefs<ListRowView>(_view, field);
        }

        private T Child<T>(string name) where T : Component
        {
            return EconomyTestHarness.Child<T>(_view, name);
        }

        private void ClickRow(string field, int index)
        {
            EconomyTestHarness.Click(Rows(field)[index].GetComponentInChildren<Button>(true));
        }

        // 줄 수를 맞추고 이름을 "<접두어> N", 수량을 1로 고정해 줄마다 겹치지 않게 한다
        private void FillNamed(EconomyListSchema schema, int count, string prefix)
        {
            EconomyList.SetCount(Scenario(), schema, count);
            for (int i = 0; i < count; ++i)
            {
                EconomyList.SetText(Scenario(), schema, i, "name", prefix + " " + (i + 1));
                EconomyList.SetInt(Scenario(), schema, i, "qty", 1);
            }
        }
    }
}
