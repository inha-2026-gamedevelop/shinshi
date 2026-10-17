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
    /// <summary> 길드 창. 길드가 없으면 만들기, 가입 신청, 받은 초대를, 있으면 길드원, 가입 신청 탭과 초대, 탈퇴를 보여 준다 </summary>
    public sealed class GuildView : MonoBehaviour
    {
        // 네트워크는 모르고 이벤트로 의도만 알린다. 두 상태의 목록은 같은 줄 칸을 쓰고 글자와 버튼은 GuildBinder가 정한다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Button   _closeButton = null;
        [SerializeField] private TMP_Text _titleLabel  = null;
        [SerializeField] private TMP_Text _countLabel  = null;

        [Header("길드 없음")]
        [SerializeField] private GameObject     _noGuildGroup = null;
        [SerializeField] private TMP_InputField _createInput  = null;
        [SerializeField] private Button         _createButton = null;
        [SerializeField] private TMP_InputField _applyInput   = null;
        [SerializeField] private Button         _applyButton  = null;

        [Header("길드 있음 (길드원, 가입 신청 탭)")]
        [SerializeField] private GameObject     _guildGroup   = null;
        [SerializeField] private Button[]       _tabButtons   = null;
        [SerializeField] private TMP_Text[]     _tabLabels    = null;
        [SerializeField] private GameObject[]   _tabSelected  = null;
        [SerializeField] private GameObject     _leaderGroup  = null;
        [SerializeField] private TMP_InputField _inviteInput  = null;
        [SerializeField] private Button         _inviteButton = null;
        [SerializeField] private Button         _leaveButton  = null;

        [Header("목록")]
        [SerializeField] private TMP_Text        _listTitle  = null;
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

        private UnityAction[] _tabHandlers;
        private Action<int>[] _rowHandlers;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen   => gameObject.activeSelf;
        public int  RowCount => (_rows == null) ? 0 : _rows.Length;

        public event Action           CloseRequested;
        public event Action<string>   CreateSubmitted;
        public event Action<string>   ApplySubmitted;
        public event Action<string>   InviteSubmitted;
        public event Action           LeaveRequested;
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
            AddListener(_closeButton, OnCloseClicked);
            AddListener(_createButton, OnCreateClicked);
            AddListener(_applyButton, OnApplyClicked);
            AddListener(_inviteButton, OnInviteClicked);
            AddListener(_leaveButton, OnLeaveClicked);
            AddListener(_prevPage, OnPrevPage);
            AddListener(_nextPage, OnNextPage);
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
            RemoveListener(_createButton, OnCreateClicked);
            RemoveListener(_applyButton, OnApplyClicked);
            RemoveListener(_inviteButton, OnInviteClicked);
            RemoveListener(_leaveButton, OnLeaveClicked);
            RemoveListener(_prevPage, OnPrevPage);
            RemoveListener(_nextPage, OnNextPage);
            RemoveListener(_confirmAccept, OnConfirmAccept);
            RemoveListener(_confirmCancel, OnConfirmCancel);
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

        public void SetOpen(bool open)
        {
            gameObject.SetActive(open);
        }

        public void SetHeader(string title, string count)
        {
            SetText(_titleLabel, title);
            SetText(_countLabel, count);
        }

        /// <summary> 길드 없음, 있음 화면을 바꾼다. 길드장 도구(초대, 가입 신청 탭)는 길드장에게만 보인다 </summary>
        public void SetMode(EGuildWindowMode mode, bool showLeaderTools)
        {
            bool hasGuild    = mode == EGuildWindowMode.Guild;
            bool leaderTools = (hasGuild) && (showLeaderTools);
            SetActive(_noGuildGroup, !hasGuild);
            SetActive(_guildGroup, hasGuild);
            SetActive(_leaderGroup, leaderTools);
            Button applications = Pick(_tabButtons, GuildViewRules.TAB_APPLICATIONS);
            SetActive((applications != null) ? applications.gameObject : null, leaderTools);
        }

        public void SetTab(int tab, string label, bool selected)
        {
            SetText(Pick(_tabLabels, tab), label);
            SetActive(Pick(_tabSelected, tab), selected);
        }

        public void SetListTitle(string title)
        {
            SetText(_listTitle, title);
            SetActive((_listTitle != null) ? _listTitle.gameObject : null, !string.IsNullOrEmpty(title));
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

        /// <summary> 쓰기 버튼과 입력 칸을 함께 잠그거나 푼다 </summary>
        public void SetWritable(bool writable)
        {
            SetInteractable(_createButton, writable);
            SetInteractable(_applyButton, writable);
            SetInteractable(_inviteButton, writable);
            SetInteractable(_leaveButton, writable);
            SetInputInteractable(_createInput, writable);
            SetInputInteractable(_applyInput, writable);
            SetInputInteractable(_inviteInput, writable);
        }

        public void ClearInputs()
        {
            ClearInput(_createInput);
            ClearInput(_applyInput);
            ClearInput(_inviteInput);
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

        private static void SetInputInteractable(TMP_InputField input, bool interactable)
        {
            if (input != null)
            {
                input.interactable = interactable;
            }
        }

        private static void ClearInput(TMP_InputField input)
        {
            if (input != null)
            {
                input.text = string.Empty;
            }
        }

        private static string Read(TMP_InputField input)
        {
            return (input != null) ? input.text : string.Empty;
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

        private void OnCreateClicked()  { CreateSubmitted?.Invoke(Read(_createInput)); }
        private void OnApplyClicked()   { ApplySubmitted?.Invoke(Read(_applyInput)); }
        private void OnInviteClicked()  { InviteSubmitted?.Invoke(Read(_inviteInput)); }
        private void OnLeaveClicked()   { LeaveRequested?.Invoke(); }
        private void OnCloseClicked()   { CloseRequested?.Invoke(); }
        private void OnPrevPage()       { PageChanged?.Invoke(-1); }
        private void OnNextPage()       { PageChanged?.Invoke(1); }
        private void OnConfirmAccept()  { ConfirmAccepted?.Invoke(); }
        private void OnConfirmCancel()  { ConfirmCancelled?.Invoke(); }
    }
}
