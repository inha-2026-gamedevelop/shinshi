// System
using System;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 서버 프로세스의 시작 순서, 역할을 읽고 GameDB를 확인한 뒤 서버를 연다 </summary>
    public static class ServerBootstrap
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서비스 자격과 현재 서버 임대, 전송 계층과 ServerPresence가 함께 쓴다
        private static ServerCredentials _credentials;

        /****************************************
        *              Properties
        ****************************************/

        public static bool               IsRunning   { get; private set; }
        public static ServerLaunchConfig Config      { get; private set; }
        public static string             ContentHash { get; private set; }

        // 풀 등록과 로그에 싣는 빌드 ID. 출시 빌드는 빌드 신원 파일 값만 쓰고 표시용 bundleVersion으로 대신하지 않는다
        public static string             BuildId     { get; private set; }

        // 서비스 자격이 있을 때만 있다. 저장 큐, 성장 요청 같은 서버 쪽 백엔드 호출이 모두 이것 하나를 쓴다
        public static BackendGateway     Backend     { get; private set; }

        // 던전 역할에서만 있다. 출정 명단, start 영수증, 저장 큐, 정산을 맡는다
        public static DungeonSessionHost DungeonHost { get; private set; }

        // 마을 역할에서만 있다. 파티, 성장 중계, 생활 요청을 맡는다
        public static TownServerHost     Town        { get; private set; }

        // 서비스 자격이 있을 때만 있다. 임대 발급, 풀 등록, heartbeat를 맡는다
        public static ServerPresence     Presence    { get; private set; }

        // 서버가 열린 뒤 항상 있다. 출정 종료나 존재 상실 때 프로세스를 내린다
        public static ServerShutdownController Shutdown { get; private set; }

        private static bool IsDevelopmentRuntime
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        // 서버 빌드는 역할 없이 실행되면 클라이언트로 넘어가지 않고 멈춘다
        private static bool IsServerBuild
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 상태가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            IsRunning    = false;
            Config       = default;
            ContentHash  = null;
            BuildId      = null;
            Backend      = null;
            DungeonHost  = null;
            Town         = null;
            Presence     = null;
            Shutdown     = null;
            _credentials = null;
        }

        // 씬의 네트워크 매니저가 Awake로 전송 계층을 잡은 뒤에 시작해야 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args       = Environment.GetCommandLineArgs();
            string   editorRole = TakeEditorRole();
            if (!string.IsNullOrEmpty(editorRole))
            {
                args = new[] { Constants.Network.ARG_ROLE, editorRole };
            }

            if (!ServerLaunchConfig.TryParse(args, Environment.GetEnvironmentVariable, out ServerLaunchConfig config, out string error))
            {
                if (error != null)
                {
                    BootFailure.Stop(error);
                    return;
                }

                if (IsServerBuild)
                {
                    BootFailure.Stop("서버 빌드인데 역할이 없다. -role town 또는 -role dungeon으로 실행한다");
                }

                return;
            }

            // 서버 빌드의 표준 출력을 배포 로그 형식으로 바꾼다. 빌드 ID는 신원 파일을 읽은 뒤 채우고 그 전 줄은 null이다
            if (IsServerBuild)
            {
                ServerJsonLogHandler.Install(config.RoleName, config.InstanceId, config.ContentVersion, ReadSessionId);
            }

            // GameDBBootstrap이 실패를 이미 기록하고 멈췄다
            if (!GameDB.IsInitialized)
            {
                return;
            }

            if (!string.Equals(config.ContentVersion, GameDB.ContentVersion, StringComparison.Ordinal))
            {
                BootFailure.Stop("CONTENT_VERSION이 GameDB 콘텐츠 버전과 다르다. 배포 설정과 데이터를 맞춘다");
                return;
            }

            string contentHash;
            try
            {
                contentHash = GameDBContentHash.Compute();
            }
            catch (Exception exception)
            {
                // 데이터나 자격 정보가 예외 메시지에 들어갈 가능성이 있어 타입만 기록한다
                BootFailure.Stop($"contentHash 계산 실패 ({exception.GetType().Name})");
                return;
            }

            BuildIdentity identity = BuildIdentityLoader.LoadForRuntime(contentHash, out EBuildIdentityStatus identityStatus);
            string buildId = LobbyFlow.ResolveBuildId(IsDevelopmentRuntime, null, identity, identityStatus);
            if (buildId == null)
            {
                BootFailure.Stop($"빌드 신원 파일을 쓸 수 없다 ({identityStatus}). 빌드 결과의 StreamingAssets를 함께 배포한다");
                return;
            }

            BuildId = buildId;
            ServerJsonLogHandler.SetBuildId(buildId);

            ShinshiNetworkManager manager = NetworkManager.singleton as ShinshiNetworkManager;
            if (manager == null)
            {
                BootFailure.Stop("씬에 ShinshiNetworkManager가 없다. Bootstrap 씬으로 실행한다");
                return;
            }

            if (!(Transport.active is PortTransport portTransport))
            {
                BootFailure.Stop("포트를 지정할 수 있는 전송 계층이 없다");
                return;
            }

            // 클라이언트가 접속하기 전에 서버 판정 처리기를 ServerLinks에 연결해야 첫 입력부터 받는다
            var sessions = new ServerSessionRegistry(Constants.Session.MAX_SESSION_PARTICIPANTS);
            try
            {
                CombatServerInstaller.Install(sessions);
            }
            catch (Exception exception)
            {
                // 데이터 값이 예외 메시지에 들어갈 가능성이 있어 타입만 기록한다
                BootFailure.Stop($"전투 설치 실패 ({exception.GetType().Name})");
                return;
            }

            EnableCompanionObjects(manager);
            EnableBossObjects(manager);
            EnableEnemyObjects(manager);

            if (!TryCreateJoinTokenVerifier(args, config, manager, out IJoinTokenVerifier verifier, out string verifierError))
            {
                BootFailure.Stop(verifierError);
                return;
            }

            // 등록 주소는 서버를 열기 전에 확인한다. 틀린 주소로 등록하면 배정받은 파티가 붙지 못한다
            string publicAddress = null;
            if (Backend != null)
            {
                if (!ServerLaunchConfig.TryReadValue(args, Environment.GetEnvironmentVariable, Constants.Network.ARG_PUBLIC_ADDRESS,
                    Constants.Network.ENV_PUBLIC_ADDRESS, out string rawAddress, out string addressError))
                {
                    BootFailure.Stop(addressError);
                    return;
                }

                if (!PublicAddressRules.TryResolve(rawAddress, IsDevelopmentRuntime, out publicAddress, out addressError))
                {
                    BootFailure.Stop(addressError);
                    return;
                }
            }

            // 첫 입장 전에 출정 명단과 캐릭터 유지 경로를 전투에 연결한다. 마을은 출정이 없어 기존 명단만 쓴다
            DungeonSessionHost dungeonHost = null;
            if (config.Role == EServerRole.Dungeon)
            {
                // 실행 설정 보스(-boss-id)는 보스전 계약이 없는 개발 출정의 기본값이다. 계약이 있으면 호스트가 계약 보스와 난이도로
                // 보스, 보상 표, 경매 재료를 고른다
                if (!ServerLaunchConfig.TryReadBossId(args, Environment.GetEnvironmentVariable, IsKnownBoss, out string bossId,
                    out string bossError))
                {
                    BootFailure.Stop(bossError);
                    return;
                }

                if (!TryCreateDungeonHost(sessions, out dungeonHost, out string hostError))
                {
                    BootFailure.Stop(hostError);
                    return;
                }

                dungeonHost.AuctionAudience = new MirrorAuctionAudience();
                dungeonHost.SortieAudience  = new MirrorSortieAudience();
                dungeonHost.ConfigureContent(GameDB.Waves, GameDB.Rewards, GameDB.Talismans, bossId, IsKnownBoss);

                // TODO(기웅): 일반전 웨이브 진행기(스폰 디렉터, 적 두뇌) 구현이 생기면 여기서 dungeonHost.WaveFactory에 넣는다.
                // 없으면 일반전 계약도 경고와 함께 웨이브 없는 개발 세션으로 열린다(IWaveSessionFactory 주석의 규칙을 지킨다)
                CombatServerInstaller.Runtime.ConfigureBoss(bossId);
                CombatServerInstaller.Runtime.BindSession(dungeonHost);
                manager.PlayerRetention = CombatServerInstaller.Runtime;
                DungeonHost             = dungeonHost;
            }

            // 마을은 연결이 끊긴 계정의 접속 임대를 바로 푼다. 백엔드가 없으면 임대도 없다
            AccountLeaseReleaser accountLeases = ((config.Role == EServerRole.Town) && (Backend != null))
                ? new AccountLeaseReleaser(Backend, config.InstanceId, null, LogBackendWarning)
                : null;

            // 인증기를 서버 시작 전에 바꿔야 첫 연결부터 인증을 거친다
            ShinshiServerAuthenticator authenticator = InstallAuthenticator(manager, sessions, contentHash, verifier, dungeonHost,
                config.Role, accountLeases);
            if (dungeonHost != null)
            {
                dungeonHost.ParticipantEvicted -= authenticator.DisconnectParticipant;
                dungeonHost.ParticipantEvicted += authenticator.DisconnectParticipant;
            }

            portTransport.Port = config.Port;
            manager.ConfigureServerRole(config.Role);
            Config      = config;
            ContentHash = contentHash;
            try
            {
                manager.StartServer();
                if (dungeonHost != null)
                {
                    // 서버가 열린 뒤 등록해야 Listen의 초기화에 지워지지 않는다. 입찰자는 인증된 연결의 참가자 번호로 정한다
                    NetworkServer.ReplaceHandler<AuctionBidMessage>((connection, message) =>
                    {
                        if (connection.authenticationData is SessionBinding binding)
                        {
                            dungeonHost.HandleAuctionBid(binding.ParticipantId, message.Amount);
                        }
                    });
                }

                // 마을도 등록한다. 처리기가 없으면 물약 키를 누른 클라이언트를 Mirror가 모르는 메시지로 끊는다
                if (CombatServerInstaller.Runtime != null)
                {
                    CombatServerInstaller.Runtime.RegisterMessageHandlers();
                }

                // 마을 파티, 성장 중계, 생활 처리기도 서버를 연 뒤에 등록한다. 해금 진행 값 미확인 허용은 개발 빌드만이다
                if (config.Role == EServerRole.Town)
                {
                    Town = TownServerInstaller.Install(manager, Backend, config.ContentVersion, IsDevelopmentRuntime,
                        LogBackendWarning);
                }
            }
            catch (Exception exception)
            {
                BootFailure.Stop($"서버 시작 실패 ({exception.GetType().Name})");
                return;
            }

            if (!NetworkServer.active)
            {
                BootFailure.Stop($"서버를 열지 못했다, 포트 {config.Port}");
                return;
            }

            IsRunning = true;
            StartProcessLifecycle(manager, config, publicAddress, dungeonHost);

            Debug.Log($"[ServerBoot] 서버 시작 - role={config.RoleName}, instance={config.InstanceId}, port={config.Port}, " +
                $"contentVersion={config.ContentVersion}, contentHash={ContentHash}, buildId={BuildId}, " +
                $"protocol={Constants.Network.PROTOCOL_VERSION}, dungeonPoolSize={config.DungeonPoolSize}, " +
                $"sendRate={Constants.Network.MIRROR_SEND_RATE}");
            ServerJsonLogHandler.LogEvent(ServerLogLine.LEVEL_INFO, ServerLogLine.EVENT_SERVER_STARTED, null, null,
                ServerLogLine.FormatPortData(config.Port));
        }

        // 출시 빌드는 서비스 자격이 없으면 저장과 정산을 할 수 없어 던전을 열지 않는다.
        // 개발 빌드는 기록만 남기는 저장과 정산으로 대신하고, 경제 결과가 남지 않는다고 경고한다
        // 보스 카탈로그에 정의가 있는지만 본다. 수치 미지정(IsTuned 거짓)은 보스 생성이 오류를 남기고 보스 없이 진행한다
        private static bool IsKnownBoss(string bossId)
        {
            return GameDB.BossPatterns.TryGetBoss(bossId, out _);
        }

        private static bool TryCreateDungeonHost(ServerSessionRegistry sessions, out DungeonSessionHost host, out string error)
        {
            host  = null;
            error = null;
            if ((Backend == null) && (!IsDevelopmentRuntime))
            {
                error = $"던전 서버는 서비스 자격이 필요하다. {Constants.Network.ENV_BACKEND_SERVICE_TOKEN} 또는 " +
                    $"{Constants.Network.ARG_BACKEND_SERVICE_TOKEN_FILE}로 넣는다";
                return false;
            }

            if (Backend == null)
            {
                Debug.LogWarning("[ServerBoot] 서비스 자격이 없어 던전 저장과 정산을 개발 기록으로 대신한다. 경제 결과는 남지 않는다");
            }

            try
            {
                host = new DungeonSessionHost(GameDB.Session, sessions, Backend, IsDevelopmentRuntime, LogBackendWarning);
            }
            catch (Exception exception)
            {
                // 데이터 값이 예외 메시지에 들어갈 가능성이 있어 타입만 기록한다
                error = $"던전 출정 준비 실패 ({exception.GetType().Name})";
                return false;
            }

            return true;
        }

        // 동료 프리팹이 없는 씬이면 동료 판정만 돌고 클라이언트에 개체가 보이지 않는다. 서버를 막지는 않는다
        private static void EnableCompanionObjects(ShinshiNetworkManager manager)
        {
            GameObject prefab = CompanionNetworkSpawner.FindPrefab(manager);
            if (prefab == null)
            {
                Debug.LogWarning("[Combat] 네트워크 매니저 spawnPrefabs에 동료 프리팹이 없어 동료 개체를 스폰하지 않는다");
                return;
            }

            CombatServerInstaller.Runtime.EnableCompanionObjects(
                new CompanionNetworkSpawner(prefab, manager, Constants.Combat.MAX_COMPANION_OBJECTS));
        }

        // 일반 적 프리팹(PF_NetEnemy)이 아직 없다. 없으면 적 판정만 돌고 클라이언트에 적이 보이지 않는다. 서버를 막지는 않는다
        private static void EnableEnemyObjects(ShinshiNetworkManager manager)
        {
            GameObject prefab = EnemyNetworkSpawner.FindPrefab(manager);
            if (prefab == null)
            {
                Debug.LogWarning($"[Enemy] 네트워크 매니저 spawnPrefabs에 일반 적 프리팹({EnemyNetworkSpawner.PREFAB_NAME})이 없어 " +
                    "적 개체를 스폰하지 않는다");
                return;
            }

            CombatServerInstaller.Runtime.EnableEnemyObjects(
                new EnemyNetworkSpawner(prefab, manager, Constants.Combat.MAX_COMBAT_TARGETS));
        }

        // 보스 프리팹이 없는 씬이면 보스 판정만 돌고 클라이언트에 보스와 공격 예고가 보이지 않는다. 서버를 막지는 않는다
        private static void EnableBossObjects(ShinshiNetworkManager manager)
        {
            GameObject prefab = BossNetworkSpawner.FindPrefab(manager);
            if (prefab == null)
            {
                Debug.LogWarning("[Boss] 네트워크 매니저 spawnPrefabs에 보스 프리팹이 없어 보스 개체를 스폰하지 않는다");
                return;
            }

            CombatServerInstaller.Runtime.EnableBossObjects(new BossNetworkSpawner(prefab));
        }

        private static ShinshiServerAuthenticator InstallAuthenticator(ShinshiNetworkManager manager, ServerSessionRegistry sessions,
            string contentHash, IJoinTokenVerifier verifier, IParticipantAdmission admission, EServerRole role,
            AccountLeaseReleaser accountLeases)
        {
            if (!manager.TryGetComponent(out ShinshiServerAuthenticator authenticator))
            {
                authenticator = manager.gameObject.AddComponent<ShinshiServerAuthenticator>();
            }

            authenticator.Configure(verifier, sessions, contentHash, admission, role, accountLeases);
            manager.authenticator = authenticator;

            // 같은 개체에 두 번 걸리지 않게 먼저 뗀다
            manager.ServerDisconnected -= authenticator.HandleServerDisconnected;
            manager.ServerDisconnected += authenticator.HandleServerDisconnected;
            return authenticator;
        }

        // 서버가 열린 뒤 한 번 부른다. 서비스 자격이 있으면 임대 -> 등록 -> heartbeat를 시작하고, 역할과 무관하게 종료 경로를 건다
        private static void StartProcessLifecycle(ShinshiNetworkManager manager, in ServerLaunchConfig config, string publicAddress,
            DungeonSessionHost dungeonHost)
        {
            ServerPresence presence = null;
            if ((Backend != null) && (_credentials != null))
            {
                presence = new ServerPresence(Backend, _credentials, config.Role, publicAddress, config.Port, BuildId,
                    config.ContentVersion, null, LogBackendWarning);
                presence.Start();
                Debug.Log($"[ServerBoot] 서버 풀 존재 신호 시작 - instance={config.InstanceId}, bootId={presence.BootId}");
            }

            var shutdown = new ServerShutdownController(new UnityServerProcessControl(), presence, dungeonHost, null,
                LogBackendWarning);
            if (!manager.TryGetComponent(out ServerProcessDriver driver))
            {
                driver = manager.gameObject.AddComponent<ServerProcessDriver>();
            }

            driver.Bind(presence, shutdown);
            if (ServerJsonLogHandler.IsInstalled)
            {
                var metrics = new ServerMetricsSampler(dungeonHost);
                if (CombatServerInstaller.Runtime != null)
                {
                    CombatServerInstaller.Runtime.Metrics = metrics;
                }

                driver.BindMetrics(metrics);
            }

            Presence = presence;
            Shutdown = shutdown;
        }

        // 던전 프로세스는 출정 하나만 맡으므로 그 출정 번호를 모든 줄에 싣는다. 시작 전과 마을은 null이다
        private static string ReadSessionId()
        {
            return DungeonHost?.SessionId;
        }

        // 출시 빌드는 백엔드 교환 검증기만 쓴다. 개발 검증기는 형식만 보므로 출시 서버 빌드에는 컴파일되지도 않는다.
        // 에디터와 개발 빌드는 기본이 개발 검증기이고 JOIN_TOKEN_VERIFIER=api로 백엔드 검증기를 고른다.
        // 서비스 자격이 있으면 개발 검증기를 써도 게이트웨이를 열어 다른 백엔드 호출이 쓸 수 있게 한다
        private static bool TryCreateJoinTokenVerifier(string[] args, in ServerLaunchConfig config, ShinshiNetworkManager manager,
            out IJoinTokenVerifier verifier, out string error)
        {
            verifier = null;
            if (!BackendLaunchSettings.TryParse(args, Environment.GetEnvironmentVariable, ReadSecretFile, IsDevelopmentRuntime,
                out BackendLaunchSettings settings, out error))
            {
                return false;
            }

            if (settings.HasServiceToken)
            {
                if (!BackendLaunchSettings.IsBaseUrlAllowed(config.BackendBaseUrl, IsDevelopmentRuntime))
                {
                    error = $"{Constants.Network.ENV_BACKEND_BASE_URL} ({Constants.Network.ARG_BACKEND_BASE_URL}): " +
                        "출시 빌드는 같은 호스트가 아니면 HTTPS로만 서비스 자격을 보낸다";
                    return false;
                }

                Backend = CreateGateway(config, settings.ServiceToken, manager);
            }

            if (settings.UseApiVerifier)
            {
                verifier = new ApiJoinTokenVerifier(Backend, config.InstanceId, config.Role, null, LogBackendWarning);
                Debug.Log($"[ServerBoot] 접속 토큰 검증기 - api, instance={config.InstanceId}");
                return true;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            verifier = new DevJoinTokenVerifier();
            Debug.Log("[ServerBoot] 접속 토큰 검증기 - dev(형식만 확인, 개발 전용)");
            return true;
#else
            error = "출시 빌드에 개발 검증기가 없다";
            return false;
#endif
        }

        private static BackendGateway CreateGateway(in ServerLaunchConfig config, string serviceToken, ShinshiNetworkManager manager)
        {
            // 서비스 자격은 임대 발급에만 쓰고, 나머지 서버 경로는 ServerPresence가 받은 임대 토큰을 싣는다
            _credentials  = new ServerCredentials(serviceToken, config.InstanceId, null);
            var transport = new HttpClientBackendTransport(new Uri(config.BackendBaseUrl), _credentials);
            var gateway   = new BackendGateway(transport, null, null, LogBackendWarning, LogBackendError);
            if (!manager.TryGetComponent(out BackendGatewayPump pump))
            {
                pump = manager.gameObject.AddComponent<BackendGatewayPump>();
            }

            pump.Bind(gateway);
            return gateway;
        }

        // 경로가 없거나 권한이 없으면 null. 예외 메시지에 경로가 들어가므로 그대로 기록하지 않는다
        private static string ReadSecretFile(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static void LogBackendWarning(string message)
        {
            Debug.LogWarning(message);
        }

        private static void LogBackendError(string message)
        {
            Debug.LogError(message);
        }

        // 에디터 메뉴로 서버 역할을 골랐으면 한 번만 쓰고 지운다. 다음 플레이는 다시 클라이언트다
        private static string TakeEditorRole()
        {
#if UNITY_EDITOR
            string role = UnityEditor.SessionState.GetString(Constants.Network.EDITOR_SERVER_ROLE_KEY, string.Empty);
            UnityEditor.SessionState.EraseString(Constants.Network.EDITOR_SERVER_ROLE_KEY);
            return role;
#else
            return string.Empty;
#endif
        }
    }
}
