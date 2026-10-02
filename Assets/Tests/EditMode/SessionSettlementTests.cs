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
    public class SessionSettlementTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID         = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const int    STATUS_OK          = 200;
        private const int    STATUS_CONFLICT    = 409;
        private const int    STATUS_UNAVAILABLE = 503;
        private const double START_TIME         = 10.0;

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
        public void TryBegin_Committed_SettlesOnceWithSessionEndedOperationId()
        {
            var       transport  = new FakeBackendTransport((request, token) =>
                Task.FromResult(new BackendTransportResult(STATUS_OK, Result(ContractCodes.OperationStatus.COMMITTED, 0))));
            using var gateway    = CreateGateway(transport);
            var       settlement = new SessionSettlement(SESSION_ID, gateway, _warnings.Add);

            Assert.IsTrue(settlement.TryBegin(START_TIME));
            Assert.IsFalse(settlement.TryBegin(START_TIME), "같은 출정은 한 번만 부른다");
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
            Assert.IsFalse(settlement.TryBegin(START_TIME + 1.0));
            settlement.Tick(START_TIME + 100.0);

            Assert.AreEqual(ESessionSettlementState.Settled, settlement.State);
            Assert.AreEqual(1, transport.Calls);
            Assert.AreEqual(1, settlement.RequestCount);
            Assert.IsTrue(transport.Requests.TryDequeue(out BackendRequest request));
            Assert.AreEqual(string.Format(GameServerRoutes.SETTLEMENT_FORMAT, SESSION_ID), request.Path);
            Assert.AreEqual(SessionContractIds.CreateSettlementOperationId(SESSION_ID), request.OperationId);
            var body = BackendJson.Deserialize<SessionSettlementRequest>(request.BodyJson);
            Assert.AreEqual(request.OperationId, body.OperationId);
        }

        [Test]
        public void OnBackendResponse_AlreadySettled_TreatsAsSettled()
        {
            var       transport  = FakeBackendTransport.Always(STATUS_CONFLICT,
                Result(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.ALREADY_SETTLED));
            using var gateway    = CreateGateway(transport);
            var       settlement = new SessionSettlement(SESSION_ID, gateway, _warnings.Add);

            settlement.TryBegin(START_TIME);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
            settlement.Tick(START_TIME + 100.0);

            Assert.AreEqual(ESessionSettlementState.Settled, settlement.State);
            Assert.AreEqual(1, transport.Calls, "이미 정산된 출정은 다시 부르지 않는다");
            Assert.AreEqual(0, _warnings.Count);
        }

        [Test]
        public void OnBackendResponse_PermanentRejection_StopsAndLeavesItToRecovery()
        {
            var       transport  = FakeBackendTransport.Always(STATUS_CONFLICT,
                Result(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.SESSION_MISMATCH));
            using var gateway    = CreateGateway(transport);
            var       settlement = new SessionSettlement(SESSION_ID, gateway, _warnings.Add);

            settlement.TryBegin(START_TIME);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
            settlement.Tick(START_TIME + 100.0);

            Assert.AreEqual(ESessionSettlementState.Abandoned, settlement.State);
            Assert.AreEqual(1, transport.Calls);
            Assert.AreEqual(1, _warnings.Count);
        }

        [Test]
        public void Tick_TransientFailureAfterGatewayRetries_ResendsSameOperationIdThenSettles()
        {
            // 게이트웨이 한 번의 Send는 재시도까지 모두 4번 보낸다. 그 4번이 모두 실패하면 정산이 다음 시각에 다시 부른다
            int       gatewayAttempts = Constants.Network.G_ApiRetryDelaysSeconds.Length + 1;
            int       calls           = 0;
            var       transport       = new FakeBackendTransport((request, token) =>
            {
                int call = Interlocked.Increment(ref calls);
                return Task.FromResult(call <= gatewayAttempts
                    ? new BackendTransportResult(STATUS_UNAVAILABLE, null)
                    : new BackendTransportResult(STATUS_OK, Result(ContractCodes.OperationStatus.COMMITTED, 0)));
            });
            using var gateway         = CreateGateway(transport);
            var       settlement      = new SessionSettlement(SESSION_ID, gateway, _warnings.Add);

            settlement.TryBegin(START_TIME);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
            Assert.AreEqual(ESessionSettlementState.Retrying, settlement.State);

            settlement.Tick(START_TIME);
            Assert.AreEqual(ESessionSettlementState.Retrying, settlement.State, "재시도 시각 전에는 보내지 않는다");
            settlement.Tick(START_TIME + Constants.Session.SETTLEMENT_RETRY_INITIAL_SECONDS);
            Assert.AreEqual(ESessionSettlementState.InFlight, settlement.State);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(ESessionSettlementState.Settled, settlement.State);
            Assert.AreEqual(2, settlement.RequestCount);
            string operationId = null;
            while (transport.Requests.TryDequeue(out BackendRequest request))
            {
                operationId = operationId ?? request.OperationId;
                Assert.AreEqual(operationId, request.OperationId);
            }

            Assert.AreEqual(SessionContractIds.CreateSettlementOperationId(SESSION_ID), operationId);
        }

        [Test]
        public void Tick_ManyTransientFailures_KeepsRetryingWithCappedBackoff()
        {
            const int FAILED_REQUESTS = 12;
            var       transport       = FakeBackendTransport.Always(STATUS_UNAVAILABLE, null);
            using var gateway         = CreateGateway(transport);
            var       settlement      = new SessionSettlement(SESSION_ID, gateway, _warnings.Add);
            double    now             = START_TIME;

            settlement.TryBegin(now);
            for (int i = 0; i < FAILED_REQUESTS; ++i)
            {
                FakeBackendTransport.WaitForCompleted(gateway, 1);
                gateway.Pump();
                Assert.AreEqual(ESessionSettlementState.Retrying, settlement.State, "일시 실패는 횟수와 무관하게 멈추지 않는다");
                Assert.LessOrEqual(settlement.RetryDelaySeconds, Constants.Session.SETTLEMENT_RETRY_MAX_SECONDS);
                now += Constants.Session.SETTLEMENT_RETRY_MAX_SECONDS;
                settlement.Tick(now);
            }

            Assert.AreEqual(FAILED_REQUESTS + 1, settlement.RequestCount);
            Assert.AreEqual(Constants.Session.SETTLEMENT_RETRY_MAX_SECONDS, settlement.RetryDelaySeconds);
            Assert.IsFalse(settlement.IsFinished);
        }

        [TestCase(401, 0)]
        [TestCase(409, ContractCodes.RejectReason.NOT_ELIGIBLE)]
        public void OnBackendResponse_RecoverableRejection_Retries(int status, int reasonCode)
        {
            var       transport  = FakeBackendTransport.Always(status,
                Result(ContractCodes.OperationStatus.REJECTED, reasonCode));
            using var gateway    = CreateGateway(transport);
            var       settlement = new SessionSettlement(SESSION_ID, gateway, _warnings.Add);

            settlement.TryBegin(START_TIME);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(ESessionSettlementState.Retrying, settlement.State, "저장된 거절이 아니면 멈추지 않는다");
        }

        [Test]
        public void OnBackendResponse_BadRequest_StopsAsTerminal()
        {
            var       transport  = FakeBackendTransport.Always(400, null);
            using var gateway    = CreateGateway(transport);
            var       settlement = new SessionSettlement(SESSION_ID, gateway, _warnings.Add);

            settlement.TryBegin(START_TIME);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(ESessionSettlementState.Abandoned, settlement.State);
            Assert.IsTrue(settlement.IsFinished);
        }

        [Test]
        public void TryBegin_NoGatewayInDevelopment_SettlesWithoutRequest()
        {
            var settlement = new SessionSettlement(SESSION_ID, null, _warnings.Add);

            Assert.IsTrue(settlement.TryBegin(START_TIME));

            Assert.AreEqual(ESessionSettlementState.Settled, settlement.State);
            Assert.AreEqual(0, settlement.RequestCount);
            Assert.IsTrue(settlement.IsFinished);
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

        private static string Result(int status, int reasonCode)
        {
            return BackendJson.Serialize(new OperationResult(SessionContractIds.CreateSettlementOperationId(SESSION_ID), status,
                reasonCode, 0L, 0L, string.Empty));
        }
    }
}
