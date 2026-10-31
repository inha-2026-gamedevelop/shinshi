// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.UI;

namespace Shinshi.Network
{
    /// <summary> 캐릭터 선택 화면. 내 캐릭터 목록을 칸에 채우고, 고른 캐릭터로 마을 입장권을 받아 마을 서버에 접속한다 </summary>
    public sealed class CharacterSelectScreen : ClientView
    {
        // 접속이 인증되면 마을 서버가 마을 씬을 불러오라고 알려 Mirror가 씬을 바꾼다. 이 화면은 그때 함께 내려간다

        /****************************************
        *                Fields
        ****************************************/

        private const float CONNECT_TIMEOUT_SECONDS = 20f;

        // 프롤로그 진행/건너뛰기 선택 결과, 0은 아직 고르지 않았다
        private const int    PROLOGUE_CHOICE_PLAY = 1;
        private const int    PROLOGUE_CHOICE_SKIP = 2;
        private const string GUID_FORMAT          = "D";

        // 이미 접속 중 거절은 같은 토큰으로 몇 번 다시 붙는다. 그 시간까지 기다린다
        private const float JOIN_WAIT_SECONDS = CONNECT_TIMEOUT_SECONDS
            + (Constants.Network.JOIN_ALREADY_CONNECTED_RETRY_INTERVAL_SECONDS * Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES);

        // 삭제 대기 시간은 백엔드 PostgresCharacterRepository.DELETE_GRACE_MINUTES와 같은 값이다. 안내 문구에만 쓰고 판정은 서버가 한다
        private const int    DELETE_GRACE_MINUTES   = 10;
        private const float  DELETE_CONFIRM_SECONDS = 5f;
        private const float  COUNTDOWN_STEP_SECONDS = 1f;
        private const int    SECONDS_PER_MINUTE     = 60;
        private const string DELETE_LABEL           = "캐릭터 삭제";
        private const string DELETE_CANCEL_LABEL    = "캐릭터 삭제 취소";
        private const string DELETE_CONFIRM_LABEL   = "한 번 더 눌러 확인";
        private const string DELETE_PENDING_SUFFIX  = " (삭제 예약)";

        [Header("선택")]
        [SerializeField] private CharacterSelectSelection _selection = null;
        [SerializeField] private CharacterSelectSlot[]    _slots     = null;

        [Header("버튼")]
        [SerializeField] private Button _startButton  = null;
        [SerializeField] private Button _createButton = null;
        [SerializeField] private Button _deleteButton = null;
        [SerializeField] private Button _backButton   = null;

        [Header("안내")]
        [SerializeField] private TMP_Text _status = null;

        private readonly LobbyApiClient         _api        = new LobbyApiClient();
        private readonly List<CharacterSummary> _characters = new List<CharacterSummary>();

        private ShinshiNetworkManager      _manager       = null;
        private ShinshiClientAuthenticator _authenticator = null;
        private bool                       _isBusy        = false;
        private ERejectReason              _joinRejected  = ERejectReason.None;
        private bool                       _retryPending  = false;
        private bool                       _isLoaded      = false;
        private TMP_Text                   _deleteLabel   = null;
        private float                      _loadedAt      = 0f;
        private float                      _nextCountdown = 0f;
        private float                      _deleteArmedUntil = 0f;
        private string                     _deleteArmedId    = null;
        private PrologueEntryCoordinator   _prologueEntry    = null;
        private string                     _autoStartId      = null;

        /****************************************
        *              Properties
        ****************************************/

        // DevCustomizationScenario와 자동화 검증용 공개 읽기 접근자
        public Button                           StartButton => _startButton;
        public Button                           CreateButton => _createButton;
        public bool                              IsBusy      => _isBusy;
        public bool                              IsLoaded    => _isLoaded;
        public System.Collections.Generic.IReadOnlyList<CharacterSummary> Characters => _characters;
        public CharacterSelectSelection          Selection   => _selection;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_selection == null) || (_slots == null) || (_slots.Length == 0) || (_startButton == null)
                || (_createButton == null) || (_deleteButton == null) || (_backButton == null) || (_status == null))
            {
                Debug.LogError("[CharacterSelect] 선택 화면 참조가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _startButton.onClick.AddListener(OnStartClicked);
            _createButton.onClick.AddListener(OnCreateClicked);
            _backButton.onClick.AddListener(OnBackClicked);
            _deleteButton.onClick.AddListener(OnDeleteClicked);
            _deleteLabel = _deleteButton.GetComponentInChildren<TMP_Text>(true);
        }

        private void OnEnable()  { _selection.SelectionChanged += OnSelectionChanged; }
        private void OnDisable() { _selection.SelectionChanged -= OnSelectionChanged; }

        private void Start()
        {
            if (!LobbyFlow.EnsureBootstrapped())
            {
                return;
            }

            if (!LauncherSession.HasSession || LauncherSession.IsExpired)
            {
                LobbyFlow.GoToLobby();
                return;
            }

            _manager = NetworkManager.singleton as ShinshiNetworkManager;
            _authenticator = (_manager != null) ? _manager.authenticator as ShinshiClientAuthenticator : null;
            if (_authenticator != null)
            {
                _authenticator.Rejected           += OnJoinRejected;
                _authenticator.JoinRetryScheduled += OnJoinRetryScheduled;
            }

            ShowSlots();
            Run(CoLoad(LobbyFlow.TakeNotice()));
        }

        // 삭제 대기 남은 시간과 확인 대기 만료를 1초마다 버튼 글자에 반영한다
        private void Update()
        {
            if ((!_isLoaded) || (Time.realtimeSinceStartup < _nextCountdown))
            {
                return;
            }

            _nextCountdown = Time.realtimeSinceStartup + COUNTDOWN_STEP_SECONDS;
            CharacterSummary selected = GetSelectedCharacter();
            if (((selected != null) && (selected.DeletePending)) || (_deleteArmedId != null))
            {
                RefreshButtons();
            }
        }

        private void OnDestroy()
        {
            if (_authenticator != null)
            {
                _authenticator.Rejected           -= OnJoinRejected;
                _authenticator.JoinRetryScheduled -= OnJoinRetryScheduled;
            }

            _prologueEntry?.Dispose();
            _api.Abort();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Run(IEnumerator operation)
        {
            if (_isBusy)
            {
                return;
            }

            // 작업이 바로 끝나면 StartCoroutine이 돌아오기 전에 CoRun이 끝나므로 표시를 먼저 세운다
            _isBusy = true;
            StartCoroutine(CoRun(operation));
            RefreshButtons();
        }

        private IEnumerator CoRun(IEnumerator operation)
        {
            yield return operation;
            _isBusy = false;
            RefreshButtons();

            // 방금 만든 캐릭터는 목록을 불러온 직후 프롤로그 진입(선택창 또는 바로 진행)을 자동으로 시작한다
            if (_autoStartId != null)
            {
                string characterId = _autoStartId;
                _autoStartId = null;
                Run(CoStart(characterId, true));
            }
        }

        private IEnumerator CoLoad(string notice)
        {
            _status.text = "캐릭터 목록을 불러오는 중이다.";
            LobbyApiReply<CharacterListResponse> reply = null;
            yield return _api.SendGame<CharacterListResponse>(ApiRoutes.CHARACTERS, null, value => reply = value);
            if ((reply == null) || (!reply.IsSuccess) || (reply.Value.Characters == null))
            {
                _status.text = LobbyText.Describe(reply);
                yield break;
            }

            _characters.Clear();
            IReadOnlyList<CharacterSummary> characters = reply.Value.Characters;
            for (int i = 0; i < characters.Count; ++i)
            {
                if ((characters[i] != null) && (_characters.Count < _slots.Length))
                {
                    _characters.Add(characters[i]);
                }
            }

            _isLoaded = true;
            _loadedAt = Time.realtimeSinceStartup;
            ShowSlots();
            _autoStartId = TakeJustCreated();
            SelectInitialSlot();
            _status.text = !string.IsNullOrEmpty(notice) ? notice
                : (_characters.Count == 0) ? "캐릭터가 없다. 캐릭터 생성을 누른다." : "캐릭터를 고르고 게임 시작을 누른다.";
        }

        private void ShowSlots()
        {
            CharacterLevelSettings settings = Shinshi.Data.GameDB.IsInitialized
                ? Shinshi.Data.GameDB.CharacterLevel.ToSettings() : null;
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (i < _characters.Count)
                {
                    CharacterSummary character = _characters[i];
                    int level = 0;
                    if (HudExperienceRules.TryRead(settings, character.TotalExperience, out HudExperienceProgress progress))
                    {
                        level = progress.Level;
                    }
                    string name = character.DeletePending ? character.Name + DELETE_PENDING_SUFFIX : character.Name;
                    _slots[i].Show(name, LobbyText.ClassName(character.ClassId), level);
                    _slots[i].ShowLook(character.ClassId, character.Appearance);
                }
                else
                {
                    _slots[i].Show(null, null);
                }
            }
        }

        // 방금 만든 캐릭터가 목록에 있으면 한 번만 꺼낸다, 없으면 null이다
        private string TakeJustCreated()
        {
            string justCreated = LobbyFlow.JustCreatedCharacterId;
            LobbyFlow.JustCreatedCharacterId = null;
            for (int i = 0; i < _characters.Count; ++i)
            {
                if (string.Equals(_characters[i].CharacterId, justCreated, StringComparison.Ordinal))
                {
                    return justCreated;
                }
            }

            return null;
        }

        // 방금 만든 캐릭터가 있으면 그 칸을, 없으면 첫 칸을 고른다
        private void SelectInitialSlot()
        {
            string preferred = LobbyFlow.PreferredCharacterId;
            LobbyFlow.PreferredCharacterId = null;
            int slot = 0;
            for (int i = 0; i < _characters.Count; ++i)
            {
                if (string.Equals(_characters[i].CharacterId, preferred, StringComparison.Ordinal))
                {
                    slot = i;
                    break;
                }
            }

            if (_characters.Count > 0)
            {
                _selection.Select(slot);
            }
        }

        private CharacterSummary GetSelectedCharacter()
        {
            int slot = _selection.SelectedSlot;
            return ((slot >= 0) && (slot < _characters.Count)) ? _characters[slot] : null;
        }

        private void OnSelectionChanged(int slot)
        {
            _deleteArmedId = null;
            RefreshButtons();
            CharacterSummary selected = GetSelectedCharacter();
            if ((!_isBusy) && (selected != null) && (selected.DeletePending))
            {
                _status.text = DeleteWaitText(selected);
            }
        }

        private void RefreshButtons()
        {
            bool idle = (!_isBusy) && (!NetworkClient.active);
            int  slot = _selection.SelectedSlot;
            bool hasValidSlot = (slot >= 0) && (slot < _slots.Length);
            bool selectedLookReady = (hasValidSlot) && (_slots[slot].IsLookReady);
            _startButton.interactable  = idle && _isLoaded && (GetSelectedCharacter() != null) && selectedLookReady;
            _createButton.interactable = idle && _isLoaded && (_characters.Count < _slots.Length);
            _backButton.interactable   = idle;

            CharacterSummary selected = GetSelectedCharacter();
            if ((_deleteArmedId != null) && (Time.realtimeSinceStartup > _deleteArmedUntil))
            {
                _deleteArmedId = null;
            }

            _deleteButton.interactable = idle && _isLoaded && (selected != null);
            SetDeleteLabel(selected);

            // 고른 칸의 해석/조립이 실패했으면 그 사유를 그대로 보인다. 정상이면 다른 흐름이 정한 문구를 가리지 않는다
            if ((hasValidSlot) && (!selectedLookReady) && (!string.IsNullOrEmpty(_slots[slot].LastError)))
            {
                _status.text = _slots[slot].LastError;
            }
        }

        private void OnStartClicked()
        {
            CharacterSummary character = GetSelectedCharacter();
            if (character != null)
            {
                Run(CoStart(character.CharacterId, false));
            }
        }

        private void OnCreateClicked()
        {
            if (_characters.Count >= _slots.Length)
            {
                _status.text = "캐릭터는 계정마다 " + _slots.Length + "개까지 만든다.";
                return;
            }

            LobbyFlow.GoToCharacterCreate();
        }

        private void OnBackClicked() { LobbyFlow.GoToLobby(); }

        // 예약 전과 대기 후에는 두 번 눌러야 보낸다. 대기 중에는 한 번에 예약을 취소한다
        private void OnDeleteClicked()
        {
            CharacterSummary character = GetSelectedCharacter();
            if (character == null)
            {
                return;
            }

            if ((character.DeletePending) && (RemainingDeleteSeconds(character) > 0))
            {
                Run(CoDelete(ApiRoutes.CHARACTER_DELETE_CANCEL_FORMAT, character, "삭제 예약을 취소했다."));
                return;
            }

            if (!string.Equals(_deleteArmedId, character.CharacterId, StringComparison.Ordinal))
            {
                _deleteArmedId    = character.CharacterId;
                _deleteArmedUntil = Time.realtimeSinceStartup + DELETE_CONFIRM_SECONDS;
                _status.text = character.DeletePending
                    ? "한 번 더 누르면 캐릭터를 영구히 삭제한다."
                    : $"삭제를 예약하면 {DELETE_GRACE_MINUTES}분 뒤에 삭제할 수 있다. 한 번 더 누르면 예약한다.";
                RefreshButtons();
                return;
            }

            _deleteArmedId = null;
            if (character.DeletePending)
            {
                Run(CoDelete(ApiRoutes.CHARACTER_DELETE_FORMAT, character, "캐릭터를 삭제했다."));
                return;
            }

            Run(CoDelete(ApiRoutes.CHARACTER_DELETE_REQUEST_FORMAT, character,
                $"삭제를 예약했다. {DELETE_GRACE_MINUTES}분 뒤에 삭제할 수 있다."));
        }

        // 예약, 취소, 삭제 모두 같은 본문을 보낸다. 결과와 상관없이 목록을 다시 읽어 서버 상태로 맞춘다
        private IEnumerator CoDelete(string format, CharacterSummary character, string success)
        {
            _status.text = "요청을 보내는 중이다.";
            string path = string.Format(CultureInfo.InvariantCulture, format, character.CharacterId);
            var request = new CharacterDeleteRequest(Guid.NewGuid().ToString("D"), character.Revision);
            LobbyApiReply<OperationResult> reply = null;
            yield return _api.SendGame<OperationResult>(path, request, value => reply = value);

            bool committed = (reply != null) && (reply.IsSuccess) && (reply.Value.Status == ContractCodes.OperationStatus.COMMITTED);
            string notice = committed ? success : LobbyText.Describe(reply);
            if ((reply != null) && (reply.IsSuccess) && (!committed))
            {
                notice = LobbyText.Describe((ERejectReason)reply.Value.ReasonCode);
            }

            LobbyFlow.PreferredCharacterId = character.CharacterId;
            yield return CoLoad(notice);
        }

        private void SetDeleteLabel(CharacterSummary character)
        {
            if (_deleteLabel == null)
            {
                return;
            }

            if ((character != null) && (string.Equals(_deleteArmedId, character.CharacterId, StringComparison.Ordinal)))
            {
                _deleteLabel.text = DELETE_CONFIRM_LABEL;
                return;
            }

            long remaining = (character != null) ? RemainingDeleteSeconds(character) : 0;
            _deleteLabel.text = ((character != null) && (character.DeletePending) && (remaining > 0))
                ? $"{DELETE_CANCEL_LABEL}  {remaining / SECONDS_PER_MINUTE:00}:{remaining % SECONDS_PER_MINUTE:00}"
                : DELETE_LABEL;
        }

        private string DeleteWaitText(CharacterSummary character)
        {
            long remaining = RemainingDeleteSeconds(character);
            return (remaining > 0)
                ? $"캐릭터 삭제 가능까지 {remaining / SECONDS_PER_MINUTE}분 {remaining % SECONDS_PER_MINUTE}초. 취소하려면 캐릭터 삭제 취소를 누른다."
                : "삭제 대기가 끝났다. 캐릭터 삭제를 두 번 누르면 삭제한다.";
        }

        // 목록을 읽은 순간의 서버 남은 시간에서 그 뒤 흐른 시간을 뺀다. 기기 시계와 서버 시계 차이에 영향받지 않는다
        private long RemainingDeleteSeconds(CharacterSummary character)
        {
            if (!character.DeletePending)
            {
                return 0;
            }

            long elapsed = (long)(Time.realtimeSinceStartup - _loadedAt);
            return Math.Max(0L, character.DeleteRemainingSeconds - elapsed);
        }

        private void OnJoinRejected(ERejectReason reason) { _joinRejected = reason; }

        private void OnJoinRetryScheduled(int retry)
        {
            _retryPending = true;
            _status.text = $"이전 접속이 정리되기를 기다린다({retry}/{Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES}).";
        }

        // API가 고른 마을 서버의 일회 입장 토큰을 받아 접속한다. 인증이 끝나면 서버가 마을 씬으로 안내한다
        private IEnumerator CoJoin(string characterId)
        {
            if ((_manager == null) || (_authenticator == null) || (NetworkClient.active)
                || !(Transport.active is PortTransport portTransport))
            {
                _status.text = "네트워크 준비가 끝나지 않았다. 게임을 다시 실행한다.";
                yield break;
            }

            _status.text = "마을 입장을 요청하는 중이다.";
            LobbyApiReply<JoinTicket> reply = null;
            yield return _api.SendGame<JoinTicket>(ApiRoutes.TOWN_JOIN_TICKET, new TownJoinTicketRequest(characterId), value => reply = value);
            JoinTicket ticket = reply?.Value;
            if (ticket == null)
            {
                _status.text = LobbyText.Describe(reply);
                yield break;
            }

            if ((ticket.ServerRole != ContractCodes.ServerRole.TOWN) || (ticket.CharacterId != characterId)
                || string.IsNullOrEmpty(ticket.JoinToken) || (Uri.CheckHostName(ticket.Address ?? string.Empty) == UriHostNameType.Unknown)
                || (ticket.Port <= 0) || (ticket.Port > ushort.MaxValue))
            {
                _status.text = "마을 입장권이 올바르지 않다.";
                yield break;
            }

            yield return CoConnect(ticket, EServerRole.Town, characterId, portTransport, "마을");
        }

        // 입장권으로 서버에 접속하고 인증될 때까지 기다린다. 마을과 프롤로그 던전이 같이 쓴다. 인증되면 서버가 씬으로 안내한다
        private IEnumerator CoConnect(JoinTicket ticket, EServerRole role, string characterId, PortTransport portTransport, string label)
        {
            _joinRejected = ERejectReason.None;
            _retryPending = false;
            _manager.ConfigureServerRole(role);
            _manager.networkAddress = ticket.Address;
            portTransport.Port      = (ushort)ticket.Port;
            _authenticator.SetJoinToken(ticket.JoinToken);
            _status.text = label + " 서버에 접속하는 중이다.";
            _manager.StartClient();

            float deadline = Time.realtimeSinceStartup + JOIN_WAIT_SECONDS;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (IsAuthenticated())
                {
                    LobbyFlow.ActiveCharacterId = characterId;
                    _status.text = label + " 접속을 마쳤다. 이동하는 중이다.";
                    Debug.Log($"[CharacterSelect] {label} 서버 인증 성공");
                    yield break;
                }

                if (_joinRejected != ERejectReason.None)
                {
                    break;
                }

                // 재접속 예약 사이에는 클라이언트가 잠시 내려가 있다. 다시 올라오면 예약이 소비된 것이다
                if (NetworkClient.active)
                {
                    _retryPending = false;
                }
                else if (!_retryPending)
                {
                    break;
                }

                yield return null;
            }

            _authenticator.CancelJoinRetry();
            if (NetworkClient.active)
            {
                _manager.StopClient();
            }

            _status.text = (_joinRejected != ERejectReason.None) ? LobbyText.Describe(_joinRejected)
                : label + " 서버에 접속하지 못했다. 서버가 켜져 있는지 확인하고 다시 시도한다.";
            Debug.LogWarning($"[CharacterSelect] {label} 접속 실패, reason={_joinRejected}");
        }

        // 캐릭터의 프롤로그 상태에 따라 마을 입장, 프롤로그 진행, 선택창을 정한다. 프롤로그를 마친 캐릭터는 곧바로 마을 입장이다
        private IEnumerator CoStart(string characterId, bool justCreated)
        {
            _status.text = "프롤로그 진행 상태를 확인하는 중이다.";
            LobbyApiReply<PrologueStateResponse> reply = null;
            yield return _api.SendGame<PrologueStateResponse>(string.Format(CultureInfo.InvariantCulture, PrologueRoutes.STATE_FORMAT,
                characterId), null, value => reply = value);
            PrologueStateResponse state = reply?.Value;
            if (state == null)
            {
                _status.text = LobbyText.Describe(reply);
                yield break;
            }

            if (state.Stage == PrologueCodes.Stage.DONE)
            {
                yield return CoJoin(characterId);
                yield break;
            }

            // 방금 만든 캐릭터이고 계정이 이미 프롤로그를 마쳤으면 진행/건너뛰기를 고르게 한다. 계정의 첫 프롤로그와 중단한 프롤로그의
            // 재개는 선택 없이 바로 진행한다(첫 프롤로그는 건너뛸 수 없다)
            if ((!justCreated) || (!state.AccountCompleted))
            {
                yield return CoPrologueEnter(characterId);
                yield break;
            }

            PrologueAssetsSO assets = PrologueAssetsSO.Load();
            if ((assets == null) || (assets.Illustrations == null))
            {
                _status.text = "프롤로그 화면 리소스를 찾지 못했다.";
                yield break;
            }

            int choice = 0;
            _prologueEntry?.Dispose();
            _prologueEntry = PrologueEntryCoordinator.CreateFromCatalog(assets.Illustrations);
            _prologueEntry.Begin(true, () => choice = PROLOGUE_CHOICE_PLAY, () => choice = PROLOGUE_CHOICE_SKIP);
            _status.text = "프롤로그를 진행할지 고른다.";
            while (choice == 0)
            {
                yield return null;
            }

            _prologueEntry.Dispose();
            _prologueEntry = null;
            if (choice == PROLOGUE_CHOICE_PLAY)
            {
                yield return CoPrologueEnter(characterId);
            }
            else
            {
                yield return CoPrologueSkip(characterId);
            }
        }

        // 마을을 거치지 않고 프롤로그 던전의 입장권을 받아 바로 접속한다
        private IEnumerator CoPrologueEnter(string characterId)
        {
            if ((_manager == null) || (_authenticator == null) || (NetworkClient.active)
                || !(Transport.active is PortTransport portTransport))
            {
                _status.text = "네트워크 준비가 끝나지 않았다. 게임을 다시 실행한다.";
                yield break;
            }

            _status.text = "프롤로그 입장을 요청하는 중이다.";
            LobbyApiReply<JoinTicket> reply = null;
            yield return _api.SendGame<JoinTicket>(PrologueRoutes.ENTRY,
                new PrologueEntryRequest(Guid.NewGuid().ToString(GUID_FORMAT), characterId), value => reply = value);
            JoinTicket ticket = reply?.Value;
            if (ticket == null)
            {
                _status.text = LobbyText.Describe(reply);
                yield break;
            }

            if ((ticket.ServerRole != ContractCodes.ServerRole.DUNGEON) || (ticket.CharacterId != characterId)
                || string.IsNullOrEmpty(ticket.JoinToken) || (Uri.CheckHostName(ticket.Address ?? string.Empty) == UriHostNameType.Unknown)
                || (ticket.Port <= 0) || (ticket.Port > ushort.MaxValue))
            {
                _status.text = "프롤로그 입장권이 올바르지 않다.";
                yield break;
            }

            yield return CoConnect(ticket, EServerRole.Dungeon, characterId, portTransport, "프롤로그");
        }

        // 이미 프롤로그를 마친 계정의 건너뛰기, 서버가 완료 기록과 기본 지급품을 맡고 확정되면 마을로 들어간다
        private IEnumerator CoPrologueSkip(string characterId)
        {
            _status.text = "프롤로그를 건너뛰는 중이다.";
            LobbyApiReply<OperationResult> reply = null;
            yield return _api.SendGame<OperationResult>(PrologueRoutes.SKIP,
                new PrologueSkipRequest(Guid.NewGuid().ToString(GUID_FORMAT), characterId), value => reply = value);
            OperationResult result = reply?.Value;
            if ((result == null) || (result.Status != ContractCodes.OperationStatus.COMMITTED))
            {
                _status.text = LobbyText.Describe(reply);
                yield break;
            }

            yield return CoJoin(characterId);
        }

        private static bool IsAuthenticated()
        {
            return NetworkClient.isConnected && (NetworkClient.connection != null) && NetworkClient.connection.isAuthenticated;
        }
    }
}
