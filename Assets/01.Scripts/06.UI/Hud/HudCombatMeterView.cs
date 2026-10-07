// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 전투 HUD의 딜미터 창. 접힘은 본인 피해, 펼침은 파티 행 막대와 본인 슬롯 분해를 보여준다 </summary>
    // 글자, 막대 비율은 바인더가 CombatMeterViewRules로 미리 만든 값만 받는다. 이 뷰는 서식을 모른다.
    // 타인 행 버튼은 interactable을 꺼서 막는다 - Unity Button은 꺼진 상태에서 클릭을 올리지 않는다
    public sealed class HudCombatMeterView : MonoBehaviour
    {
        /// <summary> 파티 행 하나 </summary>
        [Serializable]
        private sealed class PartyRow
        {
            [SerializeField] private GameObject    _root      = null;
            [SerializeField] private TMP_Text      _name      = null;
            [SerializeField] private TMP_Text      _damage    = null;
            [SerializeField] private TMP_Text      _dps       = null;
            [SerializeField] private TMP_Text      _share     = null;
            [SerializeField] private RectTransform _shareFill = null;
            [SerializeField] private Button        _button    = null;

            [NonSerialized] public bool   HasValue   = false;
            [NonSerialized] public string LastName   = null;
            [NonSerialized] public string LastDamage = null;
            [NonSerialized] public string LastDps    = null;
            [NonSerialized] public string LastShare  = null;
            [NonSerialized] public float  LastRatio  = -1f;

            public GameObject    Root      => _root;
            public TMP_Text      Name      => _name;
            public TMP_Text      Damage    => _damage;
            public TMP_Text      Dps       => _dps;
            public TMP_Text      Share     => _share;
            public RectTransform ShareFill => _shareFill;
            public Button        Button    => _button;
        }

        /// <summary> 본인 슬롯 분해 한 줄 </summary>
        [Serializable]
        private sealed class SlotRow
        {
            [SerializeField] private GameObject _root   = null;
            [SerializeField] private TMP_Text   _label  = null;
            [SerializeField] private TMP_Text   _damage = null;
            [SerializeField] private TMP_Text   _hits   = null;
            [SerializeField] private TMP_Text   _maxHit = null;

            [NonSerialized] public bool   HasValue   = false;
            [NonSerialized] public string LastDamage = null;
            [NonSerialized] public string LastHits   = null;
            [NonSerialized] public string LastMaxHit = null;

            public GameObject Root   => _root;
            public TMP_Text   Label  => _label;
            public TMP_Text   Damage => _damage;
            public TMP_Text   Hits   => _hits;
            public TMP_Text   MaxHit => _maxHit;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("접힘")]
        [SerializeField] private GameObject _collapsedRoot   = null;
        [SerializeField] private TMP_Text   _collapsedDamage = null;
        [SerializeField] private TMP_Text   _collapsedDps    = null;

        [Header("펼침")]
        [SerializeField] private GameObject _expandedRoot       = null;
        [SerializeField] private PartyRow[] _partyRows          = null;
        [SerializeField] private Button     _phaseToggleButton  = null;
        [SerializeField] private TMP_Text   _phaseToggleLabel   = null;

        [Header("본인 슬롯 분해")]
        [SerializeField] private GameObject _selfBreakdownRoot = null;
        [SerializeField] private SlotRow[]  _selfSlotRows      = null;

        /****************************************
        *              Properties
        ****************************************/

        public int PartyRowCount => (_partyRows == null) ? 0 : _partyRows.Length;
        public int SelfSlotCount => (_selfSlotRows == null) ? 0 : _selfSlotRows.Length;

        public event Action PhaseToggleClicked;
        public event Action SelfRowClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_phaseToggleButton != null)
            {
                _phaseToggleButton.onClick.AddListener(OnPhaseToggleClicked);
            }

            if (_partyRows != null)
            {
                for (int i = 0; i < _partyRows.Length; ++i)
                {
                    Button button = _partyRows[i].Button;
                    if (button != null)
                    {
                        button.onClick.AddListener(OnSelfRowClicked);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_phaseToggleButton != null)
            {
                _phaseToggleButton.onClick.RemoveListener(OnPhaseToggleClicked);
            }

            if (_partyRows != null)
            {
                for (int i = 0; i < _partyRows.Length; ++i)
                {
                    Button button = _partyRows[i].Button;
                    if (button != null)
                    {
                        button.onClick.RemoveListener(OnSelfRowClicked);
                    }
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetWindowVisible(bool visible)
        {
            SetActive(_collapsedRoot, (visible) && (!IsExpanded()));
            SetActive(_expandedRoot, (visible) && (IsExpanded()));
        }

        public void SetExpanded(bool expanded)
        {
            SetActive(_collapsedRoot, !expanded);
            SetActive(_expandedRoot, expanded);
            if (!expanded)
            {
                SetSelfBreakdownVisible(false);
            }
        }

        public bool IsExpanded()
        {
            return (_expandedRoot != null) && (_expandedRoot.activeSelf);
        }

        public void SetCollapsed(string damageText, string dpsText)
        {
            if (_collapsedDamage != null)
            {
                _collapsedDamage.SetText(damageText ?? string.Empty);
            }

            if (_collapsedDps != null)
            {
                _collapsedDps.SetText(dpsText ?? string.Empty);
            }
        }

        public void SetPhaseToggleLabel(string label)
        {
            if (_phaseToggleLabel != null)
            {
                _phaseToggleLabel.SetText(label ?? string.Empty);
            }
        }

        /// <summary> isSelf가 거짓이면 행의 버튼을 눌러도 반응하지 않는다 </summary>
        public void SetPartyRow(int index, string memberName, string damageText, string dpsText, string shareText,
            float shareRatio01, bool isSelf)
        {
            PartyRow row = GetPartyRow(index);
            if (row == null)
            {
                return;
            }

            if (row.Button != null)
            {
                row.Button.interactable = isSelf;
            }

            if ((row.HasValue) && (row.LastName == memberName) && (row.LastDamage == damageText) && (row.LastDps == dpsText)
                && (row.LastShare == shareText) && (Mathf.Approximately(row.LastRatio, shareRatio01)))
            {
                SetActive(row.Root, true);
                return;
            }

            row.HasValue   = true;
            row.LastName   = memberName;
            row.LastDamage = damageText;
            row.LastDps    = dpsText;
            row.LastShare  = shareText;
            row.LastRatio  = shareRatio01;

            SetActive(row.Root, true);
            SetText(row.Name, memberName);
            SetText(row.Damage, damageText);
            SetText(row.Dps, dpsText);
            SetText(row.Share, shareText);

            if (row.ShareFill != null)
            {
                Vector2 anchorMax = row.ShareFill.anchorMax;
                anchorMax.x            = Mathf.Clamp01(shareRatio01);
                row.ShareFill.anchorMax = anchorMax;
            }
        }

        public void ClearPartyRow(int index)
        {
            PartyRow row = GetPartyRow(index);
            if ((row == null) || ((!row.HasValue) && (row.Root != null) && (!row.Root.activeSelf)))
            {
                return;
            }

            row.HasValue = false;
            SetActive(row.Root, false);
        }

        public void SetSelfBreakdownVisible(bool visible)
        {
            SetActive(_selfBreakdownRoot, visible);
        }

        public bool IsSelfBreakdownVisible()
        {
            return (_selfBreakdownRoot != null) && (_selfBreakdownRoot.activeSelf);
        }

        public void SetSelfSlot(int index, string label, string damageText, string hitsText, string maxHitText)
        {
            SlotRow row = GetSlotRow(index);
            if (row == null)
            {
                return;
            }

            if ((row.HasValue) && (row.LastDamage == damageText) && (row.LastHits == hitsText) && (row.LastMaxHit == maxHitText))
            {
                return;
            }

            row.HasValue   = true;
            row.LastDamage = damageText;
            row.LastHits   = hitsText;
            row.LastMaxHit = maxHitText;

            SetText(row.Label, label);
            SetText(row.Damage, damageText);
            SetText(row.Hits, hitsText);
            SetText(row.MaxHit, maxHitText);
        }

        private PartyRow GetPartyRow(int index)
        {
            if ((_partyRows == null) || (index < 0) || (index >= _partyRows.Length))
            {
                return null;
            }

            return _partyRows[index];
        }

        private SlotRow GetSlotRow(int index)
        {
            if ((_selfSlotRows == null) || (index < 0) || (index >= _selfSlotRows.Length))
            {
                return null;
            }

            return _selfSlotRows[index];
        }

        private void OnPhaseToggleClicked()
        {
            PhaseToggleClicked?.Invoke();
        }

        private void OnSelfRowClicked()
        {
            SelfRowClicked?.Invoke();
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.SetText(value ?? string.Empty);
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if ((target != null) && (target.activeSelf != active))
            {
                target.SetActive(active);
            }
        }
    }
}
