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

        // 궁수 기본 공격 화살: 폭 0.3m의 반폭, 24m/s로 30Hz 한 틱에 나아가는 거리
        private const float ARROW_HALF_WIDTH = 0.15f;
        private const float ARROW_STEP       = 0.8f;
        private const float BODY_RADIUS      = 0.4f;

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
        public void TryHitBodyOnSegment_CenterOutsideWidthButBodyTouches_HitsAtBodyEdge()
        {
            // 궁수 화살 반폭 0.15m로 중심점만 보면 빗나가는 위치다
            var  target = new Vector3(ARROW_HALF_WIDTH + (BODY_RADIUS * 0.5f), 0f, DISTANCE);
            bool hit    = BasicAttackRules.TryHitBodyOnSegment(Vector3.zero, Vector3.forward, target, 0f, RAY_RANGE,
                ARROW_HALF_WIDTH, BODY_RADIUS, out float entry);

            float reach    = ARROW_HALF_WIDTH + BODY_RADIUS;
            float expected = DISTANCE - Mathf.Sqrt((reach * reach) - (target.x * target.x));
            Assert.IsFalse(BasicAttackRules.TryProjectOnRay(Vector3.zero, Vector3.forward, target, RAY_RANGE, ARROW_HALF_WIDTH, out _));
            Assert.IsTrue(hit);
            Assert.AreEqual(expected, entry, TOLERANCE);
        }

        [Test]
        public void TryHitBodyOnSegment_BossBodyFarFromCenter_Hits()
        {
            var target = new Vector3(Constants.Combat.BOSS_BODY_RADIUS, 0f, DISTANCE * 2f);

            Assert.IsTrue(BasicAttackRules.TryHitBodyOnSegment(Vector3.zero, Vector3.forward, target, 0f, RAY_RANGE,
                ARROW_HALF_WIDTH, Constants.Combat.BOSS_BODY_RADIUS, out _));
        }

        [Test]
        public void TryHitBodyOnSegment_BodyBeyondReach_False()
        {
            var target = new Vector3(ARROW_HALF_WIDTH + BODY_RADIUS + 0.01f, 0f, DISTANCE);

            Assert.IsFalse(BasicAttackRules.TryHitBodyOnSegment(Vector3.zero, Vector3.forward, target, 0f, RAY_RANGE,
                ARROW_HALF_WIDTH, BODY_RADIUS, out _));
        }

        [Test]
        public void TryHitBodyOnSegment_BodyStartsAfterSegmentEnd_False()
        {
            float end    = DISTANCE;
            var   target = new Vector3(0f, 0f, end + ARROW_HALF_WIDTH + BODY_RADIUS + 0.01f);

            Assert.IsFalse(BasicAttackRules.TryHitBodyOnSegment(Vector3.zero, Vector3.forward, target, 0f, end,
                ARROW_HALF_WIDTH, BODY_RADIUS, out _));
        }

        [Test]
        public void TryHitBodyOnSegment_BodyOverlapsStart_EntryIsStart()
        {
            var  target = new Vector3(0f, 0f, -BODY_RADIUS * 0.5f);
            bool hit    = BasicAttackRules.TryHitBodyOnSegment(Vector3.zero, Vector3.forward, target, 0f, RAY_RANGE,
                ARROW_HALF_WIDTH, BODY_RADIUS, out float entry);

            Assert.IsTrue(hit);
            Assert.AreEqual(0f, entry, TOLERANCE);
        }

        [Test]
        public void TryHitBodyOnSegment_EnemyPassedArrowTipBetweenTicks_HitOnlyWithLookback()
        {
            // 화살 쪽으로 달려온 적이 한 틱 사이 화살 끝을 넘어 몸체까지 뒤로 빠진 경우다
            float tick     = 1f / Constants.Network.SERVER_TICK_RATE;
            float lookback = Constants.Combat.ARROW_TARGET_MAX_APPROACH_SPEED * tick;
            float tipGap   = ARROW_HALF_WIDTH + BODY_RADIUS;
            var   target   = new Vector3(0f, 0f, -(tipGap + (lookback * 0.5f)));

            Assert.IsFalse(BasicAttackRules.TryHitBodyOnSegment(Vector3.zero, Vector3.forward, target, 0f, ARROW_STEP,
                ARROW_HALF_WIDTH, BODY_RADIUS, out _));
            Assert.IsTrue(BasicAttackRules.TryHitBodyOnSegment(Vector3.zero, Vector3.forward, target, -lookback, ARROW_STEP,
                ARROW_HALF_WIDTH, BODY_RADIUS, out float entry));
            Assert.AreEqual(-lookback, entry, TOLERANCE);
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
