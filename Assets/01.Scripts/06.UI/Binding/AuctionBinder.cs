// System
using System;
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 경매장 창과 TownMarketClient를 잇는다. 검색, 상세, 등록, 구매, 취소, 확인을 보내고 결과로 창을 채운다 </summary>
    public sealed class AuctionBinder : MonoBehaviour
    {
        // 품목 이름은 번역 표가 없어 정의 ID를 쓴다. 이름 검색은 클라이언트가 입력 글자로 정의 ID 목록을 만들어 보낸다.
        // 구매, 등록, 취소는 확인 팝업을 거친 뒤에만 보낸다. 결과를 모르면(BackendUnavailable) 같은 요청 ID로 최대 두 번 다시 보낸다.
        // 쓰기가 성공하면 내 등록, 현재 목록, 성장, 생활 스냅숏을 다시 받는다

        /****************************************
        *                Fields
        ****************************************/

        private const int    TAB_BUY      = 0;
        private const int    TAB_SELL     = 1;
        private const int    TAB_LISTINGS = 2;
        private const int    TAB_HISTORY  = 3;
        private const int    MAX_WRITE_RETRIES = 2;
        private const float  RETRY_SECONDS     = 1f;
        private const int    INITIAL_CAPACITY  = 64;
        private const string DONE_TEXT         = "처리되었습니다";
        private const string NO_RESULT_TEXT    = "검색 결과가 없습니다";
        private const string OWN_LISTING_TEXT  = "내 등록은 살 수 없습니다";
        private const string PENDING_TEXT      = "서버 응답을 기다리는 중입니다";
        private const string SLASH_FORMAT      = "{0}/{1}";

        [SerializeField] private AuctionView _view     = null;
        [SerializeField] private UiPopupHub  _popupHub = null;

        private readonly List<ListRowData>          _rows       = new List<ListRowData>(INITIAL_CAPACITY);
        private readonly List<bool>                 _flags      = new List<bool>(INITIAL_CAPACITY);
        private readonly List<SellableItem>         _sellables  = new List<SellableItem>(INITIAL_CAPACITY);
        private readonly List<string>               _foundIds   = new List<string>(MarketLimits.MAX_SEARCH_DEFINITIONS);
        private readonly List<string>               _candidates = new List<string>(INITIAL_CAPACITY);
        private readonly List<string>               _ackIds     = new List<string>(INITIAL_CAPACITY);

        private MarketGroupEntry[]   _groups   = Array.Empty<MarketGroupEntry>();
        private MarketListingEntry[] _listings = Array.Empty<MarketListingEntry>();
        private MarketListingEntry[] _mine     = Array.Empty<MarketListingEntry>();
        private AuctionSearchInput   _lastSearch;
        private bool                 _hasSearch;
        private int                  _searchPage;
        private int                  _historyPage;
        private int                  _groupSelected   = -1;
        private int                  _listingSelected = -1;
        private int                  _sellSelected    = -1;
        private long                 _serverNowMs;
        private bool                 _wasOpen;
        private bool                 _sellablesDirty = true;

        private Guid  _searchId;
        private Guid  _listingsId;
        private Guid  _mineId;
        private Guid  _historyId;
        private Guid  _writeId;
        private int   _retries;
        private float _retryAt;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownMarketClient.ResultReceived  += OnResult;
            TownGrowthClient.SnapshotChanged += OnGrowthSnapshot;
            TownLifeClient.SnapshotChanged   += OnLifeSnapshot;
            if (_view != null)
            {
                _view.TabChanged         += OnTabChanged;
                _view.SearchRequested    += OnSearch;
                _view.PageDeltaRequested += OnPageDelta;
                _view.GroupSelected      += OnGroupSelected;
                _view.ListingSelected    += OnListingSelected;
                _view.BuyRequested       += OnBuyRequested;
                _view.SellItemSelected   += OnSellItemSelected;
                _view.RegisterRequested  += OnRegisterRequested;
                _view.CancelRequested    += OnCancelRequested;
                _view.AckRequested       += OnAckRequested;
            }
        }

        private void OnDisable()
        {
            TownMarketClient.ResultReceived  -= OnResult;
            TownGrowthClient.SnapshotChanged -= OnGrowthSnapshot;
            TownLifeClient.SnapshotChanged   -= OnLifeSnapshot;
            if (_view != null)
            {
                _view.TabChanged         -= OnTabChanged;
                _view.SearchRequested    -= OnSearch;
                _view.PageDeltaRequested -= OnPageDelta;
                _view.GroupSelected      -= OnGroupSelected;
                _view.ListingSelected    -= OnListingSelected;
                _view.BuyRequested       -= OnBuyRequested;
                _view.SellItemSelected   -= OnSellItemSelected;
                _view.RegisterRequested  -= OnRegisterRequested;
                _view.CancelRequested    -= OnCancelRequested;
                _view.AckRequested       -= OnAckRequested;
            }
        }

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            bool isOpen = _view.IsOpen;
            if ((isOpen) && (!_wasOpen))
            {
                OnOpened();
            }

            _wasOpen = isOpen;
            if ((isOpen) && (_sellablesDirty) && (_view.Tab == TAB_SELL))
            {
                RebuildSellables();
            }

            if ((_retryAt > 0f) && (Time.unscaledTime >= _retryAt))
            {
                _retryAt = 0f;
                if (!TownMarketClient.RetryLastWrite())
                {
                    _view.SetStatus(RejectText.Describe(ERejectReason.BackendUnavailable));
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnOpened()
        {
            if (!ClientRoles.IsTown())
            {
                _view.SetStatus(string.Empty);
                return;
            }

            RequestSnapshots();
            _mineId = TownMarketClient.RequestMine();
            _view.SetStatus(string.Empty);
            _sellablesDirty = true;
            OnTabChanged(_view.Tab);
        }

        private static void RequestSnapshots()
        {
            TownGrowthClient.RequestSnapshot();
            TownLifeClient.RequestSnapshot();
        }

        private void OnTabChanged(int tab)
        {
            switch (tab)
            {
                case TAB_BUY:
                    if (!_hasSearch)
                    {
                        OnSearch(new AuctionSearchInput(string.Empty, AuctionSearchInput.NONE, AuctionSearchInput.NONE,
                            AuctionSearchInput.NONE, MarketCodes.Sort.UNIT_PRICE, MarketCodes.Order.ASCENDING));
                    }

                    return;
                case TAB_SELL:
                    _sellablesDirty = true;
                    RequestSnapshots();
                    return;
                case TAB_LISTINGS:
                    _mineId = TownMarketClient.RequestMine();
                    return;
                case TAB_HISTORY:
                    _historyPage = 0;
                    _historyId   = TownMarketClient.RequestHistory(_historyPage);
                    return;
            }
        }

        private void OnSearch(AuctionSearchInput input)
        {
            _lastSearch = input;
            _hasSearch  = true;
            _searchPage = 0;
            SendSearch();
        }

        private void SendSearch()
        {
            string[] ids = null;
            if (!string.IsNullOrWhiteSpace(_lastSearch.Text))
            {
                CollectCandidates();
                AuctionViewRules.FindDefinitionIds(_lastSearch.Text, _candidates, MarketLimits.MAX_SEARCH_DEFINITIONS, _foundIds);
                if (_foundIds.Count == 0)
                {
                    _groups = Array.Empty<MarketGroupEntry>();
                    ShowGroups(0, 0);
                    _view.SetStatus(NO_RESULT_TEXT);
                    return;
                }

                ids = _foundIds.ToArray();
            }

            _searchId = TownMarketClient.Search(_lastSearch.Category, _lastSearch.ClassId, _lastSearch.Grade, ids, _lastSearch.Sort,
                _lastSearch.Order, _searchPage);
        }

        private void OnPageDelta(int delta)
        {
            if (_view.Tab == TAB_HISTORY)
            {
                _historyPage = Math.Max(0, _historyPage + delta);
                _historyId   = TownMarketClient.RequestHistory(_historyPage);
                return;
            }

            _searchPage = Math.Max(0, _searchPage + delta);
            SendSearch();
        }

        private void OnGroupSelected(int index)
        {
            if ((index < 0) || (index >= _groups.Length))
            {
                return;
            }

            _groupSelected = index;
            _listingsId    = TownMarketClient.QueryListings(_groups[index].DefinitionId, 0);
        }

        private void OnListingSelected(int index)
        {
            if ((index < 0) || (index >= _listings.Length))
            {
                return;
            }

            _listingSelected = index;
            MarketListingEntry listing = _listings[index];
            if (listing.Mine)
            {
                _view.SetBuyTarget(0, 0);
                _view.SetStatus(OWN_LISTING_TEXT);
                return;
            }

            _view.SetStatus(string.Empty);
            _view.SetBuyTarget(listing.UnitPrice, listing.RemainingQty);
        }

        private void OnBuyRequested(int quantity)
        {
            if ((_listingSelected < 0) || (_listingSelected >= _listings.Length))
            {
                return;
            }

            MarketListingEntry listing = _listings[_listingSelected];
            if ((quantity < 1) || (quantity > listing.RemainingQty) || (!MarketFeeRules.TryGross(listing.UnitPrice, quantity, out long gross)))
            {
                return;
            }

            string text = AuctionViewRules.BuyConfirmText(listing.DefinitionId, quantity, listing.UnitPrice, gross);
            Confirm(text, () => SendWrite(TownMarketClient.Buy(listing.ListingId, quantity, listing.UnitPrice)));
        }

        private void OnSellItemSelected(int index)
        {
            if ((index < 0) || (index >= _sellables.Count))
            {
                return;
            }

            _sellSelected = index;
            _view.SetSellTarget(_sellables[index].DefinitionId, _sellables[index].MaxQuantity);
        }

        private void OnRegisterRequested(int quantity, long unitPrice)
        {
            if ((_sellSelected < 0) || (_sellSelected >= _sellables.Count))
            {
                return;
            }

            SellableItem item = _sellables[_sellSelected];
            EAuctionFormError error = AuctionViewRules.ValidateRegister(true, unitPrice, quantity, item.MaxQuantity);
            if ((error != EAuctionFormError.None) || (!AuctionViewRules.TryPreview(unitPrice, quantity, out _, out _, out long net)))
            {
                _view.SetStatus(AuctionViewRules.FormErrorText(error));
                return;
            }

            string kind = item.IsEquipment ? MarketCodes.ASSET_EQUIPMENT : MarketCodes.ASSET_ACCOUNT_ITEM;
            string text = AuctionViewRules.ListConfirmText(item.DefinitionId, quantity, unitPrice, net);
            Confirm(text, () => SendWrite(TownMarketClient.List(kind, item.DefinitionId, item.ItemId, quantity, unitPrice)));
        }

        private void OnCancelRequested(int mineIndex)
        {
            if ((mineIndex < 0) || (mineIndex >= _mine.Length))
            {
                return;
            }

            MarketListingEntry listing = _mine[mineIndex];
            string text = AuctionViewRules.CancelConfirmText(listing.DefinitionId, listing.RemainingQty);
            Confirm(text, () => SendWrite(TownMarketClient.Cancel(listing.ListingId)));
        }

        private void OnAckRequested()
        {
            _ackIds.Clear();
            for (int i = 0; (i < _mine.Length) && (_ackIds.Count < MarketMessageSerializer.MAX_STRINGS); ++i)
            {
                if ((!_mine[i].Seen) && (_mine[i].State != MarketCodes.ListingState.ACTIVE))
                {
                    _ackIds.Add(_mine[i].ListingId);
                }
            }

            if (_ackIds.Count > 0)
            {
                SendWrite(TownMarketClient.Acknowledge(_ackIds.ToArray()));
            }
        }

        private void Confirm(string text, Action onYes)
        {
            if (_popupHub == null)
            {
                return;
            }

            _popupHub.ShowConfirm(EPopupSize.Medium, text, accepted =>
            {
                if (accepted)
                {
                    onYes();
                }
            });
        }

        private void SendWrite(Guid id)
        {
            _writeId = id;
            _retries = 0;
            _view.SetStatus((id == Guid.Empty) ? string.Empty : PENDING_TEXT);
        }

        private void OnResult(MarketResultMessage result)
        {
            if (_view == null)
            {
                return;
            }

            _serverNowMs = (result.ServerTimeUnixMs > 0) ? result.ServerTimeUnixMs : _serverNowMs;
            switch (result.Action)
            {
                case EMarketAction.Search:
                    if (result.RequestId == _searchId)
                    {
                        OnSearchResult(in result);
                    }

                    return;
                case EMarketAction.Listings:
                    if (result.RequestId == _listingsId)
                    {
                        OnListingsResult(in result);
                    }

                    return;
                case EMarketAction.Mine:
                    if (result.RequestId == _mineId)
                    {
                        OnMineResult(in result);
                    }

                    return;
                case EMarketAction.History:
                    if (result.RequestId == _historyId)
                    {
                        OnHistoryResult(in result);
                    }

                    return;
                default:
                    if (result.RequestId == _writeId)
                    {
                        OnWriteResult(in result);
                    }

                    return;
            }
        }

        private void OnSearchResult(in MarketResultMessage result)
        {
            if (!result.IsAccepted)
            {
                _view.SetStatus(DescribeRead(result.Reason));
                return;
            }

            _groups = result.Groups;
            ShowGroups(result.TotalCount, result.Page);
            _view.SetListings(null, null, 0);
            _view.SetStatus(string.Empty);
        }

        private void ShowGroups(int totalCount, int page)
        {
            _rows.Clear();
            for (int i = 0; i < _groups.Length; ++i)
            {
                MarketGroupEntry group = _groups[i];
                _rows.Add(new ListRowData(group.Grade, group.DefinitionId, AuctionViewRules.FormatPrice(group.LowestUnitPrice),
                    AuctionViewRules.FormatPrice(group.TotalQty), group.ListingCount.ToString(CultureInfo.InvariantCulture),
                    (group.RecentUnitPrice > 0) ? AuctionViewRules.FormatPrice(group.RecentUnitPrice) : "-"));
            }

            _view.SetGroups(_rows, totalCount, page, AuctionViewRules.PageCount(totalCount, MarketLimits.PAGE_SIZE));
        }

        private void OnListingsResult(in MarketResultMessage result)
        {
            if (!result.IsAccepted)
            {
                _view.SetStatus(DescribeRead(result.Reason));
                return;
            }

            _listings        = result.Listings;
            _listingSelected = -1;
            _rows.Clear();
            for (int i = 0; i < _listings.Length; ++i)
            {
                MarketListingEntry listing = _listings[i];
                string time = AuctionViewRules.FormatRemaining(listing.ExpiresAtUnixMs, _serverNowMs);
                _rows.Add(new ListRowData(listing.Grade, AuctionViewRules.FormatPrice(listing.UnitPrice),
                    listing.RemainingQty.ToString(CultureInfo.InvariantCulture), time));
            }

            string title = ((_groupSelected >= 0) && (_groupSelected < _groups.Length)) ? _groups[_groupSelected].DefinitionId : string.Empty;
            _view.SetListings(title, _rows, result.AverageUnitPrice24h);
        }

        private void OnMineResult(in MarketResultMessage result)
        {
            if (!result.IsAccepted)
            {
                _view.SetStatus(DescribeRead(result.Reason));
                return;
            }

            _mine = result.Listings;
            _view.SetYeopjeon(result.AvailableYeopjeon);
            _rows.Clear();
            _flags.Clear();
            for (int i = 0; i < _mine.Length; ++i)
            {
                MarketListingEntry listing = _mine[i];
                bool active = listing.State == MarketCodes.ListingState.ACTIVE;
                string detail = active
                    ? AuctionViewRules.FormatRemaining(listing.ExpiresAtUnixMs, _serverNowMs)
                    : AuctionViewRules.FormatPrice(listing.NetTotal);
                _rows.Add(new ListRowData(listing.Grade, listing.DefinitionId, AuctionViewRules.FormatPrice(listing.UnitPrice),
                    string.Format(CultureInfo.InvariantCulture, SLASH_FORMAT, listing.RemainingQty, listing.ListedQty),
                    AuctionViewRules.ListingStateText(listing.State), detail));
                _flags.Add(active);
            }

            _view.SetMine(_rows, _flags, result.UnseenCount);
        }

        private void OnHistoryResult(in MarketResultMessage result)
        {
            if (!result.IsAccepted)
            {
                _view.SetStatus(DescribeRead(result.Reason));
                return;
            }

            _rows.Clear();
            MarketTradeEntry[] trades = result.Trades;
            for (int i = 0; i < trades.Length; ++i)
            {
                MarketTradeEntry trade = trades[i];
                bool seller = trade.Role == MarketCodes.TradeRole.SELLER;
                _rows.Add(new ListRowData(0, AuctionViewRules.RoleText(trade.Role), trade.DefinitionId,
                    trade.Quantity.ToString(CultureInfo.InvariantCulture), AuctionViewRules.FormatPrice(trade.Gross),
                    seller ? AuctionViewRules.FormatPrice(trade.Fee) : "-", seller ? AuctionViewRules.FormatPrice(trade.Net) : "-"));
            }

            _view.SetHistory(_rows, _historyPage, result.HasMore);
        }

        private void OnWriteResult(in MarketResultMessage result)
        {
            if (result.IsAccepted)
            {
                _writeId = Guid.Empty;
                _view.SetStatus(DONE_TEXT);
                RefreshAfterWrite();
                return;
            }

            // 결과를 모르면 같은 요청 ID로 다시 보낸다. 서버와 API가 처음 결과를 돌려주므로 효과는 한 번이다
            if ((result.Reason == ERejectReason.BackendUnavailable) && (_retries < MAX_WRITE_RETRIES))
            {
                ++_retries;
                _retryAt = Time.unscaledTime + RETRY_SECONDS;
                _view.SetStatus(PENDING_TEXT);
                return;
            }

            _writeId = Guid.Empty;
            _view.SetStatus(DescribeWrite(result.Reason));
            if ((result.Reason == ERejectReason.MarketPriceChanged) || (result.Reason == ERejectReason.MarketSoldOut)
                || (result.Reason == ERejectReason.MarketQuantityChanged) || (result.Reason == ERejectReason.MarketExpired))
            {
                RefreshAfterWrite();
            }
        }

        private void RefreshAfterWrite()
        {
            _mineId = TownMarketClient.RequestMine();
            RequestSnapshots();
            _sellablesDirty = true;
            if ((_view.Tab == TAB_BUY) && (_groupSelected >= 0) && (_groupSelected < _groups.Length))
            {
                _listingsId = TownMarketClient.QueryListings(_groups[_groupSelected].DefinitionId, 0);
            }
        }

        private void OnGrowthSnapshot(GrowthSnapshotMessage snapshot)
        {
            _sellablesDirty = true;
        }

        private void OnLifeSnapshot(LifeSnapshotMessage snapshot)
        {
            _sellablesDirty = true;
        }

        private void RebuildSellables()
        {
            _sellablesDirty = false;
            GrowthSnapshotMessage growth = TownGrowthClient.Snapshot;
            LifeSnapshotMessage   life   = TownLifeClient.Snapshot;
            SellableItemRules.Build(TownGrowthClient.HasSnapshot, in growth, TownLifeClient.HasSnapshot, in life, _sellables);
            _rows.Clear();
            for (int i = 0; i < _sellables.Count; ++i)
            {
                SellableItem item = _sellables[i];
                _rows.Add(new ListRowData(item.Grade, item.DefinitionId, item.MaxQuantity.ToString(CultureInfo.InvariantCulture)));
            }

            _sellSelected = -1;
            _view.SetSellables(_rows);
        }

        // 이름 검색 후보. 장비 정의, 부적 효과, 약초를 모은다
        private void CollectCandidates()
        {
            if (_candidates.Count > 0)
            {
                return;
            }

            _candidates.Add(MarketTradeRules.HERB_ID);
            if (!GameDB.IsInitialized)
            {
                return;
            }

            if (GameDB.Equipment != null)
            {
                for (int i = 0; i < GameDB.Equipment.Equipment.Count; ++i)
                {
                    _candidates.Add(GameDB.Equipment.Equipment[i].DefinitionId);
                }
            }

            if (GameDB.Talismans != null)
            {
                for (int i = 0; i < GameDB.Talismans.Effects.Count; ++i)
                {
                    _candidates.Add(GameDB.Talismans.Effects[i].DefinitionId);
                }
            }
        }

        private static string DescribeRead(ERejectReason reason)
        {
            string text = RejectText.Describe(reason);
            return string.IsNullOrEmpty(text) ? PENDING_TEXT : text;
        }

        private static string DescribeWrite(ERejectReason reason)
        {
            string text = RejectText.Describe(reason);
            return string.IsNullOrEmpty(text) ? reason.ToString() : text;
        }
    }
}
