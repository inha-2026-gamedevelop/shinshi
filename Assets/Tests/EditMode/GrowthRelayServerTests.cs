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
    public class GrowthRelayServerTests
    {
        // 성장 중계: 연결당 초당 요청 상한, 처리 중 중복 차단, 같은 operationId 재전송의 같은 roll, 서버가 넣는 기대 revision,
        // 확정 뒤 성장 상태 전달과 준비 해제 알림

        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT_ID      = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string CHARACTER_ID    = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string LEASE_ID        = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string EFFECT_ID       = "effect_test";
        private const int    PARTICIPANT_ID  = 1;
        private const long   REVISION        = 12;
        private const int    FIRST_ROLL      = 1234;
        private const int    TRAINING_TOTAL_CAP = 100;
        private const int    STATUS_OK       = 200;
        private const int    STATUS_DOWN     = 503;

        private double               _now;
        private volatile int         _operationStatus;
        private int                  _nextRoll;
        private long                 _snapshotRevision;
        private int                  _changed;
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
            _now             = 0.0;
            _operationStatus = STATUS_OK;
            _nextRoll        = FIRST_ROLL;
            _snapshotRevision = REVISION;
            _changed         = 0;
            _transport       = new FakeBackendTransport((request, token) => Task.FromResult(Respond(request)));
            _gateway         = new BackendGateway(_transport, ZeroClock,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            _audience        = new FakeTownAudience();
            _relay           = new GrowthRelayServer(_gateway, _audience, NextRoll, ReadClock, OnChanged, TRAINING_TOTAL_CAP);
            _member          = new TownMember(PARTICIPANT_ID, ACCOUNT_ID, CHARACTER_ID, LEASE_ID, "a", EPlayerClass.Warrior);
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
        public void Handle_MoreThanLimitInOneSecond_RejectsWithRateLimited()
        {
            for (int i = 0; i < Constants.Town.REQUESTS_PER_SECOND; ++i)
            {
                _relay.Handle(_member, Snapshot());
            }

            _relay.Handle(_member, Snapshot());

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage result));
            Assert.AreEqual(ERejectReason.RateLimited, result.Reason);

            _now = Constants.Town.REQUEST_WINDOW_SECONDS;
            _audience.Clear();
            _relay.Handle(_member, Snapshot());
            Assert.AreEqual(0, _audience.Count<GrowthResultMessage>(PARTICIPANT_ID));
        }

        [Test]
        public void Handle_FirstOperation_FetchesSnapshotThenSendsWithServerRevisionAndRoll()
        {
            GrowthRequestMessage message = Register(Guid.NewGuid());

            _relay.Handle(_member, message);
            Complete(1);
            Complete(1);

            Assert.IsTrue(_transport.Requests.TryDequeue(out BackendRequest snapshot));
            Assert.AreEqual(EBackendMethod.Get, snapshot.Method);
            Assert.IsTrue(_transport.Requests.TryDequeue(out BackendRequest operation));
            Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, GrowthRoutes.TALISMAN_REGISTER_FORMAT, CHARACTER_ID),
                operation.Path);
            var body = BackendJson.Deserialize<TalismanRegisterRequest>(operation.BodyJson);
            Assert.AreEqual(REVISION, body.ExpectedRevision);
            Assert.AreEqual(FIRST_ROLL, body.RollBasisPoints);
            Assert.AreEqual(message.OperationId.ToString("D"), body.OperationId);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage result));
            Assert.AreEqual(ERejectReason.None, result.Reason);
            Assert.AreEqual(GrowthCodes.Outcome.SUCCEEDED, result.Outcome);
            Assert.GreaterOrEqual(_audience.Count<GrowthSnapshotMessage>(PARTICIPANT_ID), 1);
            Assert.AreEqual(1, _changed);
        }

        [Test]
        public void Handle_WhileInFlight_SameIdIgnored_OtherIdPendingCommit()
        {
            GrowthRequestMessage first = Register(Guid.NewGuid());
            _relay.Handle(_member, first);

            _relay.Handle(_member, first);
            _relay.Handle(_member, Register(Guid.NewGuid()));

            Assert.AreEqual(1, _audience.Count<GrowthResultMessage>(PARTICIPANT_ID));
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage result));
            Assert.AreEqual(ERejectReason.PendingCommit, result.Reason);
            Complete(1);
            Complete(1);
            Assert.AreEqual(2, _transport.Calls);
        }

        [Test]
        public void Handle_ResendAfterUnknownResult_ReusesSameRoll()
        {
            _operationStatus = STATUS_DOWN;
            GrowthRequestMessage message = Register(Guid.NewGuid());
            _relay.Handle(_member, message);
            Complete(1);
            Complete(1);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage failed));
            Assert.AreEqual(ERejectReason.BackendUnavailable, failed.Reason);

            _operationStatus = STATUS_OK;
            _now             = Constants.Town.REQUEST_WINDOW_SECONDS;
            _relay.Handle(_member, message);
            Complete(1);

            Assert.AreEqual(FIRST_ROLL, _relay.RememberedRoll(PARTICIPANT_ID, message.OperationId));
            BackendRequest last = null;
            while (_transport.Requests.TryDequeue(out BackendRequest request))
            {
                last = request;
            }

            var body = BackendJson.Deserialize<TalismanRegisterRequest>(last.BodyJson);
            Assert.AreEqual(FIRST_ROLL, body.RollBasisPoints);
            Assert.AreEqual(FIRST_ROLL + 1, _nextRoll);
        }

        [Test]
        public void Handle_ResendAfterRevisionChanged_ReusesFirstRevision()
        {
            _operationStatus = STATUS_DOWN;
            GrowthRequestMessage message = Register(Guid.NewGuid());
            _relay.Handle(_member, message);
            Complete(1);
            Complete(1);
            Assert.AreEqual(REVISION, _relay.RememberedRevision(PARTICIPANT_ID, message.OperationId));

            // 다른 경로로 revision이 바뀐 상태를 받은 뒤 같은 operationId를 다시 보낸다
            _snapshotRevision = REVISION + 5;
            _now              = Constants.Town.REQUEST_WINDOW_SECONDS;
            _relay.Handle(_member, Snapshot());
            Complete(1);
            _operationStatus = STATUS_OK;
            _relay.Handle(_member, message);
            Complete(1);

            BackendRequest last = null;
            while (_transport.Requests.TryDequeue(out BackendRequest request))
            {
                last = request;
            }

            var body = BackendJson.Deserialize<TalismanRegisterRequest>(last.BodyJson);
            Assert.AreEqual(REVISION, body.ExpectedRevision);
            Assert.AreEqual(-1L, _relay.RememberedRevision(PARTICIPANT_ID, Guid.NewGuid()));
        }

        [Test]
        public void Handle_TrainingStepsBeyondTotalCap_RejectsWithoutApiCall()
        {
            var tooMany = new GrowthRequestMessage(Guid.NewGuid(), EGrowthAction.Training, null, null, 0, int.MaxValue, false,
                null, null);
            var overCap = new GrowthRequestMessage(Guid.NewGuid(), EGrowthAction.Training, null, null, 0,
                TRAINING_TOTAL_CAP + 1, false, null, null);

            _relay.Handle(_member, tooMany);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage first));
            _relay.Handle(_member, overCap);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage second));

            Assert.AreEqual(ERejectReason.InvalidTarget, first.Reason);
            Assert.AreEqual(ERejectReason.InvalidTarget, second.Reason);
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void Handle_InvalidItemId_RejectsWithoutApiCall()
        {
            var message = new GrowthRequestMessage(Guid.NewGuid(), EGrowthAction.EquipmentEnhance, "../x", null, 0, 0, false, null,
                null);

            _relay.Handle(_member, message);

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage result));
            Assert.AreEqual(ERejectReason.InvalidTarget, result.Reason);
            Assert.AreEqual(0, _transport.Calls);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static GrowthRequestMessage Snapshot()
        {
            return new GrowthRequestMessage(Guid.Empty, EGrowthAction.Snapshot, null, null, 0, 0, false, null, null);
        }

        private static GrowthRequestMessage Register(Guid operationId)
        {
            return new GrowthRequestMessage(operationId, EGrowthAction.TalismanRegister, EFFECT_ID, null, 0, 0, false, null, null);
        }

        private void Complete(int count)
        {
            FakeBackendTransport.WaitForCompleted(_gateway, count);
            _gateway.Pump();
        }

        private BackendTransportResult Respond(BackendRequest request)
        {
            CharacterGrowthSnapshot snapshot = new CharacterGrowthSnapshot(CHARACTER_ID, 0, _snapshotRevision, 0, 0,
                new int[Constants.Town.TRAINING_STAT_COUNT], Array.Empty<EquipmentView>(), Array.Empty<EnhancementPityView>(), Array.Empty<TalismanProgressView>(),
                Array.Empty<TalismanLoadoutEntry>(), Array.Empty<GrowthItemView>(), 0, 0, "test", 0);
            if (request.Method == EBackendMethod.Get)
            {
                return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(snapshot));
            }

            int status = _operationStatus;
            if (status != STATUS_OK)
            {
                return new BackendTransportResult(status, null);
            }

            var body   = BackendJson.Deserialize<TalismanRegisterRequest>(request.BodyJson);
            var result = new OperationResult(body.OperationId, ContractCodes.OperationStatus.COMMITTED, 0, REVISION + 1, 0, string.Empty);
            return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new GrowthOperationResponse(result,
                GrowthCodes.Outcome.SUCCEEDED, null, snapshot)));
        }

        private int NextRoll()
        {
            int value = _nextRoll;
            ++_nextRoll;
            return value;
        }

        private void OnChanged(int participantId)
        {
            ++_changed;
        }

        private double ReadClock() { return _now; }

        private static double ZeroClock() { return 0.0; }
    }
}
