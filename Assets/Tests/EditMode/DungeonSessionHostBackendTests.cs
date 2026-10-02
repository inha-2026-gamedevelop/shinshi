// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

// Unity
using UnityEngine;

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
    public class DungeonSessionHostBackendTests
    {
        // 백엔드 출정 경로(접속 임대 이관, start, 시작 전 이탈과 끊김, 종료 원인)를 가짜 전송기로 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string SORTIE_ID           = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string API_ONE             = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string API_TWO             = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const int    STATUS_OK           = 200;
        private const int    STATUS_UNAUTHORIZED = 401;
        private const int    STATUS_CONFLICT     = 409;

        private SessionConfigSO           _config;
        private ServerSessionRegistry     _registry;
        private FakeBackendTransport      _transport;
        private BackendGateway            _gateway;
        private DungeonSessionHost        _host;
        private List<string>              _warnings;
        private List<int>                 _evicted;
        private ConcurrentQueue<TimeSpan> _delays;

        // 스레드 풀의 가짜 전송기가 읽는다
        private volatile Func<BackendRequest, BackendTransportResult> _respond;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config    = ScriptableObject.CreateInstance<SessionConfigSO>();
            _registry  = new ServerSessionRegistry(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _warnings  = new List<string>();
            _evicted   = new List<int>();
            _delays    = new ConcurrentQueue<TimeSpan>();
            _respond   = DefaultResponse;
            _transport = new FakeBackendTransport((request, token) => Task.FromResult(_respond(request)));
            _gateway   = new BackendGateway(_transport, ZeroClock, FakeBackendTransport.ImmediateDelay(_delays), null, null);
            _host      = new DungeonSessionHost(_config, _registry, _gateway, false, _warnings.Add, new CommittingSink());
            _host.ParticipantEvicted += _evicted.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _host.ParticipantEvicted -= _evicted.Add;
            _host.Dispose();
            _gateway.Dispose();
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Admit_TransfersLeaseBeforeStart_WithStableNewLeaseId()
        {
            int one = Admit(API_ONE);

            Tick(1.0);
            Complete();
            Assert.AreEqual(1, _transport.Calls, "임대 이관 확정 전에는 start를 보내지 않는다");
            Tick(2.0);
            Complete();
            Tick(3.0);

            Assert.IsTrue(_host.IsSessionRunning);
            BackendRequest[] requests = _transport.Requests.ToArray();
            Assert.AreEqual(Path(GameServerRoutes.LEASE_TRANSFER_FORMAT), requests[0].Path);
            var transfer = BackendJson.Deserialize<LeaseTransferRequest>(requests[0].BodyJson);
            Assert.AreEqual(API_ONE, transfer.ParticipantId);
            Assert.AreEqual(SessionContractIds.CreateLeaseTransferId(SORTIE_ID, API_ONE), transfer.NewLeaseId);
            Assert.IsTrue(SessionContractIds.IsApiId(transfer.NewLeaseId));
            Assert.AreEqual(Path(GameServerRoutes.START_FORMAT), requests[1].Path);
            Assert.AreEqual(ESortieTerminalReason.None, _host.TerminalReason);
            Assert.IsTrue(_host.Roster.TryGet(one, out _));
        }

        [Test]
        public void Tick_LeaseTransferRejected_EvictsParticipantAndNeverSendsStart()
        {
            int one = Admit(API_ONE);
            _respond = request => request.Path.EndsWith("/lease-transfer", StringComparison.Ordinal)
                ? TransferResult(ContractCodes.RejectReason.ALREADY_CONNECTED, STATUS_CONFLICT)
                : DefaultResponse(request);

            Tick(1.0);
            Complete();
            Tick(2.0);
            Tick(3.0);

            CollectionAssert.AreEqual(new[] { one }, _evicted);
            Assert.IsTrue(_host.Roster.IsClosed(one));
            Assert.IsTrue(_registry.IsClosed(one), "내보낸 참가자는 같은 토큰으로 다시 들어오지 못한다");
            Assert.IsTrue(_host.IsLobbyBlocked, "시작 전 이탈이 생기면 예약 만료를 기다린다");
            Assert.AreEqual(1, _transport.Calls, "start를 보내지 않는다");
            Assert.IsFalse(_host.HasStarted);
        }

        [Test]
        public void Tick_LeaseTransferUnauthorized_RetriesSameNewLeaseId()
        {
            Admit(API_ONE);
            _respond = request => new BackendTransportResult(STATUS_UNAUTHORIZED, null);

            Tick(1.0);
            Complete();
            _respond = DefaultResponse;
            Tick(1.0 + Constants.Network.LEASE_TRANSFER_RETRY_SECONDS);
            Complete();

            BackendRequest[] requests = _transport.Requests.ToArray();
            Assert.AreEqual(2, requests.Length);
            Assert.AreEqual(requests[0].BodyJson, requests[1].BodyJson, "재시도도 같은 newLeaseId를 쓴다");
            Assert.AreEqual(0, _evicted.Count);
        }

        [Test]
        public void Tick_LobbyDisconnect_HoldsStartUntilParticipantReturns()
        {
            int one = Admit(API_ONE);
            Admit(API_TWO);
            Tick(1.0);
            Complete(2);

            Assert.IsTrue(_host.OnParticipantDetached(one, 1.5));
            Tick(2.0);
            Tick(3.0);
            Assert.AreEqual(2, _transport.Calls, "끊긴 사람이 있으면 start를 보내지 않는다");
            Assert.AreEqual(1, _host.LobbyAbsentCount);
            Assert.IsFalse(_host.ClosesParticipantOnRetentionExpiry, "시작 전에는 보존 만료로 명단을 닫지 않는다");

            Assert.IsTrue(_host.OnParticipantAttached(one, 3.5));
            Tick(4.0);
            Complete();
            Tick(5.0);

            Assert.IsTrue(_host.IsSessionRunning);
            Assert.IsTrue(_host.ClosesParticipantOnRetentionExpiry);
            Assert.AreEqual(2, _host.Session.StartingCount);
        }

        [Test]
        public void Tick_StartCommittedAfterParticipantDeparted_ReportsInvariantViolation()
        {
            int one = Admit(API_ONE);
            Admit(API_TWO);
            Tick(1.0);
            Complete(2);
            Tick(2.0);

            // start 응답 전에 한 명이 닫혔다. 영수증 뒤에는 되돌릴 수 없으므로 프로세스를 내린다
            _host.Roster.TryClose(one, ESessionEndReason.Abandon, 2.0);
            Complete();
            Tick(3.0);

            Assert.IsFalse(_host.HasStarted);
            Assert.AreEqual(ESortieTerminalReason.InvariantViolation, _host.TerminalReason);
            Assert.AreEqual(Constants.Network.EXIT_CODE_INVARIANT,
                ServerShutdownController.ExitCodeFor(_host.TerminalReason));
        }

        [TestCase(ContractCodes.RejectReason.RESERVATION_CONFLICT, true)]
        [TestCase(ContractCodes.RejectReason.NOT_ALL_READY, false)]
        public void Tick_StartRejected_PermanentEndsSortieNotReadyWaits(int reasonCode, bool terminal)
        {
            Admit(API_ONE);
            _respond = request => request.Path.EndsWith("/start", StringComparison.Ordinal)
                ? OperationResultOf(ContractCodes.OperationStatus.REJECTED, reasonCode, STATUS_CONFLICT)
                : DefaultResponse(request);

            Tick(1.0);
            Complete();
            Tick(2.0);
            Complete();

            Assert.AreEqual(terminal, _host.IsTerminal);
            Assert.AreEqual(terminal ? ESortieTerminalReason.StartRejected : ESortieTerminalReason.None, _host.TerminalReason);
        }

        [Test]
        public void Tick_StartUnauthorized_RetriesInsteadOfEndingSortie()
        {
            Admit(API_ONE);
            _respond = request => request.Path.EndsWith("/start", StringComparison.Ordinal)
                ? new BackendTransportResult(STATUS_UNAUTHORIZED, null)
                : DefaultResponse(request);

            Tick(1.0);
            Complete();
            Tick(2.0);
            Complete();

            Assert.IsFalse(_host.IsTerminal, "서버 임대 만료는 새 임대 뒤 통과할 수 있다");
        }

        [Test]
        public void OnReservationReleased_BeforeStart_EndsSortieAndRejectsNewAdmission()
        {
            Admit(API_ONE);

            _host.OnReservationReleased();

            Assert.AreEqual(ESortieTerminalReason.ReservationReleased, _host.TerminalReason);
            var claims = new JoinTokenClaims(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + API_TWO, null, null, SORTIE_ID);
            Assert.AreEqual(ERejectReason.None, _registry.TryAdmit(in claims, out SessionBinding binding));
            Assert.AreEqual(ERejectReason.SessionMismatch, _host.Admit(in claims, binding));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private int Admit(string apiParticipantId)
        {
            var claims = new JoinTokenClaims(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + apiParticipantId, null, null,
                SORTIE_ID);
            Assert.AreEqual(ERejectReason.None, _registry.TryAdmit(in claims, out SessionBinding binding));
            Assert.AreEqual(ERejectReason.None, _host.Admit(in claims, binding));
            return binding.ParticipantId;
        }

        private void Tick(double serverTime)
        {
            if (_host.BeginTick(serverTime))
            {
                _host.CompleteTick();
            }
        }

        // 보낸 요청 count개의 결과가 모두 큐에 들어온 뒤 한 번에 전달한다
        private void Complete(int count = 1)
        {
            FakeBackendTransport.WaitForCompleted(_gateway, count);
            _gateway.Pump();
        }

        private static string Path(string format)
        {
            return string.Format(CultureInfo.InvariantCulture, format, SORTIE_ID);
        }

        private static BackendTransportResult DefaultResponse(BackendRequest request)
        {
            if (request.Path.EndsWith("/lease-transfer", StringComparison.Ordinal))
            {
                return TransferResult(ContractCodes.RejectReason.NONE, STATUS_OK);
            }

            return OperationResultOf(ContractCodes.OperationStatus.COMMITTED, 0, STATUS_OK);
        }

        private static BackendTransportResult TransferResult(int reasonCode, int status)
        {
            return new BackendTransportResult(status, BackendJson.Serialize(new LeaseTransferResponse(API_ONE,
                (reasonCode == 0) ? SORTIE_ID : string.Empty, 0L, reasonCode, 0L)));
        }

        private static BackendTransportResult OperationResultOf(int status, int reasonCode, int httpStatus)
        {
            return new BackendTransportResult(httpStatus, BackendJson.Serialize(new OperationResult(SORTIE_ID, status, reasonCode,
                0L, 0L, string.Empty)));
        }

        private static double ZeroClock()
        {
            return 0.0;
        }

        private sealed class CommittingSink : IBackendEventSink
        {
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }
        }
    }
}
