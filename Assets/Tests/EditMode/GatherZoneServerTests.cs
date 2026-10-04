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
    public class GatherZoneServerTests
    {
        // 채집 선점: 같은 노드 동시 시작 1승자, 취소는 API 호출 없음, 10초 선점 제한과 5초 끊김 해제,
        // Completing은 끊김이나 취소로 풀리지 않고 API 결과로만 끝난다, 확정 거절은 비용 없이 노드를 돌려준다

        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT_A       = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string ACCOUNT_B       = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string CHARACTER_A     = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string CHARACTER_B     = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string LEASE_ID        = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const int    PARTICIPANT_A   = 1;
        private const int    PARTICIPANT_B   = 2;
        private const int    ZONE            = 0;
        private const int    NODE            = 3;
        private const int    OTHER_NODE      = 4;
        private const float  RANGE           = 1.5f;
        private const float  GATHER_SECONDS  = 3f;
        private const float  CLAIM_TIMEOUT   = 10f;
        private const float  DISCONNECT_WAIT = 5f;
        private const float  REGROW_SECONDS  = 60f;
        private const float  LONG_GATHER     = 12f;
        private const int    STATUS_OK       = 200;
        private const int    STATUS_CONFLICT = 409;
        private const int    STATUS_DOWN     = 503;

        private static readonly Vector3 G_NodePosition = new Vector3(10f, 0f, 10f);
        private static readonly Vector3 G_NearNode     = new Vector3(11f, 0f, 10f);
        private static readonly Vector3 G_FarFromNode  = new Vector3(20f, 0f, 10f);

        private double               _now;
        private volatile int         _status;
        private volatile int         _reason;
        private FakeBackendTransport _transport;
        private BackendGateway       _gateway;
        private FakeTownAudience     _audience;
        private FakeLayout           _layout;
        private TownMember           _memberA;
        private TownMember           _memberB;
        private int                  _gatheredCount;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now           = 0.0;
            _status        = STATUS_OK;
            _reason        = ContractCodes.RejectReason.NONE;
            _gatheredCount = 0;
            _transport     = new FakeBackendTransport((request, token) => Task.FromResult(Respond(request)));
            _gateway       = new BackendGateway(_transport, ZeroClock,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            _audience      = new FakeTownAudience();
            _layout        = new FakeLayout();
            _layout.Add(ZONE, NODE, G_NodePosition);
            _layout.Add(ZONE, OTHER_NODE, G_NodePosition);
            _memberA = new TownMember(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A, LEASE_ID, "a", EPlayerClass.Warrior);
            _memberB = new TownMember(PARTICIPANT_B, ACCOUNT_B, CHARACTER_B, LEASE_ID, "b", EPlayerClass.Warrior);
            _audience.SetPosition(PARTICIPANT_A, G_NearNode);
            _audience.SetPosition(PARTICIPANT_B, G_NearNode);
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
        public void Start_TwoPlayersSameNode_OnlyFirstWins()
        {
            GatherZoneServer server = Create(GATHER_SECONDS);

            ERejectReason first  = server.Start(_memberA, ZONE, NODE);
            ERejectReason second = server.Start(_memberB, ZONE, NODE);

            Assert.AreEqual(ERejectReason.None, first);
            Assert.AreEqual(ERejectReason.Occupied, second);
            Assert.AreEqual(EGatherNodeState.Claimed, server.StateOf(ZONE, NODE));
        }

        [Test]
        public void Start_SecondNodeSameAccount_Rejected()
        {
            GatherZoneServer server = Create(GATHER_SECONDS);
            server.Start(_memberA, ZONE, NODE);

            Assert.AreEqual(ERejectReason.WrongPhase, server.Start(_memberA, ZONE, OTHER_NODE));
        }

        [Test]
        public void Start_FarFromNode_ReturnsOutOfRange()
        {
            GatherZoneServer server = Create(GATHER_SECONDS);
            _audience.SetPosition(PARTICIPANT_A, G_FarFromNode);

            Assert.AreEqual(ERejectReason.OutOfRange, server.Start(_memberA, ZONE, NODE));
            Assert.AreEqual(EGatherNodeState.Available, server.StateOf(ZONE, NODE));
        }

        [Test]
        public void Cancel_WhileClaimed_ReleasesWithoutApiCall()
        {
            GatherZoneServer server = Create(GATHER_SECONDS);
            server.Start(_memberA, ZONE, NODE);

            Assert.AreEqual(ERejectReason.None, server.Cancel(_memberA));

            Assert.AreEqual(EGatherNodeState.Available, server.StateOf(ZONE, NODE));
            Assert.AreEqual(0, _transport.Calls);
            Assert.AreEqual(ERejectReason.None, server.Start(_memberB, ZONE, NODE));
        }

        [Test]
        public void Tick_MovedAfterClaim_CancelsWithoutApiCall()
        {
            GatherZoneServer server = Create(GATHER_SECONDS);
            server.Start(_memberA, ZONE, NODE);
            _audience.SetPosition(PARTICIPANT_A, G_NodePosition);

            _now = 1.0;
            server.Tick();

            Assert.AreEqual(EGatherNodeState.Available, server.StateOf(ZONE, NODE));
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void Tick_ClaimLongerThanTimeout_ReleasesAtTenSeconds()
        {
            GatherZoneServer server = Create(LONG_GATHER);
            server.Start(_memberA, ZONE, NODE);

            _now = CLAIM_TIMEOUT - 0.1;
            server.Tick();
            Assert.AreEqual(EGatherNodeState.Claimed, server.StateOf(ZONE, NODE));

            _now = CLAIM_TIMEOUT;
            server.Tick();
            Assert.AreEqual(EGatherNodeState.Available, server.StateOf(ZONE, NODE));
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void Disconnect_WhileClaimed_ReleasesAfterFiveSecondsWithoutCompleting()
        {
            GatherZoneServer server = Create(GATHER_SECONDS);
            server.Start(_memberA, ZONE, NODE);
            server.OnMemberDisconnected(_memberA);

            _now = GATHER_SECONDS + 0.5;
            server.Tick();
            Assert.AreEqual(EGatherNodeState.Claimed, server.StateOf(ZONE, NODE));

            _now = DISCONNECT_WAIT;
            server.Tick();
            Assert.AreEqual(EGatherNodeState.Available, server.StateOf(ZONE, NODE));
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void Completing_DisconnectAndCancel_DoNotRelease_UntilApiCommits()
        {
            _status = STATUS_DOWN;
            GatherZoneServer server = Create(GATHER_SECONDS);
            server.Start(_memberA, ZONE, NODE);
            _now = GATHER_SECONDS;
            server.Tick();
            Assert.AreEqual(EGatherNodeState.Completing, server.StateOf(ZONE, NODE));

            Assert.AreEqual(ERejectReason.WrongPhase, server.Cancel(_memberA));
            server.OnMemberDisconnected(_memberA);
            Complete(server, 1);
            _now = GATHER_SECONDS + DISCONNECT_WAIT + CLAIM_TIMEOUT;
            Assert.AreEqual(EGatherNodeState.Completing, server.StateOf(ZONE, NODE));

            // 결과를 모르면 같은 operationId로 다시 보내고, 확정이 와야 재생 대기로 넘어간다
            _status = STATUS_OK;
            server.Tick();
            Complete(server, 1);

            Assert.AreEqual(EGatherNodeState.Depleted, server.StateOf(ZONE, NODE));
            Assert.AreEqual(1, _gatheredCount);
            BackendRequest[] requests = _transport.Requests.ToArray();
            Assert.AreEqual(requests[0].OperationId, requests[requests.Length - 1].OperationId);
        }

        [Test]
        public void Completing_ApiRejects_ReturnsNodeWithoutCost()
        {
            _status = STATUS_CONFLICT;
            _reason = ContractCodes.RejectReason.INSUFFICIENT_ACTION;
            GatherZoneServer server = Create(GATHER_SECONDS);
            server.Start(_memberA, ZONE, NODE);
            _now = GATHER_SECONDS;
            server.Tick();

            Complete(server, 1);

            Assert.AreEqual(EGatherNodeState.Available, server.StateOf(ZONE, NODE));
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out LifeResultMessage result));
            Assert.AreEqual(ELifeAction.GatherCompleted, result.Action);
            Assert.AreEqual(ERejectReason.InsufficientAction, result.Reason);
            Assert.AreEqual(0, _gatheredCount);
        }

        [Test]
        public void Depleted_RegrowsAfterSixtySeconds()
        {
            GatherZoneServer server = Create(GATHER_SECONDS);
            server.Start(_memberA, ZONE, NODE);
            _now = GATHER_SECONDS;
            server.Tick();
            Complete(server, 1);
            Assert.AreEqual(EGatherNodeState.Depleted, server.StateOf(ZONE, NODE));

            _now = GATHER_SECONDS + REGROW_SECONDS;
            server.Tick();

            Assert.AreEqual(EGatherNodeState.Available, server.StateOf(ZONE, NODE));
            var body = BackendJson.Deserialize<LifeGatherRequest>(_transport.Requests.ToArray()[0].BodyJson);
            Assert.AreEqual(ACCOUNT_A, body.AccountId);
            Assert.AreEqual(LEASE_ID, body.AccountLeaseId);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private GatherZoneServer Create(float gatherSeconds)
        {
            var settings = new GatherSettings(RANGE, gatherSeconds, CLAIM_TIMEOUT, DISCONNECT_WAIT, REGROW_SECONDS);
            return new GatherZoneServer(_gateway, _audience, _layout, in settings, ReadClock, OnGathered);
        }

        private void OnGathered(int participantId)
        {
            ++_gatheredCount;
        }

        private void Complete(GatherZoneServer server, int count)
        {
            FakeBackendTransport.WaitForCompleted(_gateway, count);
            _gateway.Pump();
        }

        private BackendTransportResult Respond(BackendRequest request)
        {
            int status = _status;
            var body   = BackendJson.Deserialize<LifeGatherRequest>(request.BodyJson);
            if (status == STATUS_OK)
            {
                return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new OperationResult(body.OperationId,
                    ContractCodes.OperationStatus.COMMITTED, 0, 1, 0, string.Empty)));
            }

            if (status == STATUS_CONFLICT)
            {
                return new BackendTransportResult(STATUS_CONFLICT, BackendJson.Serialize(new OperationResult(body.OperationId,
                    ContractCodes.OperationStatus.REJECTED, _reason, 0, 0, string.Empty)));
            }

            return new BackendTransportResult(status, null);
        }

        private double ReadClock() { return _now; }

        private static double ZeroClock() { return 0.0; }

        private sealed class FakeLayout : IGatherNodeLayout
        {
            private readonly Dictionary<long, Vector3> _nodes = new Dictionary<long, Vector3>();

            public void Add(int zone, int node, Vector3 position)
            {
                _nodes[TownGatherNodeRegistry.ToKey(zone, node)] = position;
            }

            public bool TryGetNode(int zone, int node, out Vector3 position)
            {
                return _nodes.TryGetValue(TownGatherNodeRegistry.ToKey(zone, node), out position);
            }
        }
    }
}
