// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public class DodgeRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DISTANCE  = 3f;
        private const float RADIUS    = 1f;
        private const float DURATION  = 0.25f;
        private const float TOLERANCE = 0.0001f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void GetClearDistance_BodyOnPath_StopsBeforeBody()
        {
            float clear = DodgeRules.GetClearDistance(Vector3.zero, Vector3.forward, DISTANCE, new Vector3(0f, 0f, 2f), RADIUS);

            Assert.AreEqual(1f, clear, TOLERANCE);
        }

        [Test]
        public void GetClearDistance_BodyBehind_DoesNotBlock()
        {
            float clear = DodgeRules.GetClearDistance(Vector3.zero, Vector3.forward, DISTANCE, new Vector3(0f, 0f, -0.5f), RADIUS);

            Assert.AreEqual(DISTANCE, clear, TOLERANCE);
        }

        [Test]
        public void GetClearDistance_BodyBesidePath_DoesNotBlock()
        {
            float clear = DodgeRules.GetClearDistance(Vector3.zero, Vector3.forward, DISTANCE, new Vector3(1.5f, 0f, 1f), RADIUS);

            Assert.AreEqual(DISTANCE, clear, TOLERANCE);
        }

        [Test]
        public void GetClearDistance_BodyBeyondDistance_DoesNotBlock()
        {
            float clear = DodgeRules.GetClearDistance(Vector3.zero, Vector3.forward, DISTANCE, new Vector3(0f, 0f, 5f), RADIUS);

            Assert.AreEqual(DISTANCE, clear, TOLERANCE);
        }

        [Test]
        public void GetClearDistance_AlreadyOverlappingAhead_DoesNotMove()
        {
            float clear = DodgeRules.GetClearDistance(Vector3.zero, Vector3.forward, DISTANCE, new Vector3(0f, 0f, 0.5f), RADIUS);

            Assert.AreEqual(0f, clear, TOLERANCE);
        }

        [Test]
        public void GetClearDistance_HeightDifference_UsesHorizontalOnly()
        {
            float clear = DodgeRules.GetClearDistance(Vector3.zero, Vector3.forward, DISTANCE, new Vector3(0f, 1f, 2f), RADIUS);

            Assert.AreEqual(1f, clear, TOLERANCE);
        }

        [Test]
        public void GetPosition_Halfway_Interpolates()
        {
            Vector3 end = new Vector3(0f, 0f, DISTANCE);

            Vector3 position = DodgeRules.GetPosition(Vector3.zero, end, DURATION * 0.5, DURATION);

            Assert.AreEqual(DISTANCE * 0.5f, position.z, TOLERANCE);
        }

        [Test]
        public void GetPosition_PastDuration_ReturnsEnd()
        {
            Vector3 end = new Vector3(0f, 0f, DISTANCE);

            Assert.AreEqual(end, DodgeRules.GetPosition(Vector3.zero, end, DURATION * 2.0, DURATION));
            Assert.AreEqual(Vector3.zero, DodgeRules.GetPosition(Vector3.zero, end, -1.0, DURATION));
        }
    }
}
