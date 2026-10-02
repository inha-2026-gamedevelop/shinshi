// System
using System;
using System.Globalization;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 네트워크 주기를 적용한다, 서버 시작은 서버 부트스트랩만 한다 </summary>
    public class ShinshiNetworkManager : NetworkManager
    {
        /****************************************
        *                Fields
        ****************************************/

        private EServerRole _serverRole = EServerRole.Town;

        /****************************************
        *              Properties
        ****************************************/

        // 서버에서 연결이 끊긴 뒤(플레이어 개체 정리 후) 낸다. Mirror 정적 이벤트는 매니저가 덮어쓰므로 이것을 구독한다
        public event Action<NetworkConnectionToClient> ServerDisconnected;

        // 서버 부트스트랩이 던전 역할에서 넣는다. 클라이언트 빌드에는 구현 타입이 없어 항상 null이다
        public IServerPlayerRetention PlayerRetention { private get; set; } = null;

        /****************************************
        *              Unity Event
        ****************************************/

        public override void Awake()
        {
            ApplyNetworkSettings();
            EnsureAuthenticator();
            base.Awake();
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
        public void ConfigureServerRole(EServerRole role)
        {
            if ((role != EServerRole.Town) && (role != EServerRole.Dungeon))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            _serverRole = role;
            ApplyNetworkSettings();
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

            base.OnServerDisconnect(conn);
            ServerDisconnected?.Invoke(conn);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            PartyAuctionClient.RegisterHandlers();
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
