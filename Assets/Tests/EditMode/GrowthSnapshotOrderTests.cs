// System
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class GrowthSnapshotOrderTests
    {
        // 성장 스냅숏 순서: 조회 응답과 작업 응답이 엇갈려도 서버가 아는 revision이 뒤로 가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT_ID     = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string CHARACTER_ID   = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string LEASE_ID       = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string EFFECT_ID      = "effect_test";
        private const int    PARTICIPANT_ID = 1;

        private double               _now;
        private long                 _getRevision;
        private long                 _operationRevision;
        private FakeBackendTransport _transport;
        private BackendGateway       _gateway;
        private FakeTownAudience     _audience;
        private GrowthRelayServer    _relay;
        private TownMember           _member;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now               = 0.0;
            _getRevision       = 12;
            _operationRevision = 12;
            _transport         = new FakeBackendTransport((request, token) => Task.FromResult(Respond(request)));
            _gateway           = new BackendGateway(_transport, () => 0.0,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            _audience          = new FakeTownAudience();
            _relay             = new GrowthRelayServer(_gateway, _audience, () => 1, () => _now, null, 100);
            _member            = new TownMember(PARTICIPANT_ID, ACCOUNT_ID, CHARACTER_ID, LEASE_ID, "a", EPlayerClass.Warrior);
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
        public void OperationResponse_OlderSnapshot_IsDroppedButResultIsDelivered()
        {
            SendRegister();
            Complete();
            Complete();

            // 조회 응답(12)과 같은 revision의 첫 작업 응답이 각각 스냅숏을 보낸다
            int known = _audience.Count<GrowthSnapshotMessage>(PARTICIPANT_ID);
            Assert.AreEqual(2, known);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthSnapshotMessage first));
            Assert.AreEqual(12L, first.LoadoutRevision);

            // 이미 12를 아는 상태에서 작업 응답에 실린 스냅숏은 10이다
            _operationRevision = 10;
            _now += Constants.Town.REQUEST_WINDOW_SECONDS;
            SendRegister();
            Complete();

            Assert.AreEqual(known, _audience.Count<GrowthSnapshotMessage>(PARTICIPANT_ID));
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage result));
            Assert.AreEqual(ERejectReason.None, result.Reason);
        }

        [Test]
        public void OperationResponse_NewerSnapshot_IsApplied()
        {
            SendRegister();
            Complete();
            Complete();

            _operationRevision = 13;
            _now += Constants.Town.REQUEST_WINDOW_SECONDS;
            SendRegister();
            Complete();

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthSnapshotMessage last));
            Assert.AreEqual(13L, last.LoadoutRevision);
        }

        [Test]
        public void SnapshotResponse_OlderThanKnown_IsDroppedAfterNewerOperationResponse()
        {
            SendRegister();
            Complete();
            Complete();
            _operationRevision = 14;
            _now += Constants.Town.REQUEST_WINDOW_SECONDS;
            SendRegister();
            Complete();

            // 작업이 14를 알린 뒤 늦게 도착한 조회(12)는 버린다
            _getRevision = 12;
            _now += Constants.Town.REQUEST_WINDOW_SECONDS;
            int before = _audience.Count<GrowthSnapshotMessage>(PARTICIPANT_ID);
            _relay.Handle(_member, new GrowthRequestMessage(Guid.Empty, EGrowthAction.Snapshot, null, null, 0, 0, false, null, null));
            Complete();

            Assert.AreEqual(before, _audience.Count<GrowthSnapshotMessage>(PARTICIPANT_ID));
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthSnapshotMessage last));
            Assert.AreEqual(14L, last.LoadoutRevision);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private void SendRegister()
        {
            _relay.Handle(_member, new GrowthRequestMessage(Guid.NewGuid(), EGrowthAction.TalismanRegister, EFFECT_ID, null, 0, 0,
                false, null, null));
        }

        private void Complete()
        {
            FakeBackendTransport.WaitForCompleted(_gateway, 1);
            _gateway.Pump();
        }

        private BackendTransportResult Respond(BackendRequest request)
        {
            long revision = (request.Method == EBackendMethod.Get) ? _getRevision : _operationRevision;
            var snapshot = new CharacterGrowthSnapshot(CHARACTER_ID, 0, revision, 0, 0,
                new int[Constants.Town.TRAINING_STAT_COUNT], Array.Empty<EquipmentView>(), Array.Empty<EnhancementPityView>(),
                Array.Empty<TalismanProgressView>(), Array.Empty<TalismanLoadoutEntry>(), Array.Empty<GrowthItemView>(), 0, 0, "test", 0);
            if (request.Method == EBackendMethod.Get)
            {
                return new BackendTransportResult(200, BackendJson.Serialize(snapshot));
            }

            var result = new OperationResult(request.OperationId, ContractCodes.OperationStatus.COMMITTED, 0, revision + 1, 0, string.Empty);
            return new BackendTransportResult(200, BackendJson.Serialize(new GrowthOperationResponse(result,
                GrowthCodes.Outcome.SUCCEEDED, null, snapshot)));
        }
    }
}
