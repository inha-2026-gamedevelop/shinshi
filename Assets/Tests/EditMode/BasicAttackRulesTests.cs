// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class BasicAttackRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 무사 기본 공격 초안: 전방 3m, 90도
        private const float RANGE           = 3f;
        private const float ARC             = 90f;
        private const float FULL_CIRCLE     = 360f;
        private const float DISTANCE        = 2f;
        private const float INSIDE_DEGREES  = 44f;
        private const float OUTSIDE_DEGREES = 46f;
        private const float RAY_RANGE       = 9f;
        private const float HALF_WIDTH      = 0.5f;
        private const float TOLERANCE       = 0.0001f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void IsInArc_InsideHalfAngle_True()
        {
            Assert.IsTrue(BasicAttackRules.IsInArc(Vector3.zero, Vector3.forward, AtAngle(INSIDE_DEGREES, DISTANCE), RANGE, ARC));
        }

        [Test]
        public void IsInArc_OutsideHalfAngle_False()
        {
            Assert.IsFalse(BasicAttackRules.IsInArc(Vector3.zero, Vector3.forward, AtAngle(OUTSIDE_DEGREES, DISTANCE), RANGE, ARC));
        }

        [Test]
        public void IsInArc_BeyondRange_False()
        {
            Assert.IsFalse(BasicAttackRules.IsInArc(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, RANGE + 0.1f), RANGE, ARC));
        }

        [Test]
        public void IsInArc_FullCircleBehind_True()
        {
            Assert.IsTrue(BasicAttackRules.IsInArc(Vector3.zero, Vector3.forward, Vector3.back * DISTANCE, RANGE, FULL_CIRCLE));
        }

        [Test]
        public void IsInArc_HeightDifference_IgnoresHeight()
        {
            Assert.IsTrue(BasicAttackRules.IsInArc(Vector3.zero, Vector3.forward, new Vector3(0f, RANGE * 10f, DISTANCE), RANGE, ARC));
        }

        [Test]
        public void TryProjectOnRay_InsideWidth_ReturnsAlongDistance()
        {
            bool hit = BasicAttackRules.TryProjectOnRay(Vector3.zero, Vector3.forward, new Vector3(HALF_WIDTH * 0.5f, 0f, DISTANCE),
                RAY_RANGE, HALF_WIDTH, out float along);

            Assert.IsTrue(hit);
            Assert.AreEqual(DISTANCE, along, TOLERANCE);
        }

        [Test]
        public void TryProjectOnRay_OutsideWidthOrBehind_False()
        {
            Assert.IsFalse(BasicAttackRules.TryProjectOnRay(Vector3.zero, Vector3.forward, new Vector3(HALF_WIDTH * 2f, 0f, DISTANCE),
                RAY_RANGE, HALF_WIDTH, out _));
            Assert.IsFalse(BasicAttackRules.TryProjectOnRay(Vector3.zero, Vector3.forward, Vector3.back * DISTANCE,
                RAY_RANGE, HALF_WIDTH, out _));
        }

        [Test]
        public void IsWithinHeight_AtLimitTrue_BeyondLimitFalse()
        {
            float limit = Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE;
            var   at    = new Vector3(0f, limit, DISTANCE);
            var   above = new Vector3(0f, limit + DISTANCE, DISTANCE);
            var   below = new Vector3(0f, -(limit + DISTANCE), DISTANCE);

            Assert.IsTrue(BasicAttackRules.IsWithinHeight(Vector3.zero, at, limit));
            Assert.IsFalse(BasicAttackRules.IsWithinHeight(Vector3.zero, above, limit));
            Assert.IsFalse(BasicAttackRules.IsWithinHeight(Vector3.zero, below, limit));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static Vector3 AtAngle(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }
    }
}
