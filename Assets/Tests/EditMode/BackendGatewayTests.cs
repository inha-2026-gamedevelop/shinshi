// System
using System;
using System.Collections.Concurrent;
using System.Threading;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class BackendGatewayTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PATH                  = "/v1/test";
        private const string OPERATION_ID          = "op-1";
        private const float  TIMEOUT_SECONDS       = 1f;
        private const float  SHORT_TIMEOUT_SECONDS = 0.05f;
        private const int    STATUS_OK             = 200;
        private const int    STATUS_BAD_REQUEST    = 400;
        private const int    STATUS_UNAVAILABLE    = 503;
        private const int    SETTLE_MILLISECONDS   = 100;

        private ConcurrentQueue<TimeSpan> _delays;
        private double                    _now;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _delays = new ConcurrentQueue<TimeSpan>();
            _now    = 0.0;
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Send_Completed_DeliversOnlyInsidePumpOnCallingThread()
        {
            var       transport = FakeBackendTransport.Always(STATUS_OK, null);
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            int requestId = gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);

            Assert.AreEqual(0, listener.Responses.Count, "Pump 전에는 처리기를 부르지 않는다");

            Assert.AreEqual(1, gateway.Pump());
            Assert.AreEqual(1, listener.Responses.Count);
            Assert.AreEqual(requestId, listener.Last.RequestId);
            Assert.AreEqual(EBackendOutcome.Succeeded, listener.Last.Outcome);
            Assert.AreEqual(Thread.CurrentThread.ManagedThreadId, listener.ThreadIds[0]);
            Assert.AreEqual(0, gateway.InFlightCount);
        }

        [Test]
        public void Send_SuccessBody_DeserializesContractPayload()
        {
            string    body      = BackendJson.Serialize(new JoinTokenExchangeResponse("char-1", "dungeon-01", 1, "part-1", "sortie-1", 5L,
                ContractCodes.PlayerClass.WARRIOR));
            var       transport = FakeBackendTransport.Always(STATUS_OK, body);
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            gateway.Send(BackendRequest.Get(PATH, typeof(JoinTokenExchangeResponse), null, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            var payload = listener.Last.Payload as JoinTokenExchangeResponse;
            Assert.IsNotNull(payload);
            Assert.AreEqual("part-1", payload.ParticipantId);
        }

        [Test]
        public void Send_InvalidBody_ReturnsInvalidResponseWithoutRetry()
        {
            var       transport = FakeBackendTransport.Always(STATUS_OK, "not json");
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            gateway.Send(BackendRequest.Get(PATH, typeof(JoinTokenExchangeResponse), OPERATION_ID, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EBackendOutcome.InvalidResponse, listener.Last.Outcome);
            Assert.AreEqual(1, transport.Calls);
        }

        [Test]
        public void Send_TransportNeverAnswers_TimesOutOnceWithoutOperationId()
        {
            var       transport = FakeBackendTransport.Never();
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            gateway.Send(BackendRequest.Get(PATH, null, null, SHORT_TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EBackendOutcome.Timeout, listener.Last.Outcome);
            Assert.AreEqual(1, listener.Last.Attempts);
            Assert.AreEqual(1, transport.Calls);
            Assert.AreEqual(0, _delays.Count);
        }

        [Test]
        public void Send_TimeoutWithOperationId_RetriesWithSameRequest()
        {
            var       transport = FakeBackendTransport.Never();
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            gateway.Send(BackendRequest.Get(PATH, null, OPERATION_ID, SHORT_TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            int expectedAttempts = Constants.Network.G_ApiRetryDelaysSeconds.Length + 1;
            Assert.AreEqual(EBackendOutcome.Timeout, listener.Last.Outcome);
            Assert.AreEqual(expectedAttempts, transport.Calls);
            Assert.AreEqual(expectedAttempts, listener.Last.Attempts);

            BackendRequest first = null;
            foreach (BackendRequest sent in transport.Requests)
            {
                first = first ?? sent;
                Assert.AreSame(first, sent, "재시도는 같은 요청(같은 operationId와 본문)을 보낸다");
            }
        }

        [Test]
        public void Send_ServerErrorWithOperationId_RetriesWithContractDelays()
        {
            var       transport = FakeBackendTransport.Always(STATUS_UNAVAILABLE, null);
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            gateway.Send(BackendRequest.Post(PATH, new JoinTokenExchangeRequest("t", "i", 0), null, OPERATION_ID, TIMEOUT_SECONDS),
                listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            float[] expected = Constants.Network.G_ApiRetryDelaysSeconds;
            Assert.AreEqual(EBackendOutcome.ServerError, listener.Last.Outcome);
            Assert.AreEqual(expected.Length + 1, transport.Calls);
            Assert.AreEqual(expected.Length, _delays.Count);

            int index = 0;
            foreach (TimeSpan delay in _delays)
            {
                Assert.AreEqual(expected[index], (float)delay.TotalSeconds, 0.0001f);
                ++index;
            }
        }

        [Test]
        public void Send_ServerErrorWithoutOperationId_DoesNotRetry()
        {
            var       transport = FakeBackendTransport.Always(STATUS_UNAVAILABLE, null);
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            gateway.Send(BackendRequest.Post(PATH, new JoinTokenExchangeRequest("t", "i", 0), null, null, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EBackendOutcome.ServerError, listener.Last.Outcome);
            Assert.AreEqual(1, transport.Calls);
            Assert.AreEqual(0, _delays.Count);
        }

        [Test]
        public void Send_RejectedWithOperationId_DoesNotRetryAndReadsReasonCode()
        {
            string    body      = BackendJson.Serialize(new ApiRejection(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, "b", 1L));
            var       transport = FakeBackendTransport.Always(STATUS_BAD_REQUEST, body);
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            gateway.Send(BackendRequest.Get(PATH, null, OPERATION_ID, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EBackendOutcome.Rejected, listener.Last.Outcome);
            Assert.AreEqual(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, listener.Last.ReasonCode);
            Assert.AreEqual(STATUS_BAD_REQUEST, listener.Last.StatusCode);
            Assert.AreEqual(1, transport.Calls);
        }

        [Test]
        public void Send_ConsecutiveFailures_OpenCircuitAndSkipTransport()
        {
            var       transport = FakeBackendTransport.Always(STATUS_UNAVAILABLE, null);
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();
            int       threshold = Constants.Network.BACKEND_CIRCUIT_FAILURE_THRESHOLD;

            // 순서대로 보내야 실패 횟수가 정확히 쌓인다
            for (int i = 0; i < threshold; ++i)
            {
                gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), listener);
                FakeBackendTransport.WaitForCompleted(gateway, 1);
                gateway.Pump();
            }

            Assert.AreEqual(EBackendCircuitState.Open, gateway.CircuitState);

            gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EBackendOutcome.CircuitOpen, listener.Last.Outcome);
            Assert.AreEqual(0, listener.Last.Attempts);
            Assert.AreEqual(threshold, transport.Calls);
        }

        [Test]
        public void Send_CircuitOpenTimeElapsed_AllowsProbeAndClosesOnSuccess()
        {
            int       status    = STATUS_UNAVAILABLE;
            var       transport = new FakeBackendTransport((request, token) =>
                System.Threading.Tasks.Task.FromResult(new BackendTransportResult(Volatile.Read(ref status), null)));
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            for (int i = 0; i < Constants.Network.BACKEND_CIRCUIT_FAILURE_THRESHOLD; ++i)
            {
                gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), listener);
                FakeBackendTransport.WaitForCompleted(gateway, 1);
                gateway.Pump();
            }

            Volatile.Write(ref status, STATUS_OK);
            _now += Constants.Network.BACKEND_CIRCUIT_OPEN_SECONDS;

            gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EBackendOutcome.Succeeded, listener.Last.Outcome);
            Assert.AreEqual(EBackendCircuitState.Closed, gateway.CircuitState);
        }

        // N1 회귀: 생존 신호는 열린 회로에서도 나가고, 실패와 성공 모두 회로에 세지 않는다
        [Test]
        public void Send_BypassRequest_SentWhileCircuitOpenAndNotRecorded()
        {
            int       status    = STATUS_UNAVAILABLE;
            var       transport = new FakeBackendTransport((request, token) =>
                System.Threading.Tasks.Task.FromResult(new BackendTransportResult(Volatile.Read(ref status), null)));
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();
            OpenCircuit(gateway, listener);

            Volatile.Write(ref status, STATUS_OK);
            gateway.Send(BypassPost(), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EBackendOutcome.Succeeded, listener.Last.Outcome, "회로가 열려도 생존 신호는 보낸다");
            Assert.AreEqual(EBackendCircuitState.Open, gateway.CircuitState, "생존 신호 성공이 데이터 회로를 닫지 않는다");
        }

        [Test]
        public void Send_BypassFailures_DoNotOpenCircuit()
        {
            var       transport = FakeBackendTransport.Always(STATUS_UNAVAILABLE, null);
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();

            for (int i = 0; i < Constants.Network.BACKEND_CIRCUIT_FAILURE_THRESHOLD + 1; ++i)
            {
                gateway.Send(BypassPost(), listener);
                FakeBackendTransport.WaitForCompleted(gateway, 1);
                gateway.Pump();
            }

            Assert.AreEqual(EBackendCircuitState.Closed, gateway.CircuitState);
            Assert.AreEqual(Constants.Network.BACKEND_CIRCUIT_FAILURE_THRESHOLD + 1, transport.Calls);
        }

        [Test]
        public void Send_BypassAfterOpenTime_DoesNotTakeHalfOpenProbeSlot()
        {
            int       status    = STATUS_UNAVAILABLE;
            var       transport = new FakeBackendTransport((request, token) =>
                System.Threading.Tasks.Task.FromResult(new BackendTransportResult(Volatile.Read(ref status), null)));
            using var gateway   = CreateGateway(transport);
            var       listener  = new RecordingBackendListener();
            OpenCircuit(gateway, listener);

            _now += Constants.Network.BACKEND_CIRCUIT_OPEN_SECONDS;
            gateway.Send(BypassPost(), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
            Assert.AreEqual(EBackendCircuitState.Open, gateway.CircuitState, "생존 신호는 시험 호출 자리를 잡지 않는다");

            Volatile.Write(ref status, STATUS_OK);
            gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(EBackendOutcome.Succeeded, listener.Last.Outcome, "데이터 요청이 시험 호출로 나간다");
            Assert.AreEqual(EBackendCircuitState.Closed, gateway.CircuitState);
        }

        // 계약: 효과 경로의 생존 판정 일시 실패는 503 + 810 + Retry-After로 온다. 확정 거절이 아니라 일시 실패다
        [Test]
        public void Send_UnavailableWithBackendUnavailableReason_IsTransientNotTerminal()
        {
            const double RETRY_AFTER = 2.0;
            string       body        = BackendJson.Serialize(new ApiRejection(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, string.Empty, 0L));
            var          transport   = new FakeBackendTransport((request, token) =>
                System.Threading.Tasks.Task.FromResult(new BackendTransportResult(STATUS_UNAVAILABLE, body, RETRY_AFTER)));
            using var    gateway     = CreateGateway(transport);
            var          listener    = new RecordingBackendListener();

            gateway.Send(BackendRequest.Get(PATH, null, OPERATION_ID, TIMEOUT_SECONDS), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            BackendResponse response = listener.Last;
            Assert.AreEqual(EBackendOutcome.ServerError, response.Outcome);
            Assert.AreEqual(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, response.ReasonCode);
            Assert.AreEqual(RETRY_AFTER, response.RetryAfterSeconds);
            Assert.IsFalse(BackendFailureRules.IsTerminalRejection(in response));
        }

        [Test]
        public void Dispose_PendingRequest_IsNeverDelivered()
        {
            var transport = FakeBackendTransport.Never();
            var gateway   = CreateGateway(transport);
            var listener  = new RecordingBackendListener();

            gateway.Send(BackendRequest.Get(PATH, null, OPERATION_ID, TIMEOUT_SECONDS), listener);
            gateway.Dispose();
            Thread.Sleep(SETTLE_MILLISECONDS);

            Assert.AreEqual(0, gateway.Pump());
            Assert.AreEqual(0, listener.Responses.Count);
            Assert.IsTrue(transport.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), listener));
        }

        [Test]
        public void Request_AbsoluteOrParentPath_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => BackendRequest.Get("https://evil.example/v1", null, null, TIMEOUT_SECONDS));
            Assert.Throws<ArgumentException>(() => BackendRequest.Get("//evil.example/v1", null, null, TIMEOUT_SECONDS));
            Assert.Throws<ArgumentException>(() => BackendRequest.Get("/v1/../admin/v1", null, null, TIMEOUT_SECONDS));
            Assert.Throws<ArgumentOutOfRangeException>(() => BackendRequest.Get(PATH, null, null, 0f));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private BackendGateway CreateGateway(FakeBackendTransport transport)
        {
            return new BackendGateway(transport, ReadClock, FakeBackendTransport.ImmediateDelay(_delays), null, null);
        }

        private double ReadClock()
        {
            return _now;
        }

        private static BackendRequest BypassPost()
        {
            return BackendRequest.Post(PATH, new ServerHeartbeatRequest("boot"), null, null, TIMEOUT_SECONDS, true);
        }

        private static void OpenCircuit(BackendGateway gateway, RecordingBackendListener listener)
        {
            for (int i = 0; i < Constants.Network.BACKEND_CIRCUIT_FAILURE_THRESHOLD; ++i)
            {
                gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), listener);
                FakeBackendTransport.WaitForCompleted(gateway, 1);
                gateway.Pump();
            }

            Assert.AreEqual(EBackendCircuitState.Open, gateway.CircuitState);
        }
    }
}
