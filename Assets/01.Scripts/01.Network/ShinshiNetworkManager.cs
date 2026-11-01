// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 네트워크 주기를 적용한다, 서버 시작은 서버 부트스트랩만 한다 </summary>
    public class ShinshiNetworkManager : NetworkManager, IDungeonMapLoader
    {
        /****************************************
        *                Fields
        ****************************************/

        // 메뉴로 나갈 때 포기 뒤 이탈 기록 저장(계정 임대 해제)을 기다리는 상한. 넘기면 그대로 끊는다
        private const float LEAVE_DEPARTURE_WAIT_SECONDS = 10f;

        private readonly Dictionary<uint, GameObject> _spawnPrefabsByAssetId = new Dictionary<uint, GameObject>();

        private EServerRole _serverRole = EServerRole.Town;

        // 던전에서 마을로 옮기는 흐름. 결과 창의 귀환 버튼과 정산 뒤 던전의 연결 끊김이 같은 흐름 하나를 쓴다.
        // 클라이언트 빌드의 Awake에서만 만들어 서버 빌드에서는 null이다
        private SortieReturnFlow      _returnFlow       = null;
        private Action<ERejectReason> _onReturnFinished = null;
        private Action<ERejectReason> _returnListener   = null;

        // 메뉴의 캐릭터 선택이 포기 결과와 이탈 저장을 기다리는 중이다
        private Action<SortieAbandonResultMessage> _onLeaveAbandonResult = null;
        private bool                               _isLeaving            = false;
        private bool                               _leaveAbandonRejected = false;

        // 씬에 저장한 onlineScene은 마을 씬이다. 던전 역할은 이 값이 있을 때만 던전 씬으로 바꾼다.
        // 서버와 클라이언트가 같은 역할 설정으로 같은 값을 써야 클라이언트가 서버의 씬 안내를 기다린다
        private string _townOnlineScene = string.Empty;

        // 던전 서버가 올린(또는 올리는 중인) 전장 씬 이름과 전환 중 여부. 서버 측 전환만 쓴다
        private string _dungeonMapName = string.Empty;
        private bool   _isMapLoading   = false;

#if !UNITY_SERVER
        // 마을 서버가 보낸 내 던전 입장권으로 마을을 끊고 던전에 접속한다. 클라이언트 빌드에서만 만든다
        private SortieDepartFlow      _departFlow       = null;
        private Action<ERejectReason> _onDepartFinished = null;
#endif

        /****************************************
        *              Properties
        ****************************************/

        // 서버에서 연결이 끊긴 뒤(플레이어 개체 정리 후) 낸다. Mirror 정적 이벤트는 매니저가 덮어쓰므로 이것을 구독한다
        public event Action<NetworkConnectionToClient> ServerDisconnected;

        // 서버 부트스트랩이 던전 역할에서 넣는다. 클라이언트 빌드에는 구현 타입이 없어 항상 null이다
        public IServerPlayerRetention PlayerRetention { private get; set; } = null;

        // 던전에서 마을로(SortieReturnFlow), 마을에서 던전으로(SortieDepartFlow) 옮기는 중이다.
        // 이 동안 끊김은 캐릭터 선택 복귀로 다루지 않는다
        public bool IsTransferringServer { get; set; } = false;

        // 클라이언트가 지금 접속 대상으로 지정한 서버 역할. 화면이 이 값으로 마을과 던전을 구분한다
        public EServerRole ServerRole => _serverRole;

        public string CurrentMapName => _dungeonMapName;
        public bool   IsLoading      => _isMapLoading;

        // 메뉴로 나가면 진행 중인 출정을 포기하게 되는지. 확인 창 문구에 쓴다
        public bool WillAbandonSortieOnLeave => (NetworkClient.isConnected)
            && (SortieExitRules.ShouldAbandonBeforeLeave(_serverRole == EServerRole.Dungeon, SortieClient.HasStatus, SortieClient.Current));

#if !UNITY_SERVER
        // 마을 -> 던전 이동이 끝났다. None이면 던전 인증 성공, 아니면 거절 사유다. 출정 화면(UI 담당)이 안내에 쓴다
        public event Action<ERejectReason> SortieDepartFinished;
#endif

        /****************************************
        *              Unity Event
        ****************************************/

        public override void Awake()
        {
            _townOnlineScene = onlineScene ?? string.Empty;
            ApplyNetworkSettings();
            EnsureAuthenticator();
            base.Awake();
#if !UNITY_SERVER
            // 중복 매니저는 base.Awake가 지운다. 남는 하나만 입장권을 받는다
            if ((singleton == this) && (authenticator is ShinshiClientAuthenticator clientAuthenticator))
            {
                _departFlow       = new SortieDepartFlow(this, clientAuthenticator);
                _onDepartFinished = OnSortieDepartFinished;
                _returnFlow       = new SortieReturnFlow(new LobbyApiClient(), this, clientAuthenticator);
                _onReturnFinished = OnReturnToTownFinished;
                TownPartyClient.SortieTicketReceived += OnSortieTicketReceived;
            }
#endif
        }

        public override void OnDestroy()
        {
#if !UNITY_SERVER
            if (_departFlow != null)
            {
                TownPartyClient.SortieTicketReceived -= OnSortieTicketReceived;
            }
#endif
            base.OnDestroy();
        }

        // 헤드리스 자동 시작을 켜 두면 역할과 포트를 읽기 전에 서버가 떠 버린다
        public override void OnValidate()
        {
            ApplyNetworkSettings();
            base.OnValidate();
        }

        public override void Start()
        {
            base.Start();
            if ((singleton != this) || (NetworkServer.active) || (NetworkClient.active))
            {
                return;
            }

            string[] args = Environment.GetCommandLineArgs();
            bool hasAddress = TryReadLaunchArgument(args, Constants.Network.ARG_CONNECT, out string address, out string error);
            if (error != null)
            {
                BootFailure.Stop(error);
                return;
            }

            if (!hasAddress)
            {
#if !UNITY_SERVER
                // Bootstrap으로 시작한 클라이언트만 로비로 간다. 네트워크 매니저를 직접 둔 개발 씬은 그 자리에 남는다
                if (string.Equals(SceneManager.GetActiveScene().name, Constants.Scene.BOOTSTRAP, StringComparison.Ordinal))
                {
                    // 로딩 화면 뒤에서 표현 자산을 미리 그린 다음 로비로 간다. 러너가 없거나 건너뛰면 곧바로 로비다
                    if (!ClientPrewarmGate.TryBegin(LobbyFlow.GoToLobby))
                    {
                        LobbyFlow.GoToLobby();
                    }
                }
#endif
                return;
            }

#if !UNITY_SERVER && (UNITY_EDITOR || DEVELOPMENT_BUILD)
            if ((Uri.CheckHostName(address) == UriHostNameType.Unknown) || (ContainsWhitespace(address)))
            {
                BootFailure.Stop("-connect: 접속할 호스트 이름 또는 IP 주소가 필요하다");
                return;
            }

            bool hasRole = TryReadLaunchArgument(args, Constants.Network.ARG_CONNECT_ROLE, out string roleText, out error);
            if (error != null)
            {
                BootFailure.Stop(error);
                return;
            }

            if (hasRole)
            {
                if (string.Equals(roleText, Constants.Network.SERVER_ROLE_TOWN, StringComparison.OrdinalIgnoreCase))
                {
                    ConfigureServerRole(EServerRole.Town);
                }
                else if (string.Equals(roleText, Constants.Network.SERVER_ROLE_DUNGEON, StringComparison.OrdinalIgnoreCase))
                {
                    ConfigureServerRole(EServerRole.Dungeon);
                }
                else
                {
                    BootFailure.Stop("-connect-role: town 또는 dungeon이 필요하다");
                    return;
                }
            }

            bool hasPort = TryReadLaunchArgument(args, Constants.Network.ARG_PORT, out string portText, out error);
            if (error != null)
            {
                BootFailure.Stop(error);
                return;
            }

            ushort port = Constants.Network.DEFAULT_GAME_PORT;
            if ((hasPort) && ((!ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)) || (port == 0)))
            {
                BootFailure.Stop("-port: 1~65535 정수가 필요하다");
                return;
            }

            if (!(Transport.active is PortTransport portTransport))
            {
                BootFailure.Stop("개발 클라이언트에 포트를 지정할 수 있는 전송 계층이 없다");
                return;
            }

            bool hasJoinToken = TryReadLaunchArgument(args, Constants.Network.ARG_JOIN_TOKEN, out string joinToken, out error);
            if (error != null)
            {
                BootFailure.Stop(error);
                return;
            }

            if ((hasJoinToken) && (authenticator is ShinshiClientAuthenticator clientAuthenticator))
            {
                clientAuthenticator.SetJoinToken(joinToken);
            }

            portTransport.Port = port;
            networkAddress = address;
            try
            {
                StartClient();
            }
            catch (Exception exception)
            {
                BootFailure.Stop($"개발 클라이언트 시작 실패 ({exception.GetType().Name})");
            }
#else
            BootFailure.Stop("-connect 자동 접속은 개발 클라이언트 빌드에서만 지원한다");
#endif
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시작 또는 클라이언트 접속 전에 대상 역할을 지정한다, 마을 캐릭터는 별도 복제 주기를 쓴다 </summary>
        public void ConfigureServerRole(EServerRole role, string dungeonBossId = null)
        {
            if ((role != EServerRole.Town) && (role != EServerRole.Dungeon))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            _serverRole = role;
            onlineScene     = ResolveOnlineScene(role, _townOnlineScene, dungeonBossId);
            _dungeonMapName = ((role == EServerRole.Dungeon) && (!string.IsNullOrEmpty(onlineScene)))
                ? DungeonMapRules.ResolveBootSceneName(dungeonBossId)
                : string.Empty;
            ApplyNetworkSettings();
        }

        /// <summary>
        /// 첫 입장 계약이 요구하는 전장으로 서버만 씬을 바꾼다. Mirror ServerChangeScene과 같은 순서지만 SceneMessage를 보내지 않는다.
        /// 이 시점에 서버가 받은 연결은 아직 입장이 확정되지 않았고, 확정되는 연결은 ServerAccept 때 바뀐 networkSceneName을 받는다
        /// </summary>
        public bool TryBeginLoad(string mapName)
        {
            if ((!NetworkServer.active) || (_isMapLoading) || (_serverRole != EServerRole.Dungeon)
                || (!DungeonMapRules.IsDungeonSceneName(mapName)))
            {
                return false;
            }

            string path = DungeonMapRules.ToScenePath(mapName);
            if (!Application.CanStreamedLevelBeLoaded(mapName))
            {
                Debug.LogError($"[Network] 빌드에 없는 전장 씬이라 맵 전환을 시작하지 못한다, map={mapName}");
                return false;
            }

            NetworkServer.SetAllClientsNotReady();
            networkSceneName = path;
            OnServerChangeScene(path);
            NetworkServer.isLoadingScene = true;
            loadingSceneAsync            = SceneManager.LoadSceneAsync(path);
            if (loadingSceneAsync == null)
            {
                NetworkServer.isLoadingScene = false;
                return false;
            }

            startPositionIndex = 0;
            startPositions.Clear();
            _dungeonMapName = mapName;
            _isMapLoading   = true;
            return true;
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            _isMapLoading = false;
            base.OnServerSceneChanged(sceneName);
        }

        /// <summary> 역할이 접속할 씬 경로를 구한다, 마을 씬이 비어 있는 단일 씬 개발 구성은 던전도 씬을 바꾸지 않는다 </summary>
        public static string ResolveOnlineScene(EServerRole role, string townOnlineScene, string dungeonBossId = null)
        {
            switch (role)
            {
                case EServerRole.Town:
                    return townOnlineScene ?? string.Empty;
                case EServerRole.Dungeon:
                    return string.IsNullOrEmpty(townOnlineScene)
                        ? string.Empty
                        : DungeonMapRules.ToScenePath(DungeonMapRules.ResolveBootSceneName(dungeonBossId));
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient connection)
        {
            // 인증기가 참가자를 결합하지 않은 연결에는 캐릭터를 만들지 않는다. Mirror도 인증 전 AddPlayer를 막지만 한 번 더 막는다
            if ((!connection.isAuthenticated) || (connection.authenticationData == null))
            {
                Debug.LogWarning($"[Network] 인증되지 않은 연결의 플레이어 생성 요청을 거절하고 끊는다, connId={connection.connectionId}");
                connection.Disconnect();
                return;
            }

            if (connection.identity != null)
            {
                Debug.LogError("이미 플레이어가 있는 연결이다");
                return;
            }

            // 출정이 끝난 뒤 경매에만 다시 들어온 참가자는 캐릭터 없이 경매 상태만 받는다
            if ((PlayerRetention != null) && (PlayerRetention.TryJoinWithoutPlayer(connection)))
            {
                return;
            }

            // 재접속 유예 중 서버에 남아 있던 같은 참가자의 개체가 있으면 새로 만들지 않고 그 개체를 넘겨받는다
            if ((PlayerRetention != null) && (PlayerRetention.TryReattachPlayer(connection)))
            {
                return;
            }

            Transform start = GetStartPosition();
            GameObject player = (start != null) ? Instantiate(playerPrefab, start.position, start.rotation) : Instantiate(playerPrefab);
            if (!player.TryGetComponent(out NetworkIdentity identity))
            {
                Destroy(player);
                BootFailure.Stop("플레이어 프리팹 루트에 NetworkIdentity가 없다");
                return;
            }

            // 최초 스냅샷과 OnStartServer보다 먼저 권위 방향과 복제 간격을 확정한다
            ApplyRolePlayerReplication(identity);
            player.name = $"{playerPrefab.name} [connId={connection.connectionId}]";
            NetworkServer.AddPlayerForConnection(connection, player);
        }

        // 기본 처리(소유 개체 삭제) 뒤에 알린다. 던전 참가자는 그 전에 소유권만 떼어 플레이어 개체를 서버에 남긴다.
        // 남긴 개체는 연결의 소유 목록에서 빠져 기본 처리가 지우지 않고, 유예 동안 피격과 사망, 타이머를 계속 받는다
        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if ((PlayerRetention != null) && (conn.identity != null))
            {
                try
                {
                    PlayerRetention.TryRetainPlayer(conn);
                }
                catch (Exception exception)
                {
                    // 유지 실패가 연결 정리를 막으면 연결 슬롯과 관찰자 목록이 남는다. 기본 처리로 개체를 지운다
                    Debug.LogException(exception);
                }
            }

            // 앞 단계가 실패해도 뒤 단계(임대 해제, 파티, 소셜 정리)는 실행돼야 한다
            try
            {
                base.OnServerDisconnect(conn);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            Action<NetworkConnectionToClient> handlers = ServerDisconnected;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<NetworkConnectionToClient>)handler)(conn);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        // offlineScene은 쓰지 않는다. Mirror가 끊김 때 매니저를 DontDestroyOnLoad에서 꺼내 다음 씬과 함께 지운다.
        // 마을에 있다가 끊기면 매니저를 남긴 채 캐릭터 선택으로 돌아간다. 선택 화면에서 접속에 실패한 경우는 화면이 직접 안내한다
        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();

            // 보스전 미터는 접속이 끊기면 실시간 값만 비운다. 결과 화면은 다음 보스전의 새 SessionEpoch 수신 때 교체된다
            Shinshi.Combat.CombatMeterClient.ClearRealtime();

            // 소셜 스냅샷, 쓰기 잠금, 수신 확인 대기와 귓속말 답장 상대는 연결마다 비운다. 전달 ID 중복 기억은 대화 기록을 따른다
            SocialClient.ResetConnection();
            ChatClient.ResetConnection();

#if !UNITY_SERVER
            bool lostGameScene = (!IsTransferringServer) && (!NetworkServer.active) && (IsGameSceneActive());

            // 정산 뒤 결과 창 대기 시간이 지나 던전이 연결을 닫은 경우처럼, 출정 결과가 저장돼 귀환할 수 있는 던전에서 끊기면
            // 캐릭터 선택이 아니라 마을로 보낸다. 귀환 흐름은 첫 단계(입장권 요청)에서 Mirror를 건드리지 않아 끊김 처리 중에 시작해도 된다.
            // 흐름이 서버 이동 중 표시를 세우므로 대화 기록도 이어서 둔다
            if ((lostGameScene) && (SortieExitRules.ShouldReturnToTownOnDisconnect(_serverRole == EServerRole.Dungeon,
                SortieClient.HasStatus, SortieClient.Current)) && (TryReturnToTown(null)))
            {
                return;
            }
#endif

            // 마을과 던전 사이를 옮길 때는 대화와 전달 ID 중복 기억을 이어서 둔다. 새 서버가 다시 보낸 줄도 걸러진다
            if (!IsTransferringServer)
            {
                ChatClient.ClearHistory();
            }

#if !UNITY_SERVER
            if (lostGameScene)
            {
                LobbyFlow.ReturnToCharacterSelect("서버와 연결이 끊겼다. 다시 게임 시작을 누른다.");
            }
#endif
        }

        /// <summary> 던전에서 마을로 돌아가는 흐름을 시작한다. 이미 서버를 옮기는 중이거나 돌아갈 캐릭터가 없으면 거짓이고, listener는 결과를 한 번 받는다 </summary>
        public bool TryReturnToTown(Action<ERejectReason> listener)
        {
            string characterId = LobbyFlow.ActiveCharacterId;
            if ((_returnFlow == null) || (_returnFlow.IsRunning) || (_isLeaving) || (IsTransferringServer)
                || (NetworkServer.active) || (string.IsNullOrEmpty(characterId)))
            {
                return false;
            }

            _returnListener = listener;
            StartCoroutine(_returnFlow.Run(characterId, _onReturnFinished));
            return true;
        }

        /// <summary> 메뉴의 캐릭터 선택. 귀환 전인 던전 출정이면 포기를 보내고 이탈 저장을 기다린 뒤 끊는다. 이미 옮기는 중이면 거짓 </summary>
        public bool LeaveToCharacterSelect()
        {
            if ((IsTransferringServer) || (_isLeaving) || (NetworkServer.active))
            {
                return false;
            }

            if ((NetworkClient.isConnected) && (SortieExitRules.ShouldWaitBeforeLeave(_serverRole == EServerRole.Dungeon,
                SortieClient.HasStatus, SortieClient.Current)))
            {
                StartCoroutine(CoLeaveAfterDeparture());
                return true;
            }

            LeaveNow();
            return true;
        }

        // 그대로 끊으면 던전이 재접속 유예 동안 계정 임대를 쥐어 다음 마을, 프롤로그 입장이 이미 접속 중으로 막힌다
        private IEnumerator CoLeaveAfterDeparture()
        {
            _isLeaving            = true;
            _leaveAbandonRejected = false;
            IsTransferringServer  = true;
            if (_onLeaveAbandonResult == null)
            {
                _onLeaveAbandonResult = OnLeaveAbandonResult;
            }

            SortieClient.AbandonResult += _onLeaveAbandonResult;
            try
            {
                if (SortieExitRules.ShouldAbandonBeforeLeave(_serverRole == EServerRole.Dungeon, SortieClient.HasStatus,
                    SortieClient.Current))
                {
                    SortieClient.RequestAbandon();
                }

                float deadline = Time.realtimeSinceStartup + LEAVE_DEPARTURE_WAIT_SECONDS;
                while ((NetworkClient.isConnected) && (!_leaveAbandonRejected) && (Time.realtimeSinceStartup < deadline)
                    && (!SortieExitRules.IsLeaveReady(SortieClient.HasStatus, SortieClient.Current)))
                {
                    yield return null;
                }
            }
            finally
            {
                SortieClient.AbandonResult -= _onLeaveAbandonResult;
                IsTransferringServer = false;
                _isLeaving           = false;
            }

            LeaveNow();
        }

        // 출정이 이미 끝나 거절된 포기(SessionTerminal)는 곧 종료 저장으로 귀환 가능이 되므로 계속 기다린다
        private void OnLeaveAbandonResult(SortieAbandonResultMessage message)
        {
            if ((message.Reason != ERejectReason.None) && (message.Reason != ERejectReason.SessionTerminal))
            {
                _leaveAbandonRejected = true;
            }
        }

        private void LeaveNow()
        {
            // 의도한 종료를 접속 장애로 처리해 씬을 두 번 열지 않도록 서버 이동 중 표시를 잠시 세운다
            IsTransferringServer = true;
            try
            {
                StopClient();
            }
            finally
            {
                IsTransferringServer = false;
            }

            ChatClient.ClearHistory();
            LobbyFlow.ReturnToCharacterSelect(string.Empty);
        }

        // 실패했고 연결이 남지 않았으면(마을 접속 단계 실패, 던전이 이미 끊음) 캐릭터 선택으로 보낸다. 입장권 단계 실패는 던전에 남는다
        private void OnReturnToTownFinished(ERejectReason reason)
        {
            Action<ERejectReason> listener = _returnListener;
            _returnListener = null;
            if ((reason != ERejectReason.None) && (!NetworkClient.isConnected) && (!NetworkServer.active))
            {
                LobbyFlow.ReturnToCharacterSelect("마을 서버에 접속하지 못했다. 다시 게임 시작을 누른다.");
            }

            listener?.Invoke(reason);
        }

        // 마을 씬이나 던전 씬에 있다. 로비, 캐릭터 선택에서의 접속 실패는 화면이 직접 안내한다
        private bool IsGameSceneActive()
        {
            return (!string.IsNullOrEmpty(_townOnlineScene))
                && ((Utils.IsSceneActive(_townOnlineScene)) || (DungeonMapRules.IsDungeonSceneName(SceneManager.GetActiveScene().name)));
        }

#if !UNITY_SERVER
        private void OnSortieTicketReceived(SortieTicketMessage ticket)
        {
            if ((_departFlow == null) || (NetworkServer.active)
                || (!SortieDepartFlow.CanBegin(_departFlow.IsRunning, IsTransferringServer, NetworkClient.isConnected)))
            {
                Debug.LogWarning("[Network] 던전 입장권을 받았지만 이미 서버를 옮기는 중이거나 마을에 연결돼 있지 않아 무시한다");
                return;
            }

            // 지난 보스전 전투 기록이 이번 출정 결과 화면에 남지 않게 출정을 떠날 때 비운다
            Shinshi.Combat.CombatMeterClient.Clear();
            StartCoroutine(_departFlow.Run(ticket, _onDepartFinished));
        }

        // 던전 접속 단계에서 실패하면 마을 연결도 끊긴 상태라 캐릭터 선택으로 보낸다. 입장권 확인 실패는 마을에 남는다
        private void OnSortieDepartFinished(ERejectReason reason)
        {
            SortieDepartFinished?.Invoke(reason);
            if ((reason != ERejectReason.None) && (!NetworkClient.active) && (!NetworkServer.active))
            {
                LobbyFlow.ReturnToCharacterSelect("던전 서버에 접속하지 못했다. 다시 게임 시작을 누른다.");
            }
        }
#endif

        public override void OnStartClient()
        {
            base.OnStartClient();
            PartyAuctionClient.RegisterHandlers();
            SortieClient.RegisterHandlers();
            TownPartyClient.RegisterHandlers();
            TownGrowthClient.RegisterHandlers();
            TownLifeClient.RegisterHandlers();
            TownMarketClient.RegisterHandlers();
            TownTradeClient.RegisterHandlers();
            TownProgressClient.RegisterHandlers();
            PrologueClient.RegisterHandlers();
            ServerMenuGuideRecord.Instance.Refresh();
            ChatClient.RegisterHandlers();
            SocialClient.RegisterHandlers();
            Shinshi.Combat.PotionClient.RegisterHandlers();
            Shinshi.Combat.CombatMeterClient.RegisterHandlers();
            RegisterEnemyReplicas();
            if (playerPrefab == null)
            {
                return;
            }

            // 기본 프리팹 등록을 바꿔 클라이언트의 초기 보간 설정도 서버와 같은 간격으로 만든다
            NetworkClient.UnregisterPrefab(playerPrefab);
            NetworkClient.RegisterPrefab(playerPrefab, SpawnPlayerReplica, DestroyPlayerReplica);
        }

        // 양쪽의 생성 경로에서 같은 메서드를 호출해야 스냅샷 보간 간격도 일치한다
        /// <summary> 플레이어 생성 시 위치 복제 주기를 적용한다, 지속 상태의 syncInterval은 바꾸지 않는다 </summary>
        public void ApplyPlayerReplication(NetworkIdentity identity)
        {
            ApplyTransformReplication(identity, Constants.Network.PLAYER_SYNC_INTERVAL_SECONDS);
        }

        /// <summary> 적과 소환물의 서버 스폰, 클라이언트 생성 시 위치 복제 주기를 적용한다 </summary>
        public void ApplyEnemyReplication(NetworkIdentity identity)
        {
            ApplyTransformReplication(identity, Constants.Network.ENEMY_SYNC_INTERVAL_SECONDS);
        }

        /// <summary> 마을 원격 캐릭터의 서버 스폰, 클라이언트 생성 시 위치 복제 주기를 적용한다 </summary>
        public void ApplyTownRemoteReplication(NetworkIdentity identity)
        {
            ApplyTransformReplication(identity, Constants.Network.TOWN_REMOTE_SYNC_INTERVAL_SECONDS);
        }

        // 씬에 인증기가 없으면 클라이언트 인증기를 붙인다. 서버 부트스트랩은 서버를 열기 전에 서버 인증기로 바꾼다
        private void EnsureAuthenticator()
        {
            if (authenticator != null)
            {
                return;
            }

            if (!TryGetComponent(out ShinshiClientAuthenticator clientAuthenticator))
            {
                clientAuthenticator = gameObject.AddComponent<ShinshiClientAuthenticator>();
            }

            authenticator = clientAuthenticator;
        }

        private void ApplyNetworkSettings()
        {
            sendRate          = Constants.Network.MIRROR_SEND_RATE;
            headlessStartMode = HeadlessStartOptions.DoNothing;
        }

        private void ApplyRolePlayerReplication(NetworkIdentity identity)
        {
            if (_serverRole == EServerRole.Town)
            {
                ApplyTownRemoteReplication(identity);
                return;
            }

            ApplyPlayerReplication(identity);
        }

        private GameObject SpawnPlayerReplica(SpawnMessage message)
        {
            GameObject player = Instantiate(playerPrefab, message.position, message.rotation);
            if (!player.TryGetComponent(out NetworkIdentity identity))
            {
                Destroy(player);
                BootFailure.Stop("플레이어 프리팹 루트에 NetworkIdentity가 없다");
                return null;
            }

            ApplyRolePlayerReplication(identity);
            return player;
        }

        private void DestroyPlayerReplica(GameObject player)
        {
            Destroy(player);
        }

        // spawnPrefabs에는 서버가 스폰하는 적과 소환물(동료)만 둔다. 서버 스포너가 ApplyEnemyReplication을 쓰므로
        // 클라이언트 생성도 같은 간격을 적용하도록 Mirror의 기본 등록을 바꾼다
        private void RegisterEnemyReplicas()
        {
            _spawnPrefabsByAssetId.Clear();
            for (int i = 0; i < spawnPrefabs.Count; ++i)
            {
                GameObject prefab = spawnPrefabs[i];
                if ((prefab == null) || (!prefab.TryGetComponent(out NetworkIdentity identity)))
                {
                    continue;
                }

                _spawnPrefabsByAssetId[identity.assetId] = prefab;
                NetworkClient.UnregisterPrefab(prefab);
                NetworkClient.RegisterPrefab(prefab, SpawnEnemyReplica, DestroyEnemyReplica);
            }
        }

        private GameObject SpawnEnemyReplica(SpawnMessage message)
        {
            if (!_spawnPrefabsByAssetId.TryGetValue(message.assetId, out GameObject prefab))
            {
                Debug.LogError($"[Network] 등록되지 않은 스폰 프리팹이다, assetId={message.assetId}");
                return null;
            }

            GameObject replica = Instantiate(prefab, message.position, message.rotation);
            if (!replica.TryGetComponent(out NetworkIdentity identity))
            {
                Destroy(replica);
                return null;
            }

            ApplyEnemyReplication(identity);
            return replica;
        }

        // 호스트에서는 서버 개체와 같은 개체가 넘어온다. 서버 스포너가 풀로 다시 쓰므로 여기서 지우지 않는다
        private void DestroyEnemyReplica(GameObject replica)
        {
            if (NetworkServer.active)
            {
                return;
            }

            Destroy(replica);
        }

        /// <summary> 프리팹 루트에 위치 복제용 NetworkTransform이 있는지 본다, Mirror.Components를 못 보는 서버 어셈블리와 검증용이다 </summary>
        public static bool HasRootTransformReplication(GameObject prefab)
        {
            return (prefab != null) && (prefab.TryGetComponent(out NetworkTransformBase _));
        }

        private static void ApplyTransformReplication(NetworkIdentity identity, float intervalSeconds)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            // 생성 때만 순회한다. NetworkTransform의 multiplier는 이 간격에서 계산되는 읽기 전용 값이다
            NetworkTransformBase[] transforms = identity.GetComponentsInChildren<NetworkTransformBase>(true);
            for (int i = 0; i < transforms.Length; ++i)
            {
                transforms[i].syncInterval  = intervalSeconds;
                transforms[i].syncDirection = SyncDirection.ServerToClient;
            }
        }

        private static bool TryReadLaunchArgument(string[] args, string name, out string value, out string error)
        {
            value = null;
            error = null;
            bool found = false;
            for (int i = 0; i < args.Length; ++i)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (found)
                {
                    error = $"{name}: 실행 인자가 중복됐다";
                    return false;
                }

                if ((i == (args.Length - 1)) || (string.IsNullOrWhiteSpace(args[i + 1]))
                    || (args[i + 1].StartsWith("-", StringComparison.Ordinal)))
                {
                    error = $"{name}: 실행 인자 뒤에 값이 필요하다";
                    return false;
                }

                value = args[i + 1];
                found = true;
                ++i;
            }

            return found;
        }

        private static bool ContainsWhitespace(string value)
        {
            for (int i = 0; i < value.Length; ++i)
            {
                if ((char.IsWhiteSpace(value[i])) || (char.IsControl(value[i])))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
