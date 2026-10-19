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

        private const float EXPANDED_WIDTH  = 1560f;
        private const float EXPANDED_HEIGHT = 840f;
        private const int   PARTNER_ROWS    = 9;

        // 바닥에서 이만큼(정규화) 안이면 새 줄이 올 때 따라 내려간다
        private const float FOLLOW_EPSILON  = 0.02f;

        public const string INVITE_TOWN_ONLY_TEXT = "파티 초대는 마을에서만 보낼 수 있습니다.";
        public const string EXPAND_BUTTON_NAME    = "ChatExpandButton";
        private const float EXPAND_LABEL_INSET    = 2f;
        private const float EXPAND_BUTTON_GAP     = 4f;
        public const string VIEWPORT_NAME         = "ChatMessageViewport";

        private struct SavedRect
        {
            public Transform Target;
            public Transform Parent;
            public int       Sibling;
            public bool      Active;
            public Vector2   Min;
            public Vector2   Max;
            public Vector2   Pivot;
            public Vector2   Position;
            public Vector2   Size;
        }

        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly List<SavedRect> _saved = new List<SavedRect>();
        private readonly List<Behaviour> _disabledLayouts = new List<Behaviour>();
        private readonly List<string>    _partners = new List<string>();
        private readonly List<Button>    _partnerRows = new List<Button>();

        private RectTransform           _expandedRoot = null;
        private TMP_InputField          _search = null;
        private TMP_Text                _partnerHeader = null;
        private TMP_Text                _infoName = null;
        private TMP_Text                _infoLast = null;
        private TMP_Text                _targetChip = null;
        private Button                  _whisperButton = null;
        private Button                  _inviteButton = null;
        private Button                  _headerInvite = null;
        private string                  _partner = null;
        private TextWrappingModes       _savedWrap;
        private TextAlignmentOptions    _savedAlignment;
        private float                   _savedFontSize;
        private ScrollRect              _scroll = null;
        private RectTransform           _scrollContent = null;
        private Button                  _expandButton = null;
        private Func<bool>              _onCloseExpanded = null;
        private bool                    _followLatest = true;

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

        public bool IsExpanded { get; private set; }

        /// <summary> 큰 창에서 고른 상대 이름. 없으면 빈 값이다 </summary>
        public string SelectedPartner => _partner;

        // 켜져 있는 HUD 채팅 창. 상호작용 패널처럼 HUD 밖 프리팹이 귓속말 입력을 채울 때 쓴다. 마을, 던전 씬마다 하나다
        public static HudChatView Active { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            EnsureModernLayout();
            _onOpen          = OpenInput;
            _onSubmit        = Submit;
            _onCloseExpanded = CloseExpandedByKey;
            EnsureExpandButton();
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
                _router.Unregister(EHotkeyAction.CloseWindow, _onCloseExpanded);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>어두운 채팅 바탕과 탭·입력 색을 한 번만 맞춘다. 송신과 포커스 동작은 그대로다</summary>
        public void EnsureModernLayout()
        {
            if (!HudModernStyle.AddBackdrop(transform, UiModernDark.SPRITE_INSET, Color.white, 6f))
            {
                return;
            }

            _selectedLabelColor = UiModernDark.TextGold;
            _labelColor         = UiModernDark.TextMuted;
            _selectedTabColor   = new Color(UiModernDark.Gold.r, UiModernDark.Gold.g, UiModernDark.Gold.b, 0.18f);
            if (_messages != null)
            {
                _messages.color = UiModernDark.TextMain;
            }

            if (_input != null)
            {
                ModernDarkKit.StyleInset(_input.targetGraphic);
            }

            for (int i = 0; i < _underlines.Length; ++i)
            {
                if (_underlines[i] == null)
                {
                    continue;
                }

                Graphic line = _underlines[i].GetComponent<Graphic>();
                if (line != null)
                {
                    line.color = UiModernDark.Gold;
                }
            }
        }

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
            _submitFrame   = Time.frameCount;
            _followLatest = true;
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

        /// <summary> 큰 채팅 창으로 바꾸거나 HUD 작은 창으로 되돌린다. 메시지·입력·탭·전송 규칙은 그대로 쓰고 위치만 옮긴다 </summary>
        public void SetExpanded(bool expanded)
        {
            if (expanded == IsExpanded)
            {
                return;
            }

            if (expanded)
            {
                EnterExpanded();
            }
            else
            {
                LeaveExpanded();
            }

            Refresh();
        }

        private void EnterExpanded()
        {
            if ((_messages == null) || (_input == null))
            {
                return;
            }

            if (_expandedRoot == null)
            {
                BuildExpandedRoot();
            }

            IsExpanded = true;
            _expandedRoot.gameObject.SetActive(true);
            _expandedRoot.SetAsLastSibling();

            Remember(_messages.transform);
            Remember(_input.transform);
            for (int i = 0; i < _tabs.Length; ++i)
            {
                if (_tabs[i] != null)
                {
                    Remember(_tabs[i].transform);
                }
            }

            _savedWrap      = _messages.textWrappingMode;
            _savedAlignment = _messages.alignment;
            _savedFontSize  = _messages.fontSize;
            DisableLayoutOf(_messages.gameObject);
            DisableLayoutOf(_input.gameObject);

            _messages.transform.SetParent(_scrollContent, false);
            _input.transform.SetParent(_expandedRoot, false);
            for (int i = 0; i < _tabs.Length; ++i)
            {
                if (_tabs[i] != null)
                {
                    _tabs[i].transform.SetParent(_expandedRoot, false);
                }
            }

            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                if (child != _expandedRoot)
                {
                    Remember(child);
                    child.gameObject.SetActive(false);
                }
            }

            Remember(transform);
            var window = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>(true);
            if ((canvas != null) && (canvas.rootCanvas.transform != transform.parent))
            {
                window.SetParent(canvas.rootCanvas.transform, false);
                window.SetAsLastSibling();
            }

            window.anchorMin       = new Vector2(0.5f, 0.5f);
            window.anchorMax        = new Vector2(0.5f, 0.5f);
            window.pivot            = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta        = new Vector2(EXPANDED_WIDTH, EXPANDED_HEIGHT);

            _messages.textWrappingMode = TextWrappingModes.Normal;
            _messages.alignment        = TextAlignmentOptions.TopLeft;
            _messages.fontSize         = 24f;
            var messageRect = (RectTransform)_messages.transform;
            messageRect.anchorMin        = new Vector2(0f, 1f);
            messageRect.anchorMax        = new Vector2(1f, 1f);
            messageRect.pivot            = new Vector2(0.5f, 1f);
            messageRect.anchoredPosition = Vector2.zero;
            messageRect.sizeDelta        = new Vector2(0f, 0f);
            messageRect.localScale       = Vector3.one;
            PlaceTopLeft(_input.transform, 244f, 748f, 1136f, 64f);

            if (_router != null)
            {
                _router.Register(EHotkeyAction.CloseWindow, _onCloseExpanded);
            }

            _followLatest = true;
            int slot = 0;
            for (int i = 0; i < _tabs.Length; ++i)
            {
                if (_tabs[i] == null)
                {
                    continue;
                }

                bool visible = (EChatTab)i != EChatTab.System;
                _tabs[i].gameObject.SetActive(visible);
                if (visible)
                {
                    PlaceTopLeft(_tabs[i].transform, 24f + (slot * 140f), 74f, 132f, 44f);
                    ++slot;
                }
            }

            if (_tab == EChatTab.System)
            {
                SelectTab(EChatTab.All);
            }
        }

        private void LeaveExpanded()
        {
            if (!IsExpanded)
            {
                return;
            }

            IsExpanded = false;
            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onCloseExpanded);
            }

            // 같은 부모 안에서 원래 형제 순서가 맞도록 앞 번호부터 되돌린다
            _saved.Sort((a, b) => a.Sibling.CompareTo(b.Sibling));
            for (int i = 0; i < _saved.Count; ++i)
            {
                SavedRect s = _saved[i];
                if (s.Target == null)
                {
                    continue;
                }

                s.Target.SetParent(s.Parent, false);
                s.Target.SetSiblingIndex(s.Sibling);
                if (s.Target is RectTransform rect)
                {
                    rect.anchorMin        = s.Min;
                    rect.anchorMax        = s.Max;
                    rect.pivot            = s.Pivot;
                    rect.anchoredPosition = s.Position;
                    rect.sizeDelta        = s.Size;
                }

                s.Target.gameObject.SetActive(s.Active);
            }

            _saved.Clear();
            for (int i = 0; i < _disabledLayouts.Count; ++i)
            {
                if (_disabledLayouts[i] != null)
                {
                    _disabledLayouts[i].enabled = true;
                }
            }

            _disabledLayouts.Clear();
            _messages.textWrappingMode = _savedWrap;
            _messages.alignment        = _savedAlignment;
            _messages.fontSize         = _savedFontSize;
            _expandedRoot.gameObject.SetActive(false);
            SelectTab(_tab);
        }

        private void Remember(Transform target)
        {
            for (int i = 0; i < _saved.Count; ++i)
            {
                if (_saved[i].Target == target)
                {
                    return;
                }
            }

            var s = new SavedRect { Target = target, Parent = target.parent, Sibling = target.GetSiblingIndex(), Active = target.gameObject.activeSelf };
            if (target is RectTransform rect)
            {
                s.Min      = rect.anchorMin;
                s.Max      = rect.anchorMax;
                s.Pivot    = rect.pivot;
                s.Position = rect.anchoredPosition;
                s.Size     = rect.sizeDelta;
            }

            _saved.Add(s);
        }

        private void DisableLayoutOf(GameObject target)
        {
            foreach (LayoutGroup group in target.GetComponents<LayoutGroup>())
            {
                if (group.enabled)
                {
                    group.enabled = false;
                    _disabledLayouts.Add(group);
                }
            }

            foreach (ContentSizeFitter fitter in target.GetComponents<ContentSizeFitter>())
            {
                if (fitter.enabled)
                {
                    fitter.enabled = false;
                    _disabledLayouts.Add(fitter);
                }
            }
        }

        private void BuildExpandedRoot()
        {
            var rootObject = new GameObject("ChatExpandedRoot", typeof(RectTransform));
            rootObject.layer = gameObject.layer;
            _expandedRoot = (RectTransform)rootObject.transform;
            _expandedRoot.SetParent(transform, false);
            _expandedRoot.anchorMin = Vector2.zero;
            _expandedRoot.anchorMax = Vector2.one;
            _expandedRoot.offsetMin = Vector2.zero;
            _expandedRoot.offsetMax = Vector2.zero;

            AddPanel("Frame", UiModernDark.SPRITE_PANEL, 0f, 0f, EXPANDED_WIDTH, EXPANDED_HEIGHT);
            AddText("Title", "채팅 · 귓속말", 28f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 0f, 8f, EXPANDED_WIDTH, 48f);
            AddPanel("ListBox", UiModernDark.SPRITE_INSET, 24f, 130f, 330f, 596f);
            AddPanel("ChatBox", UiModernDark.SPRITE_INSET, 370f, 130f, 800f, 596f);
            AddPanel("InfoBox", UiModernDark.SPRITE_INSET, 1186f, 130f, 350f, 596f);
            AddPanel("ChatLine", null, 386f, 198f, 768f, 1f).color = UiModernDark.GoldDim;

            _partnerHeader = AddText("PartnerHeader", string.Empty, 26f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, 394f, 142f, 520f, 44f);
            AddText("InfoHead", "상대 정보", 24f, UiModernDark.TextGold, TextAlignmentOptions.Left, true, 1206f, 142f, 310f, 44f);
            _infoName = AddText("InfoName", string.Empty, 26f, UiModernDark.TextMain, TextAlignmentOptions.Left, true, 1206f, 206f, 310f, 44f);
            _infoLast = AddText("InfoLast", string.Empty, 20f, UiModernDark.TextMuted, TextAlignmentOptions.TopLeft, false, 1206f, 258f, 310f, 120f);
            _infoLast.textWrappingMode = TextWrappingModes.Normal;
            BuildMessageViewport();
            _targetChip = AddText("TargetChip", string.Empty, 22f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 24f, 756f, 208f, 48f);

            _whisperButton = AddButton("WhisperButton", "귓속말", false, 1206f, 400f, 310f, 52f, () => BeginWhisper(_partner));
            _inviteButton  = AddButton("InviteButton", "파티 초대", true, 1206f, 464f, 310f, 52f, InvitePartner);
            _headerInvite  = AddButton("HeaderInvite", "파티 초대", false, 1030f, 142f, 124f, 44f, InvitePartner);
            AddButton("SendButton", "전송", true, 1396f, 748f, 140f, 64f, SubmitInput);
            AddButton("CloseButton", "X", false, EXPANDED_WIDTH - 68f, 10f, 44f, 44f, () => SetExpanded(false));

            BuildSearch();
            for (int i = 0; i < PARTNER_ROWS; ++i)
            {
                int index = i;
                Button row = AddButton("Partner" + i, string.Empty, false, 40f, 210f + (i * 56f), 298f, 52f, () => SelectPartner(index));
                row.GetComponentInChildren<TMP_Text>(true).alignment = TextAlignmentOptions.Left;
                _partnerRows.Add(row);
            }
        }

        // 메시지는 마스크가 있는 세로 스크롤 안에 놓는다. 줄이 넘쳐도 상자 밖으로 그려지지 않고 위로 거슬러 볼 수 있다
        private void BuildMessageViewport()
        {
            var host = new GameObject(VIEWPORT_NAME, typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            host.layer = gameObject.layer;
            host.transform.SetParent(_expandedRoot, false);
            PlaceTopLeft(host.transform, 394f, 214f, 764f, 496f);

            // 마우스 휠과 드래그를 받으려면 레이캐스트되는 그래픽이 필요하다
            var hit = host.AddComponent<Image>();
            hit.color         = Color.clear;
            hit.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.layer = gameObject.layer;
            contentObject.transform.SetParent(host.transform, false);
            _scrollContent = (RectTransform)contentObject.transform;
            _scrollContent.anchorMin        = new Vector2(0f, 1f);
            _scrollContent.anchorMax        = new Vector2(1f, 1f);
            _scrollContent.pivot            = new Vector2(0.5f, 1f);
            _scrollContent.anchoredPosition = Vector2.zero;
            _scrollContent.sizeDelta        = Vector2.zero;

            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll                     = host.GetComponent<ScrollRect>();
            _scroll.viewport            = (RectTransform)host.transform;
            _scroll.content             = _scrollContent;
            _scroll.horizontal          = false;
            _scroll.vertical            = true;
            _scroll.movementType        = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity   = 40f;
            _scroll.onValueChanged.AddListener(OnScrolled);
        }

        // 사용자가 바닥에서 올라가 읽는 중이면 새 줄이 와도 위치를 지키고, 바닥으로 돌아오면 다시 따라간다
        private void OnScrolled(Vector2 position)
        {
            _followLatest = position.y <= FOLLOW_EPSILON;
        }

        /// <summary> 큰 창 메시지 목록을 가장 최근 줄로 내린다 </summary>
        public void ScrollToLatest()
        {
            _followLatest = true;
            ApplyScrollFollow();
        }

        private void ApplyScrollFollow()
        {
            if ((!IsExpanded) || (_scroll == null) || (!_followLatest))
            {
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
            _scroll.verticalNormalizedPosition = 0f;
            _followLatest = true;
        }

        private void EnsureExpandButton()
        {
            if ((_expandButton != null) || (_messages == null) || (transform.Find(EXPAND_BUTTON_NAME) != null))
            {
                return;
            }

            _expandButton = AddButtonTo(transform, EXPAND_BUTTON_NAME, "확대", false, 0f, 0f, 64f, 28f, () => SetExpanded(true), EXPAND_LABEL_INSET);
            var rect = (RectTransform)_expandButton.transform;
            rect.anchorMin        = new Vector2(1f, 1f);
            rect.anchorMax        = new Vector2(1f, 1f);
            rect.pivot            = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(0f, EXPAND_BUTTON_GAP);
            _expandButton.GetComponentInChildren<TMP_Text>(true).fontSize = 16f;
        }

        private bool CloseExpandedByKey()
        {
            if (!IsExpanded)
            {
                return false;
            }

            SetExpanded(false);
            return true;
        }

        private void BuildSearch()
        {
            var go = new GameObject("PartnerSearch", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gameObject.layer;
            go.SetActive(false);
            go.transform.SetParent(_expandedRoot, false);
            PlaceTopLeft(go.transform, 40f, 144f, 298f, 52f);
            ModernDarkKit.StyleInset(go.GetComponent<Image>());

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.layer = gameObject.layer;
            viewport.transform.SetParent(go.transform, false);
            Stretch((RectTransform)viewport.transform, 12f);

            TMP_Text text        = NewText(viewport.transform, "Text", string.Empty, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left, false);
            TMP_Text placeholder = NewText(viewport.transform, "Placeholder", "이름 검색", 22f, UiModernDark.TextMuted, TextAlignmentOptions.Left, false);
            Stretch((RectTransform)text.transform, 0f);
            Stretch((RectTransform)placeholder.transform, 0f);

            _search               = go.AddComponent<TMP_InputField>();
            _search.textViewport  = (RectTransform)viewport.transform;
            _search.textComponent = text;
            _search.placeholder   = placeholder;
            _search.onValueChanged.AddListener(_ => RefreshExpanded());
            go.SetActive(true);
        }

        private void RefreshExpanded()
        {
            if (!IsExpanded)
            {
                return;
            }

            string filter = (_search != null) ? _search.text : string.Empty;
            _partners.Clear();
            IReadOnlyList<ChatEntry> entries = ChatLog.Entries;
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                ChatEntry entry = entries[i];
                if ((entry.Channel == EChatChannel.System) || string.IsNullOrEmpty(entry.Sender) || _partners.Contains(entry.Sender))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(filter) || (entry.Sender.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _partners.Add(entry.Sender);
                }
            }

            if ((_partner == null) && (_partners.Count > 0))
            {
                _partner = _partners[0];
            }

            for (int i = 0; i < _partnerRows.Count; ++i)
            {
                bool has = i < _partners.Count;
                _partnerRows[i].gameObject.SetActive(has);
                if (has)
                {
                    _partnerRows[i].GetComponentInChildren<TMP_Text>(true).text = _partners[i];
                }
            }

            string last = string.Empty;
            for (int i = entries.Count - 1; (i >= 0) && (_partner != null); --i)
            {
                if (entries[i].Sender == _partner)
                {
                    last = entries[i].Text;
                    break;
                }
            }

            bool hasPartner = !string.IsNullOrEmpty(_partner);
            _partnerHeader.text = hasPartner ? _partner : string.Empty;
            _infoName.text      = hasPartner ? _partner : string.Empty;
            _infoLast.text      = hasPartner ? "최근 대화\n" + last : string.Empty;
            _whisperButton.interactable = hasPartner;
            _inviteButton.interactable  = hasPartner;
            _headerInvite.interactable  = hasPartner;
            _targetChip.text = ((_tab == EChatTab.Whisper) && hasPartner) ? "귓속말 · " + _partner : TabName(_tab);
        }

        private void SelectPartner(int index)
        {
            if ((index < 0) || (index >= _partners.Count))
            {
                return;
            }

            _partner = _partners[index];
            BeginWhisper(_partner);
            RefreshExpanded();
        }

        /// <summary> 마을에 접속해 있고 상대가 있을 때만 파티 초대를 보낼 수 있다. 던전에서는 마을 메시지가 등록돼 있지 않아 접속이 끊긴다 </summary>
        public static bool CanInvite(bool isTown, string partner)
        {
            return isTown && !string.IsNullOrEmpty(partner);
        }

        /// <summary> 두 초대 단추가 함께 쓴다. 보내지 못하면 안내만 남기고 거짓을 돌려준다 </summary>
        public bool TryInvitePartner()
        {
            bool isTown = ClientRoles.IsTown();
            if (!CanInvite(isTown, _partner))
            {
                if (!isTown && !string.IsNullOrEmpty(_partner))
                {
                    ChatLog.AddSystem(INVITE_TOWN_ONLY_TEXT);
                }

                return false;
            }

            return TownPartyClient.Invite(_partner);
        }

        /// <summary> 큰 창의 대화 상대를 이름으로 고른다 </summary>
        public void SelectPartnerName(string characterName)
        {
            _partner = string.IsNullOrEmpty(characterName) ? null : characterName;
            RefreshExpanded();
        }

        private void InvitePartner()
        {
            TryInvitePartner();
        }

        private void SubmitInput()
        {
            if (_input != null)
            {
                Submit(_input.text);
            }
        }

        private static string TabName(EChatTab tab)
        {
            switch (tab)
            {
                case EChatTab.Party:   return "파티";
                case EChatTab.Guild:   return "길드";
                case EChatTab.Whisper: return "귓속말";
                default:               return "전체";
            }
        }

        private Image AddPanel(string name, string sprite, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gameObject.layer;
            go.transform.SetParent(_expandedRoot, false);
            PlaceTopLeft(go.transform, x, y, width, height);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            if (sprite != null)
            {
                ModernDarkKit.StyleInset(image, sprite);
            }

            return image;
        }

        private TMP_Text AddText(string name, string text, float size, Color color, TextAlignmentOptions alignment, bool bold,
            float x, float y, float width, float height)
        {
            TMP_Text label = NewText(_expandedRoot, name, text, size, color, alignment, bold);
            PlaceTopLeft(label.transform, x, y, width, height);
            return label;
        }

        private TMP_Text NewText(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions alignment, bool bold)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            if (_messages != null)
            {
                label.font = _messages.font;
            }

            label.text             = text;
            label.fontSize         = size;
            label.color            = color;
            label.alignment        = alignment;
            label.fontStyle        = bold ? FontStyles.Bold : FontStyles.Normal;
            label.raycastTarget    = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode     = TextOverflowModes.Ellipsis;
            return label;
        }

        private Button AddButton(string name, string text, bool primary, float x, float y, float width, float height, UnityAction onClick)
        {
            return AddButtonTo(_expandedRoot, name, text, primary, x, y, width, height, onClick);
        }

        private Button AddButtonTo(Transform parent, string name, string text, bool primary, float x, float y, float width, float height, UnityAction onClick, float labelInset = 8f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            PlaceTopLeft(go.transform, x, y, width, height);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            button.onClick.AddListener(onClick);

            TMP_Text label = NewText(go.transform, "Label", text, 22f, UiModernDark.TextMain, TextAlignmentOptions.Center, false);
            Stretch((RectTransform)label.transform, labelInset);
            ModernDarkKit.StyleButton(button, primary);
            return button;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void PlaceTopLeft(Transform target, float x, float y, float width, float height)
        {
            if (!(target is RectTransform rect))
            {
                return;
            }

            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(0f, 1f);
            rect.pivot            = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta        = new Vector2(width, height);
            rect.localScale       = Vector3.one;
        }

        private void Refresh()
        {
            RefreshExpanded();
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
            ApplyScrollFollow();
        }
    }
}
