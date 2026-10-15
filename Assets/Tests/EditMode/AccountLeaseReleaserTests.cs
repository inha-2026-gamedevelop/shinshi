// System
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Tasks;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class AccountLeaseReleaserTests
    {
        // N5 회귀: 마을 연결이 끊기면 계정 접속 임대 해제를 보내고, 일시 실패는 몇 번 더 보낸 뒤 포기한다

        /****************************************
        *                Fields
        ****************************************/

        private const string INSTANCE_ID         = "7d1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string LEASE_ID            = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string BRACED_LEASE_ID     = "{0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01}";
        private const string CHARACTER_ID        = "char-1";
        private const string TOWN_KEY            = "c:char-1";
        private const double START_TIME          = 100.0;
        private const int    STATUS_OK           = 200;
        private const int    STATUS_UNAUTHORIZED = 401;
        private const int    STATUS_NOT_FOUND    = 404;
        private const int    STATUS_UNAVAILABLE  = 503;
        private const int    REGISTRY_CAPACITY   = 4;

        private double               _now;
        private FakeBackendTransport _transport;
        private BackendGateway       _gateway;
        private AccountLeaseReleaser _releaser;

        // 스레드 풀의 가짜 전송기가 읽는다
        private volatile int _status = STATUS_OK;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now       = START_TIME;
            _status    = STATUS_OK;
            _transport = new FakeBackendTransport((request, token) =>
                Task.FromResult(new BackendTransportResult(_status, null)));
            _gateway   = new BackendGateway(_transport, ZeroClock,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            _releaser  = new AccountLeaseReleaser(_gateway, INSTANCE_ID, ReadClock, null);
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
        public void Release_ValidLease_SendsBodylessPostToReleaseRoute()
        {
            Assert.IsTrue(_releaser.Release(LEASE_ID));
            Complete();

            Assert.IsTrue(_transport.Requests.TryDequeue(out BackendRequest request));
            Assert.AreEqual(EBackendMethod.Post, request.Method);
            Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.ACCOUNT_LEASE_RELEASE_FORMAT, INSTANCE_ID,
                LEASE_ID), request.Path);
            Assert.IsNull(request.BodyJson, "해제는 본문 없이 보낸다");
            Assert.IsFalse(request.BypassesCircuit, "해제는 데이터 요청이라 회로를 거친다");
            Assert.AreEqual(0, _releaser.PendingCount);
        }

        [Test]
        public void Release_TransientFailure_RetriesUpToMaxAttemptsThenGivesUp()
        {
            _status = STATUS_UNAVAILABLE;
            _releaser.Release(LEASE_ID);
            Complete();

            for (int attempt = 2; attempt <= Constants.Network.ACCOUNT_LEASE_RELEASE_MAX_ATTEMPTS; ++attempt)
            {
                Assert.AreEqual(1, _releaser.PendingCount);
                _now += Constants.Network.ACCOUNT_LEASE_RELEASE_RETRY_SECONDS;
                _releaser.Tick();
                Complete();
            }

            Assert.AreEqual(Constants.Network.ACCOUNT_LEASE_RELEASE_MAX_ATTEMPTS, _transport.Calls);
            Assert.AreEqual(0, _releaser.PendingCount, "끝내 실패하면 포기하고 heartbeat 만료에 맡긴다");

            _now += Constants.Network.ACCOUNT_LEASE_RELEASE_RETRY_SECONDS;
            _releaser.Tick();
            Assert.AreEqual(Constants.Network.ACCOUNT_LEASE_RELEASE_MAX_ATTEMPTS, _transport.Calls);
        }

        [Test]
        public void Release_TransientThenSuccess_StopsRetrying()
        {
            _status = STATUS_UNAVAILABLE;
            _releaser.Release(LEASE_ID);
            Complete();

            _status = STATUS_OK;
            _now += Constants.Network.ACCOUNT_LEASE_RELEASE_RETRY_SECONDS - 0.5;
            _releaser.Tick();
            Assert.AreEqual(1, _transport.Calls, "재시도 간격 전에는 다시 보내지 않는다");

            _now += 0.5;
            _releaser.Tick();
            Complete();
            Assert.AreEqual(2, _transport.Calls);
            Assert.AreEqual(0, _releaser.PendingCount);
        }

        [Test]
        public void Release_Rejected_DoesNotRetryExceptUnauthorized()
        {
            _status = STATUS_NOT_FOUND;
            _releaser.Release(LEASE_ID);
            Complete();
            Assert.AreEqual(0, _releaser.PendingCount, "이미 풀린 임대는 다시 보내지 않는다");

            _status = STATUS_UNAUTHORIZED;
            _releaser.Release(LEASE_ID);
            Complete();
            Assert.AreEqual(1, _releaser.PendingCount, "401은 서버 임대 갱신 사이의 거절이라 다시 보낸다");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("../../admin")]
        [TestCase("00000000-0000-0000-0000-000000000000")]
        public void Release_InvalidLeaseId_IsNotSent(string leaseId)
        {
            Assert.IsFalse(_releaser.Release(leaseId));
            Assert.AreEqual(0, _releaser.PendingCount);
            Assert.AreEqual(0, _gateway.InFlightCount + _gateway.CompletedCount);
        }

        [Test]
        public void Release_SameLeaseTwice_SendsOnce()
        {
            _status = STATUS_UNAVAILABLE;
            _releaser.Release(LEASE_ID);
            _releaser.Release(LEASE_ID);
            Complete();

            Assert.AreEqual(1, _transport.Calls);
        }

        [Test]
        public void ToAccountLeaseId_BracedGuid_NormalizesToHyphenFormat()
        {
            Assert.AreEqual(LEASE_ID, JoinTokenClaimsRules.ToAccountLeaseId(BRACED_LEASE_ID));
            Assert.IsNull(JoinTokenClaimsRules.ToAccountLeaseId("not-a-guid"));
        }

        [Test]
        public void ForgetTownBinding_WithLease_ForgetsKeyAndSendsRelease()
        {
            var registry = new ServerSessionRegistry(REGISTRY_CAPACITY);
            var claims   = new JoinTokenClaims(TOWN_KEY, CHARACTER_ID, INSTANCE_ID, null, LEASE_ID);
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));
            Assert.AreEqual(LEASE_ID, binding.AccountLeaseId, "교환 결과의 계정 임대가 연결 결합에 실린다");

            ShinshiServerAuthenticator.ForgetTownBinding(registry, binding, _releaser);
            Complete();

            Assert.AreEqual(0, registry.ParticipantCount);
            Assert.AreEqual(1, _transport.Calls);
        }

        [Test]
        public void ForgetTownBinding_DepartingToDungeon_KeepsLeaseForTransfer()
        {
            var registry = new ServerSessionRegistry(REGISTRY_CAPACITY);
            var claims   = new JoinTokenClaims(TOWN_KEY, CHARACTER_ID, INSTANCE_ID, null, LEASE_ID);
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));
            binding.IsDeparting = true;

            ShinshiServerAuthenticator.ForgetTownBinding(registry, binding, _releaser);

            Assert.AreEqual(0, registry.ParticipantCount);
            Assert.AreEqual(0, _releaser.PendingCount, "던전 이관 전에 임대를 풀면 이관이 거절된다");
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void ForgetTownBinding_WithoutLease_SendsNothing()
        {
            var registry = new ServerSessionRegistry(REGISTRY_CAPACITY);
            var claims   = new JoinTokenClaims(TOWN_KEY, CHARACTER_ID, INSTANCE_ID, null);
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));

            ShinshiServerAuthenticator.ForgetTownBinding(registry, binding, _releaser);

            Assert.AreEqual(0, registry.ParticipantCount);
            Assert.AreEqual(0, _releaser.PendingCount);
        }

        [Test]
        public void TryCreate_TownResponse_HasNoLeaseUntilContractFieldArrives()
        {
            // DTO 생성자는 백엔드 작업자가 필드를 더하며 바뀔 수 있어 JSON으로 만든다
            var response = BackendJson.Deserialize<JoinTokenExchangeResponse>(
                "{\"characterId\":\"" + CHARACTER_ID + "\",\"instanceId\":\"" + INSTANCE_ID + "\",\"serverRole\":" +
                ContractCodes.ServerRole.TOWN.ToString(CultureInfo.InvariantCulture) + ",\"classId\":" +
                ContractCodes.PlayerClass.WARRIOR.ToString(CultureInfo.InvariantCulture) + "}");

            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response,
                out JoinTokenClaims claims));
            Assert.IsNull(claims.AccountLeaseId, "필드가 없는 응답은 해제 없이 heartbeat 만료로 풀린다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Complete()
        {
            FakeBackendTransport.WaitForCompleted(_gateway, 1);
            _gateway.Pump();
        }

        private double ReadClock()
        {
            return _now;
        }

        private static double ZeroClock()
        {
            return 0.0;
        }
    }
}
