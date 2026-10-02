// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class ServerPresenceTests
    {
        // 게이트웨이는 스레드 풀에서 보내므로 응답 함수는 테스트가 Send 전에 바꿔 둔다

        /****************************************
        *                Fields
        ****************************************/

        private const string INSTANCE_ID            = "7d1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string SERVICE_TOKEN          = "service-token-value";
        private const string LEASE_TOKEN            = "lease-token-value";
        private const string SECOND_TOKEN           = "lease-token-second";
        private const string ADDRESS                = "203.0.113.20";
        private const int    PORT                   = 7780;
        private const string BUILD_ID               = "build-1";
        private const string CONTENT_VERSION        = "content-1";
        private const double START_TIME             = 100.0;
        private const long   SERVER_TIME_MS         = 1_000_000L;
        private const long   LEASE_SECONDS          = 900L;
        private const long   MS_PER_SECOND          = 1000L;
        private const int    STATUS_OK              = 200;
        private const int    STATUS_UNAUTHORIZED    = 401;
        private const int    STATUS_CONFLICT        = 409;
        private const int    STATUS_UNAVAILABLE     = 503;
        private const int    STATUS_BAD_REQUEST     = 400;
        private const int    STATUS_FORBIDDEN       = 403;
        private const int    STATUS_NOT_FOUND       = 404;
        private const double JUST_BEFORE            = 0.01;
        private const double OTHER_BOOT_RETRY_AFTER = 5.0;
        private const string DATA_PATH              = "/v1/test-data";
        private const int    HEARTBEAT_AND_DRAIN    = 2;
        private const string SORTIE_ID              = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string KEY_ONE                = "p:0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";

        private double                    _now;
        private ConcurrentQueue<TimeSpan> _delays;
        private List<string>              _warnings;
        private ServerCredentials         _credentials;
        private FakeBackendTransport      _transport;
        private BackendGateway            _gateway;

        // 스레드 풀의 가짜 전송기가 읽는다
        private volatile Func<BackendRequest, BackendTransportResult> _respond;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now                     = START_TIME;
            _delays                  = new ConcurrentQueue<TimeSpan>();
            _warnings                = new List<string>();
            _credentials             = new ServerCredentials(SERVICE_TOKEN, INSTANCE_ID, ReadClock);
            _respond                 = DefaultResponse;
            _transport               = new FakeBackendTransport((request, token) => Task.FromResult(_respond(request)));
            _transport.CanSendFilter = request => _credentials.TryGetAuthorization(request.Path, out _);
            _gateway                 = new BackendGateway(_transport, ZeroClock, FakeBackendTransport.ImmediateDelay(_delays), null, null);
        }

        [TearDown]
        public void TearDown()
        {
            _gateway.Dispose();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryGetAuthorization_LeaseAndOtherPaths_UsesServiceTokenOrLease()
        {
            Assert.IsTrue(_credentials.TryGetAuthorization(GameServerRoutes.SERVER_LEASE, out string serviceAuth));
            Assert.AreEqual(ApiRoutes.BEARER_PREFIX + SERVICE_TOKEN, serviceAuth);
            Assert.IsFalse(_credentials.TryGetAuthorization(GameServerRoutes.SERVER_REGISTER, out _),
                "임대 없이 서버 경로를 보내지 않는다");

            _credentials.SetLease(LEASE_TOKEN, START_TIME + LEASE_SECONDS);
            Assert.IsTrue(_credentials.TryGetAuthorization(JoinApiRoutes.JOIN_TOKEN_EXCHANGE, out string leaseAuth));
            Assert.AreEqual(ApiRoutes.BEARER_PREFIX + LEASE_TOKEN, leaseAuth, "서비스 자격을 다른 경로에 싣지 않는다");

            _now = START_TIME + LEASE_SECONDS;
            Assert.IsFalse(_credentials.TryGetAuthorization(GameServerRoutes.SERVER_REGISTER, out _), "만료된 임대는 쓰지 않는다");
        }

        [Test]
        public void Gateway_NoLease_ReturnsLeaseUnavailableWithoutSendingOrTrippingCircuit()
        {
            var            listener = new RecordingBackendListener();
            BackendRequest request  = BackendRequest.Post(GameServerRoutes.SERVER_REGISTER, new ServerHeartbeatRequest("boot"),
                null, "op", Constants.Network.API_TIMEOUT_SECONDS);

            for (int i = 0; i < Constants.Network.BACKEND_CIRCUIT_FAILURE_THRESHOLD + 1; ++i)
            {
                _gateway.Send(request, listener);
                FakeBackendTransport.WaitForCompleted(_gateway, 1);
                _gateway.Pump();
            }

            Assert.AreEqual(0, _transport.Calls);
            Assert.AreEqual(EBackendOutcome.LeaseUnavailable, listener.Last.Outcome);
            Assert.AreEqual(EBackendCircuitState.Closed, _gateway.CircuitState, "자격이 없는 시도는 백엔드 장애로 세지 않는다");
        }

        [Test]
        public void Start_LeaseThenRegisterThenHeartbeat_UsesNewBootIdEverywhere()
        {
            ServerPresence presence = CreatePresence();

            BringToRunning(presence);
            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();

            Assert.AreEqual(EServerPresenceStage.Running, presence.Stage);
            Assert.IsTrue(SessionContractIds.IsApiId(presence.BootId));
            BackendRequest[] requests = _transport.Requests.ToArray();
            Assert.AreEqual(3, requests.Length);
            Assert.AreEqual(GameServerRoutes.SERVER_LEASE, requests[0].Path);
            var lease = BackendJson.Deserialize<ServerLeaseRequest>(requests[0].BodyJson);
            Assert.AreEqual(INSTANCE_ID, lease.InstanceId);
            Assert.AreEqual(presence.BootId, lease.BootId);
            Assert.AreEqual(ContractCodes.ServerRole.DUNGEON, lease.ServerRole);

            Assert.AreEqual(GameServerRoutes.SERVER_REGISTER, requests[1].Path);
            var register = BackendJson.Deserialize<ServerRegisterRequest>(requests[1].BodyJson);
            Assert.AreEqual(presence.BootId, register.BootId);
            Assert.AreEqual(ADDRESS, register.Address);
            Assert.AreEqual(PORT, register.Port);

            Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_HEARTBEAT_FORMAT, INSTANCE_ID),
                requests[2].Path);
            Assert.AreEqual(presence.BootId, BackendJson.Deserialize<ServerHeartbeatRequest>(requests[2].BodyJson).BootId);
        }

        [Test]
        public void CreatePresence_EachProcess_GetsDifferentBootId()
        {
            Assert.AreNotEqual(CreatePresence().BootId, CreatePresence().BootId);
        }

        [Test]
        public void Tick_LeaseTransientFailure_DoesNotRegisterUntilLeaseArrives()
        {
            ServerPresence presence = CreatePresence();
            _respond = request => new BackendTransportResult(STATUS_UNAVAILABLE, null);

            presence.Start();
            Complete();
            presence.Tick();

            Assert.AreEqual(EServerPresenceStage.Leasing, presence.Stage);
            Assert.AreEqual(1, _transport.Calls, "임대가 없으면 등록을 보내지 않는다");

            _respond = DefaultResponse;
            _now += Constants.Network.SERVER_PRESENCE_RETRY_SECONDS;
            presence.Tick();
            Complete();

            Assert.AreEqual(EServerPresenceStage.Registering, presence.Stage);
            Assert.IsTrue(_credentials.HasValidLease);
        }

        [Test]
        public void Tick_BeforeLeaseExpiry_RenewsWithServiceCredential()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            int    before  = _transport.Calls;
            double renewAt = START_TIME + LEASE_SECONDS - Constants.Network.SERVER_LEASE_RENEW_MARGIN_SECONDS;

            _now = renewAt - 1.0;
            presence.Tick();
            Complete();
            int heartbeatsOnly = _transport.Calls;

            _respond = request => (request.Path == GameServerRoutes.SERVER_LEASE)
                ? LeaseResult(SECOND_TOKEN)
                : DefaultResponse(request);
            _now     = renewAt;
            presence.Tick();
            Complete();

            Assert.Greater(heartbeatsOnly, before);
            Assert.IsTrue(ContainsPath(GameServerRoutes.SERVER_LEASE, 2), "만료 60초 전에 임대를 다시 받는다");
            Assert.IsTrue(_credentials.TryGetAuthorization(GameServerRoutes.SERVER_REGISTER, out string auth));
            Assert.AreEqual(ApiRoutes.BEARER_PREFIX + SECOND_TOKEN, auth);
        }

        [Test]
        public void OnHeartbeat_Conflict_MarksLostAndShutdownExitsWithPresenceCode()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = new ServerShutdownController(control, presence, null, ReadClock, _warnings.Add);

            _respond = request => InstanceResult(GameSessionCodes.InstanceState.OFFLINE,
                ContractCodes.RejectReason.SESSION_TERMINAL, STATUS_CONFLICT);
            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();
            shutdown.Tick();

            Assert.IsTrue(presence.IsLost);
            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
            _now += Constants.Network.RECYCLE_FAULT_GRACE_SECONDS;
            shutdown.Tick();
            Assert.AreEqual(1, control.DisconnectCalls);
            _now += Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Exited, shutdown.Stage);
            Assert.AreEqual(Constants.Network.EXIT_CODE_PRESENCE_LOST, control.ExitCode);
        }

        [Test]
        public void OnHeartbeat_SuccessButOffline_MarksLost()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);

            _respond = request => InstanceResult(GameSessionCodes.InstanceState.OFFLINE, 0, STATUS_OK);
            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();

            Assert.IsTrue(presence.IsLost, "장애 취소로 Offline이 된 boot는 새 bootId로 다시 떠야 한다");
        }

        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.SESSION_TERMINAL)]
        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.SESSION_MISMATCH)]
        public void Start_LeaseUnrecoverable_MarksLost(int status, int reasonCode)
        {
            ServerPresence presence = CreatePresence();
            _respond = request => RejectionResult(status, reasonCode);

            presence.Start();
            Complete();

            Assert.IsTrue(presence.IsLost);
            Assert.IsFalse(_credentials.HasValidLease);
        }

        // 복구 가능한 거절은 프로세스를 종료하지 않고 재시도 간격 뒤 다시 받는다
        [TestCase(STATUS_UNAUTHORIZED, 0)]
        [TestCase(STATUS_FORBIDDEN, 0)]
        [TestCase(STATUS_BAD_REQUEST, 0)]
        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.TOKEN_EXPIRED)]
        public void Start_LeaseRecoverableRejection_RetriesAfterPresenceInterval(int status, int reasonCode)
        {
            ServerPresence presence = CreatePresence();
            _respond = request => RejectionResult(status, reasonCode);

            presence.Start();
            Complete();
            Assert.IsFalse(presence.IsLost);

            _respond = DefaultResponse;
            _now += Constants.Network.SERVER_PRESENCE_RETRY_SECONDS - JUST_BEFORE;
            presence.Tick();
            Assert.AreEqual(1, _transport.Calls, "재시도 간격 전에는 다시 보내지 않는다");

            _now += JUST_BEFORE;
            presence.Tick();
            Complete();
            Assert.AreEqual(EServerPresenceStage.Registering, presence.Stage);
        }

        // 계약: 같은 인스턴스의 다른 boot가 살아 있으면 임대와 등록이 503 + Retry-After 5로 온다. 일시 실패다
        [Test]
        public void Start_LeaseAndRegisterUnavailableWithRetryAfter_StaysAliveAndRetries()
        {
            ServerPresence presence = CreatePresence();
            _respond = request => new BackendTransportResult(STATUS_UNAVAILABLE, null, OTHER_BOOT_RETRY_AFTER);

            presence.Start();
            Complete();
            Assert.IsFalse(presence.IsLost);
            Assert.AreEqual(EServerPresenceStage.Leasing, presence.Stage);

            _respond = request => (request.Path == GameServerRoutes.SERVER_LEASE)
                ? LeaseResult(LEASE_TOKEN)
                : new BackendTransportResult(STATUS_UNAVAILABLE, null, OTHER_BOOT_RETRY_AFTER);
            _now += OTHER_BOOT_RETRY_AFTER;
            presence.Tick();
            Complete();
            presence.Tick();
            Complete();
            Assert.AreEqual(EServerPresenceStage.Registering, presence.Stage);
            Assert.IsFalse(presence.IsLost, "다른 boot가 살아 있는 503은 Lost가 아니다");

            _respond = DefaultResponse;
            _now += OTHER_BOOT_RETRY_AFTER;
            presence.Tick();
            Complete();
            Assert.AreEqual(EServerPresenceStage.Running, presence.Stage);
        }

        // N4 회귀: 생존 신호의 Retry-After는 heartbeat 주기까지만 따른다
        [Test]
        public void OnHeartbeat_LongRetryAfter_ClampsToHeartbeatInterval()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            int before = _transport.Calls;

            _respond = request => new BackendTransportResult(STATUS_UNAVAILABLE, null,
                Constants.Network.MAX_BACKEND_RETRY_AFTER_SECONDS);
            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();
            _respond = DefaultResponse;
            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();

            Assert.AreEqual(before + 2, _transport.Calls, "heartbeat 주기 안에 다시 보내 부재 한계(15초)를 넘기지 않는다");
            Assert.IsFalse(presence.IsLost);
        }

        [Test]
        public void OnLease_LongRetryAfter_ClampsToHeartbeatInterval()
        {
            ServerPresence presence = CreatePresence();
            _respond = request => new BackendTransportResult(STATUS_UNAVAILABLE, null,
                Constants.Network.MAX_BACKEND_RETRY_AFTER_SECONDS);

            presence.Start();
            Complete();
            _respond = DefaultResponse;
            _now += Math.Max(Constants.Network.SERVER_PRESENCE_RETRY_SECONDS, Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS);
            presence.Tick();
            Complete();

            Assert.AreEqual(EServerPresenceStage.Registering, presence.Stage, "임대도 300초를 기다리지 않는다");
        }

        [TestCase(0.0, 0.0)]
        [TestCase(-1.0, 0.0)]
        [TestCase(2.0, 2.0)]
        [TestCase(300.0, (double)Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS)]
        public void ClampLivenessRetryAfter_MixedDelays_CapsAtHeartbeatInterval(double input, double expected)
        {
            Assert.AreEqual(expected, ServerPresence.ClampLivenessRetryAfter(input));
        }

        // heartbeat는 종료된 출정이나 다른 프로세스의 부트 ID에 대한 거절만 존재 상실로 처리한다
        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.SESSION_MISMATCH, true)]
        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.TOKEN_EXPIRED, false)]
        [TestCase(STATUS_FORBIDDEN, 0, false)]
        [TestCase(STATUS_NOT_FOUND, 0, false)]
        [TestCase(STATUS_BAD_REQUEST, 0, false)]
        [TestCase(STATUS_UNAVAILABLE, ContractCodes.RejectReason.BACKEND_UNAVAILABLE, false)]
        public void OnHeartbeat_Rejection_OnlyUnrecoverableMarksLost(int status, int reasonCode, bool expectLost)
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);

            _respond = request => RejectionResult(status, reasonCode);
            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();

            Assert.AreEqual(expectLost, presence.IsLost);
            if (expectLost)
            {
                return;
            }

            int afterFailure = _transport.Calls;
            _respond = DefaultResponse;
            _now += Constants.Network.SERVER_PRESENCE_RETRY_SECONDS;
            presence.Tick();
            Complete();
            Assert.Greater(_transport.Calls, afterFailure, "재시도 간격 뒤 heartbeat를 다시 보낸다");
            Assert.AreEqual(EServerPresenceStage.Running, presence.Stage);
        }

        // N1 회귀: 데이터 요청 장애로 회로가 열려도 heartbeat는 나가고, 생존 신호 결과가 회로 상태를 바꾸지 않는다
        [Test]
        public void Heartbeat_CircuitOpenByDataFailures_StillSentAndDoesNotTouchCircuit()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            OpenCircuitWithDataFailures();
            int before = _transport.Calls;

            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();

            Assert.AreEqual(before + 1, _transport.Calls, "회로가 열려도 heartbeat는 보낸다");
            Assert.AreEqual(EServerPresenceStage.Running, presence.Stage);
            Assert.AreEqual(EBackendCircuitState.Open, _gateway.CircuitState, "생존 신호 성공이 데이터 회로를 닫지 않는다");
        }

        [Test]
        public void OnHeartbeat_Unauthorized_RenewsLeaseOnceAndStaysRunning()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);

            _respond = request => (request.Path == GameServerRoutes.SERVER_LEASE)
                ? LeaseResult(SECOND_TOKEN)
                : new BackendTransportResult(STATUS_UNAUTHORIZED, null);
            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();

            Assert.IsFalse(_credentials.HasValidLease, "API가 받지 않은 임대는 버린다");
            presence.Tick();
            Complete();

            Assert.AreEqual(EServerPresenceStage.Running, presence.Stage);
            Assert.IsTrue(_credentials.HasValidLease);
            Assert.IsTrue(ContainsPath(GameServerRoutes.SERVER_LEASE, 2));
        }

        [Test]
        public void Shutdown_HeartbeatReportsIdleAfterSortieBound_ReleasesReservationDrainsAndQuits()
        {
            SessionConfigSO config = UnityEngine.ScriptableObject.CreateInstance<SessionConfigSO>();
            try
            {
                var            registry = new ServerSessionRegistry(Constants.Session.MAX_PARTY_SIZE);
                using var      host     = new DungeonSessionHost(config, registry, _gateway, false, _warnings.Add);
                ServerPresence presence = CreatePresence();
                BringToRunning(presence);
                var control  = new FakeProcessControl { ConnectionCount = 1 };
                var shutdown = new ServerShutdownController(control, presence, host, ReadClock, _warnings.Add);
                var claims   = new JoinTokenClaims(KEY_ONE, null, null, SORTIE_ID);
                Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));
                Assert.AreEqual(ERejectReason.None, host.Admit(in claims, binding));
                shutdown.Tick();

                _respond = request => InstanceResult(GameSessionCodes.InstanceState.IDLE, 0, STATUS_OK);
                _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
                presence.Tick();
                Complete();
                shutdown.Tick();

                Assert.AreEqual(ESortieTerminalReason.ReservationReleased, host.TerminalReason);
                Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
                Complete();
                Assert.IsTrue(presence.IsDrainCompleted);
                Assert.IsTrue(ContainsPath(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_DRAIN_FORMAT,
                    INSTANCE_ID), 1), "종료를 정하면 바로 드레인해 새 배정을 막는다");

                control.ConnectionCount = 0;
                shutdown.Tick();
                _now += Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS;
                shutdown.Tick();

                Assert.AreEqual(EServerShutdownStage.Exited, shutdown.Stage);
                Assert.AreEqual(Constants.Network.EXIT_CODE_RECYCLED, control.ExitCode);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ReportsSortieEndedSince_IdleFromHeartbeatSentBeforeBinding_IsIgnored()
        {
            ServerPresence presence = CreatePresence();
            _respond = request => (request.Path == GameServerRoutes.SERVER_LEASE)
                ? LeaseResult(LEASE_TOKEN)
                : InstanceResult(GameSessionCodes.InstanceState.IDLE, 0, STATUS_OK);
            BringToRunning(presence);

            Assert.IsTrue(presence.ReportsSortieEndedSince(START_TIME));
            Assert.IsFalse(presence.ReportsSortieEndedSince(_now + 1.0), "출정을 받은 뒤 보낸 heartbeat만 센다");
        }

        // N3 회귀: 정산, 예약 해제 뒤 API가 둔 Draining도 출정 종료 신호다. 자기가 요청한 드레인 뒤의 Draining은 세지 않는다
        [Test]
        public void ReportsSortieEndedSince_DrainingNotRequestedBySelf_CountsOnlyBeforeOwnDrain()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            double boundAt = _now;

            _respond = request => (request.Path == GameServerRoutes.SERVER_LEASE)
                ? LeaseResult(LEASE_TOKEN)
                : InstanceResult(GameSessionCodes.InstanceState.DRAINING, 0, STATUS_OK);
            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();
            Assert.IsTrue(presence.ReportsSortieEndedSince(boundAt), "API가 둔 Draining은 출정 종료다");

            _now += JUST_BEFORE;
            presence.RequestDrain();
            Complete();
            Assert.IsTrue(presence.IsDrainCompleted);
            Assert.IsFalse(presence.ReportsSortieEndedSince(boundAt), "자기 드레인 응답의 Draining은 출정 종료 신호가 아니다");

            _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
            presence.Tick();
            Complete();
            Assert.IsFalse(presence.ReportsSortieEndedSince(boundAt), "자기 드레인 뒤의 Draining은 출정 종료 신호가 아니다");
        }

        [Test]
        public void Shutdown_HeartbeatReportsDrainingAfterSortieBound_ReleasesReservation()
        {
            SessionConfigSO config = UnityEngine.ScriptableObject.CreateInstance<SessionConfigSO>();
            try
            {
                var            registry = new ServerSessionRegistry(Constants.Session.MAX_PARTY_SIZE);
                using var      host     = new DungeonSessionHost(config, registry, _gateway, false, _warnings.Add);
                ServerPresence presence = CreatePresence();
                BringToRunning(presence);
                var control  = new FakeProcessControl { ConnectionCount = 1 };
                var shutdown = new ServerShutdownController(control, presence, host, ReadClock, _warnings.Add);
                var claims   = new JoinTokenClaims(KEY_ONE, null, null, SORTIE_ID);
                Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));
                Assert.AreEqual(ERejectReason.None, host.Admit(in claims, binding));
                shutdown.Tick();

                _respond = request => InstanceResult(GameSessionCodes.InstanceState.DRAINING, 0, STATUS_OK);
                _now += Constants.Network.SERVER_HEARTBEAT_INTERVAL_SECONDS;
                presence.Tick();
                Complete();
                shutdown.Tick();

                Assert.AreEqual(ESortieTerminalReason.ReservationReleased, host.TerminalReason);
                Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        // N3 회귀: 드레인이 실패하면 재시도 간격마다 성공할 때까지 다시 보낸다
        [Test]
        public void RequestDrain_Failure_RetriesUntilSuccess()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            string drainPath = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_DRAIN_FORMAT, INSTANCE_ID);

            _respond = request => (request.Path == drainPath)
                ? new BackendTransportResult(STATUS_UNAVAILABLE, null)
                : DefaultResponse(request);
            presence.RequestDrain();
            Complete();
            Assert.IsFalse(presence.IsDrainCompleted, "실패한 드레인을 완료로 보지 않는다");

            _now += Constants.Network.SERVER_PRESENCE_RETRY_SECONDS;
            presence.Tick();
            CompleteMany(HEARTBEAT_AND_DRAIN);
            Assert.IsFalse(presence.IsDrainCompleted);
            Assert.IsTrue(ContainsPath(drainPath, 2));

            _respond = DefaultResponse;
            _now += Constants.Network.SERVER_PRESENCE_RETRY_SECONDS;
            presence.Tick();
            CompleteMany(HEARTBEAT_AND_DRAIN);
            Assert.IsTrue(presence.IsDrainCompleted);
            Assert.IsTrue(ContainsPath(drainPath, 3));
        }

        [TestCase(ESortieTerminalReason.InvariantViolation, Constants.Network.EXIT_CODE_INVARIANT)]
        [TestCase(ESortieTerminalReason.Settled, Constants.Network.EXIT_CODE_RECYCLED)]
        [TestCase(ESortieTerminalReason.StartRejected, Constants.Network.EXIT_CODE_RECYCLED)]
        public void ExitCodeFor_MixedTerminalReasons_OnlyInvariantIsError(ESortieTerminalReason reason, int expected)
        {
            Assert.AreEqual(expected, ServerShutdownController.ExitCodeFor(reason));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private ServerPresence CreatePresence()
        {
            return new ServerPresence(_gateway, _credentials, EServerRole.Dungeon, ADDRESS, PORT, BUILD_ID, CONTENT_VERSION,
                ReadClock, _warnings.Add);
        }

        private void BringToRunning(ServerPresence presence)
        {
            presence.Start();
            Complete();
            Assert.AreEqual(EServerPresenceStage.Registering, presence.Stage);
            presence.Tick();
            Complete();
            Assert.AreEqual(EServerPresenceStage.Running, presence.Stage);
        }

        // 방금 보낸 요청 하나의 결과가 Pump까지 오게 한다
        private void Complete()
        {
            FakeBackendTransport.WaitForCompleted(_gateway, 1);
            _gateway.Pump();
        }

        // 한 Tick이 heartbeat와 드레인을 함께 보내면 두 결과가 모두 큐에 들어온 뒤 Pump한다
        private void CompleteMany(int count)
        {
            FakeBackendTransport.WaitForCompleted(_gateway, count);
            _gateway.Pump();
        }

        // 데이터 요청만 503을 받게 해 회로를 연다. 게이트웨이 시계가 0에 묶여 있어 시험 동안 열린 채로 남는다
        private void OpenCircuitWithDataFailures()
        {
            Func<BackendRequest, BackendTransportResult> previous = _respond;
            _respond = request => (request.Path == DATA_PATH)
                ? new BackendTransportResult(STATUS_UNAVAILABLE, null)
                : previous(request);
            var listener = new RecordingBackendListener();
            for (int i = 0; i < Constants.Network.BACKEND_CIRCUIT_FAILURE_THRESHOLD; ++i)
            {
                _gateway.Send(BackendRequest.Get(DATA_PATH, null, null, Constants.Network.API_TIMEOUT_SECONDS), listener);
                Complete();
            }

            Assert.AreEqual(EBackendCircuitState.Open, _gateway.CircuitState);
            _respond = previous;
        }

        private static BackendTransportResult RejectionResult(int status, int reasonCode)
        {
            return new BackendTransportResult(status, BackendJson.Serialize(
                new ApiRejection(reasonCode, string.Empty, SERVER_TIME_MS)));
        }

        private bool ContainsPath(string path, int minimum)
        {
            int count = 0;
            foreach (BackendRequest request in _transport.Requests)
            {
                if (request.Path == path)
                {
                    ++count;
                }
            }

            return count >= minimum;
        }

        private BackendTransportResult DefaultResponse(BackendRequest request)
        {
            if (request.Path == GameServerRoutes.SERVER_LEASE)
            {
                return LeaseResult(LEASE_TOKEN);
            }

            return InstanceResult(GameSessionCodes.InstanceState.RESERVED, 0, STATUS_OK);
        }

        private static BackendTransportResult LeaseResult(string token)
        {
            return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new ServerLeaseResponse(token,
                SERVER_TIME_MS + (LEASE_SECONDS * MS_PER_SECOND), SERVER_TIME_MS)));
        }

        private static BackendTransportResult InstanceResult(int state, int reasonCode, int status)
        {
            return new BackendTransportResult(status, BackendJson.Serialize(new ServerInstanceResponse(INSTANCE_ID, state,
                reasonCode, SERVER_TIME_MS)));
        }

        private double ReadClock()
        {
            return _now;
        }

        private static double ZeroClock()
        {
            return 0.0;
        }

        private sealed class FakeProcessControl : IServerProcessControl
        {
            public int ConnectionCount { get; set; }
            public int DisconnectCalls { get; private set; }
            public int ExitCode        { get; private set; } = -1;

            public void DisconnectAll()
            {
                ++DisconnectCalls;
                ConnectionCount = 0;
            }

            public void Quit(int exitCode)
            {
                ExitCode = exitCode;
            }
        }
    }
}
