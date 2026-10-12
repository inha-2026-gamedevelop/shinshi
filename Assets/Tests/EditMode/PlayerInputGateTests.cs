// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class PlayerInputGateTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    SESSION_EPOCH  = 7;
        private const int    PARTICIPANT_ID = 3;
        private const double START_TIME     = 100.0;
        private const double TIME_TOLERANCE = 0.000001;

        // 문서가 정한 입력 시각 허용 창
        private const double DOCUMENTED_PAST_SECONDS   = 0.15;
        private const double DOCUMENTED_FUTURE_SECONDS = 0.05;
        private const double FLOAT_CONSTANT_TOLERANCE  = 0.00001;

        // 순번·시각 위반 시험은 토큰 한 개 이상 충전되는 간격으로 보낸다
        private const double VIOLATION_STEP_SECONDS = 0.1;
        private const int    WINDOW_COUNT           = 3;
        private const double INSIDE_SECOND          = 0.9;
        private const double RATE_BOUNDARY_EPSILON  = 0.0001;
        private const int    ALLOCATION_ITERATIONS  = 1000;

        // 과거 허용 창 150ms 안쪽
        private const double INSIDE_OFFSET_SECONDS = 0.1;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Check_FirstSequence_AcceptsAndAdvances()
        {
            PlayerInputGate gate = CreateGate();

            EInputVerdict verdict = gate.Check(Header(1, START_TIME), START_TIME, out double requestTime);

            Assert.AreEqual(EInputVerdict.Accepted, verdict);
            Assert.AreEqual(1u, gate.LastAcceptedSequence);
            Assert.AreEqual(START_TIME, requestTime, TIME_TOLERANCE);
        }

        [Test]
        public void Check_DuplicateSequence_RejectsAsStale()
        {
            PlayerInputGate gate = CreateGate();
            gate.Check(Header(5, START_TIME), START_TIME, out _);

            EInputVerdict verdict = gate.Check(Header(5, START_TIME), START_TIME, out _);

            Assert.AreEqual(EInputVerdict.StaleSequence, verdict);
            Assert.AreEqual(5u, gate.LastAcceptedSequence);
            Assert.AreEqual(1, gate.ViolationCount);
        }

        [Test]
        public void Check_LowerSequence_RejectsAsStale()
        {
            PlayerInputGate gate = CreateGate();
            gate.Check(Header(5, START_TIME), START_TIME, out _);

            EInputVerdict verdict = gate.Check(Header(4, START_TIME), START_TIME, out _);

            Assert.AreEqual(EInputVerdict.StaleSequence, verdict);
            Assert.AreEqual(5u, gate.LastAcceptedSequence);
        }

        [Test]
        public void Check_SequenceGap_Accepts()
        {
            PlayerInputGate gate = CreateGate();
            gate.Check(Header(1, START_TIME), START_TIME, out _);

            EInputVerdict verdict = gate.Check(Header(9, START_TIME), START_TIME, out _);

            Assert.AreEqual(EInputVerdict.Accepted, verdict);
            Assert.AreEqual(9u, gate.LastAcceptedSequence);
        }

        [Test]
        public void Check_SequenceZero_RejectsAsStale()
        {
            PlayerInputGate gate = CreateGate();

            Assert.AreEqual(EInputVerdict.StaleSequence, gate.Check(Header(0, START_TIME), START_TIME, out _));
        }

        [Test]
        public void Restore_LastSequence_RejectsUpToRestoredSequence()
        {
            PlayerInputGate gate = CreateGate();
            gate.Restore(20);

            Assert.AreEqual(EInputVerdict.StaleSequence, gate.Check(Header(20, START_TIME), START_TIME, out _));
            Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(21, START_TIME + 1.0), START_TIME + 1.0, out _));
        }

        [Test]
        public void Check_ProtocolMismatch_RejectsAndDisconnects()
        {
            PlayerInputGate gate   = CreateGate();
            var             header = new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION + 1, SESSION_EPOCH, PARTICIPANT_ID, 1, START_TIME);

            Assert.AreEqual(EInputVerdict.ProtocolMismatch, gate.Check(header, START_TIME, out _));
            Assert.IsTrue(gate.ShouldDisconnect);
            Assert.AreEqual(0u, gate.LastAcceptedSequence);
        }

        [Test]
        public void ClearDisconnect_AfterReattach_KeepsViolationsAndSequence()
        {
            PlayerInputGate gate = CreateGate();
            Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(5, START_TIME), START_TIME, out _));
            // 같은 서버 시각의 주기 초과는 한 번만 세므로 시각을 나눠 순번 역행으로 위반을 쌓는다
            for (int i = 0; i < Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD; ++i)
            {
                double now = START_TIME + ((i + 1) * VIOLATION_STEP_SECONDS);
                gate.Check(Header(5, now), now, out _);
            }

            Assert.IsTrue(gate.ShouldDisconnect);
            int violations = gate.ViolationCount;

            gate.ClearDisconnect();

            Assert.IsFalse(gate.ShouldDisconnect, "새 연결은 이전 연결의 끊기 표시를 물려받지 않는다");
            Assert.AreEqual(violations, gate.ViolationCount, "위반 누적은 끊고 다시 붙어도 지워지지 않는다");
            Assert.AreEqual(5u, gate.LastAcceptedSequence);
            Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(6, START_TIME + 1.0), START_TIME + 1.0, out _));
            Assert.IsFalse(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_SessionMismatch_RejectsAndDisconnects()
        {
            PlayerInputGate gate   = CreateGate();
            var             header = new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH + 1, PARTICIPANT_ID, 1, START_TIME);

            Assert.AreEqual(EInputVerdict.SessionMismatch, gate.Check(header, START_TIME, out _));
            Assert.IsTrue(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_ParticipantMismatch_RejectsAndDisconnects()
        {
            PlayerInputGate gate   = CreateGate();
            var             header = new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH, PARTICIPANT_ID + 1, 1, START_TIME);

            Assert.AreEqual(EInputVerdict.ParticipantMismatch, gate.Check(header, START_TIME, out _));
            Assert.IsTrue(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_BurstOverLimit_DropsExcessWithoutAdvancingSequence()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;

            for (int i = 0; i < Constants.Player.INPUT_RATE_BURST; ++i)
            {
                ++sequence;
                Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(sequence, START_TIME), START_TIME, out _));
            }

            ++sequence;
            Assert.AreEqual(EInputVerdict.RateLimited, gate.Check(Header(sequence, START_TIME), START_TIME, out _));
            Assert.AreEqual(sequence - 1, gate.LastAcceptedSequence);
            Assert.AreEqual(1, gate.RateViolationCount);
            Assert.AreEqual(0, gate.ViolationCount);
        }

        // 끊겼다 이어진 연결의 입력이 한 틱에 몰려도 위반은 한 번만 센다. 지연 한 번으로 정상 사용자가 끊기지 않는다
        [Test]
        public void Check_BurstInSameTick_CountsOneViolation()
        {
            PlayerInputGate gate  = CreateGate();
            int             burst = Constants.Player.INPUT_RATE_BURST + Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD * 2;

            for (int i = 1; i <= burst; ++i)
            {
                gate.Check(Header((uint)i, START_TIME), START_TIME, out _);
            }

            Assert.AreEqual(1, gate.RateViolationCount);
            Assert.AreEqual(0, gate.ViolationCount);
            Assert.IsFalse(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_RateBurstsAcrossTicksInSameSecond_CountsOneInterval()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;

            SendRateBurst(gate, ref sequence, START_TIME);
            SendRateBurst(gate, ref sequence, START_TIME + VIOLATION_STEP_SECONDS);
            SendRateBurst(gate, ref sequence, START_TIME + INSIDE_SECOND);

            Assert.AreEqual(1, gate.RateViolationCount, "Math.Floor가 같은 1초 구간은 틱이 달라도 한 번 센다");
            Assert.AreEqual(0, gate.ViolationCount);
            Assert.IsFalse(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_RateBurstsAcrossFloorBoundary_CountsDifferentIntervals()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;
            double boundary = START_TIME + 1.0;

            SendRateBurst(gate, ref sequence, boundary - RATE_BOUNDARY_EPSILON);
            SendRateBurst(gate, ref sequence, boundary);

            Assert.AreEqual(2, gate.RateViolationCount,
                "1초보다 짧은 간격이어도 Math.Floor(서버 시각)가 다르면 다른 구간이다");
            Assert.IsFalse(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_FourRateIntervalsWithinTenSeconds_DoesNotDisconnect()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;

            for (int i = 0; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1; ++i)
            {
                SendRateBurst(gate, ref sequence, START_TIME + i);
                Assert.IsFalse(gate.ShouldDisconnect);
            }

            Assert.AreEqual(Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1, gate.RateViolationCount);
        }

        [Test]
        public void Check_FifthRateIntervalWithinTenSeconds_Disconnects()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;

            for (int i = 0; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD; ++i)
            {
                SendRateBurst(gate, ref sequence, START_TIME + i);
            }

            Assert.AreEqual(Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD, gate.RateViolationCount);
            Assert.AreEqual(0, gate.ViolationCount);
            Assert.IsTrue(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_FirstRateIntervalJustInsideTenSecondBoundary_DisconnectsOnFifth()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;
            for (int i = 0; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1; ++i)
            {
                SendRateBurst(gate, ref sequence, START_TIME + i);
            }

            SendRateBurst(gate, ref sequence,
                START_TIME + Constants.Player.INPUT_RATE_VIOLATION_WINDOW_SECONDS - RATE_BOUNDARY_EPSILON);

            Assert.IsTrue(gate.ShouldDisconnect, "10초 미만 지난 구간은 현재 창에 포함한다");
        }

        [Test]
        public void Check_FirstRateIntervalExactlyTenSecondsOld_ExpiresBeforeFifth()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;
            for (int i = 0; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1; ++i)
            {
                SendRateBurst(gate, ref sequence, START_TIME + i);
            }

            SendRateBurst(gate, ref sequence, START_TIME + Constants.Player.INPUT_RATE_VIOLATION_WINDOW_SECONDS);

            Assert.AreEqual(Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1, gate.RateViolationCount);
            Assert.IsFalse(gate.ShouldDisconnect, "포함 범위는 (현재-10초, 현재]이며 정확히 10초 된 구간은 제외한다");
        }

        [Test]
        public void Check_RepeatedRateInterval_UsesItsLatestViolationForExpiry()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;
            SendRateBurst(gate, ref sequence, START_TIME);
            SendRateBurst(gate, ref sequence, START_TIME + INSIDE_SECOND);
            for (int i = 1; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1; ++i)
            {
                SendRateBurst(gate, ref sequence, START_TIME + i);
            }

            SendRateBurst(gate, ref sequence,
                START_TIME + Constants.Player.INPUT_RATE_VIOLATION_WINDOW_SECONDS + INSIDE_OFFSET_SECONDS);

            Assert.IsTrue(gate.ShouldDisconnect, "같은 구간의 첫 초과가 만료됐어도 마지막 초과가 10초 안이면 구간은 남는다");
        }

        [Test]
        public void Check_RateIntervalsAcrossOldWindowStart_UsesSlidingTenSecondWindow()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;
            SendRateBurst(gate, ref sequence, START_TIME);
            double recentStart = START_TIME + Constants.Player.INPUT_RATE_VIOLATION_WINDOW_SECONDS - 2.0;
            for (int i = 0; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD; ++i)
            {
                SendRateBurst(gate, ref sequence, recentStart + i);
            }

            Assert.IsTrue(gate.ShouldDisconnect, "오래된 첫 구간이 만료돼도 나머지 구간을 통째로 지우지 않는다");
        }

        [Test]
        public void Check_RateAndMalformedViolations_DoNotShareThreshold()
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 100;
            gate.Check(Header(sequence, START_TIME), START_TIME, out _);
            for (int i = 1; i < Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD; ++i)
            {
                double now = START_TIME + (i * VIOLATION_STEP_SECONDS);
                Assert.AreEqual(EInputVerdict.StaleSequence, gate.Check(Header(sequence, now), now, out _));
            }

            double rateTime = START_TIME + (Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD * VIOLATION_STEP_SECONDS);
            SendRateBurst(gate, ref sequence, rateTime);

            Assert.AreEqual(Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD - 1, gate.ViolationCount);
            Assert.AreEqual(1, gate.RateViolationCount);
            Assert.IsFalse(gate.ShouldDisconnect);

            double malformedTime = rateTime + VIOLATION_STEP_SECONDS;
            Assert.AreEqual(EInputVerdict.InvalidTime, gate.Check(Header(++sequence, double.NaN), malformedTime, out _));
            Assert.IsTrue(gate.ShouldDisconnect, "순번·시각 이상은 기존 30회 규칙을 공유한다");
        }

        [Test]
        public void RestoreRateViolations_ReconnectInSameInterval_KeepsCountAndDisconnectsOnNextInterval()
        {
            PlayerInputGate original = CreateGate();
            uint sequence = 0;
            for (int i = 0; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1; ++i)
            {
                SendRateBurst(original, ref sequence, START_TIME + i);
            }

            PlayerInputGate.RateViolationSnapshot snapshot = original.CaptureRateViolations();
            double reconnectTime = START_TIME + Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 2 + INSIDE_OFFSET_SECONDS;
            PlayerInputGate restored = CreateGate(reconnectTime);
            restored.Restore(original.LastAcceptedSequence);
            restored.RestoreRateViolations(in snapshot, reconnectTime);
            SendRateBurst(restored, ref sequence, reconnectTime);

            Assert.AreEqual(Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1, restored.RateViolationCount);
            Assert.IsFalse(restored.ShouldDisconnect);

            SendRateBurst(restored, ref sequence, START_TIME + Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1);
            Assert.IsTrue(restored.ShouldDisconnect);
        }

        [Test]
        public void RestoreRateViolations_ReconnectAfterWindow_DiscardsExpiredIntervals()
        {
            PlayerInputGate original = CreateGate();
            uint sequence = 0;
            SendRateBurst(original, ref sequence, START_TIME);
            PlayerInputGate.RateViolationSnapshot snapshot = original.CaptureRateViolations();
            double reconnectTime = START_TIME + Constants.Player.INPUT_RATE_VIOLATION_WINDOW_SECONDS;
            PlayerInputGate restored = CreateGate(reconnectTime);

            restored.RestoreRateViolations(in snapshot, reconnectTime);
            Assert.AreEqual(0, restored.RateViolationCount);
            SendRateBurst(restored, ref sequence, reconnectTime);
            Assert.AreEqual(1, restored.RateViolationCount);
            Assert.IsFalse(restored.ShouldDisconnect);
        }

        [Test]
        public void ClearDisconnect_RateLimit_KeepsBoundedIntervals()
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 0;
            for (int i = 0; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD; ++i)
            {
                SendRateBurst(gate, ref sequence, START_TIME + i);
            }

            gate.ClearDisconnect();
            Assert.IsFalse(gate.ShouldDisconnect);
            Assert.AreEqual(Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD, gate.RateViolationCount);
            SendRateBurst(gate, ref sequence, START_TIME + Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD);
            Assert.IsTrue(gate.ShouldDisconnect);
            Assert.AreEqual(Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD, gate.RateViolationCount);
        }

        [Test]
        public void CaptureRateViolations_OriginalGateChanges_DoesNotMutateSnapshot()
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 0;
            SendRateBurst(gate, ref sequence, START_TIME);
            PlayerInputGate.RateViolationSnapshot snapshot = gate.CaptureRateViolations();
            SendRateBurst(gate, ref sequence, START_TIME + INSIDE_SECOND);

            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual(START_TIME, snapshot.GetTime(0));
            Assert.AreEqual(START_TIME + INSIDE_SECOND, gate.CaptureRateViolations().GetTime(0));
        }

        [Test]
        public void RestoreRateViolations_CombatSnapshotThroughReconnectVault_KeepsIntervals()
        {
            PlayerInputGate original = CreateGate();
            uint sequence = 0;
            for (int i = 0; i < Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1; ++i)
            {
                SendRateBurst(original, ref sequence, START_TIME + i);
            }

            var snapshot = new ParticipantCombatSnapshot(0f, EParticipantLifeState.Alive, 0.0, 0.0,
                original.LastAcceptedSequence, original.ViolationCount, original.ViolationWindowStart,
                rateViolations: original.CaptureRateViolations(), tokenBucket: original.CaptureTokenBucket());
            var vault = new ReconnectVault(1);
            double reconnectTime = START_TIME + Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1;
            vault.Store(PARTICIPANT_ID, in snapshot, reconnectTime + 1.0, out _);
            Assert.IsTrue(vault.TryTake(PARTICIPANT_ID, reconnectTime, out ParticipantCombatSnapshot preserved));
            PlayerInputGate restored = CreateGate(reconnectTime);
            restored.Restore(preserved.LastSequence);
            restored.RestoreTokenBucket(preserved.TokenBucket, reconnectTime);
            restored.RestoreViolations(preserved.ViolationWindowStart, preserved.ViolationCount);
            restored.RestoreRateViolations(preserved.RateViolations, reconnectTime);

            Assert.AreEqual(Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD - 1, restored.RateViolationCount);
            SendRateBurst(restored, ref sequence, reconnectTime);
            Assert.IsTrue(restored.ShouldDisconnect, "전투 스냅샷과 유예 보존소를 통과해도 네 구간을 초기화하지 않는다");
        }

        [Test]
        public void Check_RateLimitAndSnapshotAfterWarmup_AllocatesZero()
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 0;
            SendRateBurst(gate, ref sequence, START_TIME);
            PlayerInputGate.RateViolationSnapshot snapshot = gate.CaptureRateViolations();
            gate.RestoreRateViolations(in snapshot, START_TIME);
            PlayerInputGate.TokenBucketSnapshot tokenBucket = gate.CaptureTokenBucket();
            gate.RestoreTokenBucket(in tokenBucket, START_TIME);
            GC.GetAllocatedBytesForCurrentThread();
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[256];
            long controlAllocated = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
            GC.KeepAlive(control);
            if (controlAllocated == 0)
            {
                Assert.Ignore("현재 런타임의 GC.GetAllocatedBytesForCurrentThread가 기지 256바이트 할당도 0으로 반환한다. 입력 게이트 0 할당은 실행 검증할 수 없어 이 시험을 제외한다");
            }

            Assert.GreaterOrEqual(controlAllocated, control.Length, "기지 할당을 세지 못하는 런타임에서는 0 할당 판정을 내릴 수 없다");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < ALLOCATION_ITERATIONS; ++i)
            {
                double now = START_TIME + (i * VIOLATION_STEP_SECONDS);
                SendRateBurst(gate, ref sequence, now);
                snapshot = gate.CaptureRateViolations();
                gate.RestoreRateViolations(in snapshot, now);
                tokenBucket = gate.CaptureTokenBucket();
                gate.RestoreTokenBucket(in tokenBucket, now);
                gate.ClearDisconnect();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.LessOrEqual(snapshot.Count, Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD);
        }

        [Test]
        public void Check_AfterRefillInterval_AcceptsAgain()
        {
            PlayerInputGate gate     = CreateGate();
            uint            sequence = 0;

            for (int i = 0; i < Constants.Player.INPUT_RATE_BURST; ++i)
            {
                ++sequence;
                gate.Check(Header(sequence, START_TIME), START_TIME, out _);
            }

            double oneTokenLater = START_TIME + (1.0 / Constants.Network.INPUT_SEND_RATE_MAX);
            ++sequence;

            Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(sequence, oneTokenLater), oneTokenLater, out _));
        }

        [Test]
        public void Check_SteadyContractRate_NeverRateLimited()
        {
            PlayerInputGate gate     = CreateGate();
            double          interval = 1.0 / Constants.Network.INPUT_SEND_RATE_MAX;
            int             inputs   = Constants.Network.INPUT_SEND_RATE_MAX * 10;

            for (int i = 1; i <= inputs; ++i)
            {
                double now = START_TIME + (i * interval);
                Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header((uint)i, now), now, out _));
            }

            Assert.AreEqual(0, gate.ViolationCount);
        }

        [TestCase(START_TIME)]
        [TestCase(36000.0)]
        public void Check_DepletedBucketAtExactContractRate_NeverRateLimited(double start)
        {
            PlayerInputGate gate = CreateGate(start);
            uint sequence = 0;
            DrainTokens(gate, ref sequence, start);
            double interval = 1.0 / Constants.Network.INPUT_SEND_RATE_MAX;
            int inputs = Constants.Network.INPUT_SEND_RATE_MAX * 120;
            for (int i = 1; i <= inputs; ++i)
            {
                double now = start + (i * interval);
                Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(++sequence, now), now, out _));
            }

            Assert.AreEqual(0, gate.RateViolationCount);
            Assert.IsFalse(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_RefillBelowTokenEpsilon_RemainsRateLimited()
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 0;
            DrainTokens(gate, ref sequence, START_TIME);
            double now = START_TIME + ((1.0 - (2.0 * Constants.Player.INPUT_RATE_TOKEN_EPSILON))
                / Constants.Network.INPUT_SEND_RATE_MAX);

            Assert.AreEqual(EInputVerdict.RateLimited, gate.Check(Header(++sequence, now), now, out _));
            now = START_TIME + (1.0 / Constants.Network.INPUT_SEND_RATE_MAX);
            Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(++sequence, now), now, out _));
        }

        [Test]
        public void RestoreTokenBucket_RepeatedSameTimeReconnect_DoesNotGrantNewBurst()
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 0;
            DrainTokens(gate, ref sequence, START_TIME);
            for (int i = 0; i < Constants.Player.INPUT_RATE_BURST; ++i)
            {
                PlayerInputGate.TokenBucketSnapshot snapshot = gate.CaptureTokenBucket();
                PlayerInputGate.RateViolationSnapshot violations = gate.CaptureRateViolations();
                gate = CreateGate();
                gate.RestoreTokenBucket(in snapshot, START_TIME);
                gate.RestoreRateViolations(in violations, START_TIME);
                Assert.AreEqual(EInputVerdict.RateLimited, gate.Check(Header(++sequence, START_TIME), START_TIME, out _));
                Assert.AreEqual(0.0, gate.CaptureTokenBucket().Tokens);
                Assert.AreEqual(1, gate.RateViolationCount);
            }

            Assert.IsFalse(gate.ShouldDisconnect);
        }

        [Test]
        public void RestoreTokenBucket_CombatSnapshotThroughReconnectVault_DoesNotGrantNewBurst()
        {
            PlayerInputGate original = CreateGate();
            uint sequence = 0;
            DrainTokens(original, ref sequence, START_TIME);
            var snapshot = new ParticipantCombatSnapshot(0f, EParticipantLifeState.Alive, 0.0, 0.0,
                original.LastAcceptedSequence, original.ViolationCount, original.ViolationWindowStart,
                rateViolations: original.CaptureRateViolations(), tokenBucket: original.CaptureTokenBucket());
            var vault = new ReconnectVault(1);
            vault.Store(PARTICIPANT_ID, in snapshot, START_TIME + 1.0, out _);
            Assert.IsTrue(vault.TryTake(PARTICIPANT_ID, START_TIME, out ParticipantCombatSnapshot preserved));
            PlayerInputGate restored = CreateGate();
            restored.Restore(preserved.LastSequence);
            restored.RestoreTokenBucket(preserved.TokenBucket, START_TIME);
            restored.RestoreViolations(preserved.ViolationWindowStart, preserved.ViolationCount);
            restored.RestoreRateViolations(preserved.RateViolations, START_TIME);

            Assert.IsTrue(preserved.TokenBucket.HasValue);
            Assert.AreEqual(0.0, preserved.TokenBucket.Tokens);
            Assert.AreEqual(EInputVerdict.RateLimited, restored.Check(Header(++sequence, START_TIME), START_TIME, out _));
            Assert.AreEqual(original.LastAcceptedSequence, restored.LastAcceptedSequence);
        }

        [Test]
        public void RestoreTokenBucket_ShortReconnect_RefillsOnlyElapsedTime()
        {
            PlayerInputGate original = CreateGate();
            uint sequence = 0;
            DrainTokens(original, ref sequence, START_TIME);
            PlayerInputGate.TokenBucketSnapshot snapshot = original.CaptureTokenBucket();
            double halfTokenLater = START_TIME + (0.5 / Constants.Network.INPUT_SEND_RATE_MAX);
            PlayerInputGate restored = CreateGate(halfTokenLater);
            restored.RestoreTokenBucket(in snapshot, halfTokenLater);

            Assert.AreEqual(0.5, restored.CaptureTokenBucket().Tokens, TIME_TOLERANCE);
            Assert.AreEqual(EInputVerdict.RateLimited, restored.Check(Header(++sequence, halfTokenLater), halfTokenLater, out _));
            double oneTokenLater = START_TIME + (1.0 / Constants.Network.INPUT_SEND_RATE_MAX);
            Assert.AreEqual(EInputVerdict.Accepted, restored.Check(Header(++sequence, oneTokenLater), oneTokenLater, out _));
        }

        [Test]
        public void RestoreTokenBucket_LongReconnect_CapsRefillAtBurst()
        {
            PlayerInputGate original = CreateGate();
            uint sequence = 0;
            DrainTokens(original, ref sequence, START_TIME);
            PlayerInputGate.TokenBucketSnapshot snapshot = original.CaptureTokenBucket();
            double reconnectTime = START_TIME + Constants.Player.INPUT_RATE_VIOLATION_WINDOW_SECONDS;
            PlayerInputGate restored = CreateGate(reconnectTime);
            restored.RestoreTokenBucket(in snapshot, reconnectTime);

            Assert.AreEqual(Constants.Player.INPUT_RATE_BURST, restored.CaptureTokenBucket().Tokens);
            DrainTokens(restored, ref sequence, reconnectTime);
            Assert.AreEqual(EInputVerdict.RateLimited, restored.Check(Header(++sequence, reconnectTime), reconnectTime, out _));
        }

        [Test]
        public void RestoreTokenBucket_TimeMovesBackAcrossReconnect_KeepsOriginalTimeAndTokens()
        {
            PlayerInputGate original = CreateGate();
            uint sequence = 0;
            SendRateBurst(original, ref sequence, START_TIME);
            PlayerInputGate.TokenBucketSnapshot tokens = original.CaptureTokenBucket();
            PlayerInputGate.RateViolationSnapshot violations = original.CaptureRateViolations();
            PlayerInputGate restored = CreateGate(START_TIME - 1.0);
            restored.RestoreTokenBucket(in tokens, START_TIME - 1.0);
            restored.RestoreRateViolations(in violations, START_TIME - 1.0);
            double reversed = START_TIME - 1.0 + (1.0 / Constants.Network.INPUT_SEND_RATE_MAX);

            Assert.AreEqual(EInputVerdict.RateLimited, restored.Check(Header(++sequence, reversed), reversed, out double requestTime));
            Assert.AreEqual(START_TIME, requestTime);
            Assert.AreEqual(START_TIME, restored.CaptureTokenBucket().LastRefillTime);
            Assert.AreEqual(START_TIME, restored.CaptureRateViolations().GetTime(0));
            Assert.AreEqual(1, restored.RateViolationCount);
        }

        [Test]
        public void ClearDisconnect_DepletedSameInstance_KeepsTokensUntilElapsedRefill()
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 0;
            DrainTokens(gate, ref sequence, START_TIME);
            gate.ClearDisconnect();

            Assert.AreEqual(EInputVerdict.RateLimited, gate.Check(Header(++sequence, START_TIME), START_TIME, out _));
            double now = START_TIME + (1.0 / Constants.Network.INPUT_SEND_RATE_MAX);
            Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(++sequence, now), now, out _));
        }

        [Test]
        public void CaptureTokenBucket_OriginalGateChanges_DoesNotMutateSnapshot()
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 0;
            DrainTokens(gate, ref sequence, START_TIME);
            PlayerInputGate.TokenBucketSnapshot snapshot = gate.CaptureTokenBucket();
            double now = START_TIME + 1.0;
            gate.Check(Header(++sequence, now), now, out _);

            Assert.IsTrue(snapshot.HasValue);
            Assert.AreEqual(0.0, snapshot.Tokens);
            Assert.AreEqual(START_TIME, snapshot.LastRefillTime);
            Assert.AreEqual(START_TIME, snapshot.LastServerTime);
            Assert.AreEqual(Constants.Player.INPUT_RATE_BURST - 1, gate.CaptureTokenBucket().Tokens);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(START_TIME - 1.0)]
        public void Check_InvalidOrReversedServerTime_UsesLastFiniteTime(double serverTime)
        {
            PlayerInputGate gate = CreateGate();
            uint sequence = 0;
            SendRateBurst(gate, ref sequence, START_TIME);
            for (int i = 0; i < Constants.Player.INPUT_RATE_BURST; ++i)
            {
                Assert.AreEqual(EInputVerdict.RateLimited, gate.Check(Header(++sequence, START_TIME), serverTime, out double requestTime));
                Assert.AreEqual(START_TIME, requestTime);
            }

            Assert.AreEqual(1, gate.RateViolationCount);
            Assert.AreEqual(START_TIME, gate.CaptureTokenBucket().LastRefillTime);
            Assert.IsFalse(gate.ShouldDisconnect);
            double now = START_TIME + (1.0 / Constants.Network.INPUT_SEND_RATE_MAX);
            Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(++sequence, now), now, out _));
        }

        [Test]
        public void Check_MismatchAtLaterTime_PreservesTimeBeforeEarlyReturn()
        {
            PlayerInputGate gate = CreateGate();
            double later = START_TIME + 1.0;
            var mismatch = new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION + 1, SESSION_EPOCH,
                PARTICIPANT_ID, 1, later);
            Assert.AreEqual(EInputVerdict.ProtocolMismatch, gate.Check(in mismatch, later, out _));
            Assert.AreEqual(later, gate.CaptureTokenBucket().LastServerTime);
            gate.ClearDisconnect();
            uint sequence = 0;
            SendRateBurst(gate, ref sequence, START_TIME);

            Assert.AreEqual(later, gate.CaptureTokenBucket().LastRefillTime);
            Assert.AreEqual(later, gate.CaptureRateViolations().GetTime(0));
            Assert.AreEqual(1, gate.RateViolationCount);
        }

        [Test]
        public void RestoreRateViolations_FutureSnapshot_ClampsAndMergesAtExplicitCurrentTime()
        {
            PlayerInputGate original = CreateGate(START_TIME + 1.0);
            uint sequence = 0;
            SendRateBurst(original, ref sequence, START_TIME + 1.0);
            SendRateBurst(original, ref sequence, START_TIME + 2.0);
            PlayerInputGate.RateViolationSnapshot snapshot = original.CaptureRateViolations();
            PlayerInputGate restored = CreateGate();
            restored.RestoreRateViolations(in snapshot, START_TIME);

            Assert.AreEqual(1, restored.RateViolationCount);
            Assert.AreEqual(START_TIME, restored.CaptureRateViolations().GetTime(0));
            Assert.IsFalse(restored.ShouldDisconnect);
        }

        [TestCase(START_TIME + 1.0)]
        [TestCase(double.NaN)]
        public void RestoreViolations_FutureOrNonFiniteWindowStart_ClampsToCurrentTime(double windowStart)
        {
            PlayerInputGate gate = CreateGate();
            gate.RestoreViolations(windowStart, Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD - 1);

            Assert.AreEqual(START_TIME, gate.ViolationWindowStart);
            Assert.AreEqual(EInputVerdict.StaleSequence, gate.Check(Header(0, START_TIME), START_TIME, out _));
            Assert.IsTrue(gate.ShouldDisconnect);
        }

        [TestCase("protocolVersion")]
        [TestCase("ratePerSecond")]
        [TestCase("burst")]
        [TestCase("violationThreshold")]
        public void Constructor_InvalidPositiveInteger_ThrowsForActualParameter(string parameter)
        {
            int protocol = Constants.Network.PROTOCOL_VERSION;
            int rate = Constants.Network.INPUT_SEND_RATE_MAX;
            int burst = Constants.Player.INPUT_RATE_BURST;
            int threshold = Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD;
            switch (parameter)
            {
                case "protocolVersion": protocol = 0; break;
                case "ratePerSecond": rate = 0; break;
                case "burst": burst = 0; break;
                case "violationThreshold": threshold = 0; break;
            }

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputGate(protocol, rate, burst,
                Constants.Network.INPUT_PAST_TOLERANCE_SECONDS, Constants.Network.INPUT_FUTURE_TOLERANCE_SECONDS,
                Constants.Player.INPUT_VIOLATION_WINDOW_SECONDS, threshold, START_TIME));
            Assert.AreEqual(parameter, exception.ParamName);
        }

        [TestCase("pastTolerance", double.NaN)]
        [TestCase("pastTolerance", double.PositiveInfinity)]
        [TestCase("pastTolerance", -1.0)]
        [TestCase("futureTolerance", double.NaN)]
        [TestCase("futureTolerance", double.NegativeInfinity)]
        [TestCase("futureTolerance", -1.0)]
        [TestCase("violationWindow", double.NaN)]
        [TestCase("violationWindow", double.PositiveInfinity)]
        [TestCase("violationWindow", 0.0)]
        [TestCase("serverTime", double.NaN)]
        [TestCase("serverTime", double.PositiveInfinity)]
        [TestCase("serverTime", -1.0)]
        public void Constructor_InvalidFiniteTimeConfiguration_ThrowsForActualParameter(string parameter, double value)
        {
            double past = Constants.Network.INPUT_PAST_TOLERANCE_SECONDS;
            double future = Constants.Network.INPUT_FUTURE_TOLERANCE_SECONDS;
            double window = Constants.Player.INPUT_VIOLATION_WINDOW_SECONDS;
            double time = START_TIME;
            switch (parameter)
            {
                case "pastTolerance": past = value; break;
                case "futureTolerance": future = value; break;
                case "violationWindow": window = value; break;
                case "serverTime": time = value; break;
            }

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInputGate(Constants.Network.PROTOCOL_VERSION,
                Constants.Network.INPUT_SEND_RATE_MAX, Constants.Player.INPUT_RATE_BURST, past, future, window,
                Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD, time));
            Assert.AreEqual(parameter, exception.ParamName);
        }

        [Test]
        public void RateViolationThreshold_FixedSnapshotCapacity_RemainsFive()
        {
            Assert.AreEqual(5, Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD,
                "임계값을 바꿀 때는 다섯 스칼라를 가진 값 스냅샷 계약도 함께 고쳐야 한다");
            Assert.DoesNotThrow(() => CreateGate());
        }

        [Test]
        public void Check_ViolationsReachThreshold_RequestsDisconnect()
        {
            PlayerInputGate gate = CreateGate();
            gate.Check(Header(100, START_TIME), START_TIME, out _);

            // 시간이 흘러 토큰은 충분하고 순번만 역행하는 입력
            for (int i = 0; i < Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD - 1; ++i)
            {
                double now = START_TIME + ((i + 1) * VIOLATION_STEP_SECONDS);
                gate.Check(Header(1, now), now, out _);
                Assert.IsFalse(gate.ShouldDisconnect);
            }

            double last = START_TIME + (Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD * VIOLATION_STEP_SECONDS);
            gate.Check(Header(1, last), last, out _);

            Assert.IsTrue(gate.ShouldDisconnect);
        }

        [Test]
        public void Check_ViolationsSpreadOverWindows_DoesNotDisconnect()
        {
            PlayerInputGate gate = CreateGate();
            gate.Check(Header(100, START_TIME), START_TIME, out _);

            // 창 하나마다 임계값보다 하나 적게 위반한다. 창 시작이 정확히 창 길이만큼 떨어지면 부동소수 오차로 경계가 흔들리므로 한 칸씩 더 띄운다
            int    perWindow = Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD - 1;
            double window    = Constants.Player.INPUT_VIOLATION_WINDOW_SECONDS;
            double step      = window / (perWindow + 1);

            for (int w = 0; w < WINDOW_COUNT; ++w)
            {
                for (int i = 0; i < perWindow; ++i)
                {
                    double now = START_TIME + (w * (window + step)) + ((i + 1) * step);
                    gate.Check(Header(1, now), now, out _);
                }
            }

            Assert.IsFalse(gate.ShouldDisconnect);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Check_NonFiniteEstimatedTime_RejectsAsInvalidTime(double estimated)
        {
            PlayerInputGate gate = CreateGate();

            Assert.AreEqual(EInputVerdict.InvalidTime, gate.Check(Header(1, estimated), START_TIME, out _));
            Assert.AreEqual(0u, gate.LastAcceptedSequence);
        }

        [Test]
        public void Check_TimeTooOld_ClampsToPastTolerance()
        {
            PlayerInputGate gate = CreateGate();

            gate.Check(Header(1, START_TIME - 1.0), START_TIME, out double requestTime);

            Assert.AreEqual(START_TIME - Constants.Network.INPUT_PAST_TOLERANCE_SECONDS, requestTime, TIME_TOLERANCE);
        }

        [Test]
        public void Check_TimeTooFarAhead_ClampsToFutureTolerance()
        {
            PlayerInputGate gate = CreateGate();

            gate.Check(Header(1, START_TIME + 1.0), START_TIME, out double requestTime);

            Assert.AreEqual(START_TIME + Constants.Network.INPUT_FUTURE_TOLERANCE_SECONDS, requestTime, TIME_TOLERANCE);
        }

        [Test]
        public void ClampToWindow_InsideWindow_KeepsClientTime()
        {
            double inside = START_TIME - INSIDE_OFFSET_SECONDS;

            double clamped = PlayerInputGate.ClampToWindow(inside, START_TIME,
                Constants.Network.INPUT_PAST_TOLERANCE_SECONDS, Constants.Network.INPUT_FUTURE_TOLERANCE_SECONDS);

            Assert.AreEqual(inside, clamped, TIME_TOLERANCE);
        }

        [Test]
        public void ToleranceConstants_ExpectedWindow_MatchValues()
        {
            Assert.AreEqual(DOCUMENTED_PAST_SECONDS, Constants.Network.INPUT_PAST_TOLERANCE_SECONDS, FLOAT_CONSTANT_TOLERANCE);
            Assert.AreEqual(DOCUMENTED_FUTURE_SECONDS, Constants.Network.INPUT_FUTURE_TOLERANCE_SECONDS, FLOAT_CONSTANT_TOLERANCE);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static PlayerInputGate CreateGate(double serverTime = START_TIME)
        {
            var gate = new PlayerInputGate(Constants.Network.PROTOCOL_VERSION, Constants.Network.INPUT_SEND_RATE_MAX,
                Constants.Player.INPUT_RATE_BURST, Constants.Network.INPUT_PAST_TOLERANCE_SECONDS,
                Constants.Network.INPUT_FUTURE_TOLERANCE_SECONDS, Constants.Player.INPUT_VIOLATION_WINDOW_SECONDS,
                Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD, serverTime);
            gate.Bind(SESSION_EPOCH, PARTICIPANT_ID);
            return gate;
        }

        private static void SendRateBurst(PlayerInputGate gate, ref uint sequence, double serverTime)
        {
            for (int i = 0; i <= Constants.Player.INPUT_RATE_BURST; ++i)
            {
                gate.Check(Header(++sequence, serverTime), serverTime, out _);
            }
        }

        private static void DrainTokens(PlayerInputGate gate, ref uint sequence, double serverTime)
        {
            for (int i = 0; i < Constants.Player.INPUT_RATE_BURST; ++i)
            {
                Assert.AreEqual(EInputVerdict.Accepted, gate.Check(Header(++sequence, serverTime), serverTime, out _));
            }
        }

        private static PlayerInputHeader Header(uint sequence, double estimatedServerTime)
        {
            return new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH, PARTICIPANT_ID, sequence, estimatedServerTime);
        }
    }
}
