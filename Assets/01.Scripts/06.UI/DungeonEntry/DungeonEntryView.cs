// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 카오스 던전과 보스 맵 입장 창. 목록과 선택한 항목의 설명, 입장 버튼만 그리고 고른 것은 이벤트로 알린다 </summary>
    public sealed class DungeonEntryView : MonoBehaviour
    {
        // 네트워크와 카탈로그는 모르고 글자는 DungeonEntryBinder가 만들어 넣는다. 입장 판정은 마을 서버가 한다

        /****************************************
        *                Fields
        ****************************************/

        private const string ENTER_TEXT       = "입장";
        private const string DEFAULT_NOTICE   = "입장하면 파티 준비 창이 열립니다";

        [SerializeField] private Button _closeButton = null;

        [Header("목록 (위에서부터)")]
        [SerializeField] private Button[]     _rowButtons       = null;
        [SerializeField] private TMP_Text[]   _rowTitles        = null;
        [SerializeField] private TMP_Text[]   _rowCategories    = null;
        [SerializeField] private GameObject[] _rowMarks         = null;
        [SerializeField] private GameObject   _emptyRoot        = null;
        [SerializeField] private Color        _availableColor   = new Color(0.92f, 0.95f, 0.97f, 1f);
        [SerializeField] private Color        _unavailableColor = new Color(0.52f, 0.58f, 0.64f, 1f);

        [Header("선택한 항목")]
        [SerializeField] private GameObject _detailRoot        = null;
        [SerializeField] private TMP_Text   _detailCategory    = null;
        [SerializeField] private TMP_Text   _detailTitle       = null;
        [SerializeField] private TMP_Text   _detailSummary     = null;
        [SerializeField] private TMP_Text   _detailRequirement = null;
        [SerializeField] private TMP_Text   _detailCost        = null;

        [Header("하단")]
        [SerializeField] private Button   _enterButton = null;
        [SerializeField] private TMP_Text _enterLabel  = null;
        [SerializeField] private TMP_Text _noticeLabel = null;

        private UnityAction[] _rowHandlers = null;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => gameObject.activeSelf;

        /// <summary> 한 번에 보여 줄 수 있는 항목 수, 창에 만들어 둔 줄 수다 </summary>
        public int RowCapacity => (_rowButtons == null) ? 0 : _rowButtons.Length;

        public event Action      Closed;
        public event Action<int> EntrySelected;
        public event Action      EnterClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            DungeonEntryModernLayout.Apply(this);

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(OnCloseClicked);
            }

            if (_enterButton != null)
            {
                _enterButton.onClick.AddListener(OnEnterClicked);
            }

            if (_rowButtons == null)
            {
                return;
            }

            // 줄마다 자기 번호를 넘겨야 해서 처리기를 따로 만들어 두고 해제할 때 같은 것을 쓴다
            _rowHandlers = new UnityAction[_rowButtons.Length];
            for (int i = 0; i < _rowButtons.Length; ++i)
            {
                int index = i;
                _rowHandlers[i] = () => OnRowClicked(index);
                if (_rowButtons[i] != null)
                {
                    _rowButtons[i].onClick.AddListener(_rowHandlers[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(OnCloseClicked);
            }

            if (_enterButton != null)
            {
                _enterButton.onClick.RemoveListener(OnEnterClicked);
            }

            if ((_rowButtons == null) || (_rowHandlers == null))
            {
                return;
            }

            for (int i = 0; i < _rowButtons.Length; ++i)
            {
                if (_rowButtons[i] != null)
                {
                    _rowButtons[i].onClick.RemoveListener(_rowHandlers[i]);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Show()
        {
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary> 목록을 채운다. 창에 줄이 모자라면 앞에서부터 줄 수만큼만 보인다 </summary>
        public void SetEntries(IReadOnlyList<DungeonEntryData> entries)
        {
            int count = (entries == null) ? 0 : entries.Count;
            for (int i = 0; i < RowCapacity; ++i)
            {
                bool has = i < count;
                if (_rowButtons[i] != null)
                {
                    _rowButtons[i].gameObject.SetActive(has);
                }

                if (!has)
                {
                    continue;
                }

                DungeonEntryData entry = entries[i];
                SetText(GetAt(_rowTitles, i), entry.Title);
                SetText(GetAt(_rowCategories, i), entry.Category);
                TMP_Text title = GetAt(_rowTitles, i);
                if (title != null)
                {
                    title.color = entry.IsAvailable ? _availableColor : _unavailableColor;
                }
            }

            SetActive(_emptyRoot, count == 0);
            if (count == 0)
            {
                SetDetail(null);
            }
        }

        /// <summary> 선택 표시를 옮긴다, 음수면 모두 끈다 </summary>
        public void SetSelected(int index)
        {
            if (_rowMarks == null)
            {
                return;
            }

            for (int i = 0; i < _rowMarks.Length; ++i)
            {
                SetActive(_rowMarks[i], i == index);
            }
        }

        /// <summary> 선택한 항목의 설명을 쓴다, null이면 설명 칸을 숨기고 입장 버튼을 막는다 </summary>
        public void SetDetail(DungeonEntryData? entry)
        {
            SetActive(_detailRoot, entry.HasValue);
            if (!entry.HasValue)
            {
                SetEnterEnabled(false);
                SetNotice(DEFAULT_NOTICE);
                return;
            }

            DungeonEntryData value = entry.Value;
            SetText(_detailCategory, value.Category);
            SetText(_detailTitle, value.Title);
            SetText(_detailSummary, value.Summary);
            SetText(_detailRequirement, value.Requirement);
            SetText(_detailCost, value.Cost);
            SetEnterEnabled(value.IsAvailable);
            SetNotice(value.IsAvailable ? DEFAULT_NOTICE : value.UnavailableReason);
        }

        public void SetEnterEnabled(bool enabled)
        {
            if (_enterButton != null)
            {
                _enterButton.interactable = enabled;
            }

            SetText(_enterLabel, ENTER_TEXT);
        }

        public void SetNotice(string text)
        {
            SetText(_noticeLabel, text);
        }

        private void OnCloseClicked()
        {
            Closed?.Invoke();
        }

        private void OnEnterClicked()
        {
            EnterClicked?.Invoke();
        }

        private void OnRowClicked(int index)
        {
            EntrySelected?.Invoke(index);
        }

        private static TMP_Text GetAt(TMP_Text[] labels, int index)
        {
            return ((labels != null) && (index < labels.Length)) ? labels[index] : null;
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }
    }
}
