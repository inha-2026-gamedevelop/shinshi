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
        private const string ITEM_ID         = "3a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
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

        [Test]
        public void Handle_RerollOptions_SendsFourRollsInBodyAndRoute()
        {
            GrowthRequestMessage message = RerollOptions(Guid.NewGuid(), ITEM_ID);

            _relay.Handle(_member, message);
            Complete(1);
            Complete(1);

            BackendRequest operation = LastRequest();
            Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, GrowthRoutes.EQUIPMENT_REROLL_OPTIONS_FORMAT,
                CHARACTER_ID, ITEM_ID), operation.Path);
            var body = BackendJson.Deserialize<EquipmentRerollOptionsRequest>(operation.BodyJson);
            Assert.AreEqual(REVISION, body.ExpectedRevision);
            Assert.AreEqual(message.OperationId.ToString("D"), body.OperationId);
            Assert.AreEqual(new[] { FIRST_ROLL, FIRST_ROLL + 1, FIRST_ROLL + 2, FIRST_ROLL + 3 }, body.Rolls);
            Assert.IsTrue(EquipmentOptionRules.IsValidRolls(body.Rolls));
            Assert.AreEqual(FIRST_ROLL + EquipmentOptionRules.ROLL_COUNT, _nextRoll);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage result));
            Assert.AreEqual(ERejectReason.None, result.Reason);
            Assert.AreEqual(EGrowthAction.EquipmentRerollOptions, result.Action);
        }

        [Test]
        public void Handle_RerollOptionsResendAfterUnknownResult_ReusesSameFourRolls()
        {
            _operationStatus = STATUS_DOWN;
            GrowthRequestMessage message = RerollOptions(Guid.NewGuid(), ITEM_ID);
            _relay.Handle(_member, message);
            Complete(1);
            Complete(1);
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage failed));
            Assert.AreEqual(ERejectReason.BackendUnavailable, failed.Reason);
            int[] first = _relay.RememberedRolls(PARTICIPANT_ID, message.OperationId);

            _operationStatus = STATUS_OK;
            _now             = Constants.Town.REQUEST_WINDOW_SECONDS;
            _relay.Handle(_member, message);
            Complete(1);

            Assert.AreEqual(EquipmentOptionRules.ROLL_COUNT, first.Length);
            Assert.AreEqual(first, _relay.RememberedRolls(PARTICIPANT_ID, message.OperationId));
            Assert.AreEqual(FIRST_ROLL, _relay.RememberedRoll(PARTICIPANT_ID, message.OperationId));
            var body = BackendJson.Deserialize<EquipmentRerollOptionsRequest>(LastRequest().BodyJson);
            Assert.AreEqual(first, body.Rolls);
            Assert.AreEqual(FIRST_ROLL + EquipmentOptionRules.ROLL_COUNT, _nextRoll);
        }

        [Test]
        public void Handle_RerollOptionsWithDifferentOperationIds_DrawsFreshRolls()
        {
            GrowthRequestMessage first = RerollOptions(Guid.NewGuid(), ITEM_ID);
            _relay.Handle(_member, first);
            Complete(1);
            Complete(1);
            _now = Constants.Town.REQUEST_WINDOW_SECONDS;

            GrowthRequestMessage second = RerollOptions(Guid.NewGuid(), ITEM_ID);
            _relay.Handle(_member, second);
            Complete(1);

            Assert.AreEqual(FIRST_ROLL + EquipmentOptionRules.ROLL_COUNT, _relay.RememberedRoll(PARTICIPANT_ID, second.OperationId));
            Assert.AreEqual(FIRST_ROLL, _relay.RememberedRoll(PARTICIPANT_ID, first.OperationId));
        }

        [Test]
        public void Handle_RerollOptionsInvalidItemId_RejectsWithoutApiCall()
        {
            _relay.Handle(_member, RerollOptions(Guid.NewGuid(), "../x"));

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_ID, out GrowthResultMessage result));
            Assert.AreEqual(ERejectReason.InvalidTarget, result.Reason);
            Assert.AreEqual(EGrowthAction.EquipmentRerollOptions, result.Action);
            Assert.AreEqual(0, _transport.Calls);
        }

        [Test]
        public void Handle_EnhanceEquipment_StillUsesSingleRoll()
        {
            GrowthRequestMessage message = Enhance(Guid.NewGuid(), ITEM_ID);

            _relay.Handle(_member, message);
            Complete(1);
            Complete(1);

            BackendRequest operation = LastRequest();
            Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, GrowthRoutes.EQUIPMENT_ENHANCE_FORMAT, CHARACTER_ID, ITEM_ID),
                operation.Path);
            var body = BackendJson.Deserialize<EquipmentEnhanceRequest>(operation.BodyJson);
            Assert.AreEqual(FIRST_ROLL, body.RollBasisPoints);
            Assert.AreEqual(1, _relay.RememberedRolls(PARTICIPANT_ID, message.OperationId).Length);
            Assert.AreEqual(FIRST_ROLL + 1, _nextRoll);
        }

        [Test]
        public void RollCount_PerAction_MatchesBodyShape()
        {
            Assert.AreEqual(0, GrowthRequestRules.RollCount(EGrowthAction.EquipmentEquip));
            Assert.AreEqual(1, GrowthRequestRules.RollCount(EGrowthAction.TalismanRegister));
            Assert.AreEqual(1, GrowthRequestRules.RollCount(EGrowthAction.EquipmentEnhance));
            Assert.AreEqual(EquipmentOptionRules.ROLL_COUNT, GrowthRequestRules.RollCount(EGrowthAction.EquipmentRerollOptions));
            Assert.IsTrue(GrowthRequestRules.UsesRoll(EGrowthAction.EquipmentRerollOptions));
            Assert.IsFalse(GrowthRequestRules.UsesRoll(EGrowthAction.EquipmentLock));
        }

        [Test]
        public void TryBuild_RerollOptionsWithWrongRolls_RejectsInvalidTarget()
        {
            GrowthRequestMessage message = RerollOptions(Guid.NewGuid(), ITEM_ID);
            int[][] bad =
            {
                null,
                new[] { 1 },
                new[] { 1, 2, 3 },
                new[] { 1, 2, 3, 4, 5 },
                new[] { 1, 2, 3, -1 },
                new[] { 1, 2, 3, EquipmentOptionRules.ROLL_RANGE }
            };

            for (int i = 0; i < bad.Length; ++i)
            {
                Assert.AreEqual(ERejectReason.InvalidTarget,
                    GrowthRequestRules.TryBuild(in message, CHARACTER_ID, REVISION, bad[i], TRAINING_TOTAL_CAP, out _, out _));
            }

            int[] edge = { 0, EquipmentOptionRules.ROLL_RANGE - 1, 0, EquipmentOptionRules.ROLL_RANGE - 1 };
            Assert.AreEqual(ERejectReason.None,
                GrowthRequestRules.TryBuild(in message, CHARACTER_ID, REVISION, edge, TRAINING_TOTAL_CAP, out _, out _));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private BackendRequest LastRequest()
        {
            BackendRequest last = null;
            while (_transport.Requests.TryDequeue(out BackendRequest request))
            {
                last = request;
            }

            return last;
        }

        private static GrowthRequestMessage RerollOptions(Guid operationId, string itemId)
        {
            return new GrowthRequestMessage(operationId, EGrowthAction.EquipmentRerollOptions, itemId, null, 0, 0, false, null, null);
        }

        private static GrowthRequestMessage Enhance(Guid operationId, string itemId)
        {
            return new GrowthRequestMessage(operationId, EGrowthAction.EquipmentEnhance, itemId, null, 0, 0, false, null, null);
        }

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

            var result = new OperationResult(request.OperationId, ContractCodes.OperationStatus.COMMITTED, 0, REVISION + 1, 0, string.Empty);
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
