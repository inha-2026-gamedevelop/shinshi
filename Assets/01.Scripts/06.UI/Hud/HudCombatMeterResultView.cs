// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 결과 화면의 전투 기록 탭. 종료 메시지를 받지 못했으면 "기록 없음"을 보여준다 </summary>
    // 파티 행은 피해 내림차순이 아니라 바인더가 CombatMeterViewRules로 정한 순서(본인 먼저)를 그대로 받는다.
    // 본인 행만 눌러 슬롯 분해, 단계별, 받은 피해 상위, 마지막 사망 요약을 펼친다
    public sealed class HudCombatMeterResultView : MonoBehaviour
    {
        /// <summary> 파티 행 하나(준 피해, 받은 피해, 사망, 부활) </summary>
        [Serializable]
        private sealed class ResultRow
        {
            [SerializeField] private GameObject _root        = null;
            [SerializeField] private TMP_Text   _name        = null;
            [SerializeField] private TMP_Text   _damage      = null;
            [SerializeField] private TMP_Text   _damageTaken = null;
            [SerializeField] private TMP_Text   _deaths      = null;
            [SerializeField] private TMP_Text   _revives     = null;
            [SerializeField] private Button     _button      = null;

            [NonSerialized] public bool   HasValue = false;
            [NonSerialized] public string Key      = null;

            public GameObject Root        => _root;
            public TMP_Text   Name        => _name;
            public TMP_Text   Damage      => _damage;
            public TMP_Text   DamageTaken => _damageTaken;
            public TMP_Text   Deaths      => _deaths;
            public TMP_Text   Revives     => _revives;
            public Button     Button      => _button;
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

            [NonSerialized] public string Key = null;

            public GameObject Root   => _root;
            public TMP_Text   Label  => _label;
            public TMP_Text   Damage => _damage;
            public TMP_Text   Hits   => _hits;
            public TMP_Text   MaxHit => _maxHit;
        }

        /// <summary> 보스 단계 한 줄 </summary>
        [Serializable]
        private sealed class PhaseRow
        {
            [SerializeField] private GameObject _root    = null;
            [SerializeField] private TMP_Text   _label   = null;
            [SerializeField] private TMP_Text   _seconds = null;
            [SerializeField] private TMP_Text   _damage  = null;
            [SerializeField] private TMP_Text   _dps     = null;

            [NonSerialized] public string Key = null;

            public GameObject Root    => _root;
            public TMP_Text   Label   => _label;
            public TMP_Text   Seconds => _seconds;
            public TMP_Text   Damage  => _damage;
            public TMP_Text   Dps     => _dps;
        }

        /// <summary> 받은 피해 상위 출처, 사망 요약에 함께 쓰는 한 줄(출처, 양) </summary>
        [Serializable]
        private sealed class SourceRow
        {
            [SerializeField] private GameObject _root   = null;
            [SerializeField] private TMP_Text   _source = null;
            [SerializeField] private TMP_Text   _amount = null;

            [NonSerialized] public string Key = null;

            public GameObject Root   => _root;
            public TMP_Text   Source => _source;
            public TMP_Text   Amount => _amount;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("기록 없음")]
        [SerializeField] private GameObject _noRecordRoot = null;
        [SerializeField] private GameObject _recordRoot   = null;

        [Header("파티 행")]
        [SerializeField] private ResultRow[] _rows = null;

        [Header("본인 펼침")]
        [SerializeField] private GameObject  _selfDetailRoot     = null;
        [SerializeField] private SlotRow[]   _selfSlotRows       = null;
        [SerializeField] private PhaseRow[]  _phaseRows          = null;
        [SerializeField] private SourceRow[] _topDamageTakenRows = null;
        [SerializeField] private SourceRow[] _deathRows          = null;

        /****************************************
        *              Properties
        ****************************************/

        public int RowCount            => (_rows == null) ? 0 : _rows.Length;
        public int SelfSlotCount       => (_selfSlotRows == null) ? 0 : _selfSlotRows.Length;
        public int PhaseRowCount       => (_phaseRows == null) ? 0 : _phaseRows.Length;
        public int TopDamageTakenCount => (_topDamageTakenRows == null) ? 0 : _topDamageTakenRows.Length;
        public int DeathRowCount       => (_deathRows == null) ? 0 : _deathRows.Length;

        public event Action SelfRowClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_rows != null)
            {
                for (int i = 0; i < _rows.Length; ++i)
                {
                    Button button = _rows[i].Button;
                    if (button != null)
                    {
                        button.onClick.AddListener(OnSelfRowClicked);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_rows != null)
            {
                for (int i = 0; i < _rows.Length; ++i)
                {
                    Button button = _rows[i].Button;
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

        public void SetHasRecord(bool hasRecord)
        {
            SetActive(_noRecordRoot, !hasRecord);
            SetActive(_recordRoot, hasRecord);
            if (!hasRecord)
            {
                SetSelfDetailVisible(false);
            }

            // 일반전에는 미터가 없으므로 기록이 없으면 패널 자체를 숨긴다
            gameObject.SetActive(hasRecord);
        }

        /// <summary> isSelf가 거짓이면 행의 버튼을 눌러도 반응하지 않는다 </summary>
        public void SetRow(int index, string memberName, string damageText, string damageTakenText, string deathsText,
            string revivesText, bool isSelf)
        {
            ResultRow row = GetRow(index);
            if (row == null)
            {
                return;
            }

            if (row.Button != null)
            {
                row.Button.interactable = isSelf;
            }

            string key = string.Join("|", memberName, damageText, damageTakenText, deathsText, revivesText);
            if ((row.HasValue) && (row.Key == key))
            {
                SetActive(row.Root, true);
                return;
            }

            row.HasValue = true;
            row.Key      = key;

            SetActive(row.Root, true);
            SetText(row.Name, memberName);
            SetText(row.Damage, damageText);
            SetText(row.DamageTaken, damageTakenText);
            SetText(row.Deaths, deathsText);
            SetText(row.Revives, revivesText);
        }

        public void ClearRow(int index)
        {
            ResultRow row = GetRow(index);
            if ((row == null) || ((!row.HasValue) && (row.Root != null) && (!row.Root.activeSelf)))
            {
                return;
            }

            row.HasValue = false;
            SetActive(row.Root, false);
        }

        public void SetSelfDetailVisible(bool visible)
        {
            SetActive(_selfDetailRoot, visible);
        }

        public bool IsSelfDetailVisible()
        {
            return (_selfDetailRoot != null) && (_selfDetailRoot.activeSelf);
        }

        public void SetSelfSlot(int index, string label, string damageText, string hitsText, string maxHitText)
        {
            SlotRow row = GetSlotRow(index);
            if (row == null)
            {
                return;
            }

            string key = string.Join("|", damageText, hitsText, maxHitText);
            if (row.Key == key)
            {
                SetActive(row.Root, true);
                return;
            }

            row.Key = key;
            SetActive(row.Root, true);
            SetText(row.Label, label);
            SetText(row.Damage, damageText);
            SetText(row.Hits, hitsText);
            SetText(row.MaxHit, maxHitText);
        }

        public void SetPhaseRow(int index, string label, string secondsText, string damageText, string dpsText)
        {
            PhaseRow row = GetPhaseRow(index);
            if (row == null)
            {
                return;
            }

            string key = string.Join("|", label, secondsText, damageText, dpsText);
            if (row.Key == key)
            {
                SetActive(row.Root, true);
                return;
            }

            row.Key = key;
            SetActive(row.Root, true);
            SetText(row.Label, label);
            SetText(row.Seconds, secondsText);
            SetText(row.Damage, damageText);
            SetText(row.Dps, dpsText);
        }

        public void ClearPhaseRow(int index)
        {
            PhaseRow row = GetPhaseRow(index);
            if ((row == null) || ((row.Key == null) && (row.Root != null) && (!row.Root.activeSelf)))
            {
                return;
            }

            row.Key = null;
            SetActive(row.Root, false);
        }

        public void SetTopDamageTaken(int index, string sourceId, string amountText)
        {
            SetSourceRow(_topDamageTakenRows, index, sourceId, amountText);
        }

        public void ClearTopDamageTaken(int index)
        {
            ClearSourceRow(_topDamageTakenRows, index);
        }

        public void SetDeathRow(int index, string sourceId, string amountText)
        {
            SetSourceRow(_deathRows, index, sourceId, amountText);
        }

        public void ClearDeathRow(int index)
        {
            ClearSourceRow(_deathRows, index);
        }

        private static void SetSourceRow(SourceRow[] rows, int index, string sourceId, string amountText)
        {
            SourceRow row = GetSourceRow(rows, index);
            if (row == null)
            {
                return;
            }

            string key = string.Join("|", sourceId, amountText);
            if (row.Key == key)
            {
                SetActive(row.Root, true);
                return;
            }

            row.Key = key;
            SetActive(row.Root, true);
            SetText(row.Source, sourceId);
            SetText(row.Amount, amountText);
        }

        private static void ClearSourceRow(SourceRow[] rows, int index)
        {
            SourceRow row = GetSourceRow(rows, index);
            if ((row == null) || ((row.Key == null) && (row.Root != null) && (!row.Root.activeSelf)))
            {
                return;
            }

            row.Key = null;
            SetActive(row.Root, false);
        }

        private ResultRow GetRow(int index)
        {
            if ((_rows == null) || (index < 0) || (index >= _rows.Length))
            {
                return null;
            }

            return _rows[index];
        }

        private SlotRow GetSlotRow(int index)
        {
            if ((_selfSlotRows == null) || (index < 0) || (index >= _selfSlotRows.Length))
            {
                return null;
            }

            return _selfSlotRows[index];
        }

        private PhaseRow GetPhaseRow(int index)
        {
            if ((_phaseRows == null) || (index < 0) || (index >= _phaseRows.Length))
            {
                return null;
            }

            return _phaseRows[index];
        }

        private static SourceRow GetSourceRow(SourceRow[] rows, int index)
        {
            if ((rows == null) || (index < 0) || (index >= rows.Length))
            {
                return null;
            }

            return rows[index];
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

        private void OnSelfRowClicked()
        {
            SelfRowClicked?.Invoke();
        }
    }
}
