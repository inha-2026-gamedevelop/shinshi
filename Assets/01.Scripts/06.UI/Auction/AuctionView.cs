// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.UI
{
    /// <summary> 유저 경매장 창이며 구매, 판매 등록, 내 등록, 기록 네 탭을 표시한다. 네트워크는 모르고 이벤트로 사용자의 의도만 알린다 </summary>
    public sealed class AuctionView : MonoBehaviour
    {
        // 목록은 줄 배열(ListRowView)을 미리 만들어 두고 내용만 바꾼다. 줄 안의 칸 순서는 아래에 적은 대로 씬에서 연결한다.
        //   구매 묶음 줄: 품목, 최저가, 수량, 등록 수, 최근가 / 등록 상세 줄: 개당 가격, 남은 수량, 남은 시간
        //   판매 가능 줄: 품목, 보유 수량 / 내 등록 줄: 품목, 개당 가격, 남은 수량, 상태, 남은 시간 / 기록 줄: 구분, 품목, 수량, 총액, 수수료, 받은 금액

        /****************************************
        *                Fields
        ****************************************/

        private const string EMPTY_GROUPS_TEXT   = "등록된 물품이 없습니다";
        private const string EMPTY_MINE_TEXT     = "등록한 물품이 없습니다";
        private const string EMPTY_HISTORY_TEXT  = "거래 기록이 없습니다";
        private const string PAGE_FORMAT         = "{0} / {1}";
        private const string COUNT_FORMAT        = "검색 결과 {0}건";
        private const string AVERAGE_FORMAT      = "최근 24시간 평균 {0}";

        [Header("창")]
        [SerializeField] private Button _closeButton  = null;
        [SerializeField] private Toggle[] _tabs       = null;
        [SerializeField] private GameObject[] _pages  = null;
        [SerializeField] private TMP_Text _yeopjeonLabel = null;
        [SerializeField] private TMP_Text _statusLabel   = null;

        [Header("구매 탭 검색")]
        [SerializeField] private TMP_InputField _searchField = null;
        [SerializeField] private Button         _searchButton = null;
        [SerializeField] private Toggle[]       _categoryToggles = null;
        [SerializeField] private TMP_Dropdown   _classDropdown = null;
        [SerializeField] private TMP_Dropdown   _gradeDropdown = null;
        [SerializeField] private TMP_Dropdown   _sortDropdown  = null;
        [SerializeField] private Button         _orderButton   = null;
        [SerializeField] private GameObject     _ascendingMark = null;
        [SerializeField] private GameObject     _descendingMark = null;

        [Header("구매 탭 목록")]
        [SerializeField] private ListRowView[] _groupRows = null;
        [SerializeField] private TMP_Text      _resultCountLabel = null;
        [SerializeField] private GameObject    _groupsEmptyMark = null;
        [SerializeField] private Button        _prevPageButton = null;
        [SerializeField] private Button        _nextPageButton = null;
        [SerializeField] private TMP_Text      _pageLabel = null;

        [Header("구매 탭 상세")]
        [SerializeField] private GameObject     _detailPanel = null;
        [SerializeField] private TMP_Text       _detailTitleLabel = null;
        [SerializeField] private TMP_Text       _averagePriceLabel = null;
        [SerializeField] private ListRowView[]  _listingRows = null;
        [SerializeField] private TMP_InputField _buyQuantityField = null;
        [SerializeField] private Button         _buyQuantityMinus = null;
        [SerializeField] private Button         _buyQuantityPlus = null;
        [SerializeField] private TMP_Text       _buyTotalLabel = null;
        [SerializeField] private Button         _buyButton = null;

        [Header("판매 탭")]
        [SerializeField] private ListRowView[]  _sellableRows = null;
        [SerializeField] private TMP_Text       _sellItemLabel = null;
        [SerializeField] private TMP_InputField _sellQuantityField = null;
        [SerializeField] private Button         _sellQuantityMinus = null;
        [SerializeField] private Button         _sellQuantityPlus = null;
        [SerializeField] private Button         _sellQuantityMax = null;
        [SerializeField] private TMP_InputField _sellPriceField = null;
        [SerializeField] private TMP_Text       _grossLabel = null;
        [SerializeField] private TMP_Text       _feeLabel = null;
        [SerializeField] private TMP_Text       _proceedsLabel = null;
        [SerializeField] private TMP_Text       _formErrorLabel = null;
        [SerializeField] private Button         _registerButton = null;

        [Header("내 등록 탭")]
        [SerializeField] private ListRowView[] _mineRows = null;
        [SerializeField] private Button[]      _cancelButtons = null;
        [SerializeField] private Button        _ackButton = null;
        [SerializeField] private GameObject    _mineEmptyMark = null;

        [Header("기록 탭")]
        [SerializeField] private ListRowView[] _historyRows = null;
        [SerializeField] private GameObject    _historyEmptyMark = null;
        [SerializeField] private Button        _historyPrevButton = null;
        [SerializeField] private Button        _historyNextButton = null;
        [SerializeField] private TMP_Text      _historyPageLabel = null;

        private readonly List<ListRowData> _scratch = new List<ListRowData>();

        private bool  _ascending      = true;
        private int   _tab            = 0;
        private int   _page           = 0;
        private int   _groupSelected  = -1;
        private int   _listingSelected = -1;
        private int   _sellSelected   = -1;
        private int   _buyMax         = 0;
        private long  _buyUnitPrice   = 0;
        private int   _sellMax        = 0;
        private int   _unseenCount    = 0;
        private bool  _writeBusy      = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => gameObject.activeSelf;
        public int  Tab    => _tab;
        public int  Page   => _page;

        // 테스트가 마지막 안내 문구와 쓰기 잠금을 확인한다
        internal string LastStatus { get; private set; }
        internal bool   WriteBusy  => _writeBusy;

        public event Action<int>              TabChanged;
        public event Action<AuctionSearchInput> SearchRequested;
        public event Action<int>              PageDeltaRequested;
        public event Action<int>              GroupSelected;
        public event Action<int>              ListingSelected;
        public event Action<int>              BuyRequested;
        public event Action<int>              SellItemSelected;
        public event Action<int, long>        RegisterRequested;
        public event Action<int>              CancelRequested;
        public event Action                   AckRequested;
        public event Action                   Closed;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            Bind(_closeButton, OnCloseClicked);
            Bind(_searchButton, OnSearchClicked);
            Bind(_orderButton, OnOrderClicked);
            Bind(_prevPageButton, OnPrevPage);
            Bind(_nextPageButton, OnNextPage);
            Bind(_historyPrevButton, OnPrevPage);
            Bind(_historyNextButton, OnNextPage);
            Bind(_buyButton, OnBuyClicked);
            Bind(_buyQuantityMinus, OnBuyMinus);
            Bind(_buyQuantityPlus, OnBuyPlus);
            Bind(_sellQuantityMinus, OnSellMinus);
            Bind(_sellQuantityPlus, OnSellPlus);
            Bind(_sellQuantityMax, OnSellMax);
            Bind(_registerButton, OnRegisterClicked);
            Bind(_ackButton, OnAckClicked);

            if (_searchField != null)
            {
                _searchField.onSubmit.AddListener(OnSearchSubmit);
            }

            if (_buyQuantityField != null)
            {
                _buyQuantityField.onValueChanged.AddListener(OnBuyQuantityText);
            }

            if (_sellQuantityField != null)
            {
                _sellQuantityField.onValueChanged.AddListener(OnSellInputText);
            }

            if (_sellPriceField != null)
            {
                _sellPriceField.onValueChanged.AddListener(OnSellInputText);
            }

            BindTabs();
            BindRows(_groupRows, OnGroupRowClicked);
            BindRows(_listingRows, OnListingRowClicked);
            BindRows(_sellableRows, OnSellableRowClicked);
            BindCancelButtons();
            ApplyOrderMark();
            ShowTab(0);
        }

        private void OnDestroy()
        {
            Unbind(_closeButton, OnCloseClicked);
            Unbind(_searchButton, OnSearchClicked);
            Unbind(_orderButton, OnOrderClicked);
            Unbind(_prevPageButton, OnPrevPage);
            Unbind(_nextPageButton, OnNextPage);
            Unbind(_historyPrevButton, OnPrevPage);
            Unbind(_historyNextButton, OnNextPage);
            Unbind(_buyButton, OnBuyClicked);
            Unbind(_buyQuantityMinus, OnBuyMinus);
            Unbind(_buyQuantityPlus, OnBuyPlus);
            Unbind(_sellQuantityMinus, OnSellMinus);
            Unbind(_sellQuantityPlus, OnSellPlus);
            Unbind(_sellQuantityMax, OnSellMax);
            Unbind(_registerButton, OnRegisterClicked);
            Unbind(_ackButton, OnAckClicked);

            if (_searchField != null)
            {
                _searchField.onSubmit.RemoveListener(OnSearchSubmit);
            }

            if (_buyQuantityField != null)
            {
                _buyQuantityField.onValueChanged.RemoveListener(OnBuyQuantityText);
            }

            if (_sellQuantityField != null)
            {
                _sellQuantityField.onValueChanged.RemoveListener(OnSellInputText);
            }

            if (_sellPriceField != null)
            {
                _sellPriceField.onValueChanged.RemoveListener(OnSellInputText);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetOpen(bool open)
        {
            gameObject.SetActive(open);
        }

        public void Close()
        {
            SetOpen(false);
            Closed?.Invoke();
        }

        /// <summary> 탭을 바꾼다. 알림은 사용자가 누를 때만 나간다 </summary>
        public void ShowTab(int tab)
        {
            _tab = tab;
            if (_pages != null)
            {
                for (int i = 0; i < _pages.Length; ++i)
                {
                    if (_pages[i] != null)
                    {
                        _pages[i].SetActive(i == tab);
                    }
                }
            }

            if ((_tabs != null) && (tab >= 0) && (tab < _tabs.Length) && (_tabs[tab] != null))
            {
                _tabs[tab].SetIsOnWithoutNotify(true);
            }
        }

        public void SetYeopjeon(long value)
        {
            SetText(_yeopjeonLabel, AuctionViewRules.FormatPrice(value));
        }

        public void SetStatus(string text)
        {
            LastStatus = text;
            SetText(_statusLabel, text);
        }

        /// <summary> 검색 묶음 목록. pageIndex는 0부터 </summary>
        public void SetGroups(IReadOnlyList<ListRowData> rows, int totalCount, int pageIndex, int pageCount)
        {
            _page          = pageIndex;
            _groupSelected = -1;
            ListRowData.Fill(_groupRows, rows, _groupSelected);
            SetText(_resultCountLabel, string.Format(COUNT_FORMAT, totalCount));
            SetText(_pageLabel, string.Format(PAGE_FORMAT, pageIndex + 1, pageCount));
            SetActive(_groupsEmptyMark, (rows == null) || (rows.Count == 0));
            if ((_groupsEmptyMark != null) && (_groupsEmptyMark.TryGetComponent(out TMP_Text empty)))
            {
                empty.text = EMPTY_GROUPS_TEXT;
            }

            SetInteractable(_prevPageButton, pageIndex > 0);
            SetInteractable(_nextPageButton, (pageIndex + 1) < pageCount);
        }

        /// <summary> 고른 묶음의 등록 상세. 비우면 상세를 닫는다 </summary>
        public void SetListings(string title, IReadOnlyList<ListRowData> rows, long averageUnitPrice24h)
        {
            bool has = (rows != null) && (rows.Count > 0);
            SetActive(_detailPanel, title != null);
            SetText(_detailTitleLabel, title);
            SetText(_averagePriceLabel, (averageUnitPrice24h > 0) ? string.Format(AVERAGE_FORMAT, AuctionViewRules.FormatPrice(averageUnitPrice24h)) : string.Empty);
            _listingSelected = -1;
            ListRowData.Fill(_listingRows, rows, _listingSelected);
            SetBuyTarget(0, 0);
            SetInteractable(_buyButton, false);
            if (!has)
            {
                SetBuyTarget(0, 0);
            }
        }

        /// <summary> 고른 등록 한 줄의 개당 가격과 살 수 있는 최대 수량 </summary>
        public void SetBuyTarget(long unitPrice, int maxQuantity)
        {
            _buyUnitPrice = unitPrice;
            _buyMax       = maxQuantity;
            if (_buyQuantityField != null)
            {
                _buyQuantityField.SetTextWithoutNotify((maxQuantity > 0) ? "1" : "0");
            }

            RefreshBuyTotal();
        }

        public int BuyQuantity
        {
            get
            {
                long value = 0;
                if (_buyQuantityField != null)
                {
                    AuctionViewRules.TryParseNumber(_buyQuantityField.text, out value);
                }

                return (int)Math.Min(value, int.MaxValue);
            }
        }

        public void SetSellables(IReadOnlyList<ListRowData> rows)
        {
            _sellSelected = -1;
            ListRowData.Fill(_sellableRows, rows, _sellSelected);
            SetSellTarget(null, 0);
        }

        /// <summary> 판매 탭에서 고른 품목, 이름이 null이면 선택 없음 </summary>
        public void SetSellTarget(string itemName, int maxQuantity)
        {
            _sellMax = maxQuantity;
            SetText(_sellItemLabel, itemName);
            if (_sellQuantityField != null)
            {
                _sellQuantityField.SetTextWithoutNotify((maxQuantity > 0) ? "1" : string.Empty);
            }

            RefreshRegisterForm();
        }

        /// <summary> 쓰기 요청이 진행 중이면 구매, 등록, 취소, 수령 버튼을 모두 잠근다. 풀리면 각 입력 조건대로 되돌린다 </summary>
        public void SetWriteBusy(bool busy)
        {
            _writeBusy = busy;
            RefreshBuyTotal();
            RefreshRegisterForm();
            RefreshMineButtons();
        }

        public void SetMine(IReadOnlyList<ListRowData> rows, IReadOnlyList<bool> cancellable, int unseenCount)
        {
            ListRowData.Fill(_mineRows, rows, -1);
            _unseenCount = unseenCount;
            if (_cancelButtons != null)
            {
                for (int i = 0; i < _cancelButtons.Length; ++i)
                {
                    bool canCancel = (cancellable != null) && (i < cancellable.Count) && (cancellable[i]);
                    SetActive(_cancelButtons[i] != null ? _cancelButtons[i].gameObject : null, (rows != null) && (i < rows.Count) && (canCancel));
                }
            }

            SetActive(_mineEmptyMark, (rows == null) || (rows.Count == 0));
            if ((_mineEmptyMark != null) && (_mineEmptyMark.TryGetComponent(out TMP_Text empty)))
            {
                empty.text = EMPTY_MINE_TEXT;
            }

            RefreshMineButtons();
        }

        private void RefreshMineButtons()
        {
            SetInteractable(_ackButton, (_unseenCount > 0) && (!_writeBusy));
            if (_cancelButtons == null)
            {
                return;
            }

            for (int i = 0; i < _cancelButtons.Length; ++i)
            {
                SetInteractable(_cancelButtons[i], !_writeBusy);
            }
        }

        /// <summary> 기록 목록. pageIndex는 0부터이고 hasMore가 거짓이면 다음 쪽 버튼을 끈다 </summary>
        public void SetHistory(IReadOnlyList<ListRowData> rows, int pageIndex, bool hasMore)
        {
            SetText(_historyPageLabel, string.Format(PAGE_FORMAT, pageIndex + 1, hasMore ? pageIndex + 2 : pageIndex + 1));
            SetInteractable(_historyPrevButton, pageIndex > 0);
            SetInteractable(_historyNextButton, hasMore);
            ListRowData.Fill(_historyRows, rows, -1);
            SetActive(_historyEmptyMark, (rows == null) || (rows.Count == 0));
            if ((_historyEmptyMark != null) && (_historyEmptyMark.TryGetComponent(out TMP_Text empty)))
            {
                empty.text = EMPTY_HISTORY_TEXT;
            }
        }

        // 현재 입력으로 총액을 다시 계산한다
        private void RefreshBuyTotal()
        {
            int  quantity = BuyQuantity;
            bool valid    = (_buyMax > 0) && (quantity >= 1) && (quantity <= _buyMax)
                && (AuctionViewRules.TryPreview(_buyUnitPrice, quantity, out long gross, out _, out _));
            SetText(_buyTotalLabel, valid ? AuctionViewRules.FormatPrice(_buyUnitPrice * quantity) : string.Empty);
            SetInteractable(_buyButton, (valid) && (!_writeBusy));
        }

        private void RefreshRegisterForm()
        {
            long price = 0;
            if (_sellPriceField != null)
            {
                AuctionViewRules.TryParseNumber(_sellPriceField.text, out price);
            }

            long quantityValue = 0;
            if (_sellQuantityField != null)
            {
                AuctionViewRules.TryParseNumber(_sellQuantityField.text, out quantityValue);
            }

            int quantity = (int)Math.Min(quantityValue, int.MaxValue);
            EAuctionFormError error = AuctionViewRules.ValidateRegister(_sellMax > 0, price, quantity, _sellMax);
            bool hasPreview = AuctionViewRules.TryPreview(price, quantity, out long gross, out long fee, out long net);
            bool show       = (error == EAuctionFormError.None) && (hasPreview);
            SetText(_grossLabel, show ? AuctionViewRules.FormatPrice(gross) : string.Empty);
            SetText(_feeLabel, show ? AuctionViewRules.FormatPrice(fee) : string.Empty);
            SetText(_proceedsLabel, show ? AuctionViewRules.FormatPrice(net) : string.Empty);
            SetText(_formErrorLabel, ((_sellMax > 0) && (error != EAuctionFormError.None)) ? AuctionViewRules.FormErrorText(error) : string.Empty);
            SetInteractable(_registerButton, (show) && (!_writeBusy));
        }

        private AuctionSearchInput ReadSearch()
        {
            int category = AuctionSearchInput.NONE;
            if (_categoryToggles != null)
            {
                for (int i = 1; i < _categoryToggles.Length; ++i)
                {
                    if ((_categoryToggles[i] != null) && (_categoryToggles[i].isOn))
                    {
                        category = i;
                        break;
                    }
                }
            }

            int classId = (_classDropdown != null && _classDropdown.value > 0) ? _classDropdown.value - 1 : AuctionSearchInput.NONE;
            int grade   = (_gradeDropdown != null && _gradeDropdown.value > 0) ? _gradeDropdown.value - 1 : AuctionSearchInput.NONE;
            int sort    = (_sortDropdown != null) ? _sortDropdown.value : MarketCodes.Sort.UNIT_PRICE;
            int order   = _ascending ? MarketCodes.Order.ASCENDING : MarketCodes.Order.DESCENDING;
            return new AuctionSearchInput((_searchField != null) ? _searchField.text : string.Empty, category, classId, grade, sort, order);
        }

        private void ApplyOrderMark()
        {
            SetActive(_ascendingMark, _ascending);
            SetActive(_descendingMark, !_ascending);
        }

        private void BindTabs()
        {
            if (_tabs == null)
            {
                return;
            }

            for (int i = 0; i < _tabs.Length; ++i)
            {
                int index = i;
                if (_tabs[i] != null)
                {
                    _tabs[i].onValueChanged.AddListener(isOn => OnTabToggled(index, isOn));
                }
            }
        }

        private void BindCancelButtons()
        {
            if (_cancelButtons == null)
            {
                return;
            }

            for (int i = 0; i < _cancelButtons.Length; ++i)
            {
                int index = i;
                if (_cancelButtons[i] != null)
                {
                    _cancelButtons[i].onClick.AddListener(() => CancelRequested?.Invoke(index));
                }
            }
        }

        private static void BindRows(ListRowView[] rows, Action<ListRowView> handler)
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Length; ++i)
            {
                if (rows[i] != null)
                {
                    rows[i].Index = i;
                    rows[i].Clicked += handler;
                }
            }
        }

        private void OnTabToggled(int index, bool isOn)
        {
            if (!isOn)
            {
                return;
            }

            ShowTab(index);
            TabChanged?.Invoke(index);
        }

        private void OnCloseClicked()                  { Close(); }
        private void OnSearchClicked()                 { SearchRequested?.Invoke(ReadSearch()); }
        private void OnSearchSubmit(string text)       { SearchRequested?.Invoke(ReadSearch()); }
        private void OnPrevPage()                      { PageDeltaRequested?.Invoke(-1); }
        private void OnNextPage()                      { PageDeltaRequested?.Invoke(1); }
        private void OnBuyClicked()                    { BuyRequested?.Invoke(BuyQuantity); }
        private void OnAckClicked()                    { AckRequested?.Invoke(); }
        private void OnBuyQuantityText(string text)    { RefreshBuyTotal(); }
        private void OnSellInputText(string text)      { RefreshRegisterForm(); }

        private void OnOrderClicked()
        {
            _ascending = !_ascending;
            ApplyOrderMark();
            SearchRequested?.Invoke(ReadSearch());
        }

        private void OnBuyMinus()  { StepField(_buyQuantityField, -1, _buyMax); }
        private void OnBuyPlus()   { StepField(_buyQuantityField, 1, _buyMax); }
        private void OnSellMinus() { StepField(_sellQuantityField, -1, _sellMax); }
        private void OnSellPlus()  { StepField(_sellQuantityField, 1, _sellMax); }

        private void OnSellMax()
        {
            if (_sellQuantityField != null)
            {
                _sellQuantityField.text = _sellMax.ToString();
            }
        }

        private static void StepField(TMP_InputField field, int delta, int max)
        {
            if (field == null)
            {
                return;
            }

            AuctionViewRules.TryParseNumber(field.text, out long value);
            field.text = AuctionViewRules.ClampQuantity((int)Math.Min(value, int.MaxValue) + delta, max).ToString();
        }

        private void OnGroupRowClicked(ListRowView row)
        {
            _groupSelected = row.Index;
            GroupSelected?.Invoke(row.Index);
        }

        private void OnListingRowClicked(ListRowView row)
        {
            _listingSelected = row.Index;
            ListingSelected?.Invoke(row.Index);
        }

        private void OnSellableRowClicked(ListRowView row)
        {
            _sellSelected = row.Index;
            SellItemSelected?.Invoke(row.Index);
        }

        private void OnRegisterClicked()
        {
            long price = 0;
            if (_sellPriceField != null)
            {
                AuctionViewRules.TryParseNumber(_sellPriceField.text, out price);
            }

            long quantity = 0;
            if (_sellQuantityField != null)
            {
                AuctionViewRules.TryParseNumber(_sellQuantityField.text, out quantity);
            }

            RegisterRequested?.Invoke((int)Math.Min(quantity, int.MaxValue), price);
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }
}
