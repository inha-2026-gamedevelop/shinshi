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
    /// <summary> 경매장 화면의 실제 단추 이벤트가 가짜 응답(성공 · 거절 · 지연)으로 이어지는 흐름과 직업 3/4/5 선택, 구독 정리를 실제 AuctionView로 확인한다 </summary>
    public sealed class AuctionSandboxPageTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long DEFAULT_YEOPJEON = 50000L;

        private static readonly string[] G_EventNames =
        {
            "TabChanged", "SearchRequested", "PageDeltaRequested", "GroupSelected", "ListingSelected", "BuyRequested",
            "SellItemSelected", "RegisterRequested", "CancelRequested", "AckRequested", "Closed"
        };

        private EconomyTestHarness _harness;
        private AuctionSandboxPage _page;
        private AuctionView        _view;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _harness = new EconomyTestHarness();
            _page    = new AuctionSandboxPage();
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
        public void FilterGroups_TextAndGrade_ReturnsMatchingScenarioLines()
        {
            AuctionSearchInput all = new AuctionSearchInput(string.Empty, AuctionSearchInput.NONE, AuctionSearchInput.NONE, AuctionSearchInput.NONE, 0, 0);
            AuctionSearchInput text = new AuctionSearchInput("회복", AuctionSearchInput.NONE, AuctionSearchInput.NONE, AuctionSearchInput.NONE, 0, 0);
            AuctionSearchInput grade = new AuctionSearchInput(string.Empty, AuctionSearchInput.NONE, AuctionSearchInput.NONE, 2, 0, 0);

            Assert.AreEqual(3, AuctionSandboxPage.FilterGroups(Scenario(), all).Count);
            CollectionAssert.AreEqual(new[] { 1 }, AuctionSandboxPage.FilterGroups(Scenario(), text));
            CollectionAssert.AreEqual(new[] { 2 }, AuctionSandboxPage.FilterGroups(Scenario(), grade));
        }

        [Test]
        public void GroupStats_DefaultGroupAndSoldOutGroup_ReturnMinPriceAndZeros()
        {
            AuctionSandboxPage.GroupStats(Scenario(), 0, out long minPrice, out int quantity, out int listingCount);
            Assert.AreEqual(75L, minPrice);
            Assert.AreEqual(25, quantity);
            Assert.AreEqual(2, listingCount);

            EconomyList.SetInt(Scenario(), AuctionSandboxPage.ListingSchema, 2, "qty", 0);
            AuctionSandboxPage.GroupStats(Scenario(), 1, out minPrice, out quantity, out listingCount);
            Assert.AreEqual(0L, minPrice);
            Assert.AreEqual(0, quantity);
            Assert.AreEqual(0, listingCount);
        }

        [Test]
        public void Apply_DefaultScenario_ShowsThreeGroupRows()
        {
            ListRowView[] rows = Rows("_groupRows");

            Assert.AreEqual(8, rows.Length);
            Assert.IsTrue(rows[2].gameObject.activeSelf);
            Assert.IsFalse(rows[3].gameObject.activeSelf);
            Assert.AreEqual(-1, _page.SelectedGroup);
        }

        [Test]
        public void GroupClick_Success_OpensDetailOnlyAfterResponse()
        {
            ClickRow("_groupRows", 0);

            Assert.AreEqual(AuctionSandboxPage.OP_GROUP, _harness.LastRecord.Operation);
            Assert.AreEqual(-1, _page.SelectedGroup);

            _harness.Tick();

            Assert.AreEqual(0, _page.SelectedGroup);
            Assert.IsTrue(Rows("_listingRows")[1].gameObject.activeSelf);
            Assert.IsFalse(Rows("_listingRows")[2].gameObject.activeSelf);
        }

        [Test]
        public void GroupClick_Rejected_KeepsDetailClosed()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected);

            ClickRow("_groupRows", 0);
            _harness.Tick();

            Assert.AreEqual(-1, _page.SelectedGroup);
            StringAssert.Contains("요청이 거절되었습니다", _harness.Context.Status);
        }

        [Test]
        public void GroupClick_RowBeyondList_SendsNothing()
        {
            ClickRow("_groupRows", 5);

            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void GroupClick_GroupRenamedDuringDelay_ReportsStaleGroup()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            ClickRow("_groupRows", 0);

            EconomyList.SetText(Scenario(), AuctionSandboxPage.GroupSchema, 0, "name", "바뀐 품목");
            _harness.Advance(3.0);

            StringAssert.Contains("묶음이 이미 바뀌었다", _harness.Context.Status);
            Assert.AreEqual(-1, _page.SelectedGroup);
        }

        [Test]
        public void Buy_SelectedListing_ReducesStockYeopjeonAndAddsHistory()
        {
            SelectGroupAndListing();

            EconomyTestHarness.Click(Child<Button>("BuyButton"));
            Assert.AreEqual(AuctionSandboxPage.OP_BUY, _harness.LastRecord.Operation);
            Assert.AreEqual(20, ListingQuantity(0));
            _harness.Tick();

            Assert.AreEqual(19, ListingQuantity(0));
            Assert.AreEqual(DEFAULT_YEOPJEON - 75L, Scenario().GetLong(AuctionSandboxPage.KEY_YEOPJEON));
            Assert.AreEqual(3, EconomyList.Count(Scenario(), AuctionSandboxPage.HistorySchema));
            Assert.AreEqual(MarketCodes.TradeRole.BUYER, EconomyList.GetText(Scenario(), AuctionSandboxPage.HistorySchema, 2, "role"));
            Assert.AreEqual(75L, EconomyList.GetLong(Scenario(), AuctionSandboxPage.HistorySchema, 2, "gross"));
        }

        [Test]
        public void Buy_QuantityTypedBeforeClick_UsesTypedQuantity()
        {
            SelectGroupAndListing();
            Child<TMP_InputField>("BuyQuantity").text = "5";

            EconomyTestHarness.Click(Child<Button>("BuyButton"));
            _harness.Tick();

            Assert.AreEqual(15, ListingQuantity(0));
            Assert.AreEqual(DEFAULT_YEOPJEON - 375L, Scenario().GetLong(AuctionSandboxPage.KEY_YEOPJEON));
            Assert.AreEqual(5, EconomyList.GetInt(Scenario(), AuctionSandboxPage.HistorySchema, 2, "qty"));
        }

        [Test]
        public void Buy_Rejected_KeepsStockAndYeopjeon()
        {
            SelectGroupAndListing();
            _harness.SetMode(EUiSandboxResponseMode.Rejected);

            EconomyTestHarness.Click(Child<Button>("BuyButton"));
            _harness.Tick();

            Assert.AreEqual(20, ListingQuantity(0));
            Assert.AreEqual(DEFAULT_YEOPJEON, Scenario().GetLong(AuctionSandboxPage.KEY_YEOPJEON));
            StringAssert.Contains("요청이 거절되었습니다", _harness.Context.Status);
        }

        [Test]
        public void Buy_ListingPriceChangedDuringDelay_ReportsStaleListing()
        {
            SelectGroupAndListing();
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            EconomyTestHarness.Click(Child<Button>("BuyButton"));

            EconomyList.SetLong(Scenario(), AuctionSandboxPage.ListingSchema, 0, "unit", 77L);
            _harness.Advance(3.0);

            StringAssert.Contains("등록이 이미 바뀌었다", _harness.Context.Status);
            Assert.AreEqual(20, ListingQuantity(0));
            Assert.AreEqual(DEFAULT_YEOPJEON, Scenario().GetLong(AuctionSandboxPage.KEY_YEOPJEON));
        }

        [Test]
        public void Buy_ZeroQuantityOrNoListing_SendsNothing()
        {
            ClickRow("_groupRows", 0);
            _harness.Tick();

            EconomyTestHarness.Click(Child<Button>("BuyButton"));
            Assert.AreEqual(1, _harness.Context.History.Count);

            ClickRow("_listingRows", 0);
            Child<TMP_InputField>("BuyQuantity").text = "0";
            EconomyTestHarness.Click(Child<Button>("BuyButton"));
            Assert.AreEqual(1, _harness.Context.History.Count);
        }

        [Test]
        public void Search_ClassDropdown_HasLegacyAndNewClassOptions()
        {
            Assert.AreEqual(7, Child<TMP_Dropdown>("ClassDropdown").options.Count);
        }

        [TestCase(4, 3)]
        [TestCase(5, 4)]
        [TestCase(6, 5)]
        public void Search_NewClassDropdownValue_MapsToClassIdAfterResponse(int dropdownValue, int expectedClassId)
        {
            Child<TMP_Dropdown>("ClassDropdown").value = dropdownValue;

            EconomyTestHarness.Click(Child<Button>("SearchButton"));
            Assert.AreEqual(AuctionSandboxPage.OP_SEARCH, _harness.LastRecord.Operation);
            Assert.AreEqual(AuctionSearchInput.NONE, _page.LastSearch.ClassId);
            _harness.Tick();

            Assert.AreEqual(expectedClassId, _page.LastSearch.ClassId);
        }

        [Test]
        public void Search_GradeDropdown_FiltersGroupRowsAfterResponse()
        {
            Child<TMP_Dropdown>("GradeDropdown").value = 3;

            EconomyTestHarness.Click(Child<Button>("SearchButton"));
            _harness.Tick();

            Assert.AreEqual(2, _page.LastSearch.Grade);
            Assert.IsTrue(Rows("_groupRows")[0].gameObject.activeSelf);
            Assert.IsFalse(Rows("_groupRows")[1].gameObject.activeSelf);
        }

        [Test]
        public void Search_Rejected_KeepsPreviousSearch()
        {
            Child<TMP_InputField>("SearchField").text = "약";
            _harness.SetMode(EUiSandboxResponseMode.Rejected);

            EconomyTestHarness.Click(Child<Button>("SearchButton"));
            _harness.Tick();

            Assert.AreEqual(string.Empty, _page.LastSearch.Text);
            Assert.IsTrue(Rows("_groupRows")[2].gameObject.activeSelf);
        }

        [Test]
        public void TabToggle_Selected_SendsTabRequestAndDeselectedSendsNothing()
        {
            Child<Toggle>("Tab2").onValueChanged.Invoke(false);
            Assert.AreEqual(0, _harness.Context.History.Count);

            Child<Toggle>("Tab2").onValueChanged.Invoke(true);

            Assert.AreEqual(2, _view.Tab);
            Assert.AreEqual(AuctionSandboxPage.OP_TAB, _harness.LastRecord.Operation);
        }

        [Test]
        public void PageNext_OnHistoryTab_UsesTargetPageFixedAtRequestTime()
        {
            EconomyList.SetCount(Scenario(), AuctionSandboxPage.HistorySchema, 30);
            _page.Apply(_harness.Context);
            _view.ShowTab(AuctionSandboxPage.TAB_HISTORY);
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);

            EconomyTestHarness.Click(Ref<Button>("_historyNextButton"));
            Scenario().SetInt(AuctionSandboxPage.KEY_HISTORY_PAGE, 3);
            _harness.Advance(3.0);

            Assert.AreEqual(1, Scenario().GetInt(AuctionSandboxPage.KEY_HISTORY_PAGE));
            Assert.AreEqual(0, Scenario().GetInt(AuctionSandboxPage.KEY_GROUP_PAGE));
        }

        [Test]
        public void Register_SelectedSellable_MovesQuantityToMyListings()
        {
            SelectSellable("3", "100");

            EconomyTestHarness.Click(Child<Button>("RegisterButton"));
            Assert.AreEqual(AuctionSandboxPage.OP_REGISTER, _harness.LastRecord.Operation);
            _harness.Tick();

            Assert.AreEqual(9, EconomyList.GetInt(Scenario(), AuctionSandboxPage.SellableSchema, 0, "qty"));
            Assert.AreEqual(4, EconomyList.Count(Scenario(), AuctionSandboxPage.MineSchema));
            Assert.AreEqual("약초", EconomyList.GetText(Scenario(), AuctionSandboxPage.MineSchema, 3, "name"));
            Assert.AreEqual(100L, EconomyList.GetLong(Scenario(), AuctionSandboxPage.MineSchema, 3, "unit"));
            Assert.AreEqual(3, EconomyList.GetInt(Scenario(), AuctionSandboxPage.MineSchema, 3, "qty"));
            Assert.AreEqual(MarketCodes.ListingState.ACTIVE, EconomyList.GetText(Scenario(), AuctionSandboxPage.MineSchema, 3, "state"));
        }

        [Test]
        public void Register_MyListingsFull_KeepsSellableQuantityAndFails()
        {
            EconomyList.SetCount(Scenario(), AuctionSandboxPage.MineSchema, AuctionSandboxPage.MineSchema.MaxCount);
            SelectSellable("3", "100");

            EconomyTestHarness.Click(Child<Button>("RegisterButton"));
            _harness.Tick();

            Assert.AreEqual(12, EconomyList.GetInt(Scenario(), AuctionSandboxPage.SellableSchema, 0, "qty"));
            Assert.AreEqual(AuctionSandboxPage.MineSchema.MaxCount, EconomyList.Count(Scenario(), AuctionSandboxPage.MineSchema));
            StringAssert.Contains("내 등록 목록이 가득 찼다", _harness.Context.Status);
        }

        [Test]
        public void Register_Rejected_KeepsSellableQuantity()
        {
            SelectSellable("3", "100");
            _harness.SetMode(EUiSandboxResponseMode.Rejected);

            EconomyTestHarness.Click(Child<Button>("RegisterButton"));
            _harness.Tick();

            Assert.AreEqual(12, EconomyList.GetInt(Scenario(), AuctionSandboxPage.SellableSchema, 0, "qty"));
            Assert.AreEqual(3, EconomyList.Count(Scenario(), AuctionSandboxPage.MineSchema));
        }

        [Test]
        public void Register_WithoutPrice_SendsNothing()
        {
            ClickRow("_sellableRows", 0);

            EconomyTestHarness.Click(Child<Button>("RegisterButton"));

            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void Cancel_ActiveListing_MarksCancelled()
        {
            EconomyTestHarness.Click(Child<Button>("CancelButton0"));
            Assert.AreEqual(AuctionSandboxPage.OP_CANCEL, _harness.LastRecord.Operation);
            _harness.Tick();

            Assert.AreEqual(MarketCodes.ListingState.CANCELLED, EconomyList.GetText(Scenario(), AuctionSandboxPage.MineSchema, 0, "state"));
        }

        [Test]
        public void Cancel_ListingTurnsSoldOutDuringDelay_Fails()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            EconomyTestHarness.Click(Child<Button>("CancelButton0"));

            EconomyList.SetText(Scenario(), AuctionSandboxPage.MineSchema, 0, "state", MarketCodes.ListingState.SOLD_OUT);
            _harness.Advance(3.0);

            StringAssert.Contains("취소할 수 있는 등록이 아니다", _harness.Context.Status);
            Assert.AreEqual(MarketCodes.ListingState.SOLD_OUT, EconomyList.GetText(Scenario(), AuctionSandboxPage.MineSchema, 0, "state"));
        }

        [Test]
        public void Ack_Click_ClearsUnseenCount()
        {
            EconomyTestHarness.Click(Child<Button>("AckButton"));
            _harness.Tick();

            Assert.AreEqual(0, Scenario().GetInt(AuctionSandboxPage.KEY_UNSEEN));
        }

        [Test]
        public void Close_Click_IncrementsClosedCountAndClosesWindow()
        {
            EconomyTestHarness.Click(Child<Button>("CloseButton"));

            Assert.AreEqual(1, _page.ClosedCount);
            Assert.IsFalse(_view.IsOpen);
        }

        [Test]
        public void Detach_AfterAttach_RemovesAllElevenSubscribersAndDestroysFixture()
        {
            AuctionView view = _view;
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

            ClickRow("_groupRows", 0);

            Assert.AreEqual(1, _harness.Context.History.Count);
            Assert.AreEqual(1, EconomyTestHarness.SubscriberCount(_view, "GroupSelected"));
        }

        [Test]
        public void Detach_WithPendingBuy_CancelsAndKeepsStock()
        {
            SelectGroupAndListing();
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            EconomyTestHarness.Click(Child<Button>("BuyButton"));

            _page.Detach();
            _harness.Advance(5.0);

            Assert.AreEqual(EUiSandboxRequestState.Canceled, _harness.LastRecord.State);
            Assert.AreEqual(20, ListingQuantity(0));
        }

        private UiSandboxScenarioSO Scenario()
        {
            return _harness.Context.Scenario;
        }

        private int ListingQuantity(int line)
        {
            return EconomyList.GetInt(Scenario(), AuctionSandboxPage.ListingSchema, line, "qty");
        }

        private ListRowView[] Rows(string field)
        {
            return EconomyUiBuilder.GetRefs<ListRowView>(_view, field);
        }

        private T Ref<T>(string field) where T : Object
        {
            return EconomyUiBuilder.GetRef<T>(_view, field);
        }

        private T Child<T>(string name) where T : Component
        {
            return EconomyTestHarness.Child<T>(_view, name);
        }

        private void ClickRow(string field, int index)
        {
            EconomyTestHarness.Click(Rows(field)[index].GetComponentInChildren<Button>(true));
        }

        // 첫 묶음을 응답까지 받아 열고 첫 등록 줄을 고른다. 묶음 요청 한 건만 기록에 남는다
        private void SelectGroupAndListing()
        {
            ClickRow("_groupRows", 0);
            _harness.Tick();
            ClickRow("_listingRows", 0);
        }

        // 첫 판매 품목을 고르고 수량 · 가격을 입력 칸에 쓴다
        private void SelectSellable(string quantity, string price)
        {
            ClickRow("_sellableRows", 0);
            Child<TMP_InputField>("SellQuantity").text = quantity;
            Child<TMP_InputField>("SellPrice").text = price;
        }
    }
}
