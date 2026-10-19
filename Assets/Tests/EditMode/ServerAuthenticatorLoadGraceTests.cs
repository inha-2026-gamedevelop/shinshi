// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ServerAuthenticatorLoadGraceTests
    {
        // 서버 맵 로드 중 인증 마감 유예(요청 대기, 검증 대기, 로드 중 새 연결)와, 허가 직후 토큰 소비 확정을 시계 주입으로 시험한다.
        // 실제 NetworkConnectionToClient를 NetworkServer.connections에 넣고, 끊기는 전송 계층의 ServerDisconnect 기록으로 판정한다

        /****************************************
        *                Fields
        ****************************************/

        private const string CONTENT_HASH  = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string JOIN_TOKEN    = "join-token";
        private const int    REGISTRY_SIZE = 8;
        private const double LOAD_LIMIT    = Constants.Network.DUNGEON_MAP_LOAD_TIMEOUT_SECONDS;
        private const double REQUEST_LIMIT = Constants.Network.AUTH_REQUEST_TIMEOUT_SECONDS;
        private const double VERIFY_LIMIT  = Constants.Network.AUTH_TIMEOUT_SECONDS;
        private const double CLOSE_DELAY   = Constants.Network.AUTH_REJECT_DISCONNECT_DELAY_SECONDS;
        private const double EPSILON       = 0.01;

        private GameObject                 _host          = null;
        private ShinshiServerAuthenticator _authenticator = null;
        private DisconnectTransport        _transport     = null;
        private Transport                  _savedActive   = null;
        private RecordingVerifier          _verifier      = null;
        private FakeLoader                 _loader        = null;
        private List<int>                  _accepted      = null;
        private List<int>                  _added         = null;
        private double                     _now           = 0.0;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _savedActive = Transport.active;
            _host        = new GameObject("ServerAuthenticatorLoadGraceTests");
            _transport   = _host.AddComponent<DisconnectTransport>();
            Transport.active = _transport;

            _now      = 0.0;
            _verifier = new RecordingVerifier();
            _loader   = new FakeLoader();
            _accepted = new List<int>();
            _added    = new List<int>();

            _authenticator = _host.AddComponent<ShinshiServerAuthenticator>();
            _authenticator.Configure(_verifier, new ServerSessionRegistry(REGISTRY_SIZE), CONTENT_HASH, null, EServerRole.Town);
            _authenticator.Clock     = () => _now;
            _authenticator.MapLoader = _loader;
            _authenticator.OnServerAuthenticated.AddListener(conn => _accepted.Add(conn.connectionId));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _added.Count; ++i)
            {
                NetworkServer.connections.Remove(_added[i]);
            }

            Transport.active = _savedActive;
            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void NoLoad_WaitingConnection_DisconnectedAfterRequestTimeout()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");

            Tick(REQUEST_LIMIT - EPSILON);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(REQUEST_LIMIT);
            Assert.IsTrue(IsDisconnected(conn));
        }

        [Test]
        public void FirstJoinDuringLoad_WaitsUntilLoadLimitOnly()
        {
            _loader.IsLoading = true;
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");

            Tick(REQUEST_LIMIT + 5.0);
            Assert.IsFalse(IsDisconnected(conn), "로드 중 새 연결은 요청 대기 1초에 끊기지 않아야 한다");

            Tick(LOAD_LIMIT - EPSILON);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(LOAD_LIMIT);
            Assert.IsTrue(IsDisconnected(conn), "끝나지 않는 로드여도 유예 상한에서 끊긴다");
        }

        [Test]
        public void MidLoadJoin_SharesEpisodeLimit_NotLaterThanItsStart()
        {
            _loader.IsLoading = true;
            Tick(0.0);
            _now = 10.0;
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");

            Tick(LOAD_LIMIT - EPSILON);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(LOAD_LIMIT);
            Assert.IsTrue(IsDisconnected(conn), "로드 중 들어온 연결도 이 로드의 상한을 넘겨 사는 일은 없다");
        }

        [Test]
        public void ExistingWaiting_LoadStarts_GracedThenBoundedByLimit()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");

            Tick(0.5);
            _loader.IsLoading = true;
            Tick(0.6);

            Tick(5.0);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(0.6 + LOAD_LIMIT - EPSILON);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(0.6 + LOAD_LIMIT);
            Assert.IsTrue(IsDisconnected(conn));
        }

        [Test]
        public void LoadGrace_IsNotPushedEveryFrame()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            _loader.IsLoading = true;

            for (double t = 0.0; t < LOAD_LIMIT - 0.5; t += 0.5)
            {
                Tick(t);
            }

            Assert.IsFalse(IsDisconnected(conn));

            Tick(LOAD_LIMIT);
            Assert.IsTrue(IsDisconnected(conn), "프레임마다 마감을 밀면 이 시각에도 살아 있게 된다");
        }

        [Test]
        public void ExistingWaiting_LoadEnds_ReturnsToRequestTimeout()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");

            Tick(0.5);
            _loader.IsLoading = true;
            Tick(1.0);

            _loader.IsLoading = false;
            Tick(3.0);
            Assert.IsFalse(IsDisconnected(conn), "로드가 끝난 시각부터 정상 마감이 다시 센다");

            Tick(3.0 + REQUEST_LIMIT - EPSILON);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(3.0 + REQUEST_LIMIT);
            Assert.IsTrue(IsDisconnected(conn));
        }

        [Test]
        public void Verifying_LoadEnds_ReturnsToVerifyTimeout()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            SendJoinRequest(conn, CONTENT_HASH);

            _loader.IsLoading = true;
            Tick(1.0);

            _loader.IsLoading = false;
            Tick(2.0);

            Tick(2.0 + VERIFY_LIMIT - EPSILON);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(2.0 + VERIFY_LIMIT);
            Tick(2.0 + VERIFY_LIMIT + CLOSE_DELAY);
            Assert.IsTrue(IsDisconnected(conn));
        }

        [Test]
        public void Closing_GetsNoLoadGrace()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            SendJoinRequest(conn, "wrong-hash");
            Assert.IsFalse(IsDisconnected(conn), "거절 응답을 보낸 뒤 끊기 지연이 있다");

            _loader.IsLoading = true;
            Tick(CLOSE_DELAY - EPSILON);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(CLOSE_DELAY);
            Assert.IsTrue(IsDisconnected(conn), "끊기 대기는 로드 중이어도 미루지 않는다");
        }

        [Test]
        public void InvalidSilentConnections_StillCappedPerAddressDuringLoad()
        {
            _loader.IsLoading = true;
            int perAddress = Constants.Network.MAX_PENDING_AUTH_PER_ADDRESS;
            for (int i = 1; i <= perAddress; ++i)
            {
                Assert.IsFalse(IsDisconnected(Connect(i, "10.0.0.9")));
            }

            NetworkConnectionToClient extra = Connect(perAddress + 1, "10.0.0.9");
            Assert.IsTrue(IsDisconnected(extra), "로드 중이어도 같은 주소의 대기 상한은 그대로다");
        }

        [Test]
        public void ImmediatePath_NoLoad_ConsumesTokenOnceAndAcceptsAtOnce()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            int requestId = SendJoinRequest(conn, CONTENT_HASH);

            Verified(requestId, "c:a");

            CollectionAssert.AreEqual(new[] { requestId }, _verifier.Admitted);
            CollectionAssert.AreEqual(new[] { 1 }, _accepted);
            Assert.IsEmpty(_verifier.Abandoned);

            Tick(1.0);
            CollectionAssert.AreEqual(new[] { requestId }, _verifier.Admitted, "수락 완료가 같은 토큰을 다시 소비하면 안 된다");
        }

        [Test]
        public void HeldAdmission_ConsumesTokenAtAdmit_AcceptsAfterLoad_NoDuplicateConsume()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            int requestId = SendJoinRequest(conn, CONTENT_HASH);
            _loader.IsLoading = true;

            Verified(requestId, "c:a");

            CollectionAssert.AreEqual(new[] { requestId }, _verifier.Admitted, "미루는 중에도 허가 직후 소비가 확정된다");
            Assert.IsEmpty(_accepted);
            Assert.IsInstanceOf<SessionBinding>(conn.authenticationData);

            Tick(2.0);
            Assert.IsEmpty(_accepted);

            _loader.IsLoading = false;
            Tick(3.0);

            CollectionAssert.AreEqual(new[] { 1 }, _accepted);
            CollectionAssert.AreEqual(new[] { requestId }, _verifier.Admitted);
            Assert.IsFalse(IsDisconnected(conn));
        }

        [Test]
        public void HeldAdmission_DuplicateTokenWhileHeld_IsRejectedAndNeverTouchesFirstConsume()
        {
            NetworkConnectionToClient first  = Connect(1, "10.0.0.1");
            NetworkConnectionToClient second = Connect(2, "10.0.0.2");
            int firstId  = SendJoinRequest(first, CONTENT_HASH);
            int secondId = SendJoinRequest(second, CONTENT_HASH);
            _loader.IsLoading = true;

            Verified(firstId, "c:a");
            Verified(secondId, "c:a");

            CollectionAssert.AreEqual(new[] { firstId }, _verifier.Admitted, "중복 토큰은 소비로 기록되면 안 된다");
            Assert.IsEmpty(_accepted);

            _authenticator.HandleServerDisconnected(first);
            CollectionAssert.AreEqual(new[] { firstId }, _verifier.Admitted, "미룬 연결이 끊겨도 소비를 다시 하지 않는다");
            Assert.IsEmpty(_verifier.Abandoned);
        }

        [Test]
        public void HeldAdmission_ClockJumpsPastLimitAndLoadEndsInSameFrame_IsNotAccepted()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            int requestId = SendJoinRequest(conn, CONTENT_HASH);
            _loader.IsLoading = true;
            Verified(requestId, "c:a");
            Assert.IsEmpty(_accepted);

            _loader.IsLoading = false;
            Tick(LOAD_LIMIT + 5.0);

            Assert.IsEmpty(_accepted, "마감을 넘긴 미룬 입장은 로드가 끝난 프레임에도 승인하지 않는다");
            CollectionAssert.AreEqual(new[] { requestId }, _verifier.Admitted, "토큰 소비는 허가 때 한 번만 일어난다");
            Assert.IsInstanceOf<SessionBinding>(conn.authenticationData, "결합은 실제 연결 종료 체인이 쓰도록 연결에 남긴다");
            Assert.IsFalse(IsDisconnected(conn), "거절 응답을 보낸 뒤 끊기 지연이 있다");

            var claims = new JoinTokenClaims("c:a");
            Assert.AreEqual(ERejectReason.AlreadyConnected, _authenticator.Sessions.TryAdmit(in claims, out SessionBinding _),
                "실제 종료 전에는 명단을 풀지 않아 같은 결합의 재사용 경합이 없다");

            Tick(LOAD_LIMIT + 5.0 + CLOSE_DELAY - EPSILON);
            Assert.IsFalse(IsDisconnected(conn));

            Tick(LOAD_LIMIT + 5.0 + CLOSE_DELAY);
            Assert.IsTrue(IsDisconnected(conn));
            Assert.IsEmpty(_accepted);

            _authenticator.HandleServerDisconnected(conn);
            Assert.AreEqual(ERejectReason.None, _authenticator.Sessions.TryAdmit(in claims, out SessionBinding _), "실제 종료 뒤에야 다시 들어올 수 있다");
        }

        [Test]
        public void HeldAdmission_ExpiredWithRealDungeonHost_CleansAwaitingAndRosterOnlyOnRealDisconnect()
        {
            var config = ScriptableObject.CreateInstance<SessionConfigSO>();
            var registry = new ServerSessionRegistry(REGISTRY_SIZE);
            var host = new DungeonSessionHost(config, registry, null, true, null, null);
            host.TracksPlayerReadiness = true;
            PropertyInfo hostProperty = typeof(ServerBootstrap).GetProperty("DungeonHost", BindingFlags.Static | BindingFlags.Public);
            DungeonSessionHost savedHost = ServerBootstrap.DungeonHost;
            MethodInfo lostHandler = typeof(ServerBootstrap).GetMethod("HandleDungeonConnectionLost", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(lostHandler, "HandleDungeonConnectionLost");

            try
            {
                hostProperty.SetValue(null, host);
                _authenticator.Configure(_verifier, registry, CONTENT_HASH, host, EServerRole.Dungeon);

                string key = Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + "p1";
                NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
                int requestId = SendJoinRequest(conn, CONTENT_HASH);
                _loader.IsLoading = true;
                Verified(requestId, key);

                Assert.AreEqual(1, host.AwaitingPlayerCount, "허가가 플레이어 대기를 올린다");
                Assert.IsEmpty(_accepted);

                _loader.IsLoading = false;
                Tick(LOAD_LIMIT + 5.0);

                Assert.IsEmpty(_accepted);
                CollectionAssert.AreEqual(new[] { requestId }, _verifier.Admitted);
                var binding = conn.authenticationData as SessionBinding;
                Assert.IsNotNull(binding, "결합은 연결에 남아 실제 종료 때 호스트가 참가자를 찾는다");
                Assert.AreEqual(1, host.AwaitingPlayerCount, "실제 종료 전에는 호스트 정리를 하지 않는다");

                var claims = new JoinTokenClaims(key);
                Assert.AreEqual(ERejectReason.AlreadyConnected, registry.TryAdmit(in claims, out SessionBinding _));

                _authenticator.HandleServerDisconnected(conn);
                lostHandler.Invoke(null, new object[] { conn });

                Assert.AreEqual(0, host.AwaitingPlayerCount, "실제 종료 체인이 대기를 지운다");
                Assert.AreEqual(1, host.LobbyAbsentCount, "시작 전 이탈로 명단이 정리 상태가 된다");
                Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding _), "정리 뒤에야 재입장할 수 있다");
            }
            finally
            {
                hostProperty.SetValue(null, savedHost);
                host.Dispose();
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void HeldAdmission_ClosingBeforeLoadEnds_IsNotAccepted()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            int requestId = SendJoinRequest(conn, CONTENT_HASH);
            _loader.IsLoading = true;
            Verified(requestId, "c:a");

            // 미룬 항목이 끊기 대기로 바뀐 경우를 직접 만든다(반영으로 표를 직접 고친다)
            FieldInfo pendingField = typeof(ShinshiServerAuthenticator).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic);
            var pending = (PendingAuthTable)pendingField.GetValue(_authenticator);
            Assert.IsTrue(pending.MarkClosing(1, 100.0));

            _loader.IsLoading = false;
            Tick(1.0);

            Assert.IsEmpty(_accepted, "끊기 대기 연결은 승인하지 않는다");
        }

        [Test]
        public void HeldAdmission_ConnectionDisconnected_ReleasesBindingSoParticipantCanRejoin()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            int requestId = SendJoinRequest(conn, CONTENT_HASH);
            _loader.IsLoading = true;
            Verified(requestId, "c:a");

            _authenticator.HandleServerDisconnected(conn);
            _loader.IsLoading = false;
            Tick(2.0);

            Assert.IsEmpty(_accepted, "끊긴 연결에는 수락을 보내지 않는다");

            var claims = new JoinTokenClaims("c:a");
            Assert.AreEqual(ERejectReason.None, _authenticator.Sessions.TryAdmit(in claims, out SessionBinding _));
        }

        [Test]
        public void HeldAdmission_ConnectionVanishes_ReleasesBindingOnTickHeld()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            int requestId = SendJoinRequest(conn, CONTENT_HASH);
            _loader.IsLoading = true;
            Verified(requestId, "c:a");

            NetworkServer.connections.Remove(1);
            _loader.IsLoading = false;
            Tick(2.0);

            Assert.IsEmpty(_accepted);
            CollectionAssert.AreEqual(new[] { requestId }, _verifier.Admitted);

            var claims = new JoinTokenClaims("c:a");
            Assert.AreEqual(ERejectReason.None, _authenticator.Sessions.TryAdmit(in claims, out SessionBinding _));
        }

        [Test]
        public void LateResultForClosedRequest_IsAbandonedNotConsumed()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            int requestId = SendJoinRequest(conn, CONTENT_HASH);
            _authenticator.HandleServerDisconnected(conn);

            Verified(requestId, "c:a");

            CollectionAssert.AreEqual(new[] { requestId }, _verifier.Abandoned);
            Assert.IsEmpty(_verifier.Admitted);
            Assert.IsEmpty(_accepted);
        }

        [Test]
        public void OnStopServer_ClearsHeldAndGraceState()
        {
            NetworkConnectionToClient conn = Connect(1, "10.0.0.1");
            int requestId = SendJoinRequest(conn, CONTENT_HASH);
            _loader.IsLoading = true;
            Verified(requestId, "c:a");

            _authenticator.OnStopServer();
            _loader.IsLoading = false;
            Tick(2.0);
            Assert.IsEmpty(_accepted, "정지 뒤에는 미룬 입장을 확정하지 않는다");

            _now              = 100.0;
            _loader.IsLoading = true;
            NetworkConnectionToClient next = Connect(2, "10.0.0.2");

            Tick(100.0 + LOAD_LIMIT - EPSILON);
            Assert.IsFalse(IsDisconnected(next), "정지 뒤 새 로드는 새 유예 상한을 얻는다");

            Tick(100.0 + LOAD_LIMIT);
            Assert.IsTrue(IsDisconnected(next));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private NetworkConnectionToClient Connect(int connectionId, string address)
        {
            var conn = new NetworkConnectionToClient(connectionId, address);
            NetworkServer.connections[connectionId] = conn;
            _added.Add(connectionId);
            _authenticator.OnServerAuthenticate(conn);
            return conn;
        }

        private void Tick(double now)
        {
            _now = now;
            MethodInfo update = typeof(ShinshiServerAuthenticator).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(update, "Update");
            update.Invoke(_authenticator, null);
        }

        // 마지막 검증 요청 번호를 돌려준다, 핸드셰이크 검사에서 거절되면 요청이 늘지 않아 0이다
        private int SendJoinRequest(NetworkConnectionToClient conn, string contentHash)
        {
            MethodInfo handler = typeof(ShinshiServerAuthenticator).GetMethod("OnJoinRequest", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(handler, "OnJoinRequest");

            int before  = _verifier.Requests.Count;
            var message = new JoinRequestMessage(Constants.Network.PROTOCOL_VERSION, contentHash, JOIN_TOKEN);
            handler.Invoke(_authenticator, new object[] { conn, message });
            return (_verifier.Requests.Count > before) ? _verifier.Requests[_verifier.Requests.Count - 1] : 0;
        }

        private void Verified(int requestId, string participantKey)
        {
            var claims = new JoinTokenClaims(participantKey);
            _authenticator.OnJoinTokenVerified(requestId, ERejectReason.None, in claims);
        }

        private bool IsDisconnected(NetworkConnectionToClient conn)
        {
            return _transport.Disconnected.Contains(conn.connectionId);
        }

        private sealed class FakeLoader : IDungeonMapLoader
        {
            public string CurrentMapName => "map";
            public bool   IsLoading      { get; set; }

            public bool TryBeginLoad(string mapName)
            {
                IsLoading = true;
                return true;
            }
        }

        private sealed class RecordingVerifier : IJoinTokenVerifier
        {
            public List<int> Requests  { get; } = new List<int>();
            public List<int> Admitted  { get; } = new List<int>();
            public List<int> Abandoned { get; } = new List<int>();

            public void Verify(int requestId, string joinToken, IJoinTokenVerifyListener listener)
            {
                Requests.Add(requestId);
            }

            public void OnAdmitted(int requestId)
            {
                Admitted.Add(requestId);
            }

            public void OnAbandoned(int requestId)
            {
                Abandoned.Add(requestId);
            }
        }

        // 서버 쪽 끊기 호출만 기록하는 전송 계층, 실제 네트워크는 열지 않는다
        public sealed class DisconnectTransport : Transport
        {
            public readonly HashSet<int> Disconnected = new HashSet<int>();

            public override bool Available() => true;
            public override bool ClientConnected() => false;
            public override void ClientConnect(string address) { }
            public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
            public override void ClientDisconnect() { }
            public override Uri ServerUri() => null;
            public override bool ServerActive() => false;
            public override void ServerStart() { }
            public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
            public override void ServerDisconnect(int connectionId)
            {
                Disconnected.Add(connectionId);
            }
            public override string ServerGetClientAddress(int connectionId) => string.Empty;
            public override void ServerStop() { }
            public override int GetMaxPacketSize(int channelId = Channels.Reliable) => 1200;
            public override void Shutdown() { }
        }
    }
}
