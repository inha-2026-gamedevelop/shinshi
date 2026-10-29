// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>인벤토리 창이며 분류 탭, 정렬, 칸 사용량, 재화를 표시한다. 칸은 모든 아이템이 함께 쓴다</summary>
    public sealed class InventoryView : UiWindowView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string USED_FORMAT     = "{0}";
        private const string TOTAL_FORMAT    = "/ {0}";
        private const string CURRENCY_FORMAT = "{0:N0}";
        private const float  WARNING_RATIO   = 0.9f;
        private const float  FULL_RATIO      = 1f;
        private const int    NO_SELECTION    = -1;
        private const int    HIDE_UNKNOWN    = 0;
        private const int    HIDE_YES        = 1;
        private const int    HIDE_NO         = 2;

        // EInventorySort 값 순서와 같아야 한다
        private static readonly string[] G_SortLabels = { "등급순", "최근 획득순", "종류순", "이름순" };

        [Header("참조")]
        [SerializeField] private ItemSlotGridView _grid           = null;
        [SerializeField] private Toggle[]         _tabs           = null;
        [SerializeField] private TMP_Dropdown     _sortDropdown   = null;
        [SerializeField] private Button           _orderButton    = null;
        [SerializeField] private GameObject       _ascendingMark  = null;
        [SerializeField] private GameObject       _descendingMark = null;
        [SerializeField] private Button           _arrangeButton  = null;
        [SerializeField] private Button           _closeButton    = null;

        [Header("탭")]
        [SerializeField] private TMP_Text[] _tabLabels   = null;
        [SerializeField] private Color      _tabOnColor  = Color.white;
        [SerializeField] private Color      _tabOffColor = Color.gray;

        [Header("칸 사용량")]
        [SerializeField] private TMP_Text _capacityLabel      = null;
        [SerializeField] private TMP_Text _capacityTotalLabel = null;
        [SerializeField] private Image    _capacityFill       = null;
        [SerializeField] private Color    _normalColor        = Color.white;
        [SerializeField] private Color    _fillColor          = Color.white;
        [SerializeField] private Color    _warningColor       = Color.yellow;
        [SerializeField] private Color    _fullColor          = Color.red;

        [Header("재화")]
        [SerializeField] private TMP_Text _yeopjeonLabel = null;
        [SerializeField] private TMP_Text _essenceLabel  = null;

        // 칸 순서 그대로 담고 빈 칸도 포함한다
        private readonly List<InventoryItemData> _items         = new List<InventoryItemData>();
        private readonly List<InventoryItemData> _sortBuffer    = new List<InventoryItemData>();
        private readonly List<ItemSlotData>      _visible       = new List<ItemSlotData>();
        private readonly List<int>               _visibleToItem = new List<int>();

        [SerializeField] private bool _enableSlotInteraction = false;

        // 장면에 이 필드가 저장돼 있지 않아도 기본값으로 켜진다. 거래 창 옆에 붙는 사본은 건드리지 않는다
        [SerializeField] private bool _modernLayout = true;

        private InventoryModernLayout         _layout;
        private Comparison<InventoryItemData> _compare;
        private EInventoryCategory            _category      = EInventoryCategory.All;
        private EInventorySort                _sort          = EInventorySort.Grade;
        private bool                          _ascending     = false;
        private int                           _capacity      = 0;
        private int                           _selectedIndex = NO_SELECTION;
        private int                           _hideEquipped  = HIDE_UNKNOWN;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => gameObject.activeSelf;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _compare   = Compare;
            _ascending = IsAscendingByDefault(_sort);

            EnsureLayout();
            BindTabs();

            if (_sortDropdown != null)
            {
                _sortDropdown.ClearOptions();
                _sortDropdown.AddOptions(new List<string>(G_SortLabels));
                _sortDropdown.SetValueWithoutNotify((int)_sort);
                _sortDropdown.onValueChanged.AddListener(OnSortChanged);
            }

            if (_orderButton != null)
            {
                _orderButton.onClick.AddListener(OnOrderClicked);
            }

            if (_arrangeButton != null)
            {
                _arrangeButton.onClick.AddListener(Arrange);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }

            if (_grid != null)
            {
                _grid.SlotClicked += OnSlotClicked;
            }

            ApplyOrderMark();
            ApplyTabLabels();
        }

        private void OnDestroy()
        {
            if (_tabs != null)
            {
                for (int i = 0; i < _tabs.Length; ++i)
                {
                    if (_tabs[i] != null)
                    {
                        _tabs[i].onValueChanged.RemoveAllListeners();
                    }
                }
            }

            if (_sortDropdown != null)
            {
                _sortDropdown.onValueChanged.RemoveListener(OnSortChanged);
            }

            if (_orderButton != null)
            {
                _orderButton.onClick.RemoveListener(OnOrderClicked);
            }

            if (_arrangeButton != null)
            {
                _arrangeButton.onClick.RemoveListener(Arrange);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Close);
            }

            if (_grid != null)
            {
                _grid.SlotClicked -= OnSlotClicked;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>칸 순서대로 받은 목록을 표시한다. 칸 수보다 목록이 길면 목록 길이만큼 표시한다</summary>
        public void SetItems(IReadOnlyList<InventoryItemData> items, int capacity)
        {
            _items.Clear();
            int sourceCount = (items != null) ? items.Count : 0;
            bool hideEquipped = HidesEquipped();

            // 착용 중인 장비는 장비 창에서 보므로 가방 목록에서 뺀다. 원본 데이터는 바꾸지 않고, 착용을 풀어 다시 받으면 나타난다
            for (int i = 0; i < sourceCount; ++i)
            {
                if (hideEquipped && !items[i].IsEmpty && items[i].Slot.IsEquipped)
                {
                    continue;
                }

                _items.Add(items[i]);
            }

            int itemCount = _items.Count;
            _capacity = Mathf.Max(capacity, itemCount);

            while (_items.Count < _capacity)
            {
                _items.Add(default(InventoryItemData));
            }

            _selectedIndex = NO_SELECTION;
            Refresh();
        }

        public void SetCurrency(long yeopjeon, long essence)
        {
            SetText(_yeopjeonLabel, string.Format(CURRENCY_FORMAT, yeopjeon));
            SetText(_essenceLabel, string.Format(CURRENCY_FORMAT, essence));
        }

        /// <summary>넓은 어두운 배치와 상세 패널을 한 번만 적용한다. Awake가 부르며, 실행 전에 화면을 만들 때도 쓴다</summary>
        public void EnsureLayout()
        {
            if ((_layout != null) || (!_modernLayout) || (_grid == null))
            {
                return;
            }

            // 거래 창 옆에 붙는 사본은 거래 창과 겹치지 않도록 옛 크기를 쓴다
            if (transform.root.GetComponentInChildren<TradeView>(true) != null)
            {
                return;
            }

            TMP_FontAsset regular = ((_tabLabels != null) && (_tabLabels.Length > 0) && (_tabLabels[0] != null)) ? _tabLabels[0].font : null;
            TMP_FontAsset bold    = (_capacityLabel != null) ? _capacityLabel.font : null;
            _layout = new InventoryModernLayout(regular, bold);
            _layout.Apply((RectTransform)transform, _tabs, _grid, _sortDropdown, _orderButton, _arrangeButton, _capacityLabel);
            RefreshDetail(NO_SELECTION);
        }

        /// <summary>칸 번호(빈 칸 포함)의 아이템을 선택하고 상세를 갱신한다</summary>
        public void SelectItem(int itemIndex)
        {
            _selectedIndex = ((itemIndex >= 0) && (itemIndex < _items.Count)) ? itemIndex : NO_SELECTION;
            Refresh();
        }

        public void SetOpen(bool open) { gameObject.SetActive(open); }

        public void Close() { SetOpen(false); }

        /// <summary>잠긴 아이템은 제자리에 두고 나머지를 현재 기준으로 앞 칸부터 채운다</summary>
        public void Arrange()
        {
            _sortBuffer.Clear();

            for (int i = 0; i < _items.Count; ++i)
            {
                if (!_items[i].IsEmpty && !_items[i].Slot.IsLocked)
                {
                    _sortBuffer.Add(_items[i]);
                }
            }

            _sortBuffer.Sort(_compare);

            int next = 0;

            for (int i = 0; i < _items.Count; ++i)
            {
                if (!_items[i].IsEmpty && _items[i].Slot.IsLocked)
                {
                    continue;
                }

                _items[i] = (next < _sortBuffer.Count) ? _sortBuffer[next] : default(InventoryItemData);
                ++next;
            }

            _selectedIndex = NO_SELECTION;
            Refresh();
        }

        public bool TryMoveSlot(ItemSlotView source, ItemSlotView target)
        {
            if ((!_enableSlotInteraction) || (_grid == null) || (!IsOpen))
            {
                return false;
            }

            int sourceIndex = ResolveItemIndex(source);
            int targetIndex = ResolveItemIndex(target);
            if ((sourceIndex < 0) || (targetIndex < 0) || (sourceIndex == targetIndex)
                || (_items[sourceIndex].IsEmpty))
            {
                return false;
            }

            InventoryItemData item = _items[sourceIndex];
            _items[sourceIndex] = _items[targetIndex];
            _items[targetIndex] = item;
            _selectedIndex = targetIndex;
            Refresh();
            return true;
        }

        public bool TryToggleSlotLock(ItemSlotView slot)
        {
            if ((!_enableSlotInteraction) || (_grid == null) || (!IsOpen))
            {
                return false;
            }

            int index = ResolveItemIndex(slot);
            if ((index < 0) || (_items[index].IsEmpty))
            {
                return false;
            }

            InventoryItemData item = _items[index];
            ItemSlotData data = item.Slot;
            ItemSlotData changed = new ItemSlotData(data.Id, data.Grade, data.Count, data.Title,
                data.Description, !data.IsLocked, data.IsEquipped);
            _items[index] = new InventoryItemData(changed, item.Category, item.AcquiredOrder);
            _selectedIndex = index;
            Refresh();
            return true;
        }

        private int ResolveItemIndex(ItemSlotView slot)
        {
            if (slot == null)
            {
                return NO_SELECTION;
            }

            int index = _grid.IndexOf(slot);
            return ((index >= 0) && (index < _visibleToItem.Count))
                ? _visibleToItem[index] : NO_SELECTION;
        }

        // 거래 창 옆에 붙는 보조 인벤토리는 옛 동작대로 착용 장비도 그대로 보여 준다
        private bool HidesEquipped()
        {
            if (_hideEquipped == HIDE_UNKNOWN)
            {
                _hideEquipped = (transform.root.GetComponentInChildren<TradeView>(true) == null) ? HIDE_YES : HIDE_NO;
            }

            return _hideEquipped == HIDE_YES;
        }

        private void BindTabs()
        {
            if (_tabs == null)
            {
                return;
            }

            for (int i = 0; i < _tabs.Length; ++i)
            {
                if (_tabs[i] == null)
                {
                    continue;
                }

                EInventoryCategory category = (EInventoryCategory)i;
                _tabs[i].SetIsOnWithoutNotify(category == _category);
                _tabs[i].onValueChanged.AddListener(isOn => OnTabChanged(category, isOn));
            }
        }

        private void OnTabChanged(EInventoryCategory category, bool isOn)
        {
            if (!isOn || (category == _category))
            {
                return;
            }

            _category = category;
            ApplyTabLabels();
            Refresh();
        }

        private void OnSortChanged(int value)
        {
            _sort      = (EInventorySort)value;
            _ascending = IsAscendingByDefault(_sort);
            ApplyOrderMark();
            Arrange();
        }

        private void OnOrderClicked()
        {
            _ascending = !_ascending;
            ApplyOrderMark();
            Arrange();
        }

        private void OnSlotClicked(ItemSlotView slot)
        {
            int visibleIndex = _grid.IndexOf(slot);

            if ((visibleIndex < 0) || (visibleIndex >= _visibleToItem.Count) || slot.Data.IsEmpty)
            {
                return;
            }

            _selectedIndex = _visibleToItem[visibleIndex];
            _grid.SetSelectedIndex(visibleIndex);
            RefreshDetail(visibleIndex);
        }

        private void RefreshDetail(int visibleIndex)
        {
            if (_layout == null)
            {
                return;
            }

            bool has = (visibleIndex >= 0) && (_selectedIndex >= 0) && (_selectedIndex < _items.Count) && (!_items[_selectedIndex].IsEmpty);
            if (!has)
            {
                _layout.ShowItem(false, default, null, null);
                return;
            }

            InventoryItemData item = _items[_selectedIndex];
            int tabIndex = (int)item.Category;
            string category = ((_tabLabels != null) && (tabIndex >= 0) && (tabIndex < _tabLabels.Length) && (_tabLabels[tabIndex] != null))
                ? _tabLabels[tabIndex].text : null;
            ItemSlotView slot = _grid.GetSlot(visibleIndex);
            _layout.ShowItem(true, item.Slot, category, (slot != null) ? slot.IconSprite : null);
        }

        private void Refresh()
        {
            _visible.Clear();
            _visibleToItem.Clear();
            int selectedVisible = NO_SELECTION;

            // 분류 탭에서는 해당 아이템만 앞으로 모으고, 전체 탭에서는 실제 칸 배치를 보여 준다
            for (int i = 0; i < _items.Count; ++i)
            {
                InventoryItemData item = _items[i];
                bool show = (_category == EInventoryCategory.All) || (!item.IsEmpty && (item.Category == _category));

                if (!show)
                {
                    continue;
                }

                if (i == _selectedIndex)
                {
                    selectedVisible = _visible.Count;
                }

                _visible.Add(item.Slot);
                _visibleToItem.Add(i);
            }

            // 분류 탭의 빈 칸은 실제 빈 칸에만 연결한다. 다른 분류 아이템을 빈 칸으로 덮어쓰지 않는다.
            if ((_category != EInventoryCategory.All) && (_enableSlotInteraction))
            {
                for (int i = 0; i < _items.Count; ++i)
                {
                    if (_items[i].IsEmpty)
                    {
                        _visible.Add(default(ItemSlotData));
                        _visibleToItem.Add(i);
                    }
                }
            }

            if (_grid != null)
            {
                _grid.SetItems(_visible, _capacity);
                if (_enableSlotInteraction)
                {
                    for (int i = 0; i < _grid.SlotCount; ++i)
                    {
                        ItemSlotView slot = _grid.GetSlot(i);
                        if (!slot.TryGetComponent(out InventorySlotInteraction interaction))
                        {
                            interaction = slot.gameObject.AddComponent<InventorySlotInteraction>();
                        }

                        interaction.Bind(this, slot);
                    }
                }
                _grid.SetSelectedIndex(selectedVisible);
            }

            RefreshDetail(selectedVisible);
            RefreshCapacity();
        }

        private void RefreshCapacity()
        {
            int used = 0;

            for (int i = 0; i < _items.Count; ++i)
            {
                if (!_items[i].IsEmpty)
                {
                    ++used;
                }
            }

            float ratio = (_capacity > 0) ? ((float)used / _capacity) : 0f;
            Color labelColor = _normalColor;
            Color fillColor  = _fillColor;

            // 평소에는 숫자와 막대 색을 따로 두고, 거의 찼거나 가득 차면 둘 다 경고 색으로 바꾼다
            if (ratio >= FULL_RATIO)
            {
                labelColor = _fullColor;
                fillColor  = _fullColor;
            }
            else if (ratio >= WARNING_RATIO)
            {
                labelColor = _warningColor;
                fillColor  = _warningColor;
            }

            if (_capacityLabel != null)
            {
                _capacityLabel.text  = string.Format(USED_FORMAT, used);
                _capacityLabel.color = labelColor;
            }

            SetText(_capacityTotalLabel, string.Format(TOTAL_FORMAT, _capacity));

            if (_capacityFill != null)
            {
                _capacityFill.fillAmount = Mathf.Clamp01(ratio);
                _capacityFill.color      = fillColor;
            }
        }

        private void ApplyTabLabels()
        {
            if (_tabLabels == null)
            {
                return;
            }

            for (int i = 0; i < _tabLabels.Length; ++i)
            {
                if (_tabLabels[i] != null)
                {
                    _tabLabels[i].color = (i == (int)_category) ? _tabOnColor : _tabOffColor;
                }
            }
        }

        private void ApplyOrderMark()
        {
            if (_ascendingMark != null)
            {
                _ascendingMark.SetActive(_ascending);
            }

            if (_descendingMark != null)
            {
                _descendingMark.SetActive(!_ascending);
            }
        }

        // 같은 값끼리는 획득 순서로 갈라 정렬할 때마다 같은 배치가 나오게 한다
        private int Compare(InventoryItemData a, InventoryItemData b)
        {
            int result = 0;

            switch (_sort)
            {
                case EInventorySort.Grade:
                    result = ((int)a.Slot.Grade).CompareTo((int)b.Slot.Grade);
                    break;
                case EInventorySort.Category:
                    result = ((int)a.Category).CompareTo((int)b.Category);
                    break;
                case EInventorySort.Name:
                    result = string.CompareOrdinal(a.Slot.Title, b.Slot.Title);
                    break;
                case EInventorySort.Recent:
                    // 아래 획득 순서 비교가 곧 이 기준이다
                    break;
                default:
                    break;
            }

            if (result == 0)
            {
                result = a.AcquiredOrder.CompareTo(b.AcquiredOrder);
            }

            return _ascending ? result : -result;
        }

        // 등급과 최근 획득은 높은 것부터, 종류와 이름은 앞 순서부터 보는 것이 자연스럽다
        private static bool IsAscendingByDefault(EInventorySort sort)
        {
            return (sort == EInventorySort.Category) || (sort == EInventorySort.Name);
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
