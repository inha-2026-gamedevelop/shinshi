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

        // 토큰이 충분히 차는 위반 간격, 부동소수 오차로 토큰이 1에 못 미치지 않도록 한 칸 반을 기다린다
        private const double VIOLATION_STEP_SECONDS = 0.1;
        private const double REFILL_MARGIN          = 1.5;
        private const int    WINDOW_COUNT           = 3;

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
            Assert.AreEqual(1, gate.ViolationCount);
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

            Assert.AreEqual(1, gate.ViolationCount);
            Assert.IsFalse(gate.ShouldDisconnect);
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

            double oneTokenLater = START_TIME + (REFILL_MARGIN / Constants.Network.INPUT_SEND_RATE_MAX);
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

        [Test]
        public void Check_NaNTime_RejectsAsInvalidTime()
        {
            PlayerInputGate gate = CreateGate();

            Assert.AreEqual(EInputVerdict.InvalidTime, gate.Check(Header(1, double.NaN), START_TIME, out _));
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

        private static PlayerInputGate CreateGate()
        {
            var gate = new PlayerInputGate(Constants.Network.PROTOCOL_VERSION, Constants.Network.INPUT_SEND_RATE_MAX,
                Constants.Player.INPUT_RATE_BURST, Constants.Network.INPUT_PAST_TOLERANCE_SECONDS,
                Constants.Network.INPUT_FUTURE_TOLERANCE_SECONDS, Constants.Player.INPUT_VIOLATION_WINDOW_SECONDS,
                Constants.Player.INPUT_VIOLATION_DISCONNECT_THRESHOLD, START_TIME);
            gate.Bind(SESSION_EPOCH, PARTICIPANT_ID);
            return gate;
        }

        private static PlayerInputHeader Header(uint sequence, double estimatedServerTime)
        {
            return new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH, PARTICIPANT_ID, sequence, estimatedServerTime);
        }
    }
}
