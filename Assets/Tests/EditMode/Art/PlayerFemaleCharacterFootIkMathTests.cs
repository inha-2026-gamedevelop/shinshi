// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class PlayerFemaleCharacterFootIkMathTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float SOLE_HEIGHT       = 0.07f;
        private const float CONTACT_RANGE     = 0.08f;
        private const float MAX_RAISE         = 0.15f;
        private const float MAX_DROP          = 0.08f;
        private const float MIN_GROUND_DOT    = 0.6f;
        private const float RUN_PENETRATION   = 0.025773f;
        private const float TOLERANCE         = 0.000001f;
        private const float SLOPE_Z           = 0.5f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryGetTarget_RunSoleBelowFloor_RaisesFootByPenetrationWithoutChangingHorizontalPosition()
        {
            Vector3 animatedPosition = new Vector3(1f, SOLE_HEIGHT - RUN_PENETRATION, 2f);
            Vector3 floorPoint = new Vector3(animatedPosition.x, 0f, animatedPosition.z);
            Assert.IsTrue(GetTarget(animatedPosition, floorPoint, Vector3.up, out Vector3 position,
                out Quaternion rotation, out float weight));

            Assert.AreEqual(SOLE_HEIGHT, position.y, TOLERANCE);
            Assert.AreEqual(animatedPosition.x, position.x);
            Assert.AreEqual(animatedPosition.z, position.z);
            Assert.AreEqual(1f, weight);
            Assert.AreEqual(Quaternion.identity, rotation);
        }

        [Test]
        public void TryGetTarget_LiftedSwingFoot_DoesNotPinToGround()
        {
            Assert.IsFalse(GetTarget(Vector3.up * (SOLE_HEIGHT + CONTACT_RANGE), Vector3.zero,
                Vector3.up, out _, out _, out float weight));
            Assert.AreEqual(0f, weight);
        }

        [Test]
        public void TryGetTarget_HalfContactRange_TapersContactWeight()
        {
            Assert.IsTrue(GetTarget(Vector3.up * (SOLE_HEIGHT + CONTACT_RANGE / 2f), Vector3.zero,
                Vector3.up, out _, out _, out float weight));
            Assert.AreEqual(0.5f, weight, TOLERANCE);
        }

        [Test]
        public void TryGetTarget_DeepPenetrationBeyondRaiseLimit_RejectsTarget()
        {
            Assert.IsFalse(GetTarget(Vector3.up * (SOLE_HEIGHT - MAX_RAISE - CONTACT_RANGE), Vector3.zero,
                Vector3.up, out _, out _, out _));
        }

        [Test]
        public void TryGetTarget_WallNormal_RejectsTarget()
        {
            Assert.IsFalse(GetTarget(Vector3.up * SOLE_HEIGHT, Vector3.zero, Vector3.right, out _, out _, out _));
        }

        [Test]
        public void TryGetTarget_ZeroNormal_RejectsTarget()
        {
            Assert.IsFalse(GetTarget(Vector3.up * SOLE_HEIGHT, Vector3.zero, Vector3.zero, out _, out _, out _));
        }

        [Test]
        public void TryGetTarget_ValidSlope_AlignsFootUpWithNormal()
        {
            Vector3 normal = new Vector3(0f, 1f, SLOPE_Z).normalized;
            Assert.IsTrue(GetTarget(Vector3.up * SOLE_HEIGHT, Vector3.zero, normal,
                out _, out Quaternion rotation, out _));
            Assert.Less(Vector3.Distance(rotation * Vector3.up, normal), TOLERANCE);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void TryGetTarget_NonFiniteFloor_RejectsTarget(float floorHeight)
        {
            Assert.IsFalse(GetTarget(Vector3.up * SOLE_HEIGHT, Vector3.up * floorHeight,
                Vector3.up, out _, out _, out _));
        }

        [Test]
        public void TryGetTarget_InvalidContactRange_RejectsTarget()
        {
            Assert.IsFalse(PlayerFemaleCharacterFootIkMath.TryGetTarget(Vector3.up * SOLE_HEIGHT,
                Quaternion.identity, Vector3.zero, Vector3.up, Vector3.up, SOLE_HEIGHT, 0f,
                MAX_RAISE, MAX_DROP, MIN_GROUND_DOT, out _, out _, out _));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static bool GetTarget(Vector3 position, Vector3 ground, Vector3 normal,
            out Vector3 target, out Quaternion rotation, out float weight)
        {
            return PlayerFemaleCharacterFootIkMath.TryGetTarget(position, Quaternion.identity, ground, normal,
                Vector3.up, SOLE_HEIGHT, CONTACT_RANGE, MAX_RAISE, MAX_DROP, MIN_GROUND_DOT,
                out target, out rotation, out weight);
        }
    }
}
