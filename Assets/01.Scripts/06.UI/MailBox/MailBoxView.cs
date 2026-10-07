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
    /// <summary> 우편함 창이며 우편 목록, 상세, 수령 버튼, 쪽 이동을 보인다. 네트워크는 모르고 이벤트로 의도만 알린다 </summary>
    public sealed class MailBoxView : MonoBehaviour
    {
        // 목록 줄 칸 순서: 발신자, 제목, 보상 요약, 발신일, 만료. 우편에는 삭제 API가 없어 휴지통 버튼은 두지 않는다(씬에서 숨긴다)

        /****************************************
        *                Fields
        ****************************************/

        private const string PAGE_FORMAT = "{0} / {1}";
        private const string EMPTY_TEXT  = "받을 우편이 없습니다";

        [Header("창")]
        [SerializeField] private Button   _closeButton = null;
        [SerializeField] private TMP_Text _statusLabel = null;

        [Header("목록")]
        [SerializeField] private ListRowView[] _rows       = null;
        [SerializeField] private GameObject    _emptyMark  = null;
        [SerializeField] private Button        _prevButton = null;
        [SerializeField] private Button        _nextButton = null;
        [SerializeField] private TMP_Text      _pageLabel  = null;

        [Header("상세")]
        [SerializeField] private GameObject _detailPanel   = null;
        [SerializeField] private GameObject _detailHintMark = null;
        [SerializeField] private TMP_Text   _detailTitle   = null;
        [SerializeField] private TMP_Text   _detailSender  = null;
        [SerializeField] private TMP_Text   _detailDate    = null;
        [SerializeField] private TMP_Text   _detailBody    = null;
        [SerializeField] private Button     _claimButton   = null;

        private int _selected = -1;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen      => gameObject.activeSelf;

        // 한 쪽에 보일 수 있는 줄 수, 씬에 둔 줄 개수다
        public int  RowCapacity => (_rows != null) ? _rows.Length : 0;

        public event Action<int> RowSelected;
        public event Action      ClaimRequested;
        public event Action<int> PageDeltaRequested;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(OnCloseClicked);
            }

            if (_claimButton != null)
            {
                _claimButton.onClick.AddListener(OnClaimClicked);
            }

            if (_prevButton != null)
            {
                _prevButton.onClick.AddListener(OnPrev);
            }

            if (_nextButton != null)
            {
                _nextButton.onClick.AddListener(OnNext);
            }

            if (_rows != null)
            {
                for (int i = 0; i < _rows.Length; ++i)
                {
                    if (_rows[i] != null)
                    {
                        _rows[i].Index = i;
                        _rows[i].Clicked += OnRowClicked;
                    }
                }
            }

            ShowDetail(null, null, null, null);
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(OnCloseClicked);
            }

            if (_claimButton != null)
            {
                _claimButton.onClick.RemoveListener(OnClaimClicked);
            }

            if (_prevButton != null)
            {
                _prevButton.onClick.RemoveListener(OnPrev);
            }

            if (_nextButton != null)
            {
                _nextButton.onClick.RemoveListener(OnNext);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetOpen(bool open)
        {
            gameObject.SetActive(open);
        }

        public void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text ?? string.Empty;
            }
        }

        /// <summary> 한 쪽 분량의 목록. 비어 있으면 빈 상태 문구를 보인다 </summary>
        public void SetList(IReadOnlyList<ListRowData> rows, int pageIndex, int pageCount)
        {
            _selected = -1;
            ListRowData.Fill(_rows, rows, _selected);
            bool empty = (rows == null) || (rows.Count == 0);
            if (_emptyMark != null)
            {
                _emptyMark.SetActive(empty);
                if (_emptyMark.TryGetComponent(out TMP_Text label))
                {
                    label.text = EMPTY_TEXT;
                }
            }

            if (_pageLabel != null)
            {
                _pageLabel.text = string.Format(PAGE_FORMAT, pageIndex + 1, pageCount);
            }

            if (_prevButton != null)
            {
                _prevButton.interactable = pageIndex > 0;
            }

            if (_nextButton != null)
            {
                _nextButton.interactable = (pageIndex + 1) < pageCount;
            }
        }

        /// <summary> 고른 우편의 상세. 제목이 null이면 상세를 비운다 </summary>
        public void ShowDetail(string title, string sender, string date, string body)
        {
            bool has = title != null;
            if (_detailPanel != null)
            {
                _detailPanel.SetActive(has);
            }

            if (_detailHintMark != null)
            {
                _detailHintMark.SetActive(!has);
            }

            SetText(_detailTitle, title);
            SetText(_detailSender, sender);
            SetText(_detailDate, date);
            SetText(_detailBody, body);
            SetClaimInteractable(has);
        }

        public void SetClaimInteractable(bool interactable)
        {
            if (_claimButton != null)
            {
                _claimButton.interactable = interactable;
            }
        }

        public void SelectRow(int index)
        {
            _selected = index;
            if (_rows != null)
            {
                for (int i = 0; i < _rows.Length; ++i)
                {
                    if (_rows[i] != null)
                    {
                        _rows[i].SetSelected(i == index);
                    }
                }
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        private void OnRowClicked(ListRowView row)
        {
            SelectRow(row.Index);
            RowSelected?.Invoke(row.Index);
        }

        private void OnCloseClicked() { SetOpen(false); }
        private void OnClaimClicked() { ClaimRequested?.Invoke(); }
        private void OnPrev()         { PageDeltaRequested?.Invoke(-1); }
        private void OnNext()         { PageDeltaRequested?.Invoke(1); }
    }
}
