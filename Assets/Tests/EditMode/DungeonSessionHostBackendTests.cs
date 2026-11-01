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
        // 백엔드 출정 경로(접속 임대 이관, start, 시작 전 이탈과 끊김, 종료 원인, 보스 성공 정산과 경매 순서)를 가짜 전송기로 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string SORTIE_ID           = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string API_ONE             = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string API_TWO             = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const int    STATUS_OK           = 200;
        private const int    STATUS_UNAUTHORIZED = 401;
        private const int    STATUS_CONFLICT     = 409;
        private const int    SETTLE_POLL_COUNT        = 100;
        private const int    SETTLE_POLL_MILLISECONDS = 20;

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

        [Test]
        public void Tick_BossSuccess_HoldsReturnAndTerminalUntilSettlementAckAuctionAndFinishAck()
        {
            var audience = new StatusAudience();
            _host.SortieAudience = audience;
            int one = Admit(API_ONE);
            Tick(1.0);
            Complete();
            Tick(2.0);
            Complete();
            Tick(3.0);
            Assert.IsTrue(_host.IsSessionRunning);

            // 종료 이벤트 저장이 확인된 틱에 정산 요청이 나간다. 응답은 Pump로만 들어온다
            Assert.IsTrue(_host.Session.TryCompleteBossForDevelopment(4.0));
            Tick(6.0);
            Complete();

            BackendRequest[] requests = _transport.Requests.ToArray();
            Assert.AreEqual(3, requests.Length);
            Assert.AreEqual(Path(GameServerRoutes.SETTLEMENT_FORMAT), requests[2].Path);

            // 정산 ACK는 받았지만 경매가 아직 만들어지지 않은 다음 틱 전이다
            _host.SendStatusTo(one);
            Assert.AreEqual(ESortieSettlementState.Settled, audience.Last.SettlementState);
            Assert.IsNull(_host.Auction);
            Assert.IsFalse(audience.Last.CanReturn, "보스 성공은 정산 ACK만으로 귀환하지 않는다");
            Assert.IsFalse(_host.IsTerminal);

            // 다음 틱에 경매가 만들어지고 finish 요청이 나간다. ACK 전에는 귀환도 종료도 막는다
            Tick(8.0);
            Assert.IsNotNull(_host.Auction);
            Assert.AreEqual(EAuctionStep.Finishing, _host.Auction.Step);
            Assert.IsFalse(_host.Auction.IsFinished);
            _host.SendStatusTo(one);
            Assert.AreEqual(ESortiePhase.Auction, audience.Last.Phase);
            Assert.IsFalse(audience.Last.CanReturn);
            Assert.IsFalse(_host.IsTerminal);

            // finish ACK를 Pump로 받으면 귀환이 열리고, 다음 틱에 종료가 확정된다
            Complete();
            Assert.IsTrue(_host.Auction.IsFinished);
            _host.SendStatusTo(one);
            Assert.IsTrue(audience.Last.CanReturn);
            Tick(10.0);

            Assert.AreEqual(ESortieTerminalReason.Settled, _host.TerminalReason);
            requests = _transport.Requests.ToArray();
            Assert.AreEqual(4, requests.Length);
            Assert.AreEqual(Path(GameServerRoutes.AUCTION_FINISH_FORMAT), requests[3].Path);
        }

        [Test]
        public void Tick_BossTwentyMinutes_EndsWithTimeoutAndOpensReturn()
        {
            var audience = new StatusAudience();
            _host.SortieAudience = audience;
            int one = Admit(API_ONE);
            Tick(1.0);
            Complete();
            Tick(2.0);
            Complete();
            Tick(3.0);
            Assert.IsTrue(_host.IsSessionRunning);
            Assert.IsTrue(_host.Session.IsBossSession);
            double start = _host.Session.StartTime;

            // 제한 시간 직전까지는 계속 싸운다
            Tick(start + _config.BossTimeLimitSeconds - 1.0);
            Assert.IsTrue(_host.IsSessionRunning, "20분이 되기 전에는 끝나지 않는다");
            Assert.IsFalse(_host.Session.IsEnded);

            // 20분이 되는 틱에 시간 초과로 끝난다
            double end = start + _config.BossTimeLimitSeconds;
            Tick(end);
            Assert.IsTrue(_host.Session.IsEnded);
            Assert.AreEqual(ESessionEndReason.Timeout, _host.Session.EndReason);
            Assert.IsFalse(_host.IsSessionRunning);

            // 종료 이벤트 저장과 정산 ACK 뒤에 귀환이 열리고 출정이 닫힌다
            Tick(end + 2.0);
            Assert.IsTrue(_host.Session.CanReturn, "종료 이벤트 저장이 확인된다");
            BackendRequest[] requests = _transport.Requests.ToArray();
            string settlementPath = Path(GameServerRoutes.SETTLEMENT_FORMAT);
            Assert.IsTrue(Array.Exists(requests, request => request.Path == settlementPath), "시간 초과도 정산 요청이 나간다");

            // 가짜 전송기의 응답은 다른 스레드에서 오므로 정산 ACK가 들어올 때까지 Pump와 틱을 되풀이한다
            for (int i = 0; (i < SETTLE_POLL_COUNT) && (_host.TerminalReason == ESortieTerminalReason.None); ++i)
            {
                System.Threading.Thread.Sleep(SETTLE_POLL_MILLISECONDS);
                _gateway.Pump();
                Tick(end + 3.0 + i);
            }

            _host.SendStatusTo(one);
            Assert.AreEqual(ESessionEndReason.Timeout, audience.Last.EndReason);
            Assert.IsTrue(audience.Last.CanReturn, "시간 초과 실패도 참가자를 마을로 돌려보낸다");
            Assert.AreEqual(ESortieTerminalReason.Settled, _host.TerminalReason);
        }

        [Test]
        public void Tick_BossProgressMessage_CarriesTwentyMinuteLimitFromBegin()
        {
            var progress = new ProgressAudience();
            _host.WaveProgressAudience = progress;
            int one = Admit(API_ONE);
            Tick(1.0);
            Complete();
            Tick(2.0);
            Complete();
            Tick(3.0);
            Tick(4.0);

            Assert.IsTrue(progress.HasLast);
            Assert.AreEqual(0, progress.Last.WaveCount, "보스전은 웨이브 계획이 없다");
            Assert.AreEqual(_config.BossTimeLimitSeconds, progress.Last.TimeLimitSeconds);
            Assert.IsTrue(progress.Last.IsRunning);
            Assert.AreEqual(one, progress.ParticipantId);
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

        private sealed class ProgressAudience : IWaveProgressAudience
        {
            public WaveProgressMessage Last          { get; private set; }
            public bool                HasLast       { get; private set; }
            public int                 ParticipantId { get; private set; }

            public void SendWaveProgress(int participantId, in WaveProgressMessage message)
            {
                Last          = message;
                HasLast       = true;
                ParticipantId = participantId;
            }
        }

        private sealed class StatusAudience : ISortieAudience
        {
            public SortieStatusMessage Last { get; private set; }

            public void SendStatus(int participantId, in SortieStatusMessage message)
            {
                Last = message;
            }

            public void SendAbandonResult(int participantId, in SortieAbandonResultMessage message)
            {
            }

            public void SendStopVote(int participantId, in SortieStopVoteStateMessage message)
            {
            }
        }
    }
}
