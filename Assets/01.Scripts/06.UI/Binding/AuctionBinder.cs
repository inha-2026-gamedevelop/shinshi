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
        // 구매, 등록, 취소는 확인 팝업을 거친 뒤에만 보낸다. 미확정 쓰기 상태와 ID, 원문은 TownMarketClient가 들고 있고 이 창은 그 상태를 보여 준다.
        // 결과를 모르면(BackendUnavailable, 응답 시간 초과) 같은 요청 ID로 최대 두 번 자동으로 다시 확인하고, 그 뒤에는 결과 불명으로 남겨
        // 사용자가 팝업에서 같은 ID 재확인을 고르게 한다. 재확인 전에는 새 쓰기를 막고, 새 ID로 대신 보내지 않는다.
        // 연결이 바뀐 뒤에는 이전 원문을 보내지 않고 내 등록과 거래 내역을 서버에서 다시 받아 사용자가 직접 확인하게 한다(쓰기는 막지 않는다).
        // 쓰기가 성공하면 내 등록, 현재 목록, 성장, 생활 스냅숏을 다시 받는다

        /****************************************
        *                Fields
        ****************************************/

        private const int    TAB_BUY      = 0;
        private const int    TAB_SELL     = 1;
        private const int    TAB_LISTINGS = 2;
        private const int    TAB_HISTORY  = 3;
        private const int    INITIAL_CAPACITY  = 64;
        private const string DONE_TEXT         = "처리되었습니다";
        private const string NO_RESULT_TEXT    = "검색 결과가 없습니다";
        private const string OWN_LISTING_TEXT  = "내 등록은 살 수 없습니다";
        private const string PENDING_TEXT      = "서버 응답을 기다리는 중입니다";
        private const string RECONFIRM_TEXT    = "요청 결과를 확인하지 못했습니다. 같은 요청으로 다시 확인해야 새 요청을 보낼 수 있습니다";
        private const string RECONFIRM_PROMPT  = "이전 요청의 결과를 확인하지 못했습니다.\n같은 요청으로 결과를 다시 확인할까요?";
        private const string STALE_TEXT        = "이전 연결에서 보낸 요청의 결과를 확인하지 못했습니다. 내 등록과 거래 내역을 불러오는 중입니다";
        private const string STALE_DONE_TEXT   = "내 등록과 거래 내역을 다시 불러왔습니다. 이전 요청의 처리 여부는 목록과 내역에서 확인하세요";
        private const string STALE_RETRY_TEXT  = "내 등록과 거래 내역을 불러오지 못했습니다. 잠시 뒤 다시 불러옵니다";
        private const string RECONFIRM_NO_POPUP_TEXT = "요청 결과를 확인하지 못했습니다. 확인 창을 열 수 없어 자동으로 다시 보내지 않습니다. 내 등록과 거래 내역에서 처리 여부를 확인하세요";
        private const string RECONFIRM_LOST_TEXT     = "이전 요청은 더 이상 다시 확인할 수 없습니다. 내 등록과 거래 내역에서 처리 여부를 확인하세요";
        private const string SLASH_FORMAT      = "{0}/{1}";
        private const float  STALE_RETRY_SECONDS = 3f;

        // 응답이 오지 않는 읽기를 언제까지 진행 중으로 볼지는 API 대기 시간에 여유를 더한 값이다
        private const float  READ_WAIT_MARGIN_SECONDS = 3f;

        // 검색과 상세가 일시 거절(요청 한도, 같은 읽기 진행 중, 서버 불안정)되면 서버의 1초 요청 창을 넘긴 뒤 몇 번만 다시 받는다
        private const float  READ_RETRY_SECONDS = 1.2f;
        private const int    MAX_READ_RETRIES   = 2;

        // 같은 품목을 다시 눌렀을 때 이 시간 안에 받은 상세는 다시 요청하지 않는다. 쓰기가 끝나면 시간과 관계없이 다시 받는다
        private const float  LISTINGS_FRESH_SECONDS = 3f;
        private const string LOADING_TEXT           = "불러오는 중입니다";

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

        // 상세 읽기는 서버가 연결당 하나만 받는다. 진행 중에 다른 묶음을 누르면 요청을 덮어쓰지 않고 고른 품목만 기억해 두었다가
        // 응답이 오면 그 품목을 다시 받는다. 보낸 품목과 다른 응답은 화면에 쓰지 않는다
        private string _wantedDefinitionId;
        private string _listingsDefinitionId;
        private bool   _listingsDirty;
        private float  _listingsSentAt;
        private float  _searchSentAt;

        // 상세에 지금 보이는 품목과 받은 시각, 검색 진행 중에 바뀐 조건, 일시 거절 뒤 다시 받을 시각과 횟수
        private string _shownDefinitionId;
        private float  _shownAt;
        private bool   _searchDirty;
        private float  _listingsRetryAt;
        private int    _listingsRetries;
        private float  _searchRetryAt;
        private int    _searchRetries;

        // 읽기 실패나 대기로 띄운 안내. 다음 읽기가 성공하면 그 안내가 남아 있을 때만 지운다
        private string _readStatus;

        private Func<string, string> _nameOf;

        private Guid  _searchId;
        private Guid  _listingsId;
        private Guid  _mineId;
        private Guid  _historyId;
        private bool  _staleRefreshing;
        private bool  _staleMinePending;
        private bool  _staleHistoryPending;
        private float _staleRetryAt;
        private float _staleDeadline;

        // 테스트가 마을 연결 없이 마을 상태를 흉내 낸다
        internal static Func<bool> TestIsTown;

        private static bool IsTown()
        {
            return (TestIsTown != null) ? TestIsTown() : ClientRoles.IsTown();
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            // 창이 열린 채 이 바인더만 꺼졌다 켜지면 그 사이 응답이 확정됐을 수 있다. 다음 Update가 열림으로 다시 받아 완료 응답과 잠금 표시를 복구한다
            _wasOpen = false;

            // 꺼진 사이에는 응답을 받지 못했으므로 진행 중으로 남기지 않는다
            _searchId        = Guid.Empty;
            _listingsId      = Guid.Empty;
            _listingsDirty   = false;
            _searchDirty     = false;
            _listingsRetryAt = 0f;
            _searchRetryAt   = 0f;
            if (_nameOf == null)
            {
                _nameOf = ItemName;
            }

            TownMarketClient.ResultReceived     += OnResult;
            TownMarketClient.WriteStateChanged  += OnWriteStateChanged;
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
            TownMarketClient.ResultReceived     -= OnResult;
            TownMarketClient.WriteStateChanged  -= OnWriteStateChanged;
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

            TownMarketClient.Tick(Time.unscaledTime);
            if ((!isOpen) || (!IsTown()))
            {
                return;
            }

            TickReadRetries(Time.unscaledTime);
            if (!TownMarketClient.HasStaleWrite)
            {
                _staleRefreshing = false;
                return;
            }

            if (_staleRefreshing)
            {
                TickStale(Time.unscaledTime);
                return;
            }

            // 창이 열린 채 연결이 바뀐 경우. 읽기는 여기서 액션마다 한 번만 보낸다
            BeginStaleRefresh(false);
            RequestSnapshots();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnOpened()
        {
            if (!IsTown())
            {
                _view.SetStatus(string.Empty);
                return;
            }

            // 창이 닫힌 사이 응답이 와도 클라이언트가 상태를 들고 있으므로 여기서 이어 받는다
            TownMarketClient.Tick(Time.unscaledTime);
            RequestSnapshots();
            _view.SetStatus(string.Empty);
            _sellablesDirty      = true;
            _staleRefreshing     = false;
            _staleMinePending    = false;
            _staleHistoryPending = false;

            // 읽기는 이 열림에서 액션마다 한 번만 보낸다. 끝난 쓰기가 내 등록을 이미 다시 받았으면 다시 보내지 않고,
            // 내역은 내역 탭이거나 이전 연결 확인이 필요할 때만 받는다. 요청 ID를 새로 덮어쓰면 먼저 보낸 응답이 버려져 확인이 멈춘다
            bool mineSent = false;
            if (TownMarketClient.TryTakeCompletedWrite(out MarketResultMessage completed))
            {
                mineSent = OnWriteResult(in completed);
            }

            int  tab   = _view.Tab;
            bool stale = TownMarketClient.HasStaleWrite;
            if (!mineSent)
            {
                _mineId = TownMarketClient.RequestMine();
            }

            if ((stale) || (tab == TAB_HISTORY))
            {
                _historyPage = 0;
                _historyId   = TownMarketClient.RequestHistory(_historyPage);
            }

            if ((tab == TAB_BUY) || (tab == TAB_SELL))
            {
                OnTabChanged(tab);
            }

            if (stale)
            {
                BeginStaleRefresh(true);
            }

            OnWriteStateChanged();
        }

        // 이전 연결의 요청 결과는 알 수 없으므로 성공이나 실패로 단정하지 않고, 서버의 권위 있는 내 등록과 내역을 다시 받아 보여 준다.
        // 두 응답이 모두 정상으로 오면 표식을 내린다. 쓰기는 막지 않는다(새 연결에는 미확정 쓰기가 없다).
        // alreadySent가 참이면 호출한 쪽이 이 두 읽기를 이미 보냈다. 거절이나 시간 초과는 요청 ID를 비워 두고 TickStale이 간격을 두고 다시 받는다
        private void BeginStaleRefresh(bool alreadySent)
        {
            _staleRefreshing     = true;
            _staleMinePending    = true;
            _staleHistoryPending = true;
            _staleRetryAt        = 0f;
            _staleDeadline       = Time.unscaledTime + Constants.Network.API_TIMEOUT_SECONDS;
            if (!alreadySent)
            {
                _mineId    = Guid.Empty;
                _historyId = Guid.Empty;
                SendStaleReads();
            }

            _view.SetStatus(STALE_TEXT);
        }

        private bool StaleMineNeedsSend => (_staleMinePending) && (_mineId == Guid.Empty);

        private bool StaleHistoryNeedsSend => (_staleHistoryPending) && (_historyId == Guid.Empty);

        // 아직 응답을 기다리지 않는 읽기만 한 번씩 보낸다
        private void SendStaleReads()
        {
            if (StaleMineNeedsSend)
            {
                _mineId = TownMarketClient.RequestMine();
            }

            if (StaleHistoryNeedsSend)
            {
                _historyPage = 0;
                _historyId   = TownMarketClient.RequestHistory(_historyPage);
            }

            float now = Time.unscaledTime;
            _staleDeadline = now + Constants.Network.API_TIMEOUT_SECONDS;
            _staleRetryAt  = (StaleMineNeedsSend || StaleHistoryNeedsSend) ? now + STALE_RETRY_SECONDS : 0f;
        }

        private void TickStale(float now)
        {
            bool waiting = ((_staleMinePending) && (_mineId != Guid.Empty)) || ((_staleHistoryPending) && (_historyId != Guid.Empty));
            if ((waiting) && (now >= _staleDeadline))
            {
                // 응답이 오지 않는 요청은 버리고 다시 받는다. 버린 요청의 늦은 응답은 ID가 달라 무시된다
                if (_staleMinePending)
                {
                    _mineId = Guid.Empty;
                }

                if (_staleHistoryPending)
                {
                    _historyId = Guid.Empty;
                }

                ScheduleStaleRetry(now);
                return;
            }

            if (((StaleMineNeedsSend) || (StaleHistoryNeedsSend)) && (now >= _staleRetryAt))
            {
                SendStaleReads();
                _view.SetStatus(STALE_TEXT);
            }
        }

        private void ScheduleStaleRetry(float now)
        {
            _staleRetryAt = now + STALE_RETRY_SECONDS;
            _view.SetStatus(STALE_RETRY_TEXT);
        }

        private void TryResolveStale()
        {
            if ((!_staleRefreshing) || (_staleMinePending) || (_staleHistoryPending))
            {
                return;
            }

            _staleRefreshing = false;
            TownMarketClient.ResolveStaleWrite();
            _view.SetStatus(STALE_DONE_TEXT);
        }

        private void OnWriteStateChanged()
        {
            if (_view == null)
            {
                return;
            }

            EMarketWriteState state = TownMarketClient.WriteState;
            bool busy = (state == EMarketWriteState.Pending) || ((state == EMarketWriteState.Unknown) && (!TownMarketClient.NeedsWriteReconfirm));
            _view.SetWriteBusy(busy);
            if (state == EMarketWriteState.Pending)
            {
                _view.SetStatus(PENDING_TEXT);
                return;
            }

            if (state != EMarketWriteState.Unknown)
            {
                return;
            }

            if (!TownMarketClient.NeedsWriteReconfirm)
            {
                _view.SetStatus(PENDING_TEXT);
                return;
            }

            _view.SetStatus(RECONFIRM_TEXT);
            if (_view.IsOpen)
            {
                PromptReconfirm();
            }
        }

        // 같은 ID와 원문을 다시 보내는 것이라 서버와 API가 처음 결과를 돌려주며 효과는 한 번이다. 거절하면 결과 불명으로 남아 다시 고를 수 있다.
        // 사용자의 승인 없이는 다시 보내지 않는다. 확인 창을 열 수 없으면 보내지 않고 결과 불명임을 안내만 한다
        private void PromptReconfirm()
        {
            Guid id = TownMarketClient.PendingWriteId;
            if (id == Guid.Empty)
            {
                return;
            }

            if (_popupHub == null)
            {
                _view.SetStatus(RECONFIRM_NO_POPUP_TEXT);
                return;
            }

            _popupHub.ShowConfirm(EPopupSize.Medium, RECONFIRM_PROMPT, accepted =>
            {
                if (accepted)
                {
                    OnReconfirmApproved(id);
                }
            });
        }

        // 팝업이 열려 있는 사이 연결이 바뀌면 그 ID는 더 이상 확인할 수 없다. 보내지 않고 이전 요청을 직접 확인하도록 안내한다
        private void OnReconfirmApproved(Guid id)
        {
            if ((TownMarketClient.PendingWriteId == id) && (TownMarketClient.NeedsWriteReconfirm) && (TownMarketClient.RetryWrite(id)))
            {
                return;
            }

            if (_view != null)
            {
                _view.SetStatus(RECONFIRM_LOST_TEXT);
            }
        }

        // 미확정 쓰기가 있으면 새 쓰기를 보내지 않는다. 결과 불명이면 같은 ID 재확인을 다시 권한다
        private bool CanBeginWrite()
        {
            if (TownMarketClient.WriteState == EMarketWriteState.None)
            {
                return true;
            }

            if (TownMarketClient.NeedsWriteReconfirm)
            {
                _view.SetStatus(RECONFIRM_TEXT);
                PromptReconfirm();
                return false;
            }

            _view.SetStatus(PENDING_TEXT);
            return false;
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
                    // 이전 연결 확인용 읽기가 이미 나가 있으면 그 응답이 이 탭도 채운다. 같은 읽기를 겹쳐 보내면 서버가 PendingCommit으로 거절한다
                    if (!((_staleRefreshing) && (_staleMinePending)))
                    {
                        _mineId = TownMarketClient.RequestMine();
                    }

                    return;
                case TAB_HISTORY:
                    if (!((_staleRefreshing) && (_staleHistoryPending)))
                    {
                        _historyPage = 0;
                        _historyId   = TownMarketClient.RequestHistory(_historyPage);
                    }

                    return;
            }
        }

        private void OnSearch(AuctionSearchInput input)
        {
            _lastSearch = input;
            _hasSearch  = true;
            _searchPage = 0;
            RequestSearch();
        }

        // 서버는 연결당 검색 하나만 받는다. 진행 중에 조건이 바뀌면 겹쳐 보내지 않고(PendingCommit으로 거절되어 결과를 잃는다)
        // 응답이 오면 새 조건으로 이어 받는다
        private void RequestSearch()
        {
            _searchRetries = 0;
            _searchRetryAt = 0f;
            if (SearchInFlight)
            {
                _searchDirty = true;
                return;
            }

            SendSearch();
        }

        private void SendSearch()
        {
            _searchDirty = false;
            string[] ids = null;
            if (!string.IsNullOrWhiteSpace(_lastSearch.Text))
            {
                CollectCandidates();
                AuctionViewRules.FindDefinitionIds(_lastSearch.Text, _candidates, MarketLimits.MAX_SEARCH_DEFINITIONS, _foundIds,
                    _nameOf);
                if (_foundIds.Count == 0)
                {
                    _groups = Array.Empty<MarketGroupEntry>();
                    _wantedDefinitionId = null;
                    ShowGroups(0, 0);
                    HideDetail();
                    _view.SetStatus(NO_RESULT_TEXT);
                    return;
                }

                ids = _foundIds.ToArray();
            }

            _searchId     = TownMarketClient.Search(_lastSearch.Category, _lastSearch.ClassId, _lastSearch.Grade, ids, _lastSearch.Sort,
                _lastSearch.Order, _searchPage);
            _searchSentAt = Time.unscaledTime;
        }

        // 검색 응답을 기다리는 동안 보이는 묶음은 곧 바뀔 목록이라 누르지 못하게 한다. 응답이 오지 않으면 일정 시간 뒤 푼다
        private bool SearchInFlight
        {
            get { return (_searchId != Guid.Empty) && ((Time.unscaledTime - _searchSentAt) < ReadWaitSeconds); }
        }

        private bool ListingsInFlight
        {
            get { return (_listingsId != Guid.Empty) && ((Time.unscaledTime - _listingsSentAt) < ReadWaitSeconds); }
        }

        private static float ReadWaitSeconds => Constants.Network.API_TIMEOUT_SECONDS + READ_WAIT_MARGIN_SECONDS;

        private void OnPageDelta(int delta)
        {
            if (_view.Tab == TAB_HISTORY)
            {
                if ((_staleRefreshing) && (_staleHistoryPending))
                {
                    return;
                }

                _historyPage = Math.Max(0, _historyPage + delta);
                _historyId   = TownMarketClient.RequestHistory(_historyPage);
                return;
            }

            _searchPage = Math.Max(0, _searchPage + delta);
            RequestSearch();
        }

        private void OnGroupSelected(int index)
        {
            if ((index < 0) || (index >= _groups.Length) || (SearchInFlight))
            {
                return;
            }

            _groupSelected = index;
            string definitionId = _groups[index].DefinitionId;

            // 응답을 기다리지 않고 상세 칸을 먼저 연다. 다른 품목의 상세가 보이고 있었다면 비워 잘못 사지 않게 한다
            if (_shownDefinitionId != definitionId)
            {
                _shownDefinitionId = null;
                _listings          = Array.Empty<MarketListingEntry>();
                _listingSelected   = -1;
                _view.SetListings(ItemName(definitionId), null, 0);
            }

            RequestListings(definitionId, false);
        }

        // 서버는 연결당 상세 읽기 하나만 받는다. 진행 중이면 보내지 않고 고른 품목만 바꿔 두며, 응답을 처리한 뒤 이어서 받는다.
        // 같은 품목을 다시 누르면 진행 중인 요청이나 방금 받은 상세를 그대로 쓴다. force는 쓰기 뒤처럼 받은 상세가 낡았을 때다
        private void RequestListings(string definitionId, bool force)
        {
            _wantedDefinitionId = definitionId;
            _listingsRetries    = 0;
            _listingsRetryAt    = 0f;
            if (force)
            {
                _shownDefinitionId = null;
            }
            else if ((_shownDefinitionId == definitionId) && ((Time.unscaledTime - _shownAt) < LISTINGS_FRESH_SECONDS))
            {
                return;
            }

            if (ListingsInFlight)
            {
                if ((force) || (_listingsDefinitionId != definitionId))
                {
                    _listingsDirty = true;
                }

                return;
            }

            SendListings();

            // 쓰기 뒤 다시 받을 때는 처리 결과 안내를 덮지 않는다
            if ((!force) && (_listingsId != Guid.Empty))
            {
                SetReadStatus(LOADING_TEXT);
            }
        }

        // 일시 거절된 검색과 상세를 정한 시각에 한 번씩 다시 보낸다
        private void TickReadRetries(float now)
        {
            if ((_searchRetryAt > 0f) && (now >= _searchRetryAt))
            {
                _searchRetryAt = 0f;
                if ((_hasSearch) && (!SearchInFlight))
                {
                    SendSearch();
                }
            }

            if ((_listingsRetryAt > 0f) && (now >= _listingsRetryAt))
            {
                _listingsRetryAt = 0f;
                if ((_wantedDefinitionId != null) && (!ListingsInFlight) && (!SearchInFlight))
                {
                    SendListings();
                }
            }
        }

        // 서버 앞단의 일시 거절이다. 다시 보내면 받아들여질 수 있다
        private static bool IsTransientRead(ERejectReason reason)
        {
            return (reason == ERejectReason.RateLimited) || (reason == ERejectReason.PendingCommit)
                || (reason == ERejectReason.BackendUnavailable);
        }

        private void HideDetail()
        {
            _shownDefinitionId = null;
            _listings          = Array.Empty<MarketListingEntry>();
            _listingSelected   = -1;
            _view.SetListings(null, null, 0);
        }

        private void SetReadStatus(string text)
        {
            _readStatus = text;
            _view.SetStatus(text);
        }

        private void ClearReadStatus()
        {
            if ((_readStatus != null) && (_view.LastStatus == _readStatus))
            {
                _view.SetStatus(string.Empty);
            }

            _readStatus = null;
        }

        private string ItemName(string definitionId)
        {
            bool ready = GameDB.IsInitialized;
            return AuctionViewRules.ItemName(definitionId, ready ? GameDB.Equipment : null, ready ? GameDB.SkillDisplay : null);
        }

        // 상세 응답의 Mine은 API가 조회자를 몰라 늘 거짓이다. 받아 둔 내 등록으로도 확인한다
        private bool IsOwnListing(in MarketListingEntry listing)
        {
            if (listing.Mine)
            {
                return true;
            }

            for (int i = 0; i < _mine.Length; ++i)
            {
                if (string.Equals(_mine[i].ListingId, listing.ListingId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void SendListings()
        {
            _listingsDirty        = false;
            _listingsDefinitionId = _wantedDefinitionId;
            _listingsId           = TownMarketClient.QueryListings(_wantedDefinitionId, 0);
            _listingsSentAt       = Time.unscaledTime;
        }

        // 응답이 낡았거나(보낸 뒤 다른 품목을 골랐거나 쓰기가 있었다) 하면 고른 품목을 다시 받는다
        private void ContinueListings()
        {
            if ((_wantedDefinitionId != null) && ((_listingsDirty) || (_listingsDefinitionId != _wantedDefinitionId)))
            {
                SendListings();
            }
        }

        private void OnListingSelected(int index)
        {
            if ((index < 0) || (index >= _listings.Length))
            {
                return;
            }

            _listingSelected = index;
            MarketListingEntry listing = _listings[index];
            if (IsOwnListing(in listing))
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
            if (IsOwnListing(in listing))
            {
                _view.SetStatus(OWN_LISTING_TEXT);
                return;
            }

            if ((quantity < 1) || (quantity > listing.RemainingQty) || (!MarketFeeRules.TryGross(listing.UnitPrice, quantity, out long gross)))
            {
                return;
            }

            string text = AuctionViewRules.BuyConfirmText(ItemName(listing.DefinitionId), quantity, listing.UnitPrice, gross);
            if (!CanBeginWrite())
            {
                return;
            }

            Confirm(text, () => SendWrite(() => TownMarketClient.Buy(listing.ListingId, quantity, listing.UnitPrice)));
        }

        private void OnSellItemSelected(int index)
        {
            if ((index < 0) || (index >= _sellables.Count))
            {
                return;
            }

            _sellSelected = index;
            _view.SetSellTarget(ItemName(_sellables[index].DefinitionId), _sellables[index].MaxQuantity);
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
            if (!CanBeginWrite())
            {
                return;
            }

            string text = AuctionViewRules.ListConfirmText(ItemName(item.DefinitionId), quantity, unitPrice, net);
            Confirm(text, () => SendWrite(() => TownMarketClient.List(kind, item.DefinitionId, item.ItemId, quantity, unitPrice)));
        }

        private void OnCancelRequested(int mineIndex)
        {
            if ((mineIndex < 0) || (mineIndex >= _mine.Length))
            {
                return;
            }

            if (!CanBeginWrite())
            {
                return;
            }

            MarketListingEntry listing = _mine[mineIndex];
            string text = AuctionViewRules.CancelConfirmText(ItemName(listing.DefinitionId), listing.RemainingQty);
            Confirm(text, () => SendWrite(() => TownMarketClient.Cancel(listing.ListingId)));
        }

        private void OnAckRequested()
        {
            if (!CanBeginWrite())
            {
                return;
            }

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
                SendWrite(() => TownMarketClient.Acknowledge(_ackIds.ToArray()));
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

        // 팝업이 열려 있는 사이 상태가 바뀔 수 있어 보내기 직전에 다시 확인한다. 클라이언트도 미확정 쓰기가 있으면 보내지 않는다
        private void SendWrite(Func<Guid> send)
        {
            if (!CanBeginWrite())
            {
                return;
            }

            Guid id = send();
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
                        _searchId = Guid.Empty;
                        OnSearchResult(in result);
                    }

                    return;
                case EMarketAction.Listings:
                    if (result.RequestId == _listingsId)
                    {
                        _listingsId = Guid.Empty;
                        OnListingsResult(in result);
                        ContinueListings();
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
                    // 클라이언트가 이미 미확정 쓰기에 반영했다. 끝난 결과만 꺼내 보여 주고, 결과 불명은 OnWriteStateChanged가 보여 준다
                    if ((result.RequestId == TownMarketClient.CompletedWriteId) && (TownMarketClient.TryTakeCompletedWrite(out MarketResultMessage completed)))
                    {
                        OnWriteResult(in completed);
                    }

                    return;
            }
        }

        private void OnSearchResult(in MarketResultMessage result)
        {
            // 기다리는 사이 조건이 바뀌었으면 이 응답은 낡았다. 쓰지 않고 새 조건으로 받는다
            if (_searchDirty)
            {
                SendSearch();
                return;
            }

            if (!result.IsAccepted)
            {
                if ((IsTransientRead(result.Reason)) && (_searchRetries < MAX_READ_RETRIES))
                {
                    ++_searchRetries;
                    _searchRetryAt = Time.unscaledTime + READ_RETRY_SECONDS;
                    SetReadStatus(LOADING_TEXT);
                    return;
                }

                SetReadStatus(DescribeRead(result.Reason));
                return;
            }

            _groups             = result.Groups;
            _groupSelected      = -1;
            _wantedDefinitionId = null;
            _listingsRetryAt    = 0f;
            ShowGroups(result.TotalCount, result.Page);
            HideDetail();
            _readStatus = null;
            _view.SetStatus(string.Empty);
        }

        private void ShowGroups(int totalCount, int page)
        {
            _rows.Clear();
            for (int i = 0; i < _groups.Length; ++i)
            {
                MarketGroupEntry group = _groups[i];
                _rows.Add(new ListRowData(group.Grade, ItemName(group.DefinitionId), AuctionViewRules.FormatPrice(group.LowestUnitPrice),
                    AuctionViewRules.FormatPrice(group.TotalQty), group.ListingCount.ToString(CultureInfo.InvariantCulture),
                    (group.RecentUnitPrice > 0) ? AuctionViewRules.FormatPrice(group.RecentUnitPrice) : "-"));
            }

            _view.SetGroups(_rows, totalCount, page, AuctionViewRules.PageCount(totalCount, MarketLimits.PAGE_SIZE));
        }

        private void OnListingsResult(in MarketResultMessage result)
        {
            // 보낸 뒤 다른 묶음을 골랐거나 새 검색으로 목록이 바뀌었으면 이 응답은 화면에 쓰지 않는다
            if ((_wantedDefinitionId == null) || (_listingsDefinitionId != _wantedDefinitionId))
            {
                return;
            }

            if (!result.IsAccepted)
            {
                // 요청 한도 같은 일시 거절이면 상세 칸을 연 채 잠시 뒤 다시 받는다. 다른 품목으로 이어 받을 예정이면 그쪽이 대신한다
                if ((IsTransientRead(result.Reason)) && (_listingsRetries < MAX_READ_RETRIES) && (!_listingsDirty))
                {
                    ++_listingsRetries;
                    _listingsRetryAt = Time.unscaledTime + READ_RETRY_SECONDS;
                    SetReadStatus(LOADING_TEXT);
                    return;
                }

                SetReadStatus(DescribeRead(result.Reason));
                return;
            }

            _listings          = result.Listings;
            _listingSelected   = -1;
            _shownDefinitionId = _wantedDefinitionId;
            _shownAt           = Time.unscaledTime;
            ClearReadStatus();
            _rows.Clear();
            for (int i = 0; i < _listings.Length; ++i)
            {
                MarketListingEntry listing = _listings[i];
                string time = AuctionViewRules.FormatRemaining(listing.ExpiresAtUnixMs, _serverNowMs);
                _rows.Add(new ListRowData(listing.Grade, AuctionViewRules.FormatPrice(listing.UnitPrice),
                    listing.RemainingQty.ToString(CultureInfo.InvariantCulture), time));
            }

            _view.SetListings(ItemName(_wantedDefinitionId), _rows, result.AverageUnitPrice24h);
        }

        private void OnMineResult(in MarketResultMessage result)
        {
            if (!result.IsAccepted)
            {
                if ((_staleRefreshing) && (_staleMinePending))
                {
                    _mineId = Guid.Empty;
                    ScheduleStaleRetry(Time.unscaledTime);
                    return;
                }

                SetReadStatus(DescribeRead(result.Reason));
                return;
            }

            ClearReadStatus();
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
                _rows.Add(new ListRowData(listing.Grade, ItemName(listing.DefinitionId), AuctionViewRules.FormatPrice(listing.UnitPrice),
                    string.Format(CultureInfo.InvariantCulture, SLASH_FORMAT, listing.RemainingQty, listing.ListedQty),
                    AuctionViewRules.ListingStateText(listing.State), detail));
                _flags.Add(active);
            }

            _view.SetMine(_rows, _flags, result.UnseenCount);
            _staleMinePending = false;
            TryResolveStale();
        }

        private void OnHistoryResult(in MarketResultMessage result)
        {
            if (!result.IsAccepted)
            {
                if ((_staleRefreshing) && (_staleHistoryPending))
                {
                    _historyId = Guid.Empty;
                    ScheduleStaleRetry(Time.unscaledTime);
                    return;
                }

                SetReadStatus(DescribeRead(result.Reason));
                return;
            }

            ClearReadStatus();
            _rows.Clear();
            MarketTradeEntry[] trades = result.Trades;
            for (int i = 0; i < trades.Length; ++i)
            {
                MarketTradeEntry trade = trades[i];
                bool seller = trade.Role == MarketCodes.TradeRole.SELLER;
                _rows.Add(new ListRowData(0, AuctionViewRules.RoleText(trade.Role), ItemName(trade.DefinitionId),
                    trade.Quantity.ToString(CultureInfo.InvariantCulture), AuctionViewRules.FormatPrice(trade.Gross),
                    seller ? AuctionViewRules.FormatPrice(trade.Fee) : "-", seller ? AuctionViewRules.FormatPrice(trade.Net) : "-"));
            }

            _view.SetHistory(_rows, _historyPage, result.HasMore);
            _staleHistoryPending = false;
            TryResolveStale();
        }

        // 내 등록을 다시 요청했으면 참을 돌려준다
        private bool OnWriteResult(in MarketResultMessage result)
        {
            if (result.IsAccepted)
            {
                _view.SetStatus(DONE_TEXT);
                return RefreshAfterWrite();
            }

            _view.SetStatus(DescribeWrite(result.Reason));
            if ((result.Reason == ERejectReason.MarketPriceChanged) || (result.Reason == ERejectReason.MarketSoldOut)
                || (result.Reason == ERejectReason.MarketQuantityChanged) || (result.Reason == ERejectReason.MarketExpired))
            {
                return RefreshAfterWrite();
            }

            return false;
        }

        private bool RefreshAfterWrite()
        {
            bool mineSent = false;
            if (!((_staleRefreshing) && (_staleMinePending)))
            {
                _mineId  = TownMarketClient.RequestMine();
                mineSent = _mineId != Guid.Empty;
            }

            RequestSnapshots();
            _sellablesDirty = true;
            if ((_view.Tab == TAB_BUY) && (_wantedDefinitionId != null))
            {
                RequestListings(_wantedDefinitionId, true);
            }

            return mineSent;
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
                _rows.Add(new ListRowData(item.Grade, ItemName(item.DefinitionId), item.MaxQuantity.ToString(CultureInfo.InvariantCulture)));
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
