// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 스킬창. 기술 탭, 기술 정보, 기술별 부적 3단계 후보, 선택한 효과의 상세와 등록, 강화, 분해, 장착, 조합 저장 버튼을 보여 준다 </summary>
    public sealed class SkillWindowView : UiWindowView
    {
        // 네트워크는 모르고 이벤트로 의도만 알린다. 글자와 버튼 상태는 SkillWindowBinder가 정해 넣는다.
        // 후보 칸 배열은 단계 * 2 + 선택 순서다

        /****************************************
        *                Fields
        ****************************************/

        private const int CHOICE_SLOT_COUNT = SkillWindowViewRules.TIERS_PER_SKILL * SkillWindowViewRules.CHOICES_PER_TIER;

        [SerializeField] private Button _closeButton = null;

        [Header("머리")]
        [SerializeField] private TMP_Text _classLabel     = null;
        [SerializeField] private TMP_Text _stackRuleLabel = null;

        [Header("기술 탭 (기본 공격, Q, W, E, R)")]
        [SerializeField] private Button[]     _tabButtons  = null;
        [SerializeField] private Image[]      _tabIcons    = null;
        [SerializeField] private TMP_Text[]   _tabLabels   = null;
        [SerializeField] private GameObject[] _tabSelected = null;

        [Header("기술 정보")]
        [SerializeField] private TMP_Text _skillNameLabel   = null;
        [SerializeField] private TMP_Text _skillStatsLabel  = null;
        [SerializeField] private TMP_Text _skillDescription = null;

        [Header("부적")]
        [SerializeField] private GameObject   _talismanGroup  = null;
        [SerializeField] private TMP_Text     _talismanNotice = null;
        [SerializeField] private GameObject[] _choiceRoots    = null;
        [SerializeField] private Button[]     _choiceButtons  = null;
        [SerializeField] private Image[]      _choiceIcons    = null;
        [SerializeField] private TMP_Text[]   _choiceNames    = null;
        [SerializeField] private TMP_Text[]   _choiceStates   = null;
        [SerializeField] private GameObject[] _choiceSelected = null;
        [SerializeField] private GameObject[] _choiceEquipped = null;

        [Header("선택한 효과")]
        [SerializeField] private TMP_Text _effectTitle       = null;
        [SerializeField] private TMP_Text _effectBonus       = null;
        [SerializeField] private TMP_Text _effectDescription = null;
        [SerializeField] private TMP_Text _effectWarning     = null;

        [Header("동작")]
        [SerializeField] private Button   _registerButton    = null;
        [SerializeField] private TMP_Text _registerLabel     = null;
        [SerializeField] private Button   _upgradeButton     = null;
        [SerializeField] private TMP_Text _upgradeLabel      = null;
        [SerializeField] private Button   _disassembleButton = null;
        [SerializeField] private TMP_Text _disassembleLabel  = null;
        [SerializeField] private Button   _quantityDown      = null;
        [SerializeField] private Button   _quantityUp        = null;
        [SerializeField] private Button   _equipButton       = null;
        [SerializeField] private TMP_Text _equipLabel        = null;

        [Header("아래 줄")]
        [SerializeField] private TMP_Text _footerLabel = null;
        [SerializeField] private TMP_Text _statusLabel = null;
        [SerializeField] private Button   _saveButton  = null;
        [SerializeField] private Button   _retryButton = null;

        [Header("닫기 확인")]
        [SerializeField] private GameObject _confirmPanel  = null;
        [SerializeField] private Button     _confirmAccept = null;
        [SerializeField] private Button     _confirmCancel = null;

        [Header("표시 색")]
        [SerializeField] private Color _normalStateColor = new Color(0.65f, 0.73f, 0.8f, 1f);
        [SerializeField] private Color _warningColor     = new Color(0.94f, 0.62f, 0.15f, 1f);

        private UnityAction[] _tabHandlers;
        private UnityAction[] _choiceHandlers;
        private bool          _modernAlternate = false;
        private int           _selectedTab     = -1;
        private int           _selectedChoice  = -1;
        private Sprite        _selectedSkillIcon;
        private Sprite        _selectedTalismanIcon;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => gameObject.activeSelf;

        /// <summary> 부적(참) · 스킬(거짓) 표시 모드. 같은 뷰와 Binder를 쓰는 표시만의 차이다 </summary>
        public bool ModernAlternate => _modernAlternate;

        /// <summary> 지금 고른 기술 탭과 부적 후보의 실제 아이콘. 고른 것이 없거나 그림이 없으면 null이다 </summary>
        public Sprite SelectedSkillIcon    => _selectedSkillIcon;
        public Sprite SelectedTalismanIcon => _selectedTalismanIcon;

        public event Action<int>      TabSelected;
        public event Action<int, int> ChoiceSelected;
        public event Action           RegisterRequested;
        public event Action           UpgradeRequested;
        public event Action           DisassembleRequested;
        public event Action<int>      QuantityChanged;
        public event Action           EquipRequested;
        public event Action           SaveRequested;
        public event Action           RetryRequested;
        public event Action           CloseRequested;
        public event Action           CloseConfirmed;
        public event Action           CloseCancelled;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            AddListener(_closeButton, OnCloseClicked);
            AddListener(_registerButton, OnRegisterClicked);
            AddListener(_upgradeButton, OnUpgradeClicked);
            AddListener(_disassembleButton, OnDisassembleClicked);
            AddListener(_quantityDown, OnQuantityDown);
            AddListener(_quantityUp, OnQuantityUp);
            AddListener(_equipButton, OnEquipClicked);
            AddListener(_saveButton, OnSaveClicked);
            AddListener(_retryButton, OnRetryClicked);
            AddListener(_confirmAccept, OnConfirmAccept);
            AddListener(_confirmCancel, OnConfirmCancel);

            // 버튼마다 자기 번호를 넘겨야 해서 처리기를 따로 만들어 두고 해제할 때 같은 것을 쓴다
            if (_tabButtons != null)
            {
                _tabHandlers = new UnityAction[_tabButtons.Length];
                for (int i = 0; i < _tabButtons.Length; ++i)
                {
                    int tab = i;
                    _tabHandlers[i] = () => TabSelected?.Invoke(tab);
                    AddListener(_tabButtons[i], _tabHandlers[i]);
                }
            }

            if (_choiceButtons != null)
            {
                _choiceHandlers = new UnityAction[_choiceButtons.Length];
                for (int i = 0; i < _choiceButtons.Length; ++i)
                {
                    int tier   = i / SkillWindowViewRules.CHOICES_PER_TIER;
                    int choice = i % SkillWindowViewRules.CHOICES_PER_TIER;
                    _choiceHandlers[i] = () => ChoiceSelected?.Invoke(tier, choice);
                    AddListener(_choiceButtons[i], _choiceHandlers[i]);
                }
            }

            EnsureModernLayout();
            ShowConfirm(false);
        }

        private void OnDestroy()
        {
            RemoveListener(_closeButton, OnCloseClicked);
            RemoveListener(_registerButton, OnRegisterClicked);
            RemoveListener(_upgradeButton, OnUpgradeClicked);
            RemoveListener(_disassembleButton, OnDisassembleClicked);
            RemoveListener(_quantityDown, OnQuantityDown);
            RemoveListener(_quantityUp, OnQuantityUp);
            RemoveListener(_equipButton, OnEquipClicked);
            RemoveListener(_saveButton, OnSaveClicked);
            RemoveListener(_retryButton, OnRetryClicked);
            RemoveListener(_confirmAccept, OnConfirmAccept);
            RemoveListener(_confirmCancel, OnConfirmCancel);
            RemoveAll(_tabButtons, _tabHandlers);
            RemoveAll(_choiceButtons, _choiceHandlers);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시안 레이아웃을 한 번만 적용한다. 여러 번 불러도 같은 결과다 </summary>
        public void EnsureModernLayout()
        {
            SkillWindowModernLayout.Apply(this);
        }

        /// <summary> 표시 모드만 바꾼다. 데이터 · 초안 · 요청 상태와 부적 영역 켜기 · 끄기는 그대로다 </summary>
        public void SetModernPresentation(bool alternate)
        {
            _modernAlternate = alternate;
            SkillWindowModernLayout.Present(this, alternate);
        }

        public void SetOpen(bool open)
        {
            gameObject.SetActive(open);
        }

        public void SetHeader(string className, string stackRule)
        {
            SetText(_classLabel, className);
            SetText(_stackRuleLabel, stackRule);
        }

        public void SetTab(int tab, string label, Sprite icon, bool selected)
        {
            SetText(Pick(_tabLabels, tab), label);
            Image image = Pick(_tabIcons, tab);
            if (image != null)
            {
                image.sprite  = icon;
                image.enabled = icon != null;
            }

            SetActive(Pick(_tabSelected, tab), selected);

            if (selected)
            {
                _selectedTab       = tab;
                _selectedSkillIcon = icon;
            }
            else if (_selectedTab == tab)
            {
                _selectedTab       = -1;
                _selectedSkillIcon = null;
            }

            SkillWindowModernLayout.RefreshIcons(this);
        }

        public void SetSkill(string skillName, string stats, string description)
        {
            SetText(_skillNameLabel, skillName);
            SetText(_skillStatsLabel, stats);
            SetText(_skillDescription, description);
        }

        /// <summary> 부적 영역을 보이거나, 숨기고 안내 글자를 보인다 </summary>
        public void SetTalismanArea(bool visible, string notice)
        {
            SetActive(_talismanGroup, visible);
            SetText(_talismanNotice, notice);
            SetActive(_talismanNotice != null ? _talismanNotice.gameObject : null, !string.IsNullOrEmpty(notice));
        }

        public void SetChoice(int tier, int choice, bool visible, string effectName, string state, Sprite icon, bool selected,
            bool equipped, bool warning)
        {
            int index = (tier * SkillWindowViewRules.CHOICES_PER_TIER) + choice;
            if ((index < 0) || (index >= CHOICE_SLOT_COUNT))
            {
                return;
            }

            SetActive(Pick(_choiceRoots, index), visible);
            if (!visible || !selected)
            {
                if (_selectedChoice == index)
                {
                    _selectedChoice       = -1;
                    _selectedTalismanIcon = null;
                    SkillWindowModernLayout.RefreshIcons(this);
                }
            }

            if (!visible)
            {
                return;
            }

            SetText(Pick(_choiceNames, index), effectName);
            TMP_Text stateLabel = Pick(_choiceStates, index);
            if (stateLabel != null)
            {
                stateLabel.text  = state ?? string.Empty;
                stateLabel.color = warning ? _warningColor : _normalStateColor;
            }

            Image image = Pick(_choiceIcons, index);
            if (image != null)
            {
                image.sprite  = icon;
                image.enabled = icon != null;
            }

            SetActive(Pick(_choiceSelected, index), selected);
            SetActive(Pick(_choiceEquipped, index), equipped);

            if (selected)
            {
                _selectedChoice       = index;
                _selectedTalismanIcon = icon;
                SkillWindowModernLayout.RefreshIcons(this);
            }
        }

        public void SetEffect(string title, string bonus, string description, string warning)
        {
            SetText(_effectTitle, title);
            SetText(_effectBonus, bonus);
            SetText(_effectDescription, description);
            SetText(_effectWarning, warning);
        }

        public void SetRegister(string label, bool interactable)    { SetButton(_registerButton, _registerLabel, SkillWindowModernLayout.SplitCostLabel(label), interactable); }
        public void SetUpgrade(string label, bool interactable)     { SetButton(_upgradeButton, _upgradeLabel, SkillWindowModernLayout.SplitCostLabel(label), interactable); }
        public void SetEquip(string label, bool interactable)       { SetButton(_equipButton, _equipLabel, label, interactable); }

        /// <summary> 분해 버튼과 수량 조절 버튼 </summary>
        public void SetDisassemble(string label, bool interactable, bool canDecrease, bool canIncrease)
        {
            SetButton(_disassembleButton, _disassembleLabel, SkillWindowModernLayout.SplitCostLabel(label), interactable);
            SetInteractable(_quantityDown, canDecrease);
            SetInteractable(_quantityUp, canIncrease);
        }

        public void SetFooter(string footer)
        {
            SetText(_footerLabel, footer);
        }

        public void SetStatus(string status)
        {
            SetText(_statusLabel, status);
        }

        public void SetSave(bool interactable)
        {
            SetInteractable(_saveButton, interactable);
        }

        public void SetRetry(bool visible)
        {
            SetActive(_retryButton != null ? _retryButton.gameObject : null, visible);
        }

        public void ShowConfirm(bool visible)
        {
            SetActive(_confirmPanel, visible);
        }

        private static T Pick<T>(T[] items, int index) where T : class
        {
            return ((items != null) && (index >= 0) && (index < items.Length)) ? items[index] : null;
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
            if ((target != null) && (target.activeSelf != active))
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

        private static void SetButton(Button button, TMP_Text label, string text, bool interactable)
        {
            SetText(label, text);
            SetInteractable(button, interactable);
        }

        private static void AddListener(Button button, UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(Button button, UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        private static void RemoveAll(Button[] buttons, UnityAction[] handlers)
        {
            if ((buttons == null) || (handlers == null))
            {
                return;
            }

            for (int i = 0; i < buttons.Length; ++i)
            {
                if ((buttons[i] != null) && (i < handlers.Length) && (handlers[i] != null))
                {
                    buttons[i].onClick.RemoveListener(handlers[i]);
                }
            }
        }

        private void OnCloseClicked()       { CloseRequested?.Invoke(); }
        private void OnRegisterClicked()    { RegisterRequested?.Invoke(); }
        private void OnUpgradeClicked()     { UpgradeRequested?.Invoke(); }
        private void OnDisassembleClicked() { DisassembleRequested?.Invoke(); }
        private void OnQuantityDown()       { QuantityChanged?.Invoke(-1); }
        private void OnQuantityUp()         { QuantityChanged?.Invoke(1); }
        private void OnEquipClicked()       { EquipRequested?.Invoke(); }
        private void OnSaveClicked()        { SaveRequested?.Invoke(); }
        private void OnRetryClicked()       { RetryRequested?.Invoke(); }
        private void OnConfirmAccept()      { CloseConfirmed?.Invoke(); }
        private void OnConfirmCancel()      { CloseCancelled?.Invoke(); }
    }
}
