// System
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> Party recruit window, lists rooms from TownPartyClient and joins or creates through the real contract </summary>
    // 방 목록 · 참가 · 생성 요청은 TownPartyClient 그대로이며 서버 응답 없이 성공을 꾸며 보이지 않는다.
    // 참가나 생성이 받아들여지면 RoomChanged를 받은 SortiePrepBinder가 출정 준비 창을 열고 이 창은 닫는다
    public sealed class PartyRecruitView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private enum EPending
        {
            None,
            List,
            Join,
            Create
        }

        private enum EListState
        {
            Idle,
            Loading,
            Loaded,
            Failed
        }

        private const string WINDOW_NAME = "PartyRecruitWindow";
        private const string TITLE_TEXT  = "파티 모집";

        private const float WIDTH         = 1400f;
        private const float HEIGHT        = 800f;
        private const float LIST_X        = 32f;
        private const float LIST_Y        = 136f;
        private const float LIST_W        = 800f;
        private const float ROW_H         = 64f;
        private const float ROW_GAP       = 6f;
        private const float RIGHT_X       = 852f;
        private const float RIGHT_W       = 516f;
        private const float DETAIL_H      = 330f;
        private const float CREATE_Y      = 480f;
        private const float CREATE_H      = 240f;
        private const float FIELD_H       = 44f;
        private const float PENDING_TIMEOUT_SECONDS = 8f;
        private const int   ROWS_PER_PAGE = 8;

        private const string NO_SERVER_TEXT      = "마을 서버에 연결되어 있지 않습니다";
        private const string PREVIEW_TEXT        = "견본 화면입니다. 서버 요청은 보내지 않습니다";
        private const string TOWN_ONLY_TEXT      = "파티 모집은 마을에서만 사용할 수 있습니다";
        private const string LOADING_TEXT        = "방 목록을 불러오는 중입니다";
        private const string TIMEOUT_TEXT        = "서버 응답이 없습니다. 다시 시도하세요";
        private const string EMPTY_TEXT          = "모집 중인 파티가 없습니다";
        private const string EMPTY_SEARCH_TEXT   = "검색 결과가 없습니다";
        private const string NOT_LOADED_TEXT     = "새로고침으로 방 목록을 불러오세요";
        private const string NOT_JOINABLE_TEXT   = "참가할 수 없는 방입니다";
        private const string JOIN_PENDING_TEXT   = "참가 요청을 보냈습니다";
        private const string CREATE_PENDING_TEXT = "방 생성 요청을 보냈습니다";
        private const string NO_CONTENT_TEXT     = "생성할 콘텐츠 목록이 없습니다";
        private const string ROOM_NAME_FORMAT    = "{0}의 파티";
        private const string DEFAULT_ROOM_NAME   = "파티";
        private const string PASSWORD_ON_TEXT    = "비밀번호 방";
        private const string PASSWORD_OFF_TEXT   = "공개 방";

        private static readonly Color ERROR_COLOR    = new Color(0.95f, 0.45f, 0.4f, 1f);
        private static readonly Color SELECTED_COLOR = new Color(1.3f, 1.2f, 0.85f, 1f);

        private readonly List<int>              _filtered = new List<int>();
        private readonly List<DungeonEntryData> _targets  = new List<DungeonEntryData>();
        private readonly List<RowWidgets>       _rows     = new List<RowWidgets>();

        private PartyRoomSummary[] _rooms         = new PartyRoomSummary[0];
        private EPending           _pending       = EPending.None;
        private EListState         _listState     = EListState.Idle;
        private float              _pendingSince  = 0f;
        private int                _page          = 0;
        private int                _selectedRoom  = -1;
        private int                _targetIndex   = 0;
        private string             _query         = string.Empty;
        private bool               _built         = false;
        private bool               _statusIsError = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _preview = false;
#endif

        private TMP_InputField _searchInput;
        private TMP_InputField _nameInput;
        private TMP_InputField _createPasswordInput;
        private TMP_InputField _joinPasswordInput;
        private Button         _refreshButton;
        private Button         _prevButton;
        private Button         _nextButton;
        private Button         _joinButton;
        private Button         _createButton;
        private Button         _targetPrevButton;
        private Button         _targetNextButton;
        private TMP_Text       _pageLabel;
        private TMP_Text       _emptyLabel;
        private TMP_Text       _statusLabel;
        private TMP_Text       _detailTitle;
        private TMP_Text       _detailMode;
        private TMP_Text       _detailMembers;
        private TMP_Text       _detailLock;
        private TMP_Text       _detailClasses;
        private TMP_Text       _targetLabel;
        private TMP_Text       _joinLabel;
        private GameObject     _detailBody;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => gameObject.activeSelf;

        /// <summary> Rooms that pass the current search </summary>
        public int VisibleRoomCount => _filtered.Count;

        /// <summary> True while a request is in flight, every request control is locked </summary>
        public bool IsRequestLocked => _pending != EPending.None;

        public bool IsEmptyShown => (_emptyLabel != null) && _emptyLabel.gameObject.activeSelf;

        public int SelectedRoomId => _selectedRoom;

        public string StatusText => (_statusLabel != null) ? _statusLabel.text : string.Empty;

        public bool PreviewMode
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return _preview;
#else
                return false;
#endif
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            TownPartyClient.RoomListReceived += OnRoomList;
            TownPartyClient.ResultReceived   += OnResult;
            TownPartyClient.RoomChanged      += OnRoomChanged;
        }

        private void OnDisable()
        {
            TownPartyClient.RoomListReceived -= OnRoomList;
            TownPartyClient.ResultReceived   -= OnResult;
            TownPartyClient.RoomChanged      -= OnRoomChanged;
            if (_pending != EPending.None)
            {
                // 닫힌 창이 늦게 온 응답으로 다시 움직이지 않도록 잠금을 풀고 다음에 새로 조회한다
                _pending   = EPending.None;
                _listState = (_listState == EListState.Loading) ? EListState.Idle : _listState;
            }
        }

        private void Update()
        {
            if ((_pending != EPending.None) && ((Time.unscaledTime - _pendingSince) > PENDING_TIMEOUT_SECONDS))
            {
                if (_pending == EPending.List)
                {
                    _listState = EListState.Failed;
                }

                EndPending();
                SetStatus(TIMEOUT_TEXT, true);
                RefreshAll();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> Builds the window under parent and returns it closed </summary>
        public static PartyRecruitView Create(Transform parent, TMP_FontAsset font)
        {
            var go = new GameObject(WINDOW_NAME, typeof(RectTransform));
            go.layer = (parent != null) ? parent.gameObject.layer : go.layer;
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            var view = go.AddComponent<PartyRecruitView>();
            view.Build(font);
            return view;
        }

        public void SetOpen(bool open)
        {
            if (open == gameObject.activeSelf)
            {
                return;
            }

            gameObject.SetActive(open);
            if (!open)
            {
                return;
            }

            _joinPasswordInput.text = string.Empty;
            BuildTargets();
            RefreshAll();
            if (!PreviewMode)
            {
                RequestList();
            }
        }

        /// <summary> Shows explicit sample rooms and blocks every real request, null ends preview mode </summary>
        public void ApplyPreviewRooms(PartyRoomSummary[] rooms)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _preview = (rooms != null);
            EndPending();
            ApplyRooms(rooms);
            _listState = _preview ? EListState.Loaded : EListState.Idle;
            SetStatus(_preview ? PREVIEW_TEXT : string.Empty, false);
            RefreshAll();
#endif
        }

        /// <summary> Replaces the room list and keeps the selection only if that room still exists </summary>
        public void ApplyRooms(PartyRoomSummary[] rooms)
        {
            _rooms = (rooms != null) ? rooms : new PartyRoomSummary[0];
            if (FindRoom(_selectedRoom) < 0)
            {
                _selectedRoom = -1;
            }

            Refilter();
        }

        public void SetSearch(string query)
        {
            _query = (query == null) ? string.Empty : query;
            if ((_searchInput != null) && (_searchInput.text != _query))
            {
                _searchInput.SetTextWithoutNotify(_query);
            }

            _page = 0;
            Refilter();
            RefreshAll();
        }

        public void SelectRoom(int roomId)
        {
            _selectedRoom = (FindRoom(roomId) >= 0) ? roomId : -1;
            RefreshAll();
        }

        /// <summary> Asks the server for the room list, ignored while another request is pending </summary>
        public void RequestList()
        {
            if (_pending != EPending.None)
            {
                return;
            }

            if (PreviewMode)
            {
                SetStatus(PREVIEW_TEXT, false);
                RefreshAll();
                return;
            }

            if (!ClientRoles.IsTown())
            {
                _listState = EListState.Failed;
                SetStatus(TOWN_ONLY_TEXT, true);
                RefreshAll();
                return;
            }

            if (!TownPartyClient.RequestRoomList())
            {
                _listState = EListState.Failed;
                SetStatus(NO_SERVER_TEXT, true);
                RefreshAll();
                return;
            }

            BeginPending(EPending.List);
            _listState = EListState.Loading;
            SetStatus(LOADING_TEXT, false);
            RefreshAll();
        }

        public void RequestJoin()
        {
            int index = FindRoom(_selectedRoom);
            if ((_pending != EPending.None) || (index < 0))
            {
                return;
            }

            PartyRoomSummary room = _rooms[index];
            if (!PartyRecruitRules.IsJoinable(in room))
            {
                SetStatus(NOT_JOINABLE_TEXT, true);
                return;
            }

            if (PreviewMode)
            {
                SetStatus(PREVIEW_TEXT, false);
                return;
            }

            if (!ClientRoles.IsTown())
            {
                SetStatus(TOWN_ONLY_TEXT, true);
                return;
            }

            string password = room.HasPassword ? _joinPasswordInput.text : null;
            if (!TownPartyClient.JoinRoom(room.RoomId, password))
            {
                SetStatus(NO_SERVER_TEXT, true);
                return;
            }

            BeginPending(EPending.Join);
            SetStatus(JOIN_PENDING_TEXT, false);
            RefreshAll();
        }

        public void RequestCreate()
        {
            if (_pending != EPending.None)
            {
                return;
            }

            if (_targets.Count == 0)
            {
                SetStatus(NO_CONTENT_TEXT, true);
                return;
            }

            if (PreviewMode)
            {
                SetStatus(PREVIEW_TEXT, false);
                return;
            }

            if (!ClientRoles.IsTown())
            {
                SetStatus(TOWN_ONLY_TEXT, true);
                return;
            }

            DungeonEntryData target = _targets[Mathf.Clamp(_targetIndex, 0, _targets.Count - 1)];
            string roomName = _nameInput.text.Trim();
            if (roomName.Length == 0)
            {
                string localName = ClientRoles.LocalCharacterName();
                roomName = string.IsNullOrEmpty(localName)
                    ? DEFAULT_ROOM_NAME
                    : string.Format(CultureInfo.InvariantCulture, ROOM_NAME_FORMAT, localName);
            }

            string password = _createPasswordInput.text;
            if (string.IsNullOrEmpty(password))
            {
                password = null;
            }

            if (!TownPartyClient.CreateRoom(roomName, target.Mode, target.ContentId, password))
            {
                SetStatus(NO_SERVER_TEXT, true);
                return;
            }

            BeginPending(EPending.Create);
            SetStatus(CREATE_PENDING_TEXT, false);
            RefreshAll();
        }

        private void OnRoomList(PartyRoomListMessage message)
        {
            if (_pending == EPending.List)
            {
                EndPending();
            }

            ApplyRooms(message.Rooms);
            _listState = EListState.Loaded;
            SetStatus(string.Empty, false);
            RefreshAll();
        }

        private void OnResult(PartyResultMessage message)
        {
            if (_pending == EPending.None)
            {
                return;
            }

            bool matches = ((_pending == EPending.List) && (message.Action == EPartyAction.ListRooms))
                || ((_pending == EPending.Join) && (message.Action == EPartyAction.JoinRoom))
                || ((_pending == EPending.Create) && (message.Action == EPartyAction.CreateRoom));
            if ((!matches) || (message.IsAccepted))
            {
                // 받아들여진 목록은 RoomListReceived가, 참가 · 생성은 RoomChanged가 마무리한다
                return;
            }

            if (_pending == EPending.List)
            {
                _listState = EListState.Failed;
            }

            EndPending();
            SetStatus(RejectText.Describe(message.Reason), true);
            RefreshAll();
        }

        private void OnRoomChanged(PartyRoomStateMessage room)
        {
            if (!room.InRoom)
            {
                return;
            }

            bool wasJoining = (_pending == EPending.Join) || (_pending == EPending.Create);
            EndPending();
            if (wasJoining)
            {
                SetOpen(false);
            }
        }

        private void BeginPending(EPending pending)
        {
            _pending      = pending;
            _pendingSince = Time.unscaledTime;
        }

        private void EndPending()
        {
            _pending = EPending.None;
        }

        private int FindRoom(int roomId)
        {
            for (int i = 0; i < _rooms.Length; ++i)
            {
                if (_rooms[i].RoomId == roomId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void Refilter()
        {
            PartyRecruitRules.Filter(_rooms, _query, _filtered);
            _page = Mathf.Clamp(_page, 0, PageCount() - 1);
        }

        private int PageCount()
        {
            return Mathf.Max(1, Mathf.CeilToInt(_filtered.Count / (float)ROWS_PER_PAGE));
        }

        private void BuildTargets()
        {
            _targets.Clear();
            if (GameDB.IsInitialized)
            {
                DungeonEntryRules.Build(GameDB.Progression, _targets);
            }

            _targetIndex = Mathf.Clamp(_targetIndex, 0, Mathf.Max(0, _targets.Count - 1));
        }

        private void SetStatus(string text, bool isError)
        {
            _statusIsError = isError;
            if (_statusLabel != null)
            {
                _statusLabel.text  = text;
                _statusLabel.color = isError ? ERROR_COLOR : UiModernDark.TextMuted;
            }
        }

        private void RefreshAll()
        {
            if (!_built)
            {
                return;
            }

            bool locked = _pending != EPending.None;
            int first   = _page * ROWS_PER_PAGE;
            for (int i = 0; i < _rows.Count; ++i)
            {
                int filteredIndex = first + i;
                RowWidgets row = _rows[i];
                if (filteredIndex >= _filtered.Count)
                {
                    row.Root.gameObject.SetActive(false);
                    continue;
                }

                PartyRoomSummary room = _rooms[_filtered[filteredIndex]];
                row.Root.gameObject.SetActive(true);
                row.RoomId              = room.RoomId;
                row.Name.text           = room.Name;
                row.Meta.text           = BuildMeta(in room);
                row.Members.text        = room.MemberCount + "/" + PartyRecruitRules.MAX_MEMBERS;
                row.Phase.text          = PartyRecruitRules.DescribePhase(room.Phase);
                row.Phase.color         = PartyRecruitRules.IsJoinable(in room) ? UiModernDark.Cyan : UiModernDark.TextMuted;
                row.Lock.text           = room.HasPassword ? PASSWORD_ON_TEXT : string.Empty;
                row.Back.color          = (room.RoomId == _selectedRoom) ? SELECTED_COLOR : Color.white;
                row.Button.interactable = !locked;
            }

            RefreshEmpty();
            RefreshDetail(locked);
            RefreshCreate(locked);

            _pageLabel.text             = (_page + 1) + " / " + PageCount();
            _prevButton.interactable    = (!locked) && (_page > 0);
            _nextButton.interactable    = (!locked) && ((_page + 1) < PageCount());
            _refreshButton.interactable = !locked;
            _searchInput.interactable   = !locked;
            _statusLabel.color          = _statusIsError ? ERROR_COLOR : UiModernDark.TextMuted;
        }

        private void RefreshEmpty()
        {
            string text = null;
            if (_filtered.Count == 0)
            {
                switch (_listState)
                {
                    case EListState.Loading:
                        text = LOADING_TEXT;
                        break;
                    case EListState.Failed:
                        text = NO_SERVER_TEXT;
                        break;
                    case EListState.Idle:
                        text = NOT_LOADED_TEXT;
                        break;
                    default:
                        text = (_rooms.Length > 0) ? EMPTY_SEARCH_TEXT : EMPTY_TEXT;
                        break;
                }
            }

            _emptyLabel.gameObject.SetActive(text != null);
            if (text != null)
            {
                _emptyLabel.text  = text;
                _emptyLabel.color = (_listState == EListState.Failed) ? ERROR_COLOR : UiModernDark.TextMuted;
            }
        }

        private void RefreshDetail(bool locked)
        {
            int index = FindRoom(_selectedRoom);
            _detailBody.SetActive(index >= 0);
            _joinPasswordInput.gameObject.SetActive(false);
            _joinButton.interactable = false;
            if (index < 0)
            {
                return;
            }

            PartyRoomSummary room = _rooms[index];
            _detailTitle.text   = room.Name;
            _detailMode.text    = BuildMeta(in room);
            _detailMembers.text = PartyRecruitRules.DescribePhase(room.Phase) + " · " + room.MemberCount + "/" + PartyRecruitRules.MAX_MEMBERS
                + "명 · 준비 " + room.ReadyCount;
            _detailLock.text    = room.HasPassword ? PASSWORD_ON_TEXT : PASSWORD_OFF_TEXT;
            _detailClasses.text = PartyRecruitRules.DescribeClasses(in room);
            _joinPasswordInput.gameObject.SetActive(room.HasPassword);
            _joinPasswordInput.interactable = !locked;
            bool joinable = PartyRecruitRules.IsJoinable(in room);
            _joinButton.interactable = (!locked) && joinable;
            _joinLabel.text          = joinable ? "참가" : "참가 불가";
        }

        private void RefreshCreate(bool locked)
        {
            bool hasTarget = _targets.Count > 0;
            _targetLabel.text              = hasTarget ? _targets[Mathf.Clamp(_targetIndex, 0, _targets.Count - 1)].Title : NO_CONTENT_TEXT;
            _createButton.interactable     = (!locked) && hasTarget;
            _targetPrevButton.interactable = (!locked) && (_targets.Count > 1);
            _targetNextButton.interactable = (!locked) && (_targets.Count > 1);
            _nameInput.interactable           = !locked;
            _createPasswordInput.interactable = !locked;
        }

        private static string BuildMeta(in PartyRoomSummary room)
        {
            string meta = PartyRecruitRules.DescribeMode(room.Mode);
            string difficulty = PartyRecruitRules.DescribeDifficulty(room.Difficulty);
            return (difficulty.Length > 0) ? (meta + " · " + difficulty) : meta;
        }

        private void SelectRow(RowWidgets row)
        {
            if (_pending != EPending.None)
            {
                return;
            }

            _selectedRoom = row.RoomId;
            _joinPasswordInput.text = string.Empty;
            RefreshAll();
        }

        private void StepTarget(int step)
        {
            if (_targets.Count == 0)
            {
                return;
            }

            _targetIndex = (_targetIndex + step + _targets.Count) % _targets.Count;
            RefreshAll();
        }

        private void StepPage(int step)
        {
            _page = Mathf.Clamp(_page + step, 0, PageCount() - 1);
            RefreshAll();
        }

        private void Build(TMP_FontAsset font)
        {
            var window = (RectTransform)transform;
            TMP_Text fontSource = NewText(window, "FontSource", font, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            if (!ModernDarkKit.TryBegin(this, window, fontSource, out ModernDarkKit kit))
            {
                return;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), TITLE_TEXT);
            kit.Inset("ListBox", LIST_X, LIST_Y, LIST_W, (ROWS_PER_PAGE * (ROW_H + ROW_GAP)) + 12f);
            kit.Inset("DetailBox", RIGHT_X, LIST_Y, RIGHT_W, DETAIL_H);
            kit.Inset("CreateBox", RIGHT_X, CREATE_Y, RIGHT_W, CREATE_H);

            _searchInput = NewInput(window, "Search", font, "방 이름 · 콘텐츠 검색", LIST_X, 84f, 520f, FIELD_H,
                TMP_InputField.ContentType.Standard, Constants.Town.MAX_ROOM_NAME_LENGTH);
            _searchInput.onValueChanged.AddListener(SetSearch);
            _refreshButton = NewButton(window, "Refresh", font, "새로고침", LIST_X + 540f, 84f, 140f, FIELD_H, false, RequestList);
            NewButton(window, "Close", font, "닫기", WIDTH - 72f, 8f, 44f, 44f, false, CloseWindow);

            for (int i = 0; i < ROWS_PER_PAGE; ++i)
            {
                _rows.Add(BuildRow(window, font, LIST_X + 16f, LIST_Y + 8f + (i * (ROW_H + ROW_GAP)), LIST_W - 32f));
            }

            _emptyLabel = NewText(window, "Empty", font, 24f, UiModernDark.TextMuted, TextAlignmentOptions.Center);
            ModernDarkKit.PlaceRect((RectTransform)_emptyLabel.transform, LIST_X + 16f, LIST_Y + 200f, LIST_W - 32f, 48f);

            float footY = LIST_Y + (ROWS_PER_PAGE * (ROW_H + ROW_GAP)) + 28f;
            _prevButton = NewButton(window, "Prev", font, "이전", LIST_X + 280f, footY, 90f, FIELD_H, false, () => StepPage(-1));
            _pageLabel  = NewText(window, "Page", font, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Center);
            ModernDarkKit.PlaceRect((RectTransform)_pageLabel.transform, LIST_X + 380f, footY, 100f, FIELD_H);
            _nextButton = NewButton(window, "Next", font, "다음", LIST_X + 490f, footY, 90f, FIELD_H, false, () => StepPage(1));

            BuildDetail(window, font);
            BuildCreate(window, font);

            _statusLabel = NewText(window, "Status", font, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            _statusLabel.alignment = TextAlignmentOptions.Right;
            ModernDarkKit.PlaceRect((RectTransform)_statusLabel.transform, RIGHT_X, footY + 6f, RIGHT_W, 32f);

            _built = true;
            RefreshAll();
        }

        private void CloseWindow()
        {
            SetOpen(false);
        }

        private void BuildDetail(RectTransform window, TMP_FontAsset font)
        {
            var body = new GameObject("DetailBody", typeof(RectTransform));
            body.layer = window.gameObject.layer;
            body.transform.SetParent(window, false);
            ModernDarkKit.PlaceRect((RectTransform)body.transform, RIGHT_X, LIST_Y, RIGHT_W, DETAIL_H);
            _detailBody = body;
            var root = (RectTransform)body.transform;

            _detailTitle   = NewText(root, "DetailTitle", font, 30f, UiModernDark.TextGold, TextAlignmentOptions.Left);
            _detailMode    = NewText(root, "DetailMode", font, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left);
            _detailMembers = NewText(root, "DetailMembers", font, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left);
            _detailLock    = NewText(root, "DetailLock", font, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            _detailClasses = NewText(root, "DetailClasses", font, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            ModernDarkKit.PlaceRect((RectTransform)_detailTitle.transform, 24f, 20f, RIGHT_W - 48f, 44f);
            ModernDarkKit.PlaceRect((RectTransform)_detailMode.transform, 24f, 74f, RIGHT_W - 48f, 30f);
            ModernDarkKit.PlaceRect((RectTransform)_detailMembers.transform, 24f, 108f, RIGHT_W - 48f, 30f);
            ModernDarkKit.PlaceRect((RectTransform)_detailLock.transform, 24f, 142f, RIGHT_W - 48f, 28f);
            ModernDarkKit.PlaceRect((RectTransform)_detailClasses.transform, 24f, 176f, RIGHT_W - 48f, 28f);

            _joinPasswordInput = NewInput(root, "JoinPassword", font, "비밀번호", 24f, 252f, 240f, FIELD_H,
                TMP_InputField.ContentType.Password, Constants.Town.MAX_ROOM_PASSWORD_LENGTH);
            _joinButton = NewButton(root, "Join", font, "참가", 284f, 246f, RIGHT_W - 308f, 56f, true, RequestJoin);
            _joinLabel  = _joinButton.GetComponentInChildren<TMP_Text>(true);
        }

        private void BuildCreate(RectTransform window, TMP_FontAsset font)
        {
            TMP_Text head = NewText(window, "CreateHead", font, 24f, UiModernDark.TextGold, TextAlignmentOptions.Left);
            head.text = "방 만들기";
            ModernDarkKit.PlaceRect((RectTransform)head.transform, RIGHT_X + 24f, CREATE_Y + 14f, RIGHT_W - 48f, 36f);

            _nameInput = NewInput(window, "RoomName", font, "방 이름", RIGHT_X + 24f, CREATE_Y + 58f, 280f, FIELD_H,
                TMP_InputField.ContentType.Standard, Constants.Town.MAX_ROOM_NAME_LENGTH);
            _createPasswordInput = NewInput(window, "RoomPassword", font, "비밀번호(선택)", RIGHT_X + 316f, CREATE_Y + 58f, RIGHT_W - 340f, FIELD_H,
                TMP_InputField.ContentType.Password, Constants.Town.MAX_ROOM_PASSWORD_LENGTH);

            _targetPrevButton = NewButton(window, "TargetPrev", font, "<", RIGHT_X + 24f, CREATE_Y + 112f, 44f, FIELD_H, false, () => StepTarget(-1));
            _targetLabel = NewText(window, "TargetLabel", font, 22f, UiModernDark.TextMain, TextAlignmentOptions.Center);
            ModernDarkKit.PlaceRect((RectTransform)_targetLabel.transform, RIGHT_X + 76f, CREATE_Y + 112f, RIGHT_W - 152f, FIELD_H);
            _targetNextButton = NewButton(window, "TargetNext", font, ">", RIGHT_X + RIGHT_W - 68f, CREATE_Y + 112f, 44f, FIELD_H, false, () => StepTarget(1));

            _createButton = NewButton(window, "Create", font, "방 만들기", RIGHT_X + 24f, CREATE_Y + 168f, RIGHT_W - 48f, 56f, true, RequestCreate);
        }

        private RowWidgets BuildRow(RectTransform window, TMP_FontAsset font, float x, float y, float width)
        {
            var row = new RowWidgets();
            row.Button = NewButton(window, "Row", font, null, x, y, width, ROW_H, false, null);
            row.Back   = (Image)row.Button.targetGraphic;
            row.Root   = (RectTransform)row.Button.transform;
            ModernDarkKit.StyleInset(row.Back, UiModernDark.SPRITE_BUTTON);

            row.Name    = NewText(row.Root, "Name", font, 22f, UiModernDark.TextMain, TextAlignmentOptions.Left);
            row.Meta    = NewText(row.Root, "Meta", font, 17f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            row.Members = NewText(row.Root, "Members", font, 22f, UiModernDark.TextGold, TextAlignmentOptions.Right);
            row.Phase   = NewText(row.Root, "Phase", font, 17f, UiModernDark.TextMuted, TextAlignmentOptions.Right);
            row.Lock    = NewText(row.Root, "Lock", font, 17f, UiModernDark.TextMuted, TextAlignmentOptions.Right);
            ModernDarkKit.PlaceRect((RectTransform)row.Name.transform, 16f, 6f, 470f, 28f);
            ModernDarkKit.PlaceRect((RectTransform)row.Meta.transform, 16f, 36f, 470f, 22f);
            ModernDarkKit.PlaceRect((RectTransform)row.Members.transform, 500f, 6f, 120f, 28f);
            ModernDarkKit.PlaceRect((RectTransform)row.Phase.transform, 500f, 36f, 120f, 22f);
            ModernDarkKit.PlaceRect((RectTransform)row.Lock.transform, 630f, 36f, 122f, 22f);
            row.Button.onClick.AddListener(() => SelectRow(row));
            return row;
        }

        private static TMP_Text NewText(Transform parent, string name, TMP_FontAsset font, float size, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize         = size;
            text.color            = color;
            text.alignment        = alignment;
            text.raycastTarget    = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode     = TextOverflowModes.Ellipsis;
            return text;
        }

        private static Button NewButton(Transform parent, string name, TMP_FontAsset font, string label, float x, float y, float w, float h,
            bool primary, UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            ModernDarkKit.PlaceRect((RectTransform)go.transform, x, y, w, h);
            var image = go.GetComponent<Image>();
            ModernDarkKit.SetSliced(image, primary ? UiModernDark.SPRITE_BUTTON_PRIMARY : UiModernDark.SPRITE_BUTTON, Color.white);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            if (label != null)
            {
                TMP_Text text = NewText(go.transform, "Label", font, 22f, UiModernDark.TextMain, TextAlignmentOptions.Center);
                text.text = label;
                Stretch((RectTransform)text.transform);
            }

            ModernDarkKit.StyleButton(button, primary);
            return button;
        }

        private static TMP_InputField NewInput(Transform parent, string name, TMP_FontAsset font, string placeholder, float x, float y, float w, float h,
            TMP_InputField.ContentType contentType, int limit)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            ModernDarkKit.PlaceRect((RectTransform)go.transform, x, y, w, h);
            ModernDarkKit.SetSliced(go.GetComponent<Image>(), UiModernDark.SPRITE_INSET, Color.white);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.layer = go.layer;
            viewport.transform.SetParent(go.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(12f, 4f);
            viewportRect.offsetMax = new Vector2(-12f, -4f);

            TMP_Text text = NewText(viewport.transform, "Text", font, 20f, UiModernDark.TextMain, TextAlignmentOptions.Left);
            TMP_Text hint = NewText(viewport.transform, "Placeholder", font, 20f, UiModernDark.TextMuted, TextAlignmentOptions.Left);
            hint.text      = placeholder;
            hint.fontStyle = FontStyles.Italic;
            Stretch((RectTransform)text.transform);
            Stretch((RectTransform)hint.transform);

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport   = viewportRect;
            input.textComponent  = text;
            input.placeholder    = hint;
            input.contentType    = contentType;
            input.characterLimit = limit;
            input.lineType       = TMP_InputField.LineType.SingleLine;
            return input;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private sealed class RowWidgets
        {
            public RectTransform Root;
            public Button        Button;
            public Image         Back;
            public TMP_Text      Name;
            public TMP_Text      Meta;
            public TMP_Text      Members;
            public TMP_Text      Phase;
            public TMP_Text      Lock;
            public int           RoomId = -1;
        }
    }
}
