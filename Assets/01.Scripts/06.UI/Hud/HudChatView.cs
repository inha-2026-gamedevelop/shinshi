// System
using System;
using System.Collections.Generic;
using System.Text;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Network;
using Shinshi.Systems;

namespace Shinshi.UI
{
    // 내용은 ChatLog가 바뀔 때만 다시 그린다. 서버가 확인해 돌려준 줄만 로그에 들어오므로 보낸 줄을 미리 그리지 않는다
    /// <summary> HUD 왼쪽 아래 채팅 창. 탭 거르기, 입력 칸 송신, Enter로 입력 시작을 맡는다 </summary>
    public sealed class HudChatView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int TAB_COUNT = 3;

        [Header("연결")]
        [SerializeField] private HotkeyRouter   _router   = null;
        [SerializeField] private TMP_Text       _messages = null;
        [SerializeField] private TMP_InputField _input    = null;
        [SerializeField] private TMP_Dropdown   _channel  = null;

        [Header("탭 (전체, 파티, 시스템 순)")]
        [SerializeField] private Button[]     _tabs       = new Button[TAB_COUNT];
        [SerializeField] private TMP_Text[]   _tabLabels  = new TMP_Text[TAB_COUNT];
        [SerializeField] private GameObject[] _underlines = new GameObject[TAB_COUNT];

        [Header("색")]
        [SerializeField] private Color _partyColor         = new Color(0.561f, 0.827f, 1f, 1f);
        [SerializeField] private Color _systemColor        = new Color(0.914f, 0.788f, 0.478f, 1f);
        [SerializeField] private Color _selectedTabColor   = new Color(0.02f, 0.03f, 0.05f, 0.55f);
        [SerializeField] private Color _unselectedTabColor = new Color(0.02f, 0.03f, 0.05f, 0f);
        [SerializeField] private Color _selectedLabelColor = new Color(0.92f, 0.94f, 0.97f, 1f);
        [SerializeField] private Color _labelColor         = new Color(0.58f, 0.62f, 0.68f, 1f);

        private readonly StringBuilder _buffer = new StringBuilder();

        private Func<bool>          _onOpen      = null;
        private UnityAction<string> _onSubmit    = null;
        private string              _partyHex    = null;
        private string              _systemHex   = null;
        private EChatTab            _tab         = EChatTab.All;
        private int                 _submitFrame = -1;

        /****************************************
        *              Properties
        ****************************************/

        public EChatTab ActiveTab => _tab;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _onOpen    = OpenInput;
            _onSubmit  = Submit;
            _partyHex  = ColorUtility.ToHtmlStringRGB(_partyColor);
            _systemHex = ColorUtility.ToHtmlStringRGB(_systemColor);
            for (int i = 0; i < _tabs.Length; ++i)
            {
                if (_tabs[i] == null)
                {
                    continue;
                }

                var tab = (EChatTab)i;
                _tabs[i].onClick.AddListener(() => SelectTab(tab));
            }
        }

        private void OnEnable()
        {
            ChatLog.Changed += Refresh;
            if (_input != null)
            {
                _input.onSubmit.AddListener(_onSubmit);
            }

            if (_router != null)
            {
                _router.Register(EHotkeyAction.Chat, _onOpen);
            }

            SelectTab(_tab);
        }

        private void OnDisable()
        {
            ChatLog.Changed -= Refresh;
            if (_input != null)
            {
                _input.onSubmit.RemoveListener(_onSubmit);
            }

            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.Chat, _onOpen);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SelectTab(EChatTab tab)
        {
            _tab = tab;
            for (int i = 0; i < TAB_COUNT; ++i)
            {
                bool selected = i == (int)tab;
                if ((i < _tabs.Length) && (_tabs[i] != null) && (_tabs[i].targetGraphic != null))
                {
                    _tabs[i].targetGraphic.color = selected ? _selectedTabColor : _unselectedTabColor;
                }

                if ((i < _tabLabels.Length) && (_tabLabels[i] != null))
                {
                    _tabLabels[i].color = selected ? _selectedLabelColor : _labelColor;
                }

                if ((i < _underlines.Length) && (_underlines[i] != null))
                {
                    _underlines[i].SetActive(selected);
                }
            }

            // 시스템 탭은 보낼 채널이 아니라 입력 채널을 바꾸지 않는다
            if ((_channel != null) && (tab != EChatTab.System))
            {
                _channel.SetValueWithoutNotify(ChatViewRules.ToDropdownIndex(tab));
            }

            Refresh();
        }

        /// <summary> 입력 칸에 포커스를 준다. 방금 보낸 Enter로 다시 열리지 않게 같은 프레임은 무시한다 </summary>
        public bool OpenInput()
        {
            if ((_input == null) || (Time.frameCount == _submitFrame) || (_input.isFocused))
            {
                return false;
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_input.gameObject);
            }

            _input.ActivateInputField();
            return true;
        }

        // 거절되면 쓴 문장을 남겨 고쳐 보낼 수 있게 한다. 빈 Enter는 입력만 닫는다
        private void Submit(string text)
        {
            _submitFrame = Time.frameCount;
            if (!string.IsNullOrWhiteSpace(text))
            {
                EChatChannel channel = (_channel != null) ? ChatViewRules.ToSendChannel(_channel.value) : EChatChannel.All;
                if (ChatClient.Send(channel, text) != EChatReject.None)
                {
                    return;
                }
            }

            _input.text = string.Empty;
            CloseInput();
        }

        private void CloseInput()
        {
            _input.DeactivateInputField();
            if ((EventSystem.current != null) && (EventSystem.current.currentSelectedGameObject == _input.gameObject))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void Refresh()
        {
            if (_messages == null)
            {
                return;
            }

            _buffer.Clear();
            IReadOnlyList<ChatEntry> entries = ChatLog.Entries;
            for (int i = 0; i < entries.Count; ++i)
            {
                ChatEntry entry = entries[i];
                if (ChatViewRules.Matches(_tab, entry.Channel))
                {
                    ChatViewRules.AppendLine(_buffer, in entry, null, _partyHex, _systemHex);
                }
            }

            _messages.SetText(_buffer);
        }
    }
}
