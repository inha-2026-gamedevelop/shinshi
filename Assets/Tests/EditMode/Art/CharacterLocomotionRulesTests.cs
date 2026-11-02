// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class CharacterLocomotionRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float REFERENCE_SPEED = 4f;
        private const float MAX_SCALE       = 1.5f;
        private const float SPEED_RATIO     = 1.25f;
        private const float FAR_RATIO       = 10f;
        private const float TURN_SPEED      = 90f;
        private const float FRAME_SECONDS   = 0.5f;
        private const float SHORT_FRAME     = 0.05f;
        private const float HALF_TURN_ANGLE = 45f;
        private const float RIGHT_ANGLE     = 90f;
        private const float MAX_RAISE       = 0.35f;
        private const float MAX_DROP        = 0.3f;
        private const float STEP_HEIGHT     = 0.2f;
        private const float SMALL_HEIGHT    = 0.1f;
        private const float RAIL_HEIGHT     = 0.45f;
        private const float FAR_HEIGHT      = 5f;
        private const float SHARPNESS       = 10f;
        private const float TOLERANCE       = 0.0001f;
        private const float ANGLE_TOLERANCE = 0.01f;

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(0f)]
        [TestCase(REFERENCE_SPEED * 0.5f)]
        [TestCase(REFERENCE_SPEED)]
        public void ComputePlaybackScale_AtOrBelowReference_KeepsNormalSpeed(float speed)
        {
            Assert.AreEqual(1f, CharacterLocomotionRules.ComputePlaybackScale(speed, REFERENCE_SPEED, MAX_SCALE));
        }

        [Test]
        public void ComputePlaybackScale_AboveReference_ScalesBySpeedRatio()
        {
            float scale = CharacterLocomotionRules.ComputePlaybackScale(REFERENCE_SPEED * SPEED_RATIO, REFERENCE_SPEED, MAX_SCALE);
            Assert.AreEqual(SPEED_RATIO, scale, TOLERANCE);
        }

        [Test]
        public void ComputePlaybackScale_FarAboveReference_ClampsToMaximum()
        {
            float scale = CharacterLocomotionRules.ComputePlaybackScale(REFERENCE_SPEED * FAR_RATIO, REFERENCE_SPEED, MAX_SCALE);
            Assert.AreEqual(MAX_SCALE, scale);
        }

        [TestCase(float.NaN, REFERENCE_SPEED, MAX_SCALE)]
        [TestCase(float.PositiveInfinity, REFERENCE_SPEED, MAX_SCALE)]
        [TestCase(-1f, REFERENCE_SPEED, MAX_SCALE)]
        [TestCase(REFERENCE_SPEED * 2f, 0f, MAX_SCALE)]
        [TestCase(REFERENCE_SPEED * 2f, -1f, MAX_SCALE)]
        [TestCase(REFERENCE_SPEED * 2f, float.NaN, MAX_SCALE)]
        [TestCase(REFERENCE_SPEED * 2f, REFERENCE_SPEED, float.NaN)]
        [TestCase(REFERENCE_SPEED * 2f, REFERENCE_SPEED, 0.5f)]
        public void ComputePlaybackScale_InvalidInput_ReturnsNormalSpeed(float speed, float reference, float maximum)
        {
            Assert.AreEqual(1f, CharacterLocomotionRules.ComputePlaybackScale(speed, reference, maximum));
        }

        [Test]
        public void TryGetHorizontal_TiltedDirection_DropsHeightAndNormalizes()
        {
            Assert.IsTrue(CharacterLocomotionRules.TryGetHorizontal(new Vector3(3f, 5f, 4f), out Vector3 horizontal));
            Assert.AreEqual(0f, horizontal.y);
            Assert.AreEqual(1f, horizontal.magnitude, TOLERANCE);
            Assert.AreEqual(0.6f, horizontal.x, TOLERANCE);
        }

        [Test]
        public void TryGetHorizontal_VerticalOrTinyDirection_ReturnsFalse()
        {
            Assert.IsFalse(CharacterLocomotionRules.TryGetHorizontal(Vector3.up, out Vector3 vertical));
            Assert.AreEqual(Vector3.zero, vertical);
            Assert.IsFalse(CharacterLocomotionRules.TryGetHorizontal(new Vector3(0.001f, 0f, 0.001f), out _));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void TryGetHorizontal_NonFiniteDirection_ReturnsFalse(float value)
        {
            Assert.IsFalse(CharacterLocomotionRules.TryGetHorizontal(new Vector3(value, 0f, 1f), out Vector3 horizontal));
            Assert.AreEqual(Vector3.zero, horizontal);
        }

        [Test]
        public void TurnTowards_LimitedSpeed_TurnsOnlyAllowedAngle()
        {
            Quaternion result = CharacterLocomotionRules.TurnTowards(Quaternion.identity, Vector3.right, TURN_SPEED, FRAME_SECONDS);
            Assert.AreEqual(HALF_TURN_ANGLE, Quaternion.Angle(Quaternion.identity, result), ANGLE_TOLERANCE);
        }

        [Test]
        public void TurnTowards_EnoughTime_ReachesTargetWithoutOvershoot()
        {
            Quaternion result = CharacterLocomotionRules.TurnTowards(Quaternion.identity, Vector3.right, TURN_SPEED,
                FRAME_SECONDS * 4f);
            Assert.AreEqual(RIGHT_ANGLE, Quaternion.Angle(Quaternion.identity, result), ANGLE_TOLERANCE);
            Assert.Less(Vector3.Distance(Vector3.right, result * Vector3.forward), TOLERANCE);
        }

        [TestCase(0f, FRAME_SECONDS)]
        [TestCase(TURN_SPEED, 0f)]
        [TestCase(float.NaN, FRAME_SECONDS)]
        public void TurnTowards_NoTurnLimit_SnapsToTarget(float degreesPerSecond, float deltaSeconds)
        {
            Quaternion result = CharacterLocomotionRules.TurnTowards(Quaternion.identity, Vector3.right, degreesPerSecond,
                deltaSeconds);
            Assert.Less(Vector3.Distance(Vector3.right, result * Vector3.forward), TOLERANCE);
        }

        [Test]
        public void TurnTowards_NoHorizontalTarget_KeepsCurrent()
        {
            Quaternion current = Quaternion.Euler(0f, RIGHT_ANGLE, 0f);
            Assert.AreEqual(current, CharacterLocomotionRules.TurnTowards(current, Vector3.up, TURN_SPEED, FRAME_SECONDS));
            Assert.AreEqual(current, CharacterLocomotionRules.TurnTowards(current, Vector3.zero, TURN_SPEED, FRAME_SECONDS));
        }

        [Test]
        public void ComputeFootOffset_StepWithinLimits_ReturnsHeightDifference()
        {
            Assert.AreEqual(STEP_HEIGHT, CharacterLocomotionRules.ComputeFootOffset(1f + STEP_HEIGHT, 1f, MAX_RAISE, MAX_DROP),
                TOLERANCE);
            Assert.AreEqual(-STEP_HEIGHT, CharacterLocomotionRules.ComputeFootOffset(1f - STEP_HEIGHT, 1f, MAX_RAISE, MAX_DROP),
                TOLERANCE);
            Assert.AreEqual(0f, CharacterLocomotionRules.ComputeFootOffset(1f, 1f, MAX_RAISE, MAX_DROP));
        }

        [Test]
        public void ComputeFootOffset_BeyondLimits_ClampsToRaiseAndDrop()
        {
            Assert.AreEqual(MAX_RAISE, CharacterLocomotionRules.ComputeFootOffset(FAR_HEIGHT, 0f, MAX_RAISE, MAX_DROP));
            Assert.AreEqual(-MAX_DROP, CharacterLocomotionRules.ComputeFootOffset(-FAR_HEIGHT, 0f, MAX_RAISE, MAX_DROP));
        }

        [Test]
        public void ComputeFootOffset_NegativeLimits_UsesMagnitude()
        {
            Assert.AreEqual(-MAX_DROP, CharacterLocomotionRules.ComputeFootOffset(-FAR_HEIGHT, 0f, -MAX_RAISE, -MAX_DROP));
            Assert.AreEqual(MAX_RAISE, CharacterLocomotionRules.ComputeFootOffset(FAR_HEIGHT, 0f, -MAX_RAISE, -MAX_DROP));
        }

        [TestCase(float.NaN, 0f)]
        [TestCase(0f, float.NaN)]
        [TestCase(float.PositiveInfinity, 0f)]
        public void ComputeFootOffset_NonFiniteHeight_ReturnsZero(float ground, float root)
        {
            Assert.AreEqual(0f, CharacterLocomotionRules.ComputeFootOffset(ground, root, MAX_RAISE, MAX_DROP));
        }

        [Test]
        public void IsReachableGround_StepAndLowerFloor_Accepted()
        {
            Assert.IsTrue(CharacterLocomotionRules.IsReachableGround(STEP_HEIGHT, 0f, MAX_RAISE));
            Assert.IsTrue(CharacterLocomotionRules.IsReachableGround(MAX_RAISE, 0f, MAX_RAISE));
            Assert.IsTrue(CharacterLocomotionRules.IsReachableGround(-FAR_HEIGHT, 0f, MAX_RAISE));
        }

        [Test]
        public void IsReachableGround_RailTopAboveRaiseLimit_Rejected()
        {
            Assert.IsFalse(CharacterLocomotionRules.IsReachableGround(RAIL_HEIGHT, 0f, MAX_RAISE));
            Assert.IsFalse(CharacterLocomotionRules.IsReachableGround(float.NaN, 0f, MAX_RAISE));
        }

        [Test]
        public void ComputePelvisOffset_OneFootLower_DropsToLowerFoot()
        {
            Assert.AreEqual(-STEP_HEIGHT, CharacterLocomotionRules.ComputePelvisOffset(SMALL_HEIGHT, -STEP_HEIGHT), TOLERANCE);
            Assert.AreEqual(-STEP_HEIGHT, CharacterLocomotionRules.ComputePelvisOffset(-STEP_HEIGHT, -SMALL_HEIGHT), TOLERANCE);
        }

        [Test]
        public void ComputePelvisOffset_BothFeetRaised_NeverRaisesPelvis()
        {
            Assert.AreEqual(0f, CharacterLocomotionRules.ComputePelvisOffset(STEP_HEIGHT, SMALL_HEIGHT));
            Assert.AreEqual(0f, CharacterLocomotionRules.ComputePelvisOffset(0f, 0f));
        }

        [Test]
        public void ComputePelvisOffset_NonFiniteFoot_IgnoresThatFoot()
        {
            Assert.AreEqual(-STEP_HEIGHT, CharacterLocomotionRules.ComputePelvisOffset(float.NaN, -STEP_HEIGHT), TOLERANCE);
            Assert.AreEqual(0f, CharacterLocomotionRules.ComputePelvisOffset(float.NegativeInfinity, float.NaN));
        }

        [Test]
        public void Smooth_PositiveStep_MovesPartwayWithoutOvershoot()
        {
            float result = CharacterLocomotionRules.Smooth(0f, 1f, SHARPNESS, SHORT_FRAME);
            Assert.AreEqual(1f - Mathf.Exp(-SHARPNESS * SHORT_FRAME), result, TOLERANCE);
            Assert.Greater(result, 0f);
            Assert.Less(result, 1f);
        }

        [Test]
        public void Smooth_TwoHalfSteps_MatchOneFullStep()
        {
            float half = CharacterLocomotionRules.Smooth(0f, 1f, SHARPNESS, SHORT_FRAME * 0.5f);
            float twice = CharacterLocomotionRules.Smooth(half, 1f, SHARPNESS, SHORT_FRAME * 0.5f);
            Assert.AreEqual(CharacterLocomotionRules.Smooth(0f, 1f, SHARPNESS, SHORT_FRAME), twice, TOLERANCE);
        }

        [TestCase(0f, FRAME_SECONDS)]
        [TestCase(SHARPNESS, 0f)]
        [TestCase(float.PositiveInfinity, FRAME_SECONDS)]
        public void Smooth_NoDamping_SnapsToTarget(float sharpness, float deltaSeconds)
        {
            Assert.AreEqual(1f, CharacterLocomotionRules.Smooth(0f, 1f, sharpness, deltaSeconds));
        }

        [Test]
        public void Smooth_NonFiniteValues_StayFinite()
        {
            Assert.AreEqual(0.5f, CharacterLocomotionRules.Smooth(0.5f, float.NaN, SHARPNESS, FRAME_SECONDS));
            Assert.AreEqual(0f, CharacterLocomotionRules.Smooth(float.NaN, float.NaN, SHARPNESS, FRAME_SECONDS));
            Assert.AreEqual(1f, CharacterLocomotionRules.Smooth(float.NaN, 1f, SHARPNESS, FRAME_SECONDS));
        }
    }
}
