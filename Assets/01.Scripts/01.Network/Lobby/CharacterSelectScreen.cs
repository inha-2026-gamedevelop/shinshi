// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
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

        // 이미 접속 중 거절은 같은 토큰으로 몇 번 다시 붙는다. 그 시간까지 기다린다
        private const float JOIN_WAIT_SECONDS = CONNECT_TIMEOUT_SECONDS
            + (Constants.Network.JOIN_ALREADY_CONNECTED_RETRY_INTERVAL_SECONDS * Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES);

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

            // 삭제 API가 아직 없다. 버튼은 배치만 두고 누를 수 없게 한다
            _deleteButton.interactable = false;
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

        private void OnDestroy()
        {
            if (_authenticator != null)
            {
                _authenticator.Rejected           -= OnJoinRejected;
                _authenticator.JoinRetryScheduled -= OnJoinRetryScheduled;
            }

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
            ShowSlots();
            SelectInitialSlot();
            _status.text = !string.IsNullOrEmpty(notice) ? notice
                : (_characters.Count == 0) ? "캐릭터가 없다. 캐릭터 생성을 누른다." : "캐릭터를 고르고 게임 시작을 누른다.";
        }

        private void ShowSlots()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (i < _characters.Count)
                {
                    _slots[i].Show(_characters[i].Name, LobbyText.ClassName(_characters[i].ClassId));
                }
                else
                {
                    _slots[i].Show(null, null);
                }
            }
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

        private void OnSelectionChanged(int slot) { RefreshButtons(); }

        private void RefreshButtons()
        {
            bool idle = (!_isBusy) && (!NetworkClient.active);
            _startButton.interactable  = idle && _isLoaded && (GetSelectedCharacter() != null);
            _createButton.interactable = idle && _isLoaded && (_characters.Count < _slots.Length);
            _backButton.interactable   = idle;
        }

        private void OnStartClicked()
        {
            CharacterSummary character = GetSelectedCharacter();
            if (character != null)
            {
                Run(CoJoin(character.CharacterId));
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

            _joinRejected = ERejectReason.None;
            _retryPending = false;
            _manager.ConfigureServerRole(EServerRole.Town);
            _manager.networkAddress = ticket.Address;
            portTransport.Port      = (ushort)ticket.Port;
            _authenticator.SetJoinToken(ticket.JoinToken);
            _status.text = "마을 서버에 접속하는 중이다.";
            _manager.StartClient();

            float deadline = Time.realtimeSinceStartup + JOIN_WAIT_SECONDS;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (IsAuthenticated())
                {
                    _status.text = "마을로 이동하는 중이다.";
                    Debug.Log("[CharacterSelect] 마을 서버 인증 성공");
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
                : "마을 서버에 접속하지 못했다. 서버가 켜져 있는지 확인하고 다시 시도한다.";
            Debug.LogWarning($"[CharacterSelect] 마을 접속 실패, reason={_joinRejected}");
        }

        private static bool IsAuthenticated()
        {
            return NetworkClient.isConnected && (NetworkClient.connection != null) && NetworkClient.connection.isAuthenticated;
        }
    }
}
