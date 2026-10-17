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

        private const int    TAB_COUNT          = 5;
        private const string WHISPER_USAGE_TEXT = "귓속말은 /w 이름 내용 으로 보냅니다.";

        [Header("연결")]
        [SerializeField] private HotkeyRouter   _router   = null;
        [SerializeField] private TMP_Text       _messages = null;
        [SerializeField] private TMP_InputField _input    = null;
        [SerializeField] private TMP_Dropdown   _channel  = null;

        [Header("탭 (전체, 파티, 시스템, 길드, 귓속말 순)")]
        [SerializeField] private Button[]     _tabs       = new Button[TAB_COUNT];
        [SerializeField] private TMP_Text[]   _tabLabels  = new TMP_Text[TAB_COUNT];
        [SerializeField] private GameObject[] _underlines = new GameObject[TAB_COUNT];

        [Header("색")]
        [SerializeField] private Color _partyColor         = new Color(0.561f, 0.827f, 1f, 1f);
        [SerializeField] private Color _systemColor        = new Color(0.914f, 0.788f, 0.478f, 1f);
        [SerializeField] private Color _guildColor         = new Color(0.463f, 0.902f, 0.741f, 1f);
        [SerializeField] private Color _whisperColor       = new Color(0.906f, 0.612f, 0.914f, 1f);
        [SerializeField] private Color _selectedTabColor   = new Color(0.02f, 0.03f, 0.05f, 0.55f);
        [SerializeField] private Color _unselectedTabColor = new Color(0.02f, 0.03f, 0.05f, 0f);
        [SerializeField] private Color _selectedLabelColor = new Color(0.92f, 0.94f, 0.97f, 1f);
        [SerializeField] private Color _labelColor         = new Color(0.58f, 0.62f, 0.68f, 1f);

        private readonly StringBuilder _buffer = new StringBuilder();

        private Func<bool>          _onOpen      = null;
        private UnityAction<string> _onSubmit    = null;
        private string              _partyHex    = null;
        private string              _systemHex   = null;
        private string              _guildHex    = null;
        private string              _whisperHex  = null;
        private EChatTab            _tab         = EChatTab.All;
        private int                 _submitFrame = -1;

        /****************************************
        *              Properties
        ****************************************/

        public EChatTab ActiveTab => _tab;

        // 켜져 있는 HUD 채팅 창. 상호작용 패널처럼 HUD 밖 프리팹이 귓속말 입력을 채울 때 쓴다. 마을, 던전 씬마다 하나다
        public static HudChatView Active { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _onOpen     = OpenInput;
            _onSubmit   = Submit;
            _partyHex   = ColorUtility.ToHtmlStringRGB(_partyColor);
            _systemHex  = ColorUtility.ToHtmlStringRGB(_systemColor);
            _guildHex   = ColorUtility.ToHtmlStringRGB(_guildColor);
            _whisperHex = ColorUtility.ToHtmlStringRGB(_whisperColor);
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Active = null;
        }

        private void OnEnable()
        {
            Active = this;
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
            if (Active == this)
            {
                Active = null;
            }

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

            // 시스템, 귓속말 탭은 보낼 채널이 아니라 입력 채널을 바꾸지 않는다
            if ((_channel != null) && (ChatViewRules.ChangesInputChannel(tab)))
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

        // 친구 창의 귓속말 버튼이 부른다. 다른 창이 열려 있어도 입력 칸에 "/w 이름 "을 채우고 포커스를 준다
        /// <summary> 귓속말 명령을 채운 채 입력을 연다 </summary>
        public void BeginWhisper(string characterName)
        {
            if ((_input == null) || (string.IsNullOrEmpty(characterName)))
            {
                return;
            }

            _input.text = ChatViewRules.WhisperPrefix(characterName);
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_input.gameObject);
            }

            _input.ActivateInputField();
            _input.caretPosition = _input.text.Length;
        }

        // 거절되면 쓴 문장을 남겨 고쳐 보낼 수 있게 한다. 빈 Enter는 입력만 닫는다
        private void Submit(string text)
        {
            _submitFrame = Time.frameCount;
            if ((!string.IsNullOrWhiteSpace(text)) && (Send(text) != EChatReject.None))
            {
                return;
            }

            _input.text = string.Empty;
            CloseInput();
        }

        private EChatReject Send(string text)
        {
            switch (ChatViewRules.TryParseCommand(text, out string target, out string body))
            {
                case EChatCommand.Whisper:
                    return ChatClient.SendWhisper(target, body);
                case EChatCommand.Reply:
                    return ChatClient.SendReply(body);
                case EChatCommand.Invalid:
                    ChatLog.AddSystem(WHISPER_USAGE_TEXT);
                    return EChatReject.WhisperTargetNotFound;
                default:
                    EChatChannel channel = (_channel != null) ? ChatViewRules.ToSendChannel(_channel.value) : EChatChannel.All;
                    return ChatClient.Send(channel, text);
            }
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
                    ChatViewRules.AppendLine(_buffer, in entry, null, _partyHex, _systemHex, _guildHex, _whisperHex);
                }
            }

            _messages.SetText(_buffer);
        }
    }
}
