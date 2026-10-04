// System
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Tasks;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class IntroProgressRelayTests
    {
        // 신시 안내 완료 중계: 계정은 연결 결합에서 정하고 경로에 넣는다, 처리 중 중복 차단, 결과 모름은 BackendUnavailable,
        // 확정 거절은 API 사유, 메시지 직렬화 순서

        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT_ID      = "0A1B2C3D-4E5F-4A6B-8C7D-8899AABBCC01";
        private const string CHARACTER_ID    = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string LEASE_ID        = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const int    PARTICIPANT_ID  = 1;
        private const int    STATUS_OK       = 200;
        private const int    STATUS_DOWN     = 503;
        private const int    STATUS_CONFLICT = 409;

        private volatile int         _status;
        private volatile int         _reason;
        private FakeBackendTransport _transport;
        private BackendGateway       _gateway;
        private FakeTownAudience     _audience;
        private IntroProgressRelay   _relay;
        private TownMember           _member;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _status    = STATUS_OK;
            _reason    = ContractCodes.RejectReason.NONE;
            _transport = new FakeBackendTransport((request, token) => Task.FromResult(Respond(request)));
            _gateway   = new BackendGateway(_transport, ZeroClock,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            _audience  = new FakeTownAudience();
            _relay     = new IntroProgressRelay(_gateway, _audience);
            _member    = new TownMember(PARTICIPANT_ID, ACCOUNT_ID, CHARACTER_ID, LEASE_ID, "a", EPlayerClass.Warrior);
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
        public void Request_Committed_PostsBindingAccountAndRepliesNone()
        {
            Guid operationId = Guid.NewGuid();

            Assert.AreEqual(ERejectReason.None, _relay.Request(_member, new IntroCompleteRequestMessage(operationId)));
            Complete(1);

            Assert.IsTrue(_transport.Requests.TryDequeue(out BackendRequest request));
            Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, GrowthRoutes.INTRO_COMPLETE_FORMAT,
                Guid.Parse(ACCOUNT_ID).ToString("D")), request.Path);
            var body = BackendJson.Deserialize<IntroCompleteRequest>(request.BodyJson);
            Assert.AreEqual(operationId.ToString("D"), body.OperationId);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out IntroCompleteResultMessage result));
            Assert.AreEqual(operationId, result.OperationId);
            Assert.AreEqual(ERejectReason.None, result.Reason);
            Assert.AreEqual(0, _relay.PendingCount);
        }

        [Test]
        public void Request_WhileInFlight_SameIdAcceptedOtherIdPendingCommit()
        {
            Guid operationId = Guid.NewGuid();
            _relay.Request(_member, new IntroCompleteRequestMessage(operationId));

            Assert.AreEqual(ERejectReason.None, _relay.Request(_member, new IntroCompleteRequestMessage(operationId)));
            Assert.AreEqual(ERejectReason.PendingCommit,
                _relay.Request(_member, new IntroCompleteRequestMessage(Guid.NewGuid())));
            Complete(1);
            Assert.AreEqual(1, _transport.Calls);
        }

        [Test]
        public void Request_UnknownResultThenRejected_RepliesUnavailableThenApiReason()
        {
            _status = STATUS_DOWN;
            Guid operationId = Guid.NewGuid();
            _relay.Request(_member, new IntroCompleteRequestMessage(operationId));
            Complete(1);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out IntroCompleteResultMessage unknown));
            Assert.AreEqual(ERejectReason.BackendUnavailable, unknown.Reason);

            _status = STATUS_CONFLICT;
            _reason = ContractCodes.RejectReason.SESSION_MISMATCH;
            _relay.Request(_member, new IntroCompleteRequestMessage(operationId));
            Complete(1);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out IntroCompleteResultMessage rejected));
            Assert.AreEqual(ERejectReason.SessionMismatch, rejected.Reason);
        }

        [Test]
        public void Request_EmptyOperationOrNoIdentity_RejectsWithoutApiCall()
        {
            var anonymous = new TownMember(PARTICIPANT_ID, null, null, null, "a", EPlayerClass.Warrior);

            Assert.AreEqual(ERejectReason.InvalidTarget, _relay.Request(_member, new IntroCompleteRequestMessage(Guid.Empty)));
            Assert.AreEqual(ERejectReason.SessionMismatch,
                _relay.Request(anonymous, new IntroCompleteRequestMessage(Guid.NewGuid())));
            Assert.AreEqual(ERejectReason.BackendUnavailable,
                new IntroProgressRelay(null, _audience).Request(_member, new IntroCompleteRequestMessage(Guid.NewGuid())));
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void Messages_RoundTrip_KeepOperationAndReason()
        {
            Guid operationId = Guid.NewGuid();
            var writer = new NetworkWriter();
            writer.WriteIntroCompleteRequestMessage(new IntroCompleteRequestMessage(operationId));
            writer.WriteIntroCompleteResultMessage(new IntroCompleteResultMessage(operationId, ERejectReason.PendingCommit));

            var reader = new NetworkReader(writer.ToArraySegment());
            IntroCompleteRequestMessage request = reader.ReadIntroCompleteRequestMessage();
            IntroCompleteResultMessage  result  = reader.ReadIntroCompleteResultMessage();

            Assert.AreEqual(operationId, request.OperationId);
            Assert.AreEqual(operationId, result.OperationId);
            Assert.AreEqual(ERejectReason.PendingCommit, result.Reason);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private void Complete(int count)
        {
            FakeBackendTransport.WaitForCompleted(_gateway, count);
            _gateway.Pump();
        }

        private BackendTransportResult Respond(BackendRequest request)
        {
            int status = _status;
            if (status == STATUS_DOWN)
            {
                return new BackendTransportResult(status, null);
            }

            var body   = BackendJson.Deserialize<IntroCompleteRequest>(request.BodyJson);
            int reason = _reason;
            int state  = (reason == ContractCodes.RejectReason.NONE)
                ? ContractCodes.OperationStatus.COMMITTED
                : ContractCodes.OperationStatus.REJECTED;
            var result = new OperationResult(body.OperationId, state, reason, 1, 0, string.Empty);
            return new BackendTransportResult(status, BackendJson.Serialize(result));
        }

        private static double ZeroClock() { return 0.0; }
    }
}
