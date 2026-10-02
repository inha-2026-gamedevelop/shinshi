// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class FacingRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 한 칸이 약 0.0055도라 왕복 오차는 0.01도 안이다
        private const float MAX_ERROR_DEGREES = 0.01f;
        private const int   SAMPLE_COUNT      = 36;
        private const float FULL_TURN         = 360f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void EncodeDecode_RoundTrip_WithinQuantizationError()
        {
            for (int i = 0; i < SAMPLE_COUNT; ++i)
            {
                float   radians   = (FULL_TURN / SAMPLE_COUNT) * i * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

                Vector3 decoded = FacingRules.DecodeYaw(FacingRules.EncodeYaw(direction));

                Assert.Less(Vector3.Angle(direction, decoded), MAX_ERROR_DEGREES);
            }
        }

        [Test]
        public void EncodeYaw_JustBelowFullTurn_WrapsToValidValue()
        {
            Vector3 almostForward = new Vector3(-Constants.Player.DIRECTION_EPSILON_SQR, 0f, 1f).normalized;

            Vector3 decoded = FacingRules.DecodeYaw(FacingRules.EncodeYaw(almostForward));

            Assert.Less(Vector3.Angle(almostForward, decoded), MAX_ERROR_DEGREES);
        }

        [Test]
        public void TryGetHorizontalDirection_VerticalOrZero_False()
        {
            Assert.IsFalse(FacingRules.TryGetHorizontalDirection(Vector3.up, out _));
            Assert.IsFalse(FacingRules.TryGetHorizontalDirection(Vector3.zero, out _));
            Assert.IsFalse(FacingRules.TryGetHorizontalDirection(new Vector3(float.NaN, 0f, 1f), out _));
        }

        [Test]
        public void TryGetHorizontalDirection_NonzeroHeight_DropsHeightAndNormalizes()
        {
            Assert.IsTrue(FacingRules.TryGetHorizontalDirection(new Vector3(3f, 5f, 4f), out Vector3 direction));
            Assert.AreEqual(0f, direction.y);
            Assert.AreEqual(1f, direction.magnitude, MAX_ERROR_DEGREES);
        }
    }
}
