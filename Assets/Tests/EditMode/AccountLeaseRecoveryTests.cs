// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class AccountLeaseRecoveryTests
    {
        // D2 회귀: 버려진 계정 임대(검증 중 연결 소실, 마감 뒤 결과, 출정 입장권 뒤 이관 없음)를 명시적으로 풀고 교환 기억을 지운다

        /****************************************
        *                Fields
        ****************************************/

        private const string INSTANCE_ID       = "7d1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string LEASE_ID          = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string CHARACTER_ID      = "char-1";
        private const string TOWN_KEY          = "c:char-1";
        private const string TOKEN             = "join-token-1";
        private const int    AUTH_REQUEST      = 7;
        private const int    STATUS_OK         = 200;
        private const int    REGISTRY_CAPACITY = 4;
        private const long   TICKET_EXPIRES_MS = 1_000_000L;

        private FakeBackendTransport _transport;
        private BackendGateway       _gateway;
        private AccountLeaseReleaser _releaser;
        private GameObject           _host;
        private double               _now;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeBackendTransport((request, token) => Task.FromResult(new BackendTransportResult(STATUS_OK, null)));
            _gateway   = new BackendGateway(_transport, ZeroClock,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            _now       = 100.0;
            _releaser  = new AccountLeaseReleaser(_gateway, INSTANCE_ID, () => _now, null);
        }

        [TearDown]
        public void TearDown()
        {
            _gateway.Dispose();
            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void VerifiedSuccess_WithoutWaitingConnection_ReleasesLeaseAndAbandonsExchange()
        {
            var verifier = new RecordingVerifier();
            ShinshiServerAuthenticator authenticator = CreateAuthenticator(verifier);
            var claims = new JoinTokenClaims(TOWN_KEY, CHARACTER_ID, INSTANCE_ID, null, LEASE_ID);

            // 검증 중인 대기 항목이 없다(연결이 사라졌거나 마감 뒤 도착)
            authenticator.OnJoinTokenVerified(AUTH_REQUEST, ERejectReason.None, in claims);
            FakeBackendTransport.WaitForCompleted(_gateway, 1);

            Assert.AreEqual(1, _transport.Calls, "임대 해제를 보낸다");
            CollectionAssert.AreEqual(new[] { AUTH_REQUEST }, verifier.Abandoned);
        }

        [Test]
        public void VerifiedFailure_WithoutWaitingConnection_ReleasesNothing()
        {
            var verifier = new RecordingVerifier();
            ShinshiServerAuthenticator authenticator = CreateAuthenticator(verifier);

            authenticator.OnJoinTokenVerified(AUTH_REQUEST, ERejectReason.TokenExpired, default);

            Assert.AreEqual(0, _transport.Calls);
            Assert.AreEqual(0, _gateway.InFlightCount + _gateway.CompletedCount);
        }

        [Test]
        public void ApiVerifier_OnAbandoned_ForgetsExchangeSoSameTokenIsExchangedAgain()
        {
            using BackendGateway gateway = CreateDungeonExchangeGateway();
            var verifier = new ApiJoinTokenVerifier(gateway, "dungeon-01", EServerRole.Dungeon, ZeroClock, null);
            var listener = new CountingListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
            Assert.AreEqual(1, verifier.RememberedCount);

            verifier.OnAbandoned(AUTH_REQUEST);
            Assert.AreEqual(0, verifier.RememberedCount);

            int callsBefore = listener.Calls;
            verifier.Verify(AUTH_REQUEST + 1, TOKEN, listener);
            Assert.AreEqual(callsBefore, listener.Calls, "캐시로 바로 성공하지 않는다");
            Assert.AreEqual(1, verifier.PendingCount, "API를 다시 부른다");
        }

        [Test]
        public void ApiVerifier_OnAbandoned_DoesNotForgetOtherRequestsExchange()
        {
            using BackendGateway gateway = CreateDungeonExchangeGateway();
            var verifier = new ApiJoinTokenVerifier(gateway, "dungeon-01", EServerRole.Dungeon, ZeroClock, null);

            verifier.Verify(AUTH_REQUEST, TOKEN, new CountingListener());
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();

            verifier.OnAbandoned(AUTH_REQUEST + 99);

            Assert.AreEqual(1, verifier.RememberedCount);
        }

        [Test]
        public void ForgetTownBinding_DepartingBeforeTicketExpiry_KeepsLease()
        {
            SessionBinding binding = AdmitDeparting(out ServerSessionRegistry registry);

            // 실제 시계(UtcNow) 대신 입장권 만료 직전 시각을 넣어 기한 판정이 시계에 기대지 않게 한다
            Assert.IsTrue(binding.IsDepartingAt(TICKET_EXPIRES_MS - 1L));
            ShinshiServerAuthenticator.ForgetTownBinding(registry, binding, _releaser, TICKET_EXPIRES_MS - 1L);

            Assert.AreEqual(0, _releaser.PendingCount);
            Assert.AreEqual(0, _transport.Calls);
            Assert.AreEqual(1, _releaser.DeferredCount, "이관이 없을 때를 위해 해제를 미룬다");
        }

        [Test]
        public void ForgetTownBinding_DepartingAfterTicketExpiryAndGrace_ReleasesImmediately()
        {
            SessionBinding binding = AdmitDeparting(out ServerSessionRegistry registry);
            long           graceMs = (long)(Constants.Network.DEPARTING_TRANSFER_GRACE_SECONDS * 1000.0);

            ShinshiServerAuthenticator.ForgetTownBinding(registry, binding, _releaser, TICKET_EXPIRES_MS + graceMs);
            FakeBackendTransport.WaitForCompleted(_gateway, 1);

            Assert.AreEqual(0, _releaser.DeferredCount);
            Assert.AreEqual(1, _transport.Calls);
        }

        [Test]
        public void DeferredRelease_IsSentOnlyAfterDueTime()
        {
            SessionBinding binding = AdmitDeparting(out ServerSessionRegistry registry);
            ShinshiServerAuthenticator.ForgetTownBinding(registry, binding, _releaser, TICKET_EXPIRES_MS);

            // 입장권 만료 시각이므로 남은 시간은 여유 시간(초)이다
            _now += Constants.Network.DEPARTING_TRANSFER_GRACE_SECONDS - 0.1;
            _releaser.Tick();
            Assert.AreEqual(1, _releaser.DeferredCount);
            Assert.AreEqual(0, _transport.Calls);

            _now += 0.1;
            _releaser.Tick();
            FakeBackendTransport.WaitForCompleted(_gateway, 1);

            Assert.AreEqual(0, _releaser.DeferredCount);
            Assert.AreEqual(1, _transport.Calls, "이관이 없었으므로 원래 임대를 푼다");
        }

        [Test]
        public void DeferredRelease_CancelledByReentryWithSameLease()
        {
            Assert.IsTrue(_releaser.ReleaseAfter(LEASE_ID, 5.0));

            Assert.IsTrue(_releaser.Cancel(LEASE_ID));
            _now += 10.0;
            _releaser.Tick();

            Assert.AreEqual(0, _releaser.DeferredCount);
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void ReleaseAfter_AtCapacity_RejectsNewLease()
        {
            for (int i = 0; i < Constants.Network.MAX_DEFERRED_ACCOUNT_LEASE_RELEASES; ++i)
            {
                Assert.IsTrue(_releaser.ReleaseAfter(Guid.NewGuid().ToString(), 5.0));
            }

            Assert.IsFalse(_releaser.ReleaseAfter(Guid.NewGuid().ToString(), 5.0));
            Assert.AreEqual(Constants.Network.MAX_DEFERRED_ACCOUNT_LEASE_RELEASES, _releaser.DeferredCount);
        }

        [Test]
        public void IsDepartingAt_AfterTicketExpiryAndGrace_ClearsDeparting()
        {
            SessionBinding binding = AdmitDeparting(out ServerSessionRegistry _);
            long           graceMs = (long)(Constants.Network.DEPARTING_TRANSFER_GRACE_SECONDS * 1000.0);

            Assert.IsTrue(binding.IsDepartingAt(TICKET_EXPIRES_MS + graceMs - 1L), "여유 시간 안에는 이관을 기다린다");
            Assert.IsFalse(binding.IsDepartingAt(TICKET_EXPIRES_MS + graceMs));
            Assert.IsFalse(binding.IsDeparting, "되돌린 표시는 다시 켜지지 않는다");
        }

        [Test]
        public void IsDepartingAt_WithoutDeadline_StaysDeparting()
        {
            var binding = new SessionBinding(TOWN_KEY, 1, LEASE_ID, EPlayerClass.Sorcerer);
            binding.IsDeparting = true;

            Assert.IsTrue(binding.IsDepartingAt(long.MaxValue / 2L));
        }

        [Test]
        public void IsDepartingAt_NotDeparting_IsFalse()
        {
            var binding = new SessionBinding(TOWN_KEY, 1, LEASE_ID, EPlayerClass.Sorcerer);

            Assert.IsFalse(binding.IsDepartingAt(0L));
        }

        [Test]
        public void PendingAuthLimit_DoesNotExceedMirrorMaxConnections()
        {
            Assert.LessOrEqual(Constants.Network.MAX_PENDING_AUTH_CONNECTIONS, 100);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private SessionBinding AdmitDeparting(out ServerSessionRegistry registry)
        {
            registry = new ServerSessionRegistry(REGISTRY_CAPACITY);
            var claims = new JoinTokenClaims(TOWN_KEY, CHARACTER_ID, INSTANCE_ID, null, LEASE_ID);
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));
            binding.MarkDeparting(TICKET_EXPIRES_MS);
            return binding;
        }

        private ShinshiServerAuthenticator CreateAuthenticator(IJoinTokenVerifier verifier)
        {
            _host = new GameObject("AuthenticatorTest");
            var authenticator = _host.AddComponent<ShinshiServerAuthenticator>();
            authenticator.Configure(verifier, new ServerSessionRegistry(REGISTRY_CAPACITY), "hash", null, EServerRole.Town, _releaser);
            return authenticator;
        }

        private static BackendGateway CreateDungeonExchangeGateway()
        {
            string body = BackendJson.Serialize(new JoinTokenExchangeResponse("char-1", "dungeon-01", ContractCodes.ServerRole.DUNGEON,
                "part-1", "sortie-1", 1L, ContractCodes.PlayerClass.SORCERER));
            var transport = new FakeBackendTransport((request, token) => Task.FromResult(new BackendTransportResult(STATUS_OK, body)));
            return new BackendGateway(transport, ZeroClock, FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
        }

        private static double ZeroClock()
        {
            return 0.0;
        }

        private sealed class RecordingVerifier : IJoinTokenVerifier
        {
            public List<int> Abandoned { get; } = new List<int>();

            public void Verify(int requestId, string joinToken, IJoinTokenVerifyListener listener) { }

            public void OnAdmitted(int requestId) { }

            public void OnAbandoned(int requestId)
            {
                Abandoned.Add(requestId);
            }
        }

        private sealed class CountingListener : IJoinTokenVerifyListener
        {
            public int Calls { get; private set; }

            public void OnJoinTokenVerified(int requestId, ERejectReason reason, in JoinTokenClaims claims)
            {
                ++Calls;
            }
        }
    }
}
