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
    /// <summary> 친구 창. 이름 입력과 친구 요청, 친구, 받은 요청, 보낸 요청 탭, 쪽 넘김 목록, 상태 줄, 확인 대화상자를 보여 준다 </summary>
    public sealed class FriendsView : MonoBehaviour
    {
        // 네트워크는 모르고 이벤트로 의도만 알린다. 줄의 글자와 버튼은 FriendsBinder가 탭마다 정해 넣는다

        /****************************************
        *                Fields
        ****************************************/

        public const float WINDOW_WIDTH  = 960f;
        public const float WINDOW_HEIGHT = 680f;

        // 장면에 놓인 창이 옛 크기를 덮어쓰고 있어도 프리팹의 배치 크기로 맞춘다. 0이면 건드리지 않는다
        [SerializeField] private Vector2 _windowSize = Vector2.zero;

        [SerializeField] private Button   _closeButton = null;
        [SerializeField] private TMP_Text _countLabel  = null;

        [Header("친구 요청")]
        [SerializeField] private TMP_InputField _nameInput     = null;
        [SerializeField] private Button         _requestButton = null;

        [Header("탭 (친구, 받은 요청, 보낸 요청)")]
        [SerializeField] private Button[]     _tabButtons  = null;
        [SerializeField] private TMP_Text[]   _tabLabels   = null;
        [SerializeField] private GameObject[] _tabSelected = null;

        [Header("목록")]
        [SerializeField] private SocialRowView[] _rows       = null;
        [SerializeField] private TMP_Text        _emptyLabel = null;
        [SerializeField] private Button          _prevPage   = null;
        [SerializeField] private Button          _nextPage   = null;
        [SerializeField] private TMP_Text        _pageLabel  = null;

        [Header("아래 줄")]
        [SerializeField] private TMP_Text _statusLabel = null;

        [Header("확인")]
        [SerializeField] private GameObject _confirmPanel  = null;
        [SerializeField] private TMP_Text   _confirmLabel  = null;
        [SerializeField] private Button     _confirmAccept = null;
        [SerializeField] private Button     _confirmCancel = null;

        private UnityAction[]  _tabHandlers;
        private Action<int>[]  _rowHandlers;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen   => gameObject.activeSelf;
        public int  RowCount => (_rows == null) ? 0 : _rows.Length;

        public event Action           CloseRequested;
        public event Action<string>   RequestSubmitted;
        public event Action<int>      TabSelected;
        public event Action<int, int> RowAction;
        public event Action<int>      PageChanged;
        public event Action           ConfirmAccepted;
        public event Action           ConfirmCancelled;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            ApplyWindowSize();

            AddListener(_closeButton, OnCloseClicked);
            AddListener(_requestButton, OnRequestClicked);
            AddListener(_prevPage, OnPrevPage);
            AddListener(_nextPage, OnNextPage);
            AddListener(_confirmAccept, OnConfirmAccept);
            AddListener(_confirmCancel, OnConfirmCancel);
            if (_nameInput != null)
            {
                _nameInput.onSubmit.AddListener(OnNameSubmitted);
            }

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

            if (_rows != null)
            {
                _rowHandlers = new Action<int>[_rows.Length];
                for (int i = 0; i < _rows.Length; ++i)
                {
                    int row = i;
                    _rowHandlers[i] = button => RowAction?.Invoke(row, button);
                    if (_rows[i] != null)
                    {
                        _rows[i].ButtonClicked += _rowHandlers[i];
                    }
                }
            }

            ShowConfirm(null);
        }

        private void OnDestroy()
        {
            RemoveListener(_closeButton, OnCloseClicked);
            RemoveListener(_requestButton, OnRequestClicked);
            RemoveListener(_prevPage, OnPrevPage);
            RemoveListener(_nextPage, OnNextPage);
            RemoveListener(_confirmAccept, OnConfirmAccept);
            RemoveListener(_confirmCancel, OnConfirmCancel);
            if (_nameInput != null)
            {
                _nameInput.onSubmit.RemoveListener(OnNameSubmitted);
            }

            if ((_tabButtons != null) && (_tabHandlers != null))
            {
                for (int i = 0; i < _tabButtons.Length; ++i)
                {
                    RemoveListener(_tabButtons[i], _tabHandlers[i]);
                }
            }

            if ((_rows != null) && (_rowHandlers != null))
            {
                for (int i = 0; i < _rows.Length; ++i)
                {
                    if (_rows[i] != null)
                    {
                        _rows[i].ButtonClicked -= _rowHandlers[i];
                    }
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void ApplyWindowSize()
        {
            if ((_windowSize.x > 0f) && (_windowSize.y > 0f))
            {
                ((RectTransform)transform).sizeDelta = _windowSize;
            }
        }

        public void SetOpen(bool open)
        {
            gameObject.SetActive(open);
        }

        public void SetCount(string text)
        {
            SetText(_countLabel, text);
        }

        public void SetTab(int tab, string label, bool selected)
        {
            SetText(Pick(_tabLabels, tab), label);
            SetActive(Pick(_tabSelected, tab), selected);
        }

        public SocialRowView Row(int index)
        {
            return Pick(_rows, index);
        }

        public void SetEmpty(string text)
        {
            SetText(_emptyLabel, text);
            SetActive((_emptyLabel != null) ? _emptyLabel.gameObject : null, !string.IsNullOrEmpty(text));
        }

        public void SetPage(string text, bool canPrev, bool canNext)
        {
            SetText(_pageLabel, text);
            SetInteractable(_prevPage, canPrev);
            SetInteractable(_nextPage, canNext);
        }

        public void SetRequestInteractable(bool interactable)
        {
            SetInteractable(_requestButton, interactable);
            if (_nameInput != null)
            {
                _nameInput.interactable = interactable;
            }
        }

        public void ClearNameInput()
        {
            if (_nameInput != null)
            {
                _nameInput.text = string.Empty;
            }
        }

        public void SetStatus(string status)
        {
            SetText(_statusLabel, status);
        }

        /// <summary> 확인 대화상자를 글자와 함께 띄운다, 비우면 닫는다 </summary>
        public void ShowConfirm(string text)
        {
            SetText(_confirmLabel, text);
            SetActive(_confirmPanel, !string.IsNullOrEmpty(text));
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

        private void OnRequestClicked()
        {
            RequestSubmitted?.Invoke((_nameInput != null) ? _nameInput.text : string.Empty);
        }

        private void OnNameSubmitted(string text)
        {
            RequestSubmitted?.Invoke(text);
        }

        private void OnCloseClicked()   { CloseRequested?.Invoke(); }
        private void OnPrevPage()       { PageChanged?.Invoke(-1); }
        private void OnNextPage()       { PageChanged?.Invoke(1); }
        private void OnConfirmAccept()  { ConfirmAccepted?.Invoke(); }
        private void OnConfirmCancel()  { ConfirmCancelled?.Invoke(); }
    }
}
