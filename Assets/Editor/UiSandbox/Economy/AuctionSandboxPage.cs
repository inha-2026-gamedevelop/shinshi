// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary> 실제 AuctionView의 열한 가지 이벤트를 가짜 서버 응답에 묶는다. 묶음 · 등록 · 판매 가능 · 내 등록 · 기록 목록을 모두 편집할 수 있고 시험 배치 대상이다 </summary>
    public sealed class AuctionSandboxPage : EconomyPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "economy_auction";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";

        public const string KEY_YEOPJEON     = "auction.yeopjeon";
        public const string KEY_NOW          = "auction.serverNowMs";
        public const string KEY_GROUP_PAGE   = "auction.groupPage";
        public const string KEY_HISTORY_PAGE = "auction.historyPage";
        public const string KEY_UNSEEN       = "auction.unseen";

        public const string OP_TAB      = "경매장 탭 조회";
        public const string OP_SEARCH   = "경매장 검색";
        public const string OP_PAGE     = "경매장 쪽 이동";
        public const string OP_GROUP    = "묶음 상세 조회";
        public const string OP_BUY      = "경매장 구매";
        public const string OP_REGISTER = "경매장 등록";
        public const string OP_CANCEL   = "등록 취소";
        public const string OP_ACK      = "결과 확인";
        public const string OP_REFRESH  = "경매장 갱신";

        public const int TAB_BUY     = 0;
        public const int TAB_SELL    = 1;
        public const int TAB_MINE    = 2;
        public const int TAB_HISTORY = 3;

        private const long DEFAULT_YEOPJEON = 50000L;
        private const long DEFAULT_NOW      = 1760000000000L;
        private const long MS_PER_HOUR      = 3600000L;
        private const long MAX_MONEY        = 999999999L;
        private const int  MAX_GROUPS       = 100;
        private const int  MAX_LISTINGS     = 200;
        private const int  MAX_SELLABLES    = 100;
        private const int  MAX_MINE         = 100;
        private const int  MAX_HISTORY      = 200;
        private const int  MAX_QUANTITY     = 9999;
        private const int  MAX_PAGE         = 1000;
        private const int  FALLBACK_ROWS    = 1;

        private static readonly string[] G_GradeOptions = { "일반", "희귀", "영웅", "전설" };
        private static readonly string[] G_StateOptions = { MarketCodes.ListingState.ACTIVE, MarketCodes.ListingState.SOLD_OUT, MarketCodes.ListingState.CANCELLED, MarketCodes.ListingState.EXPIRED };
        private static readonly string[] G_RoleOptions  = { MarketCodes.TradeRole.BUYER, MarketCodes.TradeRole.SELLER };

        private static readonly EconomyListSchema G_GroupSchema = new EconomyListSchema(
            "auction.group", "구매 묶음 (최저가 · 수량 · 등록 수는 등록 목록에서 계산)", MAX_GROUPS,
            new[]
            {
                new EconomyField("name", "품목", EEconomyFieldKind.Text, "품목 {n}"),
                new EconomyField("grade", "등급", EEconomyFieldKind.Text, G_GradeOptions[0], 0, 0, G_GradeOptions),
                new EconomyField("recent", "최근가", EEconomyFieldKind.Long, "100", 0, MAX_MONEY)
            },
            new[]
            {
                new[] { "약초", "일반", "80" },
                new[] { "회복약", "희귀", "450" },
                new[] { "낡은 검", "영웅", "12000" }
            });

        private static readonly EconomyListSchema G_ListingSchema = new EconomyListSchema(
            "auction.listing", "등록 상세 (묶음 줄 번호를 group에 적는다)", MAX_LISTINGS,
            new[]
            {
                new EconomyField("group", "묶음 번호(0부터)", EEconomyFieldKind.Int, "0", 0, MAX_GROUPS - 1),
                new EconomyField("unit", "개당 가격", EEconomyFieldKind.Long, "100", 0, MAX_MONEY),
                new EconomyField("qty", "남은 수량", EEconomyFieldKind.Int, "1", 0, MAX_QUANTITY),
                new EconomyField("hours", "남은 시간(시간)", EEconomyFieldKind.Long, "24", -9999, 9999)
            },
            new[]
            {
                new[] { "0", "75", "20", "10" },
                new[] { "0", "80", "5", "30" },
                new[] { "1", "430", "3", "5" },
                new[] { "2", "11800", "1", "48" },
                new[] { "2", "12500", "1", "2" }
            });

        private static readonly EconomyListSchema G_SellableSchema = new EconomyListSchema(
            "auction.sellable", "판매 가능 품목", MAX_SELLABLES,
            new[]
            {
                new EconomyField("name", "품목", EEconomyFieldKind.Text, "품목 {n}"),
                new EconomyField("qty", "보유 수량", EEconomyFieldKind.Int, "1", 0, MAX_QUANTITY)
            },
            new[]
            {
                new[] { "약초", "12" },
                new[] { "부적", "2" }
            });

        private static readonly EconomyListSchema G_MineSchema = new EconomyListSchema(
            "auction.mine", "내 등록 (상태가 Active일 때만 취소 단추가 보인다)", MAX_MINE,
            new[]
            {
                new EconomyField("name", "품목", EEconomyFieldKind.Text, "품목 {n}"),
                new EconomyField("unit", "개당 가격", EEconomyFieldKind.Long, "100", 0, MAX_MONEY),
                new EconomyField("qty", "남은 수량", EEconomyFieldKind.Int, "1", 0, MAX_QUANTITY),
                new EconomyField("state", "상태", EEconomyFieldKind.Text, G_StateOptions[0], 0, 0, G_StateOptions),
                new EconomyField("hours", "남은 시간(시간)", EEconomyFieldKind.Long, "24", -9999, 9999)
            },
            new[]
            {
                new[] { "약초", "90", "4", "Active", "20" },
                new[] { "회복약", "500", "0", "SoldOut", "0" },
                new[] { "부적", "3000", "1", "Expired", "-3" }
            });

        private static readonly EconomyListSchema G_HistorySchema = new EconomyListSchema(
            "auction.history", "거래 기록", MAX_HISTORY,
            new[]
            {
                new EconomyField("role", "구분", EEconomyFieldKind.Text, G_RoleOptions[0], 0, 0, G_RoleOptions),
                new EconomyField("name", "품목", EEconomyFieldKind.Text, "품목 {n}"),
                new EconomyField("qty", "수량", EEconomyFieldKind.Int, "1", 0, MAX_QUANTITY),
                new EconomyField("gross", "총액", EEconomyFieldKind.Long, "100", 0, MAX_MONEY),
                new EconomyField("fee", "수수료", EEconomyFieldKind.Long, "0", 0, MAX_MONEY),
                new EconomyField("net", "받은 금액", EEconomyFieldKind.Long, "100", 0, MAX_MONEY)
            },
            new[]
            {
                new[] { "Buyer", "약초", "3", "240", "0", "240" },
                new[] { "Seller", "회복약", "1", "450", "22", "428" }
            });

        private readonly List<int> _visibleGroups   = new List<int>();
        private readonly List<int> _visibleListings = new List<int>();

        private AuctionView        _view;
        private AuctionSearchInput _search = new AuctionSearchInput(string.Empty, AuctionSearchInput.NONE, AuctionSearchInput.NONE, AuctionSearchInput.NONE, MarketCodes.Sort.UNIT_PRICE, MarketCodes.Order.ASCENDING);
        private int                _selectedGroup    = -1;
        private int                _selectedListing  = -1;
        private int                _selectedSellable = -1;
        private int                _closedCount;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "경매장"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get { return string.Empty; }
        }

        public AuctionView View
        {
            get { return _view; }
        }

        /// <summary> 마지막으로 받은 검색 입력 </summary>
        public AuctionSearchInput LastSearch
        {
            get { return _search; }
        }

        /// <summary> 지금 고른 묶음의 시나리오 줄 번호. 고른 것이 없으면 -1 </summary>
        public int SelectedGroup
        {
            get { return _selectedGroup; }
        }

        /// <summary> 닫기 단추로 Closed 이벤트가 온 횟수 </summary>
        public int ClosedCount
        {
            get { return _closedCount; }
        }

        public static EconomyListSchema GroupSchema
        {
            get { return G_GroupSchema; }
        }

        public static EconomyListSchema ListingSchema
        {
            get { return G_ListingSchema; }
        }

        public static EconomyListSchema SellableSchema
        {
            get { return G_SellableSchema; }
        }

        public static EconomyListSchema MineSchema
        {
            get { return G_MineSchema; }
        }

        public static EconomyListSchema HistorySchema
        {
            get { return G_HistorySchema; }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 검색 조건에 맞는 묶음의 시나리오 줄 번호 목록. 글자는 이름 포함, 등급은 일치로 거른다 </summary>
        public static List<int> FilterGroups(UiSandboxScenarioSO scenario, AuctionSearchInput input)
        {
            List<int> result = new List<int>();
            int count = EconomyList.Count(scenario, G_GroupSchema);
            for (int i = 0; i < count; ++i)
            {
                string name = EconomyList.GetText(scenario, G_GroupSchema, i, "name");
                if ((input.Text.Length > 0) && (name.IndexOf(input.Text, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                if ((input.Grade != AuctionSearchInput.NONE) && (GradeOf(scenario, i) != input.Grade))
                {
                    continue;
                }

                result.Add(i);
            }

            return result;
        }

        /// <summary> 한 묶음에 속한 등록(남은 수량이 있는 것)의 시나리오 줄 번호 목록 </summary>
        public static List<int> ListingsOfGroup(UiSandboxScenarioSO scenario, int group)
        {
            List<int> result = new List<int>();
            int count = EconomyList.Count(scenario, G_ListingSchema);
            for (int i = 0; i < count; ++i)
            {
                if ((EconomyList.GetInt(scenario, G_ListingSchema, i, "group") == group) && (EconomyList.GetInt(scenario, G_ListingSchema, i, "qty") > 0))
                {
                    result.Add(i);
                }
            }

            return result;
        }

        /// <summary> 한 묶음의 최저가 · 총 수량 · 등록 수. 등록이 없으면 모두 0 </summary>
        public static void GroupStats(UiSandboxScenarioSO scenario, int group, out long minPrice, out int quantity, out int listingCount)
        {
            minPrice = 0L;
            quantity = 0;
            List<int> listings = ListingsOfGroup(scenario, group);
            listingCount = listings.Count;
            for (int i = 0; i < listings.Count; ++i)
            {
                long unit = EconomyList.GetLong(scenario, G_ListingSchema, listings[i], "unit");
                if ((i == 0) || (unit < minPrice))
                {
                    minPrice = unit;
                }

                quantity += EconomyList.GetInt(scenario, G_ListingSchema, listings[i], "qty");
            }
        }

        public static int GradeOf(UiSandboxScenarioSO scenario, int group)
        {
            return Mathf.Max(0, Array.IndexOf(G_GradeOptions, EconomyList.GetText(scenario, G_GroupSchema, group, "grade")));
        }

        protected override void SeedScenario(UiSandboxScenarioSO scenario)
        {
            EconomyScenarioValues.SeedLong(scenario, KEY_YEOPJEON, DEFAULT_YEOPJEON);
            EconomyScenarioValues.SeedLong(scenario, KEY_NOW, DEFAULT_NOW);
            EconomyScenarioValues.SeedInt(scenario, KEY_GROUP_PAGE, 0);
            EconomyScenarioValues.SeedInt(scenario, KEY_HISTORY_PAGE, 0);
            EconomyScenarioValues.SeedInt(scenario, KEY_UNSEEN, 1);
            EconomyList.Seed(scenario, G_GroupSchema);
            EconomyList.Seed(scenario, G_ListingSchema);
            EconomyList.Seed(scenario, G_SellableSchema);
            EconomyList.Seed(scenario, G_MineSchema);
            EconomyList.Seed(scenario, G_HistorySchema);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _closedCount = 0;
            ResetSelection();
            _view = EconomyFixtureFactory.EnsureAuction(context, out string note);
            SetFixtureNote(note);
            if (_view == null)
            {
                context.SetStatus(note);
                return;
            }

            _view.TabChanged         += OnTabChanged;
            _view.SearchRequested    += OnSearchRequested;
            _view.PageDeltaRequested += OnPageDeltaRequested;
            _view.GroupSelected      += OnGroupSelected;
            _view.ListingSelected    += OnListingSelected;
            _view.BuyRequested       += OnBuyRequested;
            _view.SellItemSelected   += OnSellItemSelected;
            _view.RegisterRequested  += OnRegisterRequested;
            _view.CancelRequested    += OnCancelRequested;
            _view.AckRequested       += OnAckRequested;
            _view.Closed             += OnClosed;
        }

        protected override void OnApply(UiSandboxContext context)
        {
            ApplyAll(context.Scenario, false);
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            EconomyScenarioValues.DrawLong(scenario, KEY_YEOPJEON, "엽전", 0L, MAX_MONEY);
            EconomyScenarioValues.DrawLong(scenario, KEY_NOW, "서버 시각 (Unix ms)", 0L, long.MaxValue / 2L);
            EconomyScenarioValues.DrawInt(scenario, KEY_GROUP_PAGE, "구매 탭 쪽 (0부터)", 0, MAX_PAGE);
            EconomyScenarioValues.DrawInt(scenario, KEY_HISTORY_PAGE, "기록 탭 쪽 (0부터)", 0, MAX_PAGE);
            EconomyScenarioValues.DrawInt(scenario, KEY_UNSEEN, "확인하지 않은 결과 수", 0, MAX_QUANTITY);
            EconomyList.Draw(scenario, G_GroupSchema);
            EconomyList.Draw(scenario, G_ListingSchema);
            EconomyList.Draw(scenario, G_SellableSchema);
            EconomyList.Draw(scenario, G_MineSchema);
            EconomyList.Draw(scenario, G_HistorySchema);

            EditorGUILayout.LabelField("고른 묶음", (_selectedGroup < 0) ? "(없음)" : _selectedGroup.ToString());
            if (GUILayout.Button("편집 값을 화면에 다시 적용"))
            {
                Apply(context);
            }

            if ((_view != null) && (!_view.IsOpen) && (GUILayout.Button("창 다시 열기")))
            {
                _view.SetOpen(true);
            }

            if (DrawRequestButton(context, "서버 갱신 요청 (가짜)"))
            {
                Send(OP_REFRESH, () => ApplyAll(context.Scenario, true), reason => FailView(OP_REFRESH, reason));
            }
        }

        protected override void OnDetach()
        {
            if (_view != null)
            {
                _view.TabChanged         -= OnTabChanged;
                _view.SearchRequested    -= OnSearchRequested;
                _view.PageDeltaRequested -= OnPageDeltaRequested;
                _view.GroupSelected      -= OnGroupSelected;
                _view.ListingSelected    -= OnListingSelected;
                _view.BuyRequested       -= OnBuyRequested;
                _view.SellItemSelected   -= OnSellItemSelected;
                _view.RegisterRequested  -= OnRegisterRequested;
                _view.CancelRequested    -= OnCancelRequested;
                _view.AckRequested       -= OnAckRequested;
                _view.Closed             -= OnClosed;
                EconomyFixtureFactory.Release(Context, _view);
            }

            _view = null;
            ResetSelection();
        }

        private void ResetSelection()
        {
            _visibleGroups.Clear();
            _visibleListings.Clear();
            _selectedGroup    = -1;
            _selectedListing  = -1;
            _selectedSellable = -1;
        }

        private int Capacity(string field)
        {
            ListRowView[] rows = EconomyUiBuilder.GetRefs<ListRowView>(_view, field);
            return Mathf.Max(FALLBACK_ROWS, (rows == null) ? 0 : rows.Length);
        }

        private void FailView(string operation, string reason)
        {
            ReportFailure(operation, reason);
            if (_view != null)
            {
                _view.SetStatus(reason);
            }
        }

        // 모든 탭의 목록을 시나리오에서 다시 채운다. keepGroup이면 고른 묶음의 상세를 다시 연다
        private void ApplyAll(UiSandboxScenarioSO scenario, bool keepGroup)
        {
            if (_view == null)
            {
                return;
            }

            int previousGroup = keepGroup ? _selectedGroup : -1;
            ResetSelection();

            _view.SetYeopjeon(scenario.GetLong(KEY_YEOPJEON, DEFAULT_YEOPJEON));
            ApplyGroups(scenario);
            ApplySellables(scenario);
            ApplyMine(scenario);
            ApplyHistory(scenario);
            _view.SetStatus(string.Empty);

            if ((previousGroup >= 0) && (previousGroup < EconomyList.Count(scenario, G_GroupSchema)))
            {
                ShowGroup(scenario, previousGroup);
            }
            else
            {
                _view.SetListings(null, new List<ListRowData>(), 0L);
            }
        }

        private void ApplyGroups(UiSandboxScenarioSO scenario)
        {
            List<int> filtered = FilterGroups(scenario, _search);
            int pageSize = Capacity("_groupRows");
            int pageCount = Mathf.Max(1, AuctionViewRules.PageCount(filtered.Count, pageSize));
            int page = Mathf.Clamp(scenario.GetInt(KEY_GROUP_PAGE, 0), 0, pageCount - 1);
            scenario.SetInt(KEY_GROUP_PAGE, page);

            List<ListRowData> rows = new List<ListRowData>();
            _visibleGroups.Clear();
            int start = page * pageSize;
            for (int i = start; (i < filtered.Count) && (i < start + pageSize); ++i)
            {
                int group = filtered[i];
                GroupStats(scenario, group, out long minPrice, out int quantity, out int listingCount);
                _visibleGroups.Add(group);
                rows.Add(new ListRowData(GradeOf(scenario, group),
                    EconomyList.GetText(scenario, G_GroupSchema, group, "name"),
                    AuctionViewRules.FormatPrice(minPrice),
                    quantity.ToString(),
                    listingCount.ToString(),
                    AuctionViewRules.FormatPrice(EconomyList.GetLong(scenario, G_GroupSchema, group, "recent"))));
            }

            _view.SetGroups(rows, filtered.Count, page, pageCount);
        }

        private void ApplySellables(UiSandboxScenarioSO scenario)
        {
            int count = Mathf.Min(EconomyList.Count(scenario, G_SellableSchema), Capacity("_sellableRows"));
            List<ListRowData> rows = new List<ListRowData>(count);
            for (int i = 0; i < count; ++i)
            {
                rows.Add(new ListRowData(0,
                    EconomyList.GetText(scenario, G_SellableSchema, i, "name"),
                    EconomyList.GetInt(scenario, G_SellableSchema, i, "qty").ToString()));
            }

            _view.SetSellables(rows);
        }

        private void ApplyMine(UiSandboxScenarioSO scenario)
        {
            long now = scenario.GetLong(KEY_NOW, DEFAULT_NOW);
            int count = Mathf.Min(EconomyList.Count(scenario, G_MineSchema), Capacity("_mineRows"));
            List<ListRowData> rows = new List<ListRowData>(count);
            List<bool> cancellable = new List<bool>(count);
            for (int i = 0; i < count; ++i)
            {
                string state = EconomyList.GetText(scenario, G_MineSchema, i, "state");
                rows.Add(new ListRowData(0,
                    EconomyList.GetText(scenario, G_MineSchema, i, "name"),
                    AuctionViewRules.FormatPrice(EconomyList.GetLong(scenario, G_MineSchema, i, "unit")),
                    EconomyList.GetInt(scenario, G_MineSchema, i, "qty").ToString(),
                    AuctionViewRules.ListingStateText(state),
                    AuctionViewRules.FormatRemaining(now + (EconomyList.GetLong(scenario, G_MineSchema, i, "hours") * MS_PER_HOUR), now)));
                cancellable.Add(state == MarketCodes.ListingState.ACTIVE);
            }

            _view.SetMine(rows, cancellable, scenario.GetInt(KEY_UNSEEN, 0));
        }

        private void ApplyHistory(UiSandboxScenarioSO scenario)
        {
            int total = EconomyList.Count(scenario, G_HistorySchema);
            int pageSize = Capacity("_historyRows");
            int pageCount = Mathf.Max(1, AuctionViewRules.PageCount(total, pageSize));
            int page = Mathf.Clamp(scenario.GetInt(KEY_HISTORY_PAGE, 0), 0, pageCount - 1);
            scenario.SetInt(KEY_HISTORY_PAGE, page);

            List<ListRowData> rows = new List<ListRowData>();
            int start = page * pageSize;
            for (int i = start; (i < total) && (i < start + pageSize); ++i)
            {
                rows.Add(new ListRowData(0,
                    AuctionViewRules.RoleText(EconomyList.GetText(scenario, G_HistorySchema, i, "role")),
                    EconomyList.GetText(scenario, G_HistorySchema, i, "name"),
                    EconomyList.GetInt(scenario, G_HistorySchema, i, "qty").ToString(),
                    AuctionViewRules.FormatPrice(EconomyList.GetLong(scenario, G_HistorySchema, i, "gross")),
                    AuctionViewRules.FormatPrice(EconomyList.GetLong(scenario, G_HistorySchema, i, "fee")),
                    AuctionViewRules.FormatPrice(EconomyList.GetLong(scenario, G_HistorySchema, i, "net"))));
            }

            _view.SetHistory(rows, page, (page + 1) < pageCount);
        }

        private void ShowGroup(UiSandboxScenarioSO scenario, int group)
        {
            _selectedGroup   = group;
            _selectedListing = -1;
            _visibleListings.Clear();

            long now = scenario.GetLong(KEY_NOW, DEFAULT_NOW);
            int grade = GradeOf(scenario, group);
            List<int> listings = ListingsOfGroup(scenario, group);
            int capacity = Capacity("_listingRows");
            List<ListRowData> rows = new List<ListRowData>();
            for (int i = 0; (i < listings.Count) && (i < capacity); ++i)
            {
                int line = listings[i];
                _visibleListings.Add(line);
                rows.Add(new ListRowData(grade,
                    AuctionViewRules.FormatPrice(EconomyList.GetLong(scenario, G_ListingSchema, line, "unit")),
                    EconomyList.GetInt(scenario, G_ListingSchema, line, "qty").ToString(),
                    AuctionViewRules.FormatRemaining(now + (EconomyList.GetLong(scenario, G_ListingSchema, line, "hours") * MS_PER_HOUR), now)));
            }

            _view.SetListings(EconomyList.GetText(scenario, G_GroupSchema, group, "name"), rows, EconomyList.GetLong(scenario, G_GroupSchema, group, "recent"));
        }

        private void OnTabChanged(int tab)
        {
            Send(OP_TAB, () => ApplyAll(Context.Scenario, true), reason => FailView(OP_TAB, reason));
        }

        private void OnSearchRequested(AuctionSearchInput input)
        {
            Send(OP_SEARCH, () =>
            {
                _search = input;
                Context.Scenario.SetInt(KEY_GROUP_PAGE, 0);
                ApplyAll(Context.Scenario, false);
                ReportSuccess(OP_SEARCH);
            }, reason => FailView(OP_SEARCH, reason));
        }

        // 구매 탭이면 묶음 쪽, 기록 탭이면 기록 쪽을 옮긴다
        private void OnPageDeltaRequested(int delta)
        {
            if (_view == null)
            {
                return;
            }

            string key = (_view.Tab == TAB_HISTORY) ? KEY_HISTORY_PAGE : KEY_GROUP_PAGE;
            int targetPage = Mathf.Max(0, Context.Scenario.GetInt(key, 0) + delta);
            Send(OP_PAGE, () =>
            {
                UiSandboxScenarioSO scenario = Context.Scenario;
                scenario.SetInt(key, targetPage);
                ApplyAll(scenario, false);
                ReportSuccess(OP_PAGE);
            }, reason => FailView(OP_PAGE, reason));
        }

        private void OnGroupSelected(int row)
        {
            if ((row < 0) || (row >= _visibleGroups.Count))
            {
                return;
            }

            int group = _visibleGroups[row];
            string groupName = EconomyList.GetText(Context.Scenario, G_GroupSchema, group, "name");
            Send(OP_GROUP, () =>
            {
                if ((group >= EconomyList.Count(Context.Scenario, G_GroupSchema))
                    || (EconomyList.GetText(Context.Scenario, G_GroupSchema, group, "name") != groupName))
                {
                    FailView(OP_GROUP, "묶음이 이미 바뀌었다");
                    return;
                }

                ShowGroup(Context.Scenario, group);
                ReportSuccess(OP_GROUP);
            }, reason => FailView(OP_GROUP, reason));
        }

        // 등록 한 줄을 고르는 일은 화면 안에서 끝나므로 요청을 보내지 않는다
        private void OnListingSelected(int row)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (_view == null) || (row < 0) || (row >= _visibleListings.Count))
            {
                return;
            }

            _selectedListing = _visibleListings[row];
            _view.SetBuyTarget(EconomyList.GetLong(context.Scenario, G_ListingSchema, _selectedListing, "unit"),
                EconomyList.GetInt(context.Scenario, G_ListingSchema, _selectedListing, "qty"));
        }

        private void OnBuyRequested(int quantity)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (_selectedListing < 0) || (_selectedGroup < 0) || (quantity < 1))
            {
                return;
            }

            int listing = _selectedListing;
            int group = _selectedGroup;
            long unit = EconomyList.GetLong(context.Scenario, G_ListingSchema, listing, "unit");
            Send(OP_BUY, () => CompleteBuy(group, listing, unit, quantity), reason => FailView(OP_BUY, reason));
        }

        // 응답이 올 때 등록이 바뀌었으면 줄 번호 대신 묶음과 개당 가격으로 확인해 거절한다
        private void CompleteBuy(int group, int listing, long unit, int quantity)
        {
            UiSandboxScenarioSO scenario = Context.Scenario;
            bool same = (listing < EconomyList.Count(scenario, G_ListingSchema))
                && (EconomyList.GetInt(scenario, G_ListingSchema, listing, "group") == group)
                && (EconomyList.GetLong(scenario, G_ListingSchema, listing, "unit") == unit);
            int remaining = same ? EconomyList.GetInt(scenario, G_ListingSchema, listing, "qty") : 0;
            if ((!same) || (remaining < quantity))
            {
                FailView(OP_BUY, "등록이 이미 바뀌었다");
                return;
            }

            long gross = unit * quantity;
            EconomyList.SetInt(scenario, G_ListingSchema, listing, "qty", remaining - quantity);
            scenario.SetLong(KEY_YEOPJEON, Math.Max(0L, scenario.GetLong(KEY_YEOPJEON, 0L) - gross));
            AddHistory(scenario, MarketCodes.TradeRole.BUYER, EconomyList.GetText(scenario, G_GroupSchema, group, "name"), quantity, gross, 0L, gross);
            ApplyAll(scenario, true);
            ReportSuccess(OP_BUY);
        }

        private void OnSellItemSelected(int row)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (_view == null) || (row < 0) || (row >= EconomyList.Count(context.Scenario, G_SellableSchema)))
            {
                return;
            }

            _selectedSellable = row;
            _view.SetSellTarget(EconomyList.GetText(context.Scenario, G_SellableSchema, row, "name"),
                EconomyList.GetInt(context.Scenario, G_SellableSchema, row, "qty"));
        }

        private void OnRegisterRequested(int quantity, long price)
        {
            if ((Context == null) || (_selectedSellable < 0) || (quantity < 1) || (price < 1L))
            {
                return;
            }

            int sellable = _selectedSellable;
            string name = EconomyList.GetText(Context.Scenario, G_SellableSchema, sellable, "name");
            Send(OP_REGISTER, () => CompleteRegister(sellable, name, quantity, price), reason => FailView(OP_REGISTER, reason));
        }

        private void CompleteRegister(int sellable, string name, int quantity, long price)
        {
            UiSandboxScenarioSO scenario = Context.Scenario;
            bool same = (sellable < EconomyList.Count(scenario, G_SellableSchema))
                && (EconomyList.GetText(scenario, G_SellableSchema, sellable, "name") == name);
            int owned = same ? EconomyList.GetInt(scenario, G_SellableSchema, sellable, "qty") : 0;
            if ((!same) || (owned < quantity))
            {
                FailView(OP_REGISTER, "보유 수량이 이미 바뀌었다");
                return;
            }

            // 내 등록 목록이 가득 차 줄을 못 더하면 보유 수량을 건드리지 않고 실패로 끝낸다
            if (!EconomyList.Add(scenario, G_MineSchema))
            {
                FailView(OP_REGISTER, "내 등록 목록이 가득 찼다");
                return;
            }

            int line = EconomyList.Count(scenario, G_MineSchema) - 1;
            EconomyList.SetText(scenario, G_MineSchema, line, "name", name);
            EconomyList.SetLong(scenario, G_MineSchema, line, "unit", price);
            EconomyList.SetInt(scenario, G_MineSchema, line, "qty", quantity);
            EconomyList.SetText(scenario, G_MineSchema, line, "state", MarketCodes.ListingState.ACTIVE);

            if (owned == quantity)
            {
                EconomyList.RemoveAt(scenario, G_SellableSchema, sellable);
            }
            else
            {
                EconomyList.SetInt(scenario, G_SellableSchema, sellable, "qty", owned - quantity);
            }

            ApplyAll(scenario, true);
            ReportSuccess(OP_REGISTER);
        }

        private void OnCancelRequested(int row)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (row < 0) || (row >= EconomyList.Count(context.Scenario, G_MineSchema)))
            {
                return;
            }

            string name = EconomyList.GetText(context.Scenario, G_MineSchema, row, "name");
            Send(OP_CANCEL, () =>
            {
                UiSandboxScenarioSO scenario = Context.Scenario;
                bool same = (row < EconomyList.Count(scenario, G_MineSchema))
                    && (EconomyList.GetText(scenario, G_MineSchema, row, "name") == name)
                    && (EconomyList.GetText(scenario, G_MineSchema, row, "state") == MarketCodes.ListingState.ACTIVE);
                if (!same)
                {
                    FailView(OP_CANCEL, "취소할 수 있는 등록이 아니다");
                    return;
                }

                EconomyList.SetText(scenario, G_MineSchema, row, "state", MarketCodes.ListingState.CANCELLED);
                ApplyAll(scenario, true);
                ReportSuccess(OP_CANCEL);
            }, reason => FailView(OP_CANCEL, reason));
        }

        private void OnAckRequested()
        {
            if (Context == null)
            {
                return;
            }

            Send(OP_ACK, () =>
            {
                Context.Scenario.SetInt(KEY_UNSEEN, 0);
                ApplyAll(Context.Scenario, true);
                ReportSuccess(OP_ACK);
            }, reason => FailView(OP_ACK, reason));
        }

        private void OnClosed()
        {
            ++_closedCount;
            if (Context != null)
            {
                Context.SetStatus("경매장 창이 닫혔다");
            }
        }

        private static void AddHistory(UiSandboxScenarioSO scenario, string role, string name, int quantity, long gross, long fee, long net)
        {
            if (!EconomyList.Add(scenario, G_HistorySchema))
            {
                return;
            }

            int line = EconomyList.Count(scenario, G_HistorySchema) - 1;
            EconomyList.SetText(scenario, G_HistorySchema, line, "role", role);
            EconomyList.SetText(scenario, G_HistorySchema, line, "name", name);
            EconomyList.SetInt(scenario, G_HistorySchema, line, "qty", quantity);
            EconomyList.SetLong(scenario, G_HistorySchema, line, "gross", gross);
            EconomyList.SetLong(scenario, G_HistorySchema, line, "fee", fee);
            EconomyList.SetLong(scenario, G_HistorySchema, line, "net", net);
        }
    }
}
