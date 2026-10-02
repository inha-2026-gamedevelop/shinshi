// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class HttpBackendEventSinkTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID         = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string OTHER_SESSION_ID   = "6f1c2d3e-4a5b-4c6d-8e7f-001122334466";
        private const string PARTICIPANT_ID     = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbccdd";
        private const int    STATUS_OK          = 200;
        private const int    STATUS_ACCEPTED    = 202;
        private const int    STATUS_CONFLICT    = 409;
        private const int    STATUS_UNAVAILABLE = 503;
        private const long   ATTEMPT_ID         = 7;

        private ConcurrentQueue<TimeSpan> _delays;
        private List<string>              _warnings;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _delays   = new ConcurrentQueue<TimeSpan>();
            _warnings = new List<string>();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CreateRequest_SameBatch_ReturnsSameOperationIdAndBody()
        {
            SessionEvent[] events = CreateEvents(1, 3);

            BackendRequest first  = HttpBackendEventSink.CreateRequest(SESSION_ID, events, events.Length);
            BackendRequest second = HttpBackendEventSink.CreateRequest(SESSION_ID, CreateEvents(1, 3), events.Length);

            Assert.AreEqual(first.OperationId, second.OperationId);
            Assert.AreEqual(first.BodyJson, second.BodyJson);
            Assert.AreEqual(SessionContractIds.CreateEventsOperationId(SESSION_ID, 1, 3), first.OperationId);
            Assert.IsTrue(SessionContractIds.IsApiId(first.OperationId));
            Assert.AreEqual(string.Format(GameServerRoutes.EVENTS_FORMAT, SESSION_ID), first.Path);
            Assert.IsTrue(first.IsRetryable);
        }

        [Test]
        public void CreateRequest_DifferentRangeOrSession_ChangesOperationId()
        {
            BackendRequest baseline     = HttpBackendEventSink.CreateRequest(SESSION_ID, CreateEvents(1, 3), 3);
            BackendRequest otherRange   = HttpBackendEventSink.CreateRequest(SESSION_ID, CreateEvents(4, 3), 3);
            BackendRequest otherSession = HttpBackendEventSink.CreateRequest(OTHER_SESSION_ID, CreateEvents(1, 3), 3);

            Assert.AreNotEqual(baseline.OperationId, otherRange.OperationId);
            Assert.AreNotEqual(baseline.OperationId, otherSession.OperationId);
        }

        [Test]
        public void CreateRequest_SessionEnded_UsesSettlementOperationIdAndContractKinds()
        {
            var grants = new[] { new SettlementGrant(PARTICIPANT_ID, 0L, Array.Empty<SettlementItemGrant>()) };
            var events = new[]
            {
                new SessionEvent(1, new SessionEventData(ESessionEventType.ParticipantJoined, 0.0,
                    participantId: Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + PARTICIPANT_ID)),
                new SessionEvent(2, new SessionEventData(ESessionEventType.SessionEnded, 1.0, reason: ESessionEndReason.Success,
                    grants: grants))
            };

            BackendRequest request = HttpBackendEventSink.CreateRequest(SESSION_ID, events, events.Length);
            var            body    = BackendJson.Deserialize<SessionEventBatchRequest>(request.BodyJson);

            Assert.AreEqual(request.OperationId, body.OperationId);
            Assert.AreEqual(2, body.Events.Length);
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, body.Events[0].EventKind);
            Assert.AreEqual(PARTICIPANT_ID, body.Events[0].ParticipantId);
            Assert.AreEqual(SessionContractIds.CreateEventId(SESSION_ID, 1), body.Events[0].EventId);
            Assert.AreEqual(GameSessionCodes.EventKind.SESSION_ENDED, body.Events[1].EventKind);
            Assert.AreEqual(GameSessionCodes.EndReason.SUCCESS, body.Events[1].EndReason);
            Assert.AreEqual(SessionContractIds.CreateSettlementOperationId(SESSION_ID), body.Events[1].SettlementOperationId);
            Assert.AreEqual(1, body.Events[1].Grants.Length);
            Assert.AreEqual(PARTICIPANT_ID, body.Events[1].Grants[0].ParticipantId);
        }

        [Test]
        public void CreateRequest_ServiceCancelledEnd_IsNotSentAsSessionEnded()
        {
            var events = new[]
            {
                new SessionEvent(1, new SessionEventData(ESessionEventType.SessionEnded, 1.0,
                    reason: ESessionEndReason.ServiceCancelled))
            };

            BackendRequest request = HttpBackendEventSink.CreateRequest(SESSION_ID, events, events.Length);
            var            body    = BackendJson.Deserialize<SessionEventBatchRequest>(request.BodyJson);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, body.Events[0].EventKind);
            Assert.IsTrue(string.IsNullOrEmpty(body.Events[0].SettlementOperationId));
        }

        [Test]
        public void TrySend_TransientFailureThenCommit_RetriesSameBodyAndAcknowledges()
        {
            int       calls     = 0;
            var       transport = new FakeBackendTransport((request, token) =>
            {
                int call = Interlocked.Increment(ref calls);
                return Task.FromResult(call == 1
                    ? new BackendTransportResult(STATUS_UNAVAILABLE, null)
                    : new BackendTransportResult(STATUS_OK, Committed(request.OperationId)));
            });
            using var gateway   = CreateGateway(transport);
            var       sink      = new HttpBackendEventSink(gateway, _warnings.Add);
            var       listener  = new RecordingSinkListener();

            Assert.IsTrue(sink.TrySend(SESSION_ID, ATTEMPT_ID, CreateEvents(1, 2), 2, listener));
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(2, transport.Calls);
            Assert.IsTrue(transport.Requests.TryDequeue(out BackendRequest first));
            Assert.IsTrue(transport.Requests.TryDequeue(out BackendRequest second));
            Assert.AreSame(first, second, "재시도는 처음 만든 같은 요청 개체를 다시 보낸다");
            Assert.AreEqual(1, listener.Completions.Count);
            Assert.AreEqual(ATTEMPT_ID, listener.Completions[0].Key);
            Assert.AreEqual(EEventBatchResult.Committed, listener.Completions[0].Value);
            Assert.AreEqual(0, sink.PendingCount);
        }

        [Test]
        public void TrySend_QueueRetryAfterFailure_SendsByteIdenticalBody()
        {
            var            transport = FakeBackendTransport.Always(STATUS_CONFLICT, Rejected(ContractCodes.RejectReason.WRONG_PHASE));
            using var      gateway   = CreateGateway(transport);
            var            sink      = new HttpBackendEventSink(gateway, _warnings.Add);
            var            listener  = new RecordingSinkListener();
            SessionEvent[] events    = CreateEvents(5, 2);

            sink.TrySend(SESSION_ID, 1, events, 2, listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
            sink.TrySend(SESSION_ID, 2, events, 2, listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.IsTrue(transport.Requests.TryDequeue(out BackendRequest first));
            Assert.IsTrue(transport.Requests.TryDequeue(out BackendRequest second));
            Assert.AreEqual(first.OperationId, second.OperationId);
            Assert.AreEqual(first.BodyJson, second.BodyJson);
            Assert.AreEqual(EEventBatchResult.Transient, listener.Completions[0].Value, "단계 오류는 저장된 거절이 아니다");
            Assert.AreEqual(EEventBatchResult.Transient, listener.Completions[1].Value);
            Assert.AreEqual(2, _warnings.Count);
        }

        [Test]
        public void OnBackendResponse_PendingOperation_ReportsFailureForRetry()
        {
            var       transport = new FakeBackendTransport((request, token) => Task.FromResult(
                new BackendTransportResult(STATUS_ACCEPTED, Result(request.OperationId, ContractCodes.OperationStatus.PENDING, 0))));
            using var gateway   = CreateGateway(transport);
            var       sink      = new HttpBackendEventSink(gateway, _warnings.Add);
            var       listener  = new RecordingSinkListener();

            sink.TrySend(SESSION_ID, ATTEMPT_ID, CreateEvents(1, 1), 1, listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(1, listener.Completions.Count);
            Assert.AreEqual(EEventBatchResult.Transient, listener.Completions[0].Value);
        }

        [TestCase(400, 0)]
        [TestCase(409, ContractCodes.RejectReason.POTION_LIMIT)]
        [TestCase(409, ContractCodes.RejectReason.SESSION_MISMATCH)]
        [TestCase(409, ContractCodes.RejectReason.STALE_REVISION)]
        [TestCase(409, ContractCodes.RejectReason.ALREADY_SETTLED)]
        public void OnBackendResponse_StoredRejection_ReportsTerminalWithoutRetry(int status, int reasonCode)
        {
            var       transport = FakeBackendTransport.Always(status, Rejected(reasonCode));
            using var gateway   = CreateGateway(transport);
            var       sink      = new HttpBackendEventSink(gateway, _warnings.Add);
            var       listener  = new RecordingSinkListener();

            sink.TrySend(SESSION_ID, ATTEMPT_ID, CreateEvents(1, 1), 1, listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(1, transport.Calls, "확정 거절은 게이트웨이도 다시 보내지 않는다");
            Assert.AreEqual(EEventBatchResult.Terminal, listener.Completions[0].Value);
        }

        [TestCase(401)]
        [TestCase(403)]
        [TestCase(404)]
        public void OnBackendResponse_RecoverableRejection_ReportsTransient(int status)
        {
            var       transport = FakeBackendTransport.Always(status, null);
            using var gateway   = CreateGateway(transport);
            var       sink      = new HttpBackendEventSink(gateway, _warnings.Add);
            var       listener  = new RecordingSinkListener();

            sink.TrySend(SESSION_ID, ATTEMPT_ID, CreateEvents(1, 1), 1, listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EEventBatchResult.Transient, listener.Completions[0].Value, "임대 만료나 반영 전 거절은 다시 보낸다");
        }

        [Test]
        public void OnBackendResponse_NoServerLease_ReportsTransientWithoutSending()
        {
            var transport = FakeBackendTransport.Always(STATUS_OK, null);
            transport.CanSendFilter = request => false;
            using var gateway  = CreateGateway(transport);
            var       sink     = new HttpBackendEventSink(gateway, _warnings.Add);
            var       listener = new RecordingSinkListener();

            sink.TrySend(SESSION_ID, ATTEMPT_ID, CreateEvents(1, 1), 1, listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(0, transport.Calls);
            Assert.AreEqual(EEventBatchResult.Transient, listener.Completions[0].Value);
        }

        [Test]
        public void TrySend_NonGuidSession_ReturnsFalseWithoutSending()
        {
            var       transport = FakeBackendTransport.Always(STATUS_OK, null);
            using var gateway   = CreateGateway(transport);
            var       sink      = new HttpBackendEventSink(gateway, _warnings.Add);

            Assert.IsFalse(sink.TrySend("session", ATTEMPT_ID, CreateEvents(1, 1), 1, new RecordingSinkListener()));
            Assert.AreEqual(0, transport.Calls);
            Assert.AreEqual(1, _warnings.Count);
        }

        [Test]
        public void SessionContractIds_SameInputs_AreDeterministicAndDistinctByKind()
        {
            Assert.AreEqual(SessionContractIds.CreateEventId(SESSION_ID, 1), SessionContractIds.CreateEventId(SESSION_ID, 1));
            Assert.AreNotEqual(SessionContractIds.CreateEventId(SESSION_ID, 1), SessionContractIds.CreateEventId(SESSION_ID, 2));
            Assert.AreNotEqual(SessionContractIds.CreateSettlementOperationId(SESSION_ID),
                SessionContractIds.CreateEventsOperationId(SESSION_ID, 1, 1));
            Assert.IsTrue(SessionContractIds.IsApiId(SessionContractIds.CreateSettlementOperationId(SESSION_ID)));

            Assert.IsTrue(SessionContractIds.TryGetApiParticipantId(
                Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + PARTICIPANT_ID, out string participantId));
            Assert.AreEqual(PARTICIPANT_ID, participantId);
            Assert.IsFalse(SessionContractIds.TryGetApiParticipantId("player_one", out _));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private BackendGateway CreateGateway(FakeBackendTransport transport)
        {
            return new BackendGateway(transport, ReadClock, FakeBackendTransport.ImmediateDelay(_delays), null, null);
        }

        private static double ReadClock()
        {
            return 0.0;
        }

        private static SessionEvent[] CreateEvents(long firstSequence, int count)
        {
            var events = new SessionEvent[count];
            for (int i = 0; i < count; ++i)
            {
                events[i] = new SessionEvent(firstSequence + i, new SessionEventData(ESessionEventType.EnemyKilled, 1.5 + i,
                    participantId: Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + PARTICIPANT_ID, sourceId: "source_one",
                    killSequence: i + 1, loot: new LootRollResult(5, 1, 0, null)));
            }

            return events;
        }

        private static string Committed(string operationId)
        {
            return Result(operationId, ContractCodes.OperationStatus.COMMITTED, 0);
        }

        private static string Rejected(int reasonCode)
        {
            return Result("op", ContractCodes.OperationStatus.REJECTED, reasonCode);
        }

        private static string Result(string operationId, int status, int reasonCode)
        {
            return BackendJson.Serialize(new OperationResult(operationId, status, reasonCode, 0L, 0L, string.Empty));
        }

        private sealed class RecordingSinkListener : IBackendEventSinkListener
        {
            public List<KeyValuePair<long, EEventBatchResult>> Completions { get; } =
                new List<KeyValuePair<long, EEventBatchResult>>();

            public double LastRetryAfter { get; private set; }

            public void OnBatchCompleted(long attemptId, EEventBatchResult result, double retryAfterSeconds = 0.0)
            {
                LastRetryAfter = retryAfterSeconds;
                Completions.Add(new KeyValuePair<long, EEventBatchResult>(attemptId, result));
            }
        }
    }
}
