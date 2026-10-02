// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
    public class HttpClientBackendTransportTests
    {
        // 실제 HttpClient 경로를 메시지 처리기로 끊어 헤더, 401 갱신 경합 재시도, Retry-After 해석을 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string BASE_URL      = "https://api.example.test/";
        private const string INSTANCE_ID   = "7d1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string SERVICE_TOKEN = "service-token-value";
        private const string OLD_TOKEN     = "lease-token-old";
        private const string NEW_TOKEN     = "lease-token-new";
        private const double LEASE_EXPIRY  = 1000.0;
        private const int    RETRY_AFTER   = 7;

        private ServerCredentials _credentials;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _credentials = new ServerCredentials(SERVICE_TOKEN, INSTANCE_ID, ZeroClock);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void SendAsync_LeasePath_CarriesServiceTokenAndInstanceHeader()
        {
            var       handler   = new ScriptedHandler(HttpStatusCode.OK);
            using var transport = new HttpClientBackendTransport(new Uri(BASE_URL), _credentials, handler);

            Send(transport, GameServerRoutes.SERVER_LEASE);

            Assert.AreEqual(ApiRoutes.BEARER_PREFIX + SERVICE_TOKEN, handler.Authorizations[0]);
            Assert.AreEqual(INSTANCE_ID, handler.InstanceIds[0]);
        }

        [Test]
        public void SendAsync_UnauthorizedAfterLeaseRenewed_RetriesOnceWithNewToken()
        {
            _credentials.SetLease(OLD_TOKEN, LEASE_EXPIRY);
            var handler = new ScriptedHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);

            // 첫 요청이 나간 사이 임대가 갱신돼 옛 토큰이 폐기된 경합을 흉내 낸다
            handler.OnFirstRequest = () => _credentials.SetLease(NEW_TOKEN, LEASE_EXPIRY);
            using var transport = new HttpClientBackendTransport(new Uri(BASE_URL), _credentials, handler);

            BackendTransportResult result = Send(transport, GameServerRoutes.SERVER_REGISTER);

            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);
            Assert.AreEqual(2, handler.Authorizations.Count);
            Assert.AreEqual(ApiRoutes.BEARER_PREFIX + OLD_TOKEN, handler.Authorizations[0]);
            Assert.AreEqual(ApiRoutes.BEARER_PREFIX + NEW_TOKEN, handler.Authorizations[1]);
            Assert.AreEqual(handler.Bodies[0], handler.Bodies[1], "같은 operationId와 본문으로 다시 보낸다");
        }

        [Test]
        public void SendAsync_UnauthorizedWithoutRenewal_DoesNotRetry()
        {
            _credentials.SetLease(OLD_TOKEN, LEASE_EXPIRY);
            var       handler   = new ScriptedHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
            using var transport = new HttpClientBackendTransport(new Uri(BASE_URL), _credentials, handler);

            BackendTransportResult result = Send(transport, GameServerRoutes.SERVER_REGISTER);

            Assert.AreEqual((int)HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.AreEqual(1, handler.Authorizations.Count, "새 임대가 없으면 한 번만 보낸다");
        }

        [Test]
        public void SendAsync_TooManyRequests_ReadsRetryAfterSeconds()
        {
            _credentials.SetLease(OLD_TOKEN, LEASE_EXPIRY);
            var       handler   = new ScriptedHandler((HttpStatusCode)429) { RetryAfterSeconds = RETRY_AFTER };
            using var transport = new HttpClientBackendTransport(new Uri(BASE_URL), _credentials, handler);

            BackendTransportResult result = Send(transport, GameServerRoutes.SERVER_REGISTER);

            Assert.AreEqual(429, result.StatusCode);
            Assert.AreEqual(RETRY_AFTER, result.RetryAfterSeconds, 0.001);
        }

        [Test]
        public void CanSend_NoLease_OnlyLeasePathAllowed()
        {
            using var transport = new HttpClientBackendTransport(new Uri(BASE_URL), _credentials,
                new ScriptedHandler(HttpStatusCode.OK));

            Assert.IsTrue(transport.CanSend(Request(GameServerRoutes.SERVER_LEASE)));
            Assert.IsFalse(transport.CanSend(Request(GameServerRoutes.SERVER_REGISTER)));
            Assert.IsFalse(transport.CanSend(Request(JoinApiRoutes.JOIN_TOKEN_EXCHANGE)));
        }

        [Test]
        public void Gateway_RetryAfter_ReturnsWithoutInternalRetryAndKeepsDelay()
        {
            var       transport = new FakeBackendTransport((request, token) =>
                Task.FromResult(new BackendTransportResult(429, null, RETRY_AFTER)));
            var       listener  = new RecordingBackendListener();
            using var gateway   = new BackendGateway(transport, ZeroClock, null, null, null);

            gateway.Send(Request(GameServerRoutes.SERVER_REGISTER), listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            Assert.AreEqual(1, transport.Calls, "Retry-After가 오면 게이트웨이가 바로 다시 보내지 않는다");
            Assert.AreEqual(EBackendOutcome.ServerError, listener.Last.Outcome);
            Assert.AreEqual(RETRY_AFTER, listener.Last.RetryAfterSeconds, 0.001);
        }

        [Test]
        public void Settlement_RetryAfter_WaitsAtLeastThatLong()
        {
            var       transport  = new FakeBackendTransport((request, token) =>
                Task.FromResult(new BackendTransportResult(429, null, RETRY_AFTER * 10)));
            using var gateway    = new BackendGateway(transport, ZeroClock, null, null, null);
            var       settlement = new SessionSettlement("6f1c2d3e-4a5b-4c6d-8e7f-001122334455", gateway, null);

            settlement.TryBegin(0.0);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
            settlement.Tick(Constants.Session.SETTLEMENT_RETRY_MAX_SECONDS);

            Assert.AreEqual(ESessionSettlementState.Retrying, settlement.State, "Retry-After 전에는 다시 부르지 않는다");
            settlement.Tick(RETRY_AFTER * 10);
            Assert.AreEqual(ESessionSettlementState.InFlight, settlement.State);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static BackendTransportResult Send(HttpClientBackendTransport transport, string path)
        {
            return transport.SendAsync(Request(path), CancellationToken.None).GetAwaiter().GetResult();
        }

        private static BackendRequest Request(string path)
        {
            return BackendRequest.Post(path, new ServerHeartbeatRequest("boot"), null, "op",
                Constants.Network.API_TIMEOUT_SECONDS);
        }

        private static double ZeroClock()
        {
            return 0.0;
        }

        private sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode[] _statuses;
            private int _calls = 0;

            public List<string> Authorizations    { get; } = new List<string>();
            public List<string> InstanceIds       { get; } = new List<string>();
            public List<string> Bodies            { get; } = new List<string>();
            public Action       OnFirstRequest    { get; set; }
            public int          RetryAfterSeconds { get; set; }

            public ScriptedHandler(params HttpStatusCode[] statuses)
            {
                _statuses = statuses;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Authorizations.Add(string.Join(",", request.Headers.GetValues(ApiRoutes.AUTHORIZATION_HEADER)));
                InstanceIds.Add(string.Join(",", request.Headers.GetValues(JoinApiRoutes.SERVER_INSTANCE_HEADER)));
                string body = (request.Content != null)
                    ? await request.Content.ReadAsStringAsync().ConfigureAwait(false)
                    : null;
                Bodies.Add(body);
                if (_calls == 0)
                {
                    OnFirstRequest?.Invoke();
                }

                HttpStatusCode status = _statuses[Math.Min(_calls, _statuses.Length - 1)];
                ++_calls;
                var response = new HttpResponseMessage(status) { Content = new StringContent(string.Empty) };
                if (RetryAfterSeconds > 0)
                {
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                        TimeSpan.FromSeconds(RetryAfterSeconds));
                }

                return response;
            }
        }
    }
}
