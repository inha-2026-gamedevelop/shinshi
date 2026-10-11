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
    public class ServerGracefulShutdownTests
    {
        // 컨트롤러 상태 시험은 presence 없이 시계와 출정 진행 술어만 주입한다. 드레인 응답 확인과 존재 상실 회귀만 가짜 전송기를 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const string INSTANCE_ID         = "7d1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string SERVICE_TOKEN       = "service-token-value";
        private const string LEASE_TOKEN         = "lease-token-value";
        private const string ADDRESS             = "203.0.113.20";
        private const int    PORT                = 7780;
        private const string BUILD_ID            = "build-1";
        private const string CONTENT_VERSION     = "content-1";
        private const double START_TIME          = 100.0;
        private const long   SERVER_TIME_MS      = 1_000_000L;
        private const long   LEASE_SECONDS       = 900L;
        private const long   MS_PER_SECOND       = 1000L;
        private const int    STATUS_OK           = 200;
        private const int    STATUS_CONFLICT     = 409;
        private const int    STATUS_UNAVAILABLE  = 503;
        private const double JUST_BEFORE         = 0.01;
        private const int    OWN_PID             = 4242;
        private const int    OTHER_PID           = 777;
        private const string REQUEST_FILE        = "/run/shinshi/gameserver/town/shutdown-request";

        private double                    _now;
        private ConcurrentQueue<TimeSpan> _delays;
        private List<string>              _warnings;
        private ServerCredentials         _credentials;
        private FakeBackendTransport      _transport;
        private BackendGateway            _gateway;
        private bool                      _sortieOpen;

        // 스레드 풀의 가짜 전송기가 읽는다
        private volatile Func<BackendRequest, BackendTransportResult> _respond;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now                     = START_TIME;
            _sortieOpen              = false;
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
        *       Controller: operator shutdown
        ****************************************/

        [Test]
        public void RequestOperatorShutdown_IdleTown_DrainsThenExitsWithRecycleCode()
        {
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control);

            Assert.IsTrue(shutdown.RequestOperatorShutdown("test"));
            Assert.AreEqual(EServerShutdownStage.DrainWaiting, shutdown.Stage);

            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage, "진행 출정이 없으면 기다리지 않는다");

            control.ConnectionCount = 0;
            shutdown.Tick();
            _now += Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Exited, shutdown.Stage);
            Assert.AreEqual(Constants.Network.EXIT_CODE_RECYCLED, control.ExitCode);
        }

        [Test]
        public void Tick_SortieOpenBeforeDeadline_KeepsWaitingWithoutDisconnect()
        {
            var control  = new FakeProcessControl { ConnectionCount = 3 };
            var shutdown = CreateController(control);
            _sortieOpen = true;

            shutdown.RequestOperatorShutdown("test");
            _now += ServerShutdownController.OPERATOR_DRAIN_MAX_SECONDS - JUST_BEFORE;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.DrainWaiting, shutdown.Stage);
            Assert.AreEqual(0, control.DisconnectCalls, "진행 출정의 연결을 끊지 않는다");
            Assert.AreEqual(-1, control.ExitCode);
        }

        [Test]
        public void Tick_SortieBecomesTerminal_ProceedsAsNormalRecycle()
        {
            var control  = new FakeProcessControl { ConnectionCount = 0 };
            var shutdown = CreateController(control);
            _sortieOpen = true;

            shutdown.RequestOperatorShutdown("test");
            _now += ServerShutdownController.OPERATOR_DRAIN_MAX_SECONDS / 2.0;
            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.DrainWaiting, shutdown.Stage);

            _sortieOpen = false;
            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);

            shutdown.Tick();
            _now += Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Exited, shutdown.Stage);
            Assert.AreEqual(Constants.Network.EXIT_CODE_RECYCLED, control.ExitCode, "기한 안에 마감된 출정은 정상 종료 코드다");
        }

        [Test]
        public void Tick_SortieStillOpenAtDeadline_ExitsWithForcedCodeAndWarns()
        {
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control);
            _sortieOpen = true;

            shutdown.RequestOperatorShutdown("test");
            _now += ServerShutdownController.OPERATOR_DRAIN_MAX_SECONDS;
            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);

            _now += Constants.Network.RECYCLE_FAULT_GRACE_SECONDS;
            shutdown.Tick();
            _now += Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Exited, shutdown.Stage);
            Assert.AreEqual(ServerShutdownController.EXIT_CODE_SHUTDOWN_FORCED, control.ExitCode);
            Assert.AreNotEqual(Constants.Network.EXIT_CODE_RECYCLED, control.ExitCode, "기한 초과를 정상 정산으로 꾸미지 않는다");
            Assert.IsTrue(_warnings.Exists(message => message.Contains("강제")), "강제 종료 경고가 남는다");
        }

        [Test]
        public void RequestOperatorShutdown_Repeated_IsCountedAndDoesNotExtendDeadline()
        {
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control);
            _sortieOpen = true;

            Assert.IsTrue(shutdown.RequestOperatorShutdown("first"));
            _now += 100.0;
            Assert.IsFalse(shutdown.RequestOperatorShutdown("second"));
            Assert.AreEqual(1, shutdown.DuplicateRequestCount);

            _now = START_TIME + ServerShutdownController.OPERATOR_DRAIN_MAX_SECONDS;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage, "기한은 첫 요청 시각에서 센다");
        }

        [Test]
        public void RequestOperatorShutdown_AfterReturning_IsIgnoredAndKeepsExitCode()
        {
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control);

            shutdown.Request(Constants.Network.EXIT_CODE_INVARIANT, Constants.Network.RECYCLE_FAULT_GRACE_SECONDS, "test");
            Assert.IsFalse(shutdown.RequestOperatorShutdown("late"));

            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
            Assert.AreEqual(Constants.Network.EXIT_CODE_INVARIANT, shutdown.ExitCode);
            Assert.AreEqual(1, shutdown.DuplicateRequestCount);
        }

        [Test]
        public void AllowsQuit_RunningThenExited_BlocksFirstAndAllowsAfterExit()
        {
            var control  = new FakeProcessControl { ConnectionCount = 0 };
            var shutdown = CreateController(control);

            Assert.IsFalse(shutdown.AllowsQuit("quit"), "첫 종료 요청은 막고 운영 종료 경로로 보낸다");
            Assert.AreEqual(EServerShutdownStage.DrainWaiting, shutdown.Stage);
            Assert.IsFalse(shutdown.AllowsQuit("quit again"));
            Assert.AreEqual(1, shutdown.DuplicateRequestCount);

            shutdown.Tick();
            shutdown.Tick();
            _now += Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Exited, shutdown.Stage);
            Assert.IsTrue(shutdown.AllowsQuit("final quit"), "Exited에서만 종료를 허용한다");
        }

        [Test]
        public void RequestOperatorShutdown_ClockAdvancedBeforeRequest_MeasuresDeadlineFromRequest()
        {
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control);
            _sortieOpen = true;

            _now += 5000.0;
            shutdown.RequestOperatorShutdown("test");
            _now += ServerShutdownController.OPERATOR_DRAIN_MAX_SECONDS - JUST_BEFORE;
            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.DrainWaiting, shutdown.Stage);

            _now += JUST_BEFORE;
            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
        }

        [Test]
        public void StopBudget_OperatorDeadlineFitsInsideSystemdTimeoutWithMargin()
        {
            double budget = ServerShutdownController.OPERATOR_DRAIN_MAX_SECONDS
                + Constants.Network.RECYCLE_RETURN_GRACE_SECONDS
                + Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS
                + Constants.Network.RECYCLE_DRAIN_WAIT_SECONDS;

            Assert.LessOrEqual(budget + ServerShutdownController.STOP_SAFETY_MARGIN_SECONDS,
                ServerShutdownController.SYSTEMD_STOP_TIMEOUT_SECONDS);
        }

        /****************************************
        *      Controller: presence interplay
        ****************************************/

        [Test]
        public void Tick_DrainAcknowledged_LeavesDrainWaiting()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control, presence);

            shutdown.RequestOperatorShutdown("test");
            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.DrainWaiting, shutdown.Stage, "드레인 응답 전에는 내리지 않는다");

            Complete();
            Assert.IsTrue(presence.IsDrainCompleted);
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
        }

        [Test]
        public void Tick_DrainNeverAcknowledged_ProceedsAfterAckDeadline()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            string drainPath = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_DRAIN_FORMAT, INSTANCE_ID);
            _respond = request => (request.Path == drainPath)
                ? new BackendTransportResult(STATUS_UNAVAILABLE, null)
                : DefaultResponse(request);
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control, presence);

            shutdown.RequestOperatorShutdown("test");
            Complete();
            Assert.IsFalse(presence.IsDrainCompleted);

            _now += ServerShutdownController.OPERATOR_DRAIN_ACK_WAIT_SECONDS - JUST_BEFORE;
            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.DrainWaiting, shutdown.Stage);

            _now += JUST_BEFORE;
            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
        }

        [Test]
        public void Tick_PresenceLostWhileDrainWaiting_ExitsWithPresenceLostCode()
        {
            ServerPresence presence = CreatePresence();
            BringToRunning(presence);
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control, presence);
            _sortieOpen = true;

            shutdown.RequestOperatorShutdown("test");
            Complete();

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
            _now += Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS + Constants.Network.RECYCLE_DRAIN_WAIT_SECONDS;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Exited, shutdown.Stage);
            Assert.AreEqual(Constants.Network.EXIT_CODE_PRESENCE_LOST, control.ExitCode, "기존 존재 상실 경로를 보존한다");
        }

        [Test]
        public void Tick_ReservedDungeonWithoutSession_WaitsUntilDeadlineByProductionHostTruth()
        {
            SessionConfigSO config = UnityEngine.ScriptableObject.CreateInstance<SessionConfigSO>();
            try
            {
                var            registry = new ServerSessionRegistry(Constants.Session.MAX_PARTY_SIZE);
                using var      host     = new DungeonSessionHost(config, registry, _gateway, false, _warnings.Add);
                ServerPresence presence = CreatePresence();
                BringToRunning(presence);
                Assert.AreEqual(GameSessionCodes.InstanceState.RESERVED, presence.InstanceState);
                var control  = new FakeProcessControl { ConnectionCount = 0 };
                var shutdown = new ServerShutdownController(control, presence, host, ReadClock, _warnings.Add);

                shutdown.RequestOperatorShutdown("test");
                Complete();
                shutdown.Tick();
                Assert.AreEqual(EServerShutdownStage.DrainWaiting, shutdown.Stage, "예약만 되고 아직 들어오지 않은 출정도 기다린다");

                _now += ServerShutdownController.OPERATOR_DRAIN_MAX_SECONDS;
                shutdown.Tick();
                Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
                Assert.AreEqual(ServerShutdownController.EXIT_CODE_SHUTDOWN_FORCED, shutdown.ExitCode);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Tick_RunningWithoutOperatorRequest_BehavesAsBefore()
        {
            var control  = new FakeProcessControl { ConnectionCount = 1 };
            var shutdown = CreateController(control);

            shutdown.Tick();
            Assert.AreEqual(EServerShutdownStage.Running, shutdown.Stage);

            shutdown.Request(Constants.Network.EXIT_CODE_RECYCLED, Constants.Network.RECYCLE_RETURN_GRACE_SECONDS, "test");
            Assert.AreEqual(EServerShutdownStage.Returning, shutdown.Stage);
            _now += Constants.Network.RECYCLE_RETURN_GRACE_SECONDS;
            shutdown.Tick();
            Assert.AreEqual(1, control.DisconnectCalls);
            _now += Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS;
            shutdown.Tick();

            Assert.AreEqual(EServerShutdownStage.Exited, shutdown.Stage);
            Assert.AreEqual(Constants.Network.EXIT_CODE_RECYCLED, control.ExitCode);
        }

        [TestCase(ESortieTerminalReason.InvariantViolation, Constants.Network.EXIT_CODE_INVARIANT)]
        [TestCase(ESortieTerminalReason.Settled, Constants.Network.EXIT_CODE_RECYCLED)]
        public void ExitCodeFor_ExistingRecycleMapping_IsPreserved(ESortieTerminalReason reason, int expected)
        {
            Assert.AreEqual(expected, ServerShutdownController.ExitCodeFor(reason));
        }

        /****************************************
        *            Request file watcher
        ****************************************/

        [Test]
        public void TryCreate_NoEnvironmentValue_ReturnsNull()
        {
            var fileSystem = new FakeFileSystem();

            Assert.IsNull(ServerShutdownRequestWatcher.TryCreate(null, OWN_PID, fileSystem, ReadClock, _warnings.Add));
            Assert.IsNull(ServerShutdownRequestWatcher.TryCreate("  ", OWN_PID, fileSystem, ReadClock, _warnings.Add));
        }

        [Test]
        public void TryCreate_RelativePath_ReturnsNullAndWarns()
        {
            var fileSystem = new FakeFileSystem();

            Assert.IsNull(ServerShutdownRequestWatcher.TryCreate("relative/request", OWN_PID, fileSystem, ReadClock, _warnings.Add));
            Assert.AreEqual(1, _warnings.Count);
        }

        [Test]
        public void Poll_MatchingPid_ReturnsTrueOnceAndDeletesFile()
        {
            var fileSystem = new FakeFileSystem();
            fileSystem.Files[REQUEST_FILE] = OWN_PID.ToString(CultureInfo.InvariantCulture) + "\n";
            ServerShutdownRequestWatcher watcher = CreateWatcher(fileSystem);

            Assert.IsTrue(watcher.Poll());
            Assert.IsFalse(fileSystem.Files.ContainsKey(REQUEST_FILE));

            fileSystem.Files[REQUEST_FILE] = OWN_PID.ToString(CultureInfo.InvariantCulture);
            _now += ServerShutdownRequestWatcher.POLL_INTERVAL_SECONDS;
            Assert.IsFalse(watcher.Poll(), "중복 요청은 한 번만 알린다");
        }

        [Test]
        public void Poll_StaleFileFromOtherPid_IsDeletedAndIgnored()
        {
            var fileSystem = new FakeFileSystem();
            fileSystem.Files[REQUEST_FILE] = OTHER_PID.ToString(CultureInfo.InvariantCulture);
            ServerShutdownRequestWatcher watcher = CreateWatcher(fileSystem);

            Assert.IsFalse(watcher.Poll());
            Assert.IsFalse(fileSystem.Files.ContainsKey(REQUEST_FILE), "이전 프로세스가 남긴 파일은 지운다");
            Assert.IsFalse(watcher.IsRequested);
        }

        [Test]
        public void Poll_RequestWrittenAfterStartup_IsPickedUpOnNextInterval()
        {
            var fileSystem = new FakeFileSystem();
            ServerShutdownRequestWatcher watcher = CreateWatcher(fileSystem);
            Assert.IsFalse(watcher.Poll());

            fileSystem.Files[REQUEST_FILE] = OWN_PID.ToString(CultureInfo.InvariantCulture);
            Assert.IsFalse(watcher.Poll(), "확인 주기 안에서는 다시 읽지 않는다");

            _now += ServerShutdownRequestWatcher.POLL_INTERVAL_SECONDS;
            Assert.IsTrue(watcher.Poll(), "기동 경합으로 늦게 생긴 요청도 놓치지 않는다");
        }

        [Test]
        public void Poll_PartiallyWrittenFile_RetriesThenDeletesGarbage()
        {
            var fileSystem = new FakeFileSystem();
            fileSystem.Files[REQUEST_FILE] = string.Empty;
            ServerShutdownRequestWatcher watcher = CreateWatcher(fileSystem);

            Assert.IsFalse(watcher.Poll());
            Assert.IsTrue(fileSystem.Files.ContainsKey(REQUEST_FILE), "쓰는 도중일 수 있어 바로 지우지 않는다");

            fileSystem.Files[REQUEST_FILE] = OWN_PID.ToString(CultureInfo.InvariantCulture);
            _now += ServerShutdownRequestWatcher.POLL_INTERVAL_SECONDS;
            Assert.IsTrue(watcher.Poll());

            fileSystem.Files[REQUEST_FILE] = "not-a-pid";
            var garbageWatcher = CreateWatcher(fileSystem);
            for (int i = 0; i < ServerShutdownRequestWatcher.MAX_UNPARSABLE_POLLS; ++i)
            {
                _now += ServerShutdownRequestWatcher.POLL_INTERVAL_SECONDS;
                Assert.IsFalse(garbageWatcher.Poll());
            }

            Assert.IsFalse(fileSystem.Files.ContainsKey(REQUEST_FILE), "계속 해석되지 않는 파일은 지운다");
        }

        [Test]
        public void Poll_ReadThrows_WarnsRateLimitedAndRecovers()
        {
            var fileSystem = new FakeFileSystem { ThrowOnRead = true };
            fileSystem.Files[REQUEST_FILE] = OWN_PID.ToString(CultureInfo.InvariantCulture);
            ServerShutdownRequestWatcher watcher = CreateWatcher(fileSystem);

            Assert.IsFalse(watcher.Poll());
            _now += ServerShutdownRequestWatcher.POLL_INTERVAL_SECONDS;
            Assert.IsFalse(watcher.Poll());
            Assert.AreEqual(1, _warnings.Count, "같은 경고를 매 주기 반복하지 않는다");

            fileSystem.ThrowOnRead = false;
            _now += ServerShutdownRequestWatcher.POLL_INTERVAL_SECONDS;
            Assert.IsTrue(watcher.Poll(), "읽기 예외 뒤에도 감시를 이어 간다");
        }

        [Test]
        public void Poll_DeleteThrows_StillReportsRequestOnce()
        {
            var fileSystem = new FakeFileSystem { ThrowOnDelete = true };
            fileSystem.Files[REQUEST_FILE] = OWN_PID.ToString(CultureInfo.InvariantCulture);
            ServerShutdownRequestWatcher watcher = CreateWatcher(fileSystem);

            Assert.IsTrue(watcher.Poll());
            _now += ServerShutdownRequestWatcher.POLL_INTERVAL_SECONDS;
            Assert.IsFalse(watcher.Poll(), "지우지 못해도 같은 요청을 다시 알리지 않는다");
        }

        [Test]
        public void Warnings_DoNotContainFullRequestPath()
        {
            var fileSystem = new FakeFileSystem { ThrowOnRead = true };
            fileSystem.Files[REQUEST_FILE] = OWN_PID.ToString(CultureInfo.InvariantCulture);
            ServerShutdownRequestWatcher watcher = CreateWatcher(fileSystem);

            watcher.Poll();

            Assert.AreEqual(1, _warnings.Count);
            Assert.IsFalse(_warnings[0].Contains("/run/"), "경로 전체를 로그에 남기지 않는다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private ServerShutdownController CreateController(FakeProcessControl control, ServerPresence presence = null)
        {
            return new ServerShutdownController(control, presence, null, () => _sortieOpen, ReadClock, _warnings.Add);
        }

        private ServerShutdownRequestWatcher CreateWatcher(FakeFileSystem fileSystem)
        {
            return new ServerShutdownRequestWatcher(REQUEST_FILE, OWN_PID, fileSystem, ReadClock, _warnings.Add);
        }

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

        private sealed class FakeFileSystem : IShutdownRequestFileSystem
        {
            public Dictionary<string, string> Files = new Dictionary<string, string>();
            public bool ThrowOnRead;
            public bool ThrowOnDelete;

            public bool DirectoryExists(string path)
            {
                return true;
            }

            public bool FileExists(string path)
            {
                return Files.ContainsKey(path);
            }

            public string ReadAllText(string path)
            {
                if (ThrowOnRead)
                {
                    throw new UnauthorizedAccessException();
                }

                return Files[path];
            }

            public void Delete(string path)
            {
                if (ThrowOnDelete)
                {
                    throw new UnauthorizedAccessException();
                }

                Files.Remove(path);
            }
        }
    }
}
