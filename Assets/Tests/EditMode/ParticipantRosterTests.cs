// System
using System;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ParticipantRosterTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    PARTICIPANT_A         = 1;
        private const int    PARTICIPANT_B         = 2;
        private const int    PARTICIPANT_C         = 3;
        private const int    UNKNOWN_PARTICIPANT   = 99;
        private const int    INVALID_VALUE         = -1;
        private const int    ONE_PARTICIPANT       = 1;
        private const int    TWO_PARTICIPANTS      = 2;
        private const int    ALLOCATION_ITERATIONS = 512;
        private const string KEY_A                 = "participant-a";
        private const string KEY_B                 = "participant-b";
        private const string KEY_C                 = "participant-c";
        private const double DISCONNECT_TIME       = 10.0;
        private const double TIME_STEP             = 1.0;
        private const float  CHANGED_GRACE_SECONDS = 30f;

        private SessionConfigSO   _config;
        private ParticipantRoster _roster;
        private int               _closedCount           = 0;
        private int               _closedId              = 0;
        private ESessionEndReason _closedReason          = ESessionEndReason.Success;
        private double            _closedTime            = 0.0;
        private bool              _closedStateWasVisible = false;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config                = ScriptableObject.CreateInstance<SessionConfigSO>();
            _roster                = new ParticipantRoster(_config);
            _closedCount           = 0;
            _closedId              = 0;
            _closedTime            = 0.0;
            _closedStateWasVisible = false;
            _roster.ParticipantClosed += OnParticipantClosed;
        }

        [TearDown]
        public void TearDown()
        {
            _roster.ParticipantClosed -= OnParticipantClosed;
            UnityEngine.Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Constructor_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ParticipantRoster(null));
        }

        [TestCase(0)]
        [TestCase(Constants.Session.MAX_PARTY_SIZE + ONE_PARTICIPANT)]
        public void Constructor_InvalidCapacity_Throws(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParticipantRoster(_config, capacity));
        }

        [TestCase(0f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Constructor_InvalidGrace_Throws(float graceSeconds)
        {
            SetGraceSeconds(graceSeconds);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParticipantRoster(_config));
        }

        [Test]
        public void Constructor_ConfigChangesAfterCreation_KeepsCachedGrace()
        {
            double originalGrace = _config.ReconnectGraceSeconds;
            SetGraceSeconds(CHANGED_GRACE_SECONDS);
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);

            Assert.AreEqual(DISCONNECT_TIME + originalGrace, _roster.GetAt(0).GraceUntil);
        }

        [Test]
        public void TryAdd_ValidParticipant_KeepsIdentityAndInitialAxes()
        {
            Assert.IsTrue(_roster.TryAdd(PARTICIPANT_A, KEY_A, EParticipantLifeState.Dead));
            Assert.IsTrue(_roster.TryGet(PARTICIPANT_A, out ParticipantSnapshot snapshot));
            Assert.AreEqual(PARTICIPANT_A, snapshot.ParticipantId);
            Assert.AreSame(KEY_A, snapshot.ParticipantKey);
            Assert.AreEqual(EParticipantConnectionState.Connected, snapshot.ConnectionState);
            Assert.AreEqual(EParticipantLifeState.Dead, snapshot.LifeState);
            Assert.AreEqual(ONE_PARTICIPANT, _roster.Count);
            Assert.AreEqual(ONE_PARTICIPANT, _roster.ActiveCount);
            Assert.AreEqual(0, _roster.StartingCount);
            Assert.IsFalse(_roster.IsSealed);
            Assert.IsFalse(_roster.IsClosed(PARTICIPANT_A));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void TryAdd_InvalidKey_RejectsWithoutAdding(string key)
        {
            Assert.IsFalse(_roster.TryAdd(PARTICIPANT_A, key));
            Assert.AreEqual(0, _roster.Count);
        }

        [Test]
        public void TryAdd_InvalidIdOrLifeState_RejectsWithoutAdding()
        {
            Assert.IsFalse(_roster.TryAdd(0, KEY_A));
            Assert.IsFalse(_roster.TryAdd(INVALID_VALUE, KEY_A));
            Assert.IsFalse(_roster.TryAdd(PARTICIPANT_A, KEY_A, (EParticipantLifeState)INVALID_VALUE));
            Assert.AreEqual(0, _roster.Count);
        }

        [Test]
        public void TryAdd_DuplicateIdOrKey_RejectsWithoutReplacing()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);

            Assert.IsFalse(_roster.TryAdd(PARTICIPANT_A, KEY_B));
            Assert.IsFalse(_roster.TryAdd(PARTICIPANT_B, KEY_A));
            Assert.AreEqual(ONE_PARTICIPANT, _roster.Count);
            Assert.AreEqual(KEY_A, _roster.GetAt(0).ParticipantKey);
        }

        [Test]
        public void TryAdd_AtCapacity_RejectsWithoutGrowing()
        {
            var roster = new ParticipantRoster(_config, ONE_PARTICIPANT);
            roster.TryAdd(PARTICIPANT_A, KEY_A);

            Assert.IsFalse(roster.TryAdd(PARTICIPANT_B, KEY_B));
            Assert.AreEqual(ONE_PARTICIPANT, roster.Count);
        }

        [Test]
        public void Seal_ParticipantDeparts_StartCountAndMembershipStayFixed()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryAdd(PARTICIPANT_B, KEY_B);
            _roster.Seal();
            _roster.TryClose(PARTICIPANT_A, ESessionEndReason.Abandon, DISCONNECT_TIME);
            _roster.Seal();

            Assert.IsTrue(_roster.IsSealed);
            Assert.IsFalse(_roster.TryAdd(PARTICIPANT_C, KEY_C));
            Assert.AreEqual(TWO_PARTICIPANTS, _roster.Count);
            Assert.AreEqual(TWO_PARTICIPANTS, _roster.StartingCount);
            Assert.AreEqual(ONE_PARTICIPANT, _roster.ActiveCount);
            Assert.AreEqual(PARTICIPANT_A, _roster.GetAt(0).ParticipantId);
            Assert.AreEqual(PARTICIPANT_B, _roster.GetAt(ONE_PARTICIPANT).ParticipantId);
        }

        [Test]
        public void TryDisconnect_AliveParticipant_PreservesLifeAxis()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);

            Assert.IsTrue(_roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME));
            ParticipantSnapshot snapshot = _roster.GetAt(0);
            Assert.AreEqual(EParticipantConnectionState.ReconnectGrace, snapshot.ConnectionState);
            Assert.AreEqual(EParticipantLifeState.Alive, snapshot.LifeState);
            Assert.AreEqual(DISCONNECT_TIME + _config.ReconnectGraceSeconds, snapshot.GraceUntil);
            Assert.AreEqual(ONE_PARTICIPANT, _roster.ActiveCount);
        }

        [Test]
        public void TryDisconnect_DuplicateNotification_DoesNotExtendGrace()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            double graceUntil = _roster.GetAt(0).GraceUntil;

            Assert.IsTrue(_roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME + TIME_STEP));
            Assert.AreEqual(graceUntil, _roster.GetAt(0).GraceUntil);
            _roster.Tick(graceUntil);
            Assert.IsTrue(_roster.IsClosed(PARTICIPANT_A));
        }

        [Test]
        public void TryConnect_ConnectedParticipant_IsIdempotent()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            Assert.IsTrue(_roster.TryConnect(PARTICIPANT_A, DISCONNECT_TIME));
            Assert.IsTrue(_roster.TryConnect(PARTICIPANT_A, DISCONNECT_TIME));
            Assert.AreEqual(ONE_PARTICIPANT, _roster.ActiveCount);
            Assert.AreEqual(0, _closedCount);
        }

        [TestCase(EParticipantLifeState.Dead)]
        [TestCase(EParticipantLifeState.ReviveWaiting)]
        [TestCase(EParticipantLifeState.Spectating)]
        public void TryConnect_InsideGrace_PreservesLifeState(EParticipantLifeState lifeState)
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            _roster.TrySetLifeState(PARTICIPANT_A, lifeState);
            double insideGrace = _roster.GetAt(0).GraceUntil - TIME_STEP;

            Assert.IsTrue(_roster.TryConnect(PARTICIPANT_A, insideGrace));
            Assert.AreEqual(EParticipantConnectionState.Connected, _roster.GetAt(0).ConnectionState);
            Assert.AreEqual(lifeState, _roster.GetAt(0).LifeState);
            Assert.AreEqual(0.0, _roster.GetAt(0).GraceUntil);
            Assert.AreEqual(0, _closedCount);
        }

        [Test]
        public void TryConnect_AfterSuccessfulReconnect_NewDisconnectGetsNewGrace()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            _roster.TryConnect(PARTICIPANT_A, DISCONNECT_TIME + TIME_STEP);
            double nextDisconnect = DISCONNECT_TIME + TIME_STEP + TIME_STEP;

            Assert.IsTrue(_roster.TryDisconnect(PARTICIPANT_A, nextDisconnect));
            Assert.AreEqual(nextDisconnect + _config.ReconnectGraceSeconds, _roster.GetAt(0).GraceUntil);
        }

        [TestCase(0.0)]
        [TestCase(TIME_STEP)]
        public void TryConnect_AtOrAfterExpiry_ClosesAndRejects(double afterExpiry)
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A, EParticipantLifeState.Dead);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            double now = _roster.GetAt(0).GraceUntil + afterExpiry;

            Assert.IsFalse(_roster.TryConnect(PARTICIPANT_A, now));
            Assert.IsTrue(_roster.IsClosed(PARTICIPANT_A));
            Assert.AreEqual(EParticipantLifeState.Dead, _roster.GetAt(0).LifeState);
            Assert.AreEqual(ESessionEndReason.DisconnectExpired, _roster.GetAt(0).CloseReason);
            Assert.AreEqual(ONE_PARTICIPANT, _closedCount);
            Assert.AreEqual(PARTICIPANT_A, _closedId);
            Assert.AreEqual(now, _closedTime);
        }

        [Test]
        public void TryDisconnect_AtExpiry_DoesNotReopenOrExtendGrace()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            double graceUntil = _roster.GetAt(0).GraceUntil;

            Assert.IsFalse(_roster.TryDisconnect(PARTICIPANT_A, graceUntil));
            Assert.IsTrue(_roster.IsClosed(PARTICIPANT_A));
            Assert.AreEqual(graceUntil, _roster.GetAt(0).GraceUntil);
            Assert.AreEqual(ONE_PARTICIPANT, _closedCount);
        }

        [Test]
        public void IsRewardEligible_ObservedExpiry_BackdatedReconnectCannotRestoreEligibility()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            double graceUntil = _roster.GetAt(0).GraceUntil;

            Assert.IsFalse(_roster.IsRewardEligible(PARTICIPANT_A, graceUntil));
            Assert.IsFalse(_roster.TryConnect(PARTICIPANT_A, graceUntil - TIME_STEP));
            Assert.IsFalse(_roster.IsRewardEligible(PARTICIPANT_A, graceUntil - TIME_STEP));
            _roster.Tick(graceUntil);
            Assert.IsTrue(_roster.IsClosed(PARTICIPANT_A));
            Assert.AreEqual(ONE_PARTICIPANT, _closedCount);
        }

        [TestCase(EParticipantLifeState.Alive)]
        [TestCase(EParticipantLifeState.Dead)]
        [TestCase(EParticipantLifeState.ReviveWaiting)]
        [TestCase(EParticipantLifeState.Spectating)]
        public void IsRewardEligible_ConnectedOrInsideGrace_IgnoresLifeState(EParticipantLifeState lifeState)
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A, lifeState);
            Assert.IsTrue(_roster.IsRewardEligible(PARTICIPANT_A, DISCONNECT_TIME));
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            double graceUntil = _roster.GetAt(0).GraceUntil;

            Assert.IsTrue(_roster.IsRewardEligible(PARTICIPANT_A, graceUntil - TIME_STEP));
            Assert.IsFalse(_roster.IsRewardEligible(PARTICIPANT_A, graceUntil));
            Assert.AreEqual(0, _closedCount);
            _roster.Tick(graceUntil);
            Assert.IsFalse(_roster.IsRewardEligible(PARTICIPANT_A, graceUntil));
        }

        [Test]
        public void Tick_ExpiryBoundary_ClosesOnlyExpiredParticipantOnce()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryAdd(PARTICIPANT_B, KEY_B);
            _roster.Seal();
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            double graceUntil = _roster.GetAt(0).GraceUntil;

            _roster.Tick(graceUntil - TIME_STEP);
            Assert.IsFalse(_roster.IsClosed(PARTICIPANT_A));
            _roster.Tick(graceUntil);
            _roster.Tick(graceUntil + TIME_STEP);
            Assert.AreEqual(ONE_PARTICIPANT, _closedCount);
            Assert.AreEqual(ONE_PARTICIPANT, _roster.ActiveCount);
            Assert.AreEqual(TWO_PARTICIPANTS, _roster.StartingCount);
            Assert.IsFalse(_roster.IsClosed(PARTICIPANT_B));
            Assert.AreEqual(ESessionEndReason.DisconnectExpired, _closedReason);
            Assert.IsTrue(_closedStateWasVisible);
        }

        [Test]
        public void Tick_AllGracePeriodsExpire_ClosesAllAndKeepsStartingCount()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryAdd(PARTICIPANT_B, KEY_B);
            _roster.Seal();
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            _roster.TryDisconnect(PARTICIPANT_B, DISCONNECT_TIME);

            _roster.Tick(DISCONNECT_TIME + _config.ReconnectGraceSeconds);
            Assert.AreEqual(0, _roster.ActiveCount);
            Assert.AreEqual(TWO_PARTICIPANTS, _roster.StartingCount);
            Assert.AreEqual(TWO_PARTICIPANTS, _roster.Count);
            Assert.AreEqual(TWO_PARTICIPANTS, _closedCount);
        }

        [TestCase(ESessionEndReason.Success)]
        [TestCase(ESessionEndReason.Timeout)]
        [TestCase(ESessionEndReason.AllDead)]
        [TestCase(ESessionEndReason.Abandon)]
        [TestCase(ESessionEndReason.DisconnectExpired)]
        [TestCase(ESessionEndReason.ServiceCancelled)]
        public void TryClose_ValidReason_KeepsFirstReasonAndNotifiesOnce(ESessionEndReason reason)
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A, EParticipantLifeState.Dead);

            Assert.IsTrue(_roster.TryClose(PARTICIPANT_A, reason, DISCONNECT_TIME));
            Assert.IsFalse(_roster.TryClose(PARTICIPANT_A, ESessionEndReason.Abandon, DISCONNECT_TIME + TIME_STEP));
            _roster.Close(PARTICIPANT_A);
            Assert.AreEqual(reason, _roster.GetAt(0).CloseReason);
            Assert.AreEqual(reason, _closedReason);
            Assert.AreEqual(DISCONNECT_TIME, _closedTime);
            Assert.AreEqual(ONE_PARTICIPANT, _closedCount);
            Assert.AreEqual(0, _roster.ActiveCount);
        }

        [Test]
        public void TryClose_DepartedParticipant_AllLaterMutationsAreRejected()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A, EParticipantLifeState.Dead);
            _roster.TryClose(PARTICIPANT_A, ESessionEndReason.Abandon, DISCONNECT_TIME);

            Assert.IsFalse(_roster.TryConnect(PARTICIPANT_A, DISCONNECT_TIME));
            Assert.IsFalse(_roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME));
            Assert.IsFalse(_roster.TrySetLifeState(PARTICIPANT_A, EParticipantLifeState.Alive));
            Assert.IsFalse(_roster.IsRewardEligible(PARTICIPANT_A, DISCONNECT_TIME));
            Assert.IsFalse(_roster.TryAdd(PARTICIPANT_A, KEY_A));
            Assert.IsFalse(_roster.TryAdd(PARTICIPANT_B, KEY_A));
            Assert.AreEqual(EParticipantLifeState.Dead, _roster.GetAt(0).LifeState);
            Assert.AreEqual(ONE_PARTICIPANT, _closedCount);
        }

        [Test]
        public void Close_CombatInterface_UsesDisconnectExpiredAndLastObservedTime()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.Tick(DISCONNECT_TIME);
            IParticipantRoster combatRoster = _roster;

            combatRoster.Close(PARTICIPANT_A);
            combatRoster.Close(PARTICIPANT_A);
            Assert.IsTrue(combatRoster.IsClosed(PARTICIPANT_A));
            Assert.AreEqual(ESessionEndReason.DisconnectExpired, _closedReason);
            Assert.AreEqual(DISCONNECT_TIME, _closedTime);
            Assert.AreEqual(ONE_PARTICIPANT, _closedCount);
        }

        [Test]
        public void IsClosed_UnknownParticipant_FailsClosedWithoutAdding()
        {
            Assert.IsTrue(_roster.IsClosed(UNKNOWN_PARTICIPANT));
            Assert.IsFalse(_roster.TryGet(UNKNOWN_PARTICIPANT, out _));
            Assert.IsFalse(_roster.TryConnect(UNKNOWN_PARTICIPANT, DISCONNECT_TIME));
            Assert.IsFalse(_roster.TryDisconnect(UNKNOWN_PARTICIPANT, DISCONNECT_TIME));
            Assert.IsFalse(_roster.TrySetLifeState(UNKNOWN_PARTICIPANT, EParticipantLifeState.Alive));
            Assert.IsFalse(_roster.IsRewardEligible(UNKNOWN_PARTICIPANT, DISCONNECT_TIME));
            _roster.Close(UNKNOWN_PARTICIPANT);
            Assert.AreEqual(0, _roster.Count);
            Assert.AreEqual(0, _closedCount);
        }

        [Test]
        public void TrySetLifeState_InvalidState_KeepsPreviousState()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            Assert.IsFalse(_roster.TrySetLifeState(PARTICIPANT_A, (EParticipantLifeState)INVALID_VALUE));
            Assert.AreEqual(EParticipantLifeState.Alive, _roster.GetAt(0).LifeState);
        }

        [Test]
        public void TryClose_InvalidReason_KeepsParticipantActive()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            Assert.IsFalse(_roster.TryClose(PARTICIPANT_A, (ESessionEndReason)INVALID_VALUE, DISCONNECT_TIME));
            Assert.IsFalse(_roster.IsClosed(PARTICIPANT_A));
            Assert.AreEqual(0, _closedCount);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-TIME_STEP)]
        public void Tick_InvalidTime_RejectsAllTimeOperationsWithoutChangingAxes(double invalidTime)
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            double graceUntil = _roster.GetAt(0).GraceUntil;

            Assert.IsFalse(_roster.TryConnect(PARTICIPANT_A, invalidTime));
            Assert.IsFalse(_roster.TryDisconnect(PARTICIPANT_A, invalidTime));
            Assert.IsFalse(_roster.IsRewardEligible(PARTICIPANT_A, invalidTime));
            Assert.IsFalse(_roster.TryClose(PARTICIPANT_A, ESessionEndReason.Abandon, invalidTime));
            _roster.Tick(invalidTime);
            Assert.AreEqual(EParticipantConnectionState.ReconnectGrace, _roster.GetAt(0).ConnectionState);
            Assert.AreEqual(graceUntil, _roster.GetAt(0).GraceUntil);
            Assert.AreEqual(0, _closedCount);
            Assert.IsTrue(_roster.IsRewardEligible(PARTICIPANT_A, DISCONNECT_TIME));
        }

        [Test]
        public void Tick_TimeMovesBackwards_RejectsAllTimeOperations()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.Tick(DISCONNECT_TIME);
            double previousTime = DISCONNECT_TIME - TIME_STEP;

            Assert.IsFalse(_roster.TryDisconnect(PARTICIPANT_A, previousTime));
            Assert.IsFalse(_roster.TryConnect(PARTICIPANT_A, previousTime));
            Assert.IsFalse(_roster.IsRewardEligible(PARTICIPANT_A, previousTime));
            Assert.IsFalse(_roster.TryClose(PARTICIPANT_A, ESessionEndReason.Abandon, previousTime));
            _roster.Tick(previousTime);
            Assert.AreEqual(EParticipantConnectionState.Connected, _roster.GetAt(0).ConnectionState);
            Assert.IsTrue(_roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME));
        }

        [Test]
        public void TryDisconnect_UnrepresentableDeadline_RejectsWithoutChangingConnection()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            Assert.IsFalse(_roster.TryDisconnect(PARTICIPANT_A, double.MaxValue));
            Assert.AreEqual(EParticipantConnectionState.Connected, _roster.GetAt(0).ConnectionState);
        }

        [Test]
        public void TryGet_StateChangesLater_PreservesCapturedSnapshot()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.TryGet(PARTICIPANT_A, out ParticipantSnapshot captured);
            _roster.TryDisconnect(PARTICIPANT_A, DISCONNECT_TIME);
            _roster.TrySetLifeState(PARTICIPANT_A, EParticipantLifeState.Dead);

            Assert.AreEqual(EParticipantConnectionState.Connected, captured.ConnectionState);
            Assert.AreEqual(EParticipantLifeState.Alive, captured.LifeState);
            Assert.AreEqual(0.0, captured.GraceUntil);
        }

        [Test]
        public void GetAt_InvalidIndex_Throws()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            Assert.Throws<ArgumentOutOfRangeException>(() => _roster.GetAt(INVALID_VALUE));
            Assert.Throws<ArgumentOutOfRangeException>(() => _roster.GetAt(_roster.Count));
        }

        [Test]
        public void Tick_AfterWarmup_HotPathDoesNotAllocate()
        {
            _roster.TryAdd(PARTICIPANT_A, KEY_A);
            _roster.Seal();
            ExerciseHotPath(0.0);
            GC.GetAllocatedBytesForCurrentThread();
            long before    = GC.GetAllocatedBytesForCurrentThread();
            bool succeeded = true;
            for (int i = 0; i < ALLOCATION_ITERATIONS; ++i)
            {
                succeeded &= ExerciseHotPath(i * TIME_STEP);
            }

            _roster.TryClose(PARTICIPANT_A, ESessionEndReason.Abandon, ALLOCATION_ITERATIONS * TIME_STEP);
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsTrue(succeeded);
            Assert.AreEqual(0L, allocatedBytes);
            Assert.AreEqual(ONE_PARTICIPANT, _closedCount);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private bool ExerciseHotPath(double serverTime)
        {
            bool succeeded = _roster.TryDisconnect(PARTICIPANT_A, serverTime);
            succeeded &= _roster.IsRewardEligible(PARTICIPANT_A, serverTime);
            succeeded &= _roster.TrySetLifeState(PARTICIPANT_A, EParticipantLifeState.Dead);
            succeeded &= _roster.TryConnect(PARTICIPANT_A, serverTime);
            succeeded &= _roster.TrySetLifeState(PARTICIPANT_A, EParticipantLifeState.Alive);
            succeeded &= _roster.TryGet(PARTICIPANT_A, out _);
            succeeded &= _roster.GetAt(0).ParticipantId == PARTICIPANT_A;
            succeeded &= !_roster.IsClosed(PARTICIPANT_A);
            _roster.Tick(serverTime);
            return succeeded;
        }

        private void OnParticipantClosed(int participantId, ESessionEndReason reason, double serverTime)
        {
            ++_closedCount;
            _closedId              = participantId;
            _closedReason          = reason;
            _closedTime            = serverTime;
            _closedStateWasVisible = _roster.IsClosed(participantId);
        }

        private void SetGraceSeconds(float graceSeconds)
        {
            FieldInfo field = typeof(SessionConfigSO).GetField("_reconnectGraceSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(_config, graceSeconds);
        }
    }
}
