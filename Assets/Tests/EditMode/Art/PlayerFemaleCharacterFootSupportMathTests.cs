// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public sealed class PlayerFemaleCharacterFootSupportMathTests
    {
        private const float EXTRA_HEIGHT = 0.002f;
        private const float MAX_RAISE = 0.15f;
        private const float MINIMUM_GROUND_DOT = 0.6f;
        private const float VALUE_TOLERANCE = 0.000001f;

        [Test]
        public void TryGetRaise_PenetratingSupport_AddsOnlyUpwardClearance()
        {
            Assert.IsTrue(PlayerFemaleCharacterFootSupportMath.TryGetRaise(new Vector3(0f, -0.02f, 0f),
                Vector3.zero, Vector3.up, EXTRA_HEIGHT, MAX_RAISE, MINIMUM_GROUND_DOT, out float raise));
            Assert.That(raise, Is.EqualTo(0.022f).Within(VALUE_TOLERANCE));
        }

        [Test]
        public void TryGetRaise_SwingOrTouchingSupport_DoesNotLowerOrLift()
        {
            Assert.IsFalse(PlayerFemaleCharacterFootSupportMath.TryGetRaise(new Vector3(0f, 0.05f, 0f),
                Vector3.zero, Vector3.up, EXTRA_HEIGHT, MAX_RAISE, MINIMUM_GROUND_DOT, out _));
            Assert.IsFalse(PlayerFemaleCharacterFootSupportMath.TryGetRaise(Vector3.zero,
                Vector3.zero, Vector3.up, EXTRA_HEIGHT, MAX_RAISE, MINIMUM_GROUND_DOT, out _));
        }

        [Test]
        public void TryGetRaise_ClearanceExceedsLimit_RejectsInsteadOfClamping()
        {
            Assert.IsFalse(PlayerFemaleCharacterFootSupportMath.TryGetRaise(new Vector3(0f, -MAX_RAISE, 0f),
                Vector3.zero, Vector3.up, EXTRA_HEIGHT, MAX_RAISE, MINIMUM_GROUND_DOT, out _));
        }

        [Test]
        public void TryGetRaise_SlopeOrNonfiniteInput_Rejects()
        {
            Assert.IsFalse(PlayerFemaleCharacterFootSupportMath.TryGetRaise(new Vector3(0f, -0.02f, 0f),
                Vector3.zero, Vector3.right, EXTRA_HEIGHT, MAX_RAISE, MINIMUM_GROUND_DOT, out _));
            Assert.IsFalse(PlayerFemaleCharacterFootSupportMath.TryGetRaise(new Vector3(float.NaN, -0.02f, 0f),
                Vector3.zero, Vector3.up, EXTRA_HEIGHT, MAX_RAISE, MINIMUM_GROUND_DOT, out _));
        }

        [Test]
        public void TryMapGoal_NonUnitAuthoringRotation_Rejects()
        {
            var mapping = new PlayerFemaleCharacterFootSupportProfile.GoalMapping(Vector3.zero, new Quaternion(0f, 0f, 0f, 2f));
            Assert.IsFalse(PlayerFemaleCharacterFootSupportMath.TryMapGoal(Vector3.zero, Quaternion.identity,
                in mapping, out _, out _));
        }

        [Test]
        public void TryMapGoal_RotatedRigMapping_ReconstructsOriginalFkPose()
        {
            var mapping = new PlayerFemaleCharacterFootSupportProfile.GoalMapping(new Vector3(0.01f, 0.02f, -0.03f),
                Quaternion.Euler(10f, 30f, 70f));
            Vector3 footPosition = new Vector3(1f, 0.5f, -2f);
            Quaternion footRotation = Quaternion.Euler(20f, -40f, 15f);
            Assert.IsTrue(PlayerFemaleCharacterFootSupportMath.TryMapGoal(footPosition, footRotation,
                in mapping, out Vector3 goalPosition, out Quaternion goalRotation));
            Assert.That(Vector3.Distance(goalPosition + goalRotation * mapping.GoalLocalBoneOffset, footPosition),
                Is.LessThan(VALUE_TOLERANCE));
            Assert.That(Quaternion.Angle(goalRotation * mapping.GoalLocalBoneRotation, footRotation), Is.LessThan(0.1f));
        }
    }
}
