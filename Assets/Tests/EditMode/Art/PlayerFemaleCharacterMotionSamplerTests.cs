// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class PlayerFemaleCharacterMotionSamplerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  START_DISTANCE = 10f;
        private const float  MOVE_DISTANCE  = 0.5f;
        private const float  EXPECTED_SPEED = 5f;
        private const float  TOLERANCE      = 0.0001f;
        private const double START_TIME     = 20.0;
        private const double FRAME_SECONDS  = 0.1;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryRead_FirstSample_ReturnsUnavailableInsteadOfStopped()
        {
            var sampler = new PlayerFemaleCharacterMotionSampler();
            Assert.IsFalse(sampler.TryRead(true, START_DISTANCE, START_TIME, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_UnchangedDistance_ReturnsAvailableStoppedSample()
        {
            var sampler = CreateBaseline();
            Assert.IsTrue(sampler.TryRead(true, START_DISTANCE, START_TIME + FRAME_SECONDS, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_DistanceInUnscaledInterval_ReturnsMetersPerUnscaledSecond()
        {
            var sampler = CreateBaseline();
            Assert.IsTrue(sampler.TryRead(true, START_DISTANCE + MOVE_DISTANCE, START_TIME + FRAME_SECONDS, out float speed));
            Assert.AreEqual(EXPECTED_SPEED, speed, TOLERANCE);
        }

        [Test]
        public void TryRead_UnavailableRoleThenResume_DiscardsMovementDuringGap()
        {
            var sampler = CreateBaseline();
            Assert.IsFalse(sampler.TryRead(false, START_DISTANCE, START_TIME + FRAME_SECONDS, out _));
            Assert.IsFalse(sampler.TryRead(true, START_DISTANCE + EXPECTED_SPEED, START_TIME + EXPECTED_SPEED, out _));
            Assert.IsTrue(sampler.TryRead(true, START_DISTANCE + EXPECTED_SPEED + MOVE_DISTANCE,
                START_TIME + EXPECTED_SPEED + FRAME_SECONDS, out float speed));
            Assert.AreEqual(EXPECTED_SPEED, speed, TOLERANCE);
        }

        [Test]
        public void TryRead_CounterDecreases_RebaselinesInsteadOfNegativeSpeed()
        {
            var sampler = CreateBaseline();
            Assert.IsFalse(sampler.TryRead(true, 0f, START_TIME + FRAME_SECONDS, out _));
            Assert.IsTrue(sampler.TryRead(true, MOVE_DISTANCE, START_TIME + FRAME_SECONDS + FRAME_SECONDS, out float speed));
            Assert.AreEqual(EXPECTED_SPEED, speed, TOLERANCE);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void TryRead_InvalidDistance_ResetsBaseline(float invalidDistance)
        {
            var sampler = CreateBaseline();
            Assert.IsFalse(sampler.TryRead(true, invalidDistance, START_TIME + FRAME_SECONDS, out _));
            Assert.IsFalse(sampler.TryRead(true, START_DISTANCE + MOVE_DISTANCE, START_TIME + FRAME_SECONDS + FRAME_SECONDS, out _));
        }

        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void TryRead_InvalidClock_ResetsBaseline(double invalidTime)
        {
            var sampler = CreateBaseline();
            Assert.IsFalse(sampler.TryRead(true, START_DISTANCE + MOVE_DISTANCE, invalidTime, out _));
            Assert.IsFalse(sampler.TryRead(true, START_DISTANCE + MOVE_DISTANCE, START_TIME + FRAME_SECONDS, out _));
        }

        [TestCase(START_TIME)]
        [TestCase(START_TIME - FRAME_SECONDS)]
        public void TryRead_NonIncreasingClock_DiscardsAmbiguousInterval(double nextTime)
        {
            var sampler = CreateBaseline();
            Assert.IsFalse(sampler.TryRead(true, START_DISTANCE + MOVE_DISTANCE, nextTime, out _));
            Assert.IsTrue(sampler.TryRead(true, START_DISTANCE + MOVE_DISTANCE + MOVE_DISTANCE,
                nextTime + FRAME_SECONDS, out float speed));
            Assert.AreEqual(EXPECTED_SPEED, speed, TOLERANCE);
        }

        [Test]
        public void TryRead_DivisionOverflow_ReturnsUnavailableAndRequiresNewBaseline()
        {
            var sampler = new PlayerFemaleCharacterMotionSampler();
            sampler.TryRead(true, 0f, 0.0, out _);
            Assert.IsFalse(sampler.TryRead(true, float.MaxValue, double.Epsilon, out float speed));
            Assert.AreEqual(0f, speed);
            Assert.IsFalse(sampler.TryRead(true, float.MaxValue, FRAME_SECONDS, out _));
        }

        [Test]
        public void ResetMotionSample_AfterGap_RequiresFreshBaseline()
        {
            var sampler = CreateBaseline();
            sampler.ResetMotionSample();
            Assert.IsFalse(sampler.TryRead(true, START_DISTANCE + EXPECTED_SPEED, START_TIME + EXPECTED_SPEED, out _));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static PlayerFemaleCharacterMotionSampler CreateBaseline()
        {
            var sampler = new PlayerFemaleCharacterMotionSampler();
            sampler.TryRead(true, START_DISTANCE, START_TIME, out _);
            return sampler;
        }
    }
}
