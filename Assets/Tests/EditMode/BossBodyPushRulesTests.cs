// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    // 내비 메시 벽 검사(Resolve)는 씬이 필요해 여기서는 겹침 판정과 밀 방향, 거리만 본다
    public class BossBodyPushRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float PUSH_RADIUS   = 1f;
        private const float PLAYER_RADIUS = 0.5f;
        private const float BLOCK         = 1.5f;
        private const float TOLERANCE     = 0.002f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void GetBlockDistance_AddsPlayerRadius()
        {
            Assert.AreEqual(BLOCK, BossBodyPushRules.GetBlockDistance(PUSH_RADIUS, PLAYER_RADIUS), TOLERANCE);
        }

        [Test]
        public void TryGetPushOut_Inside_PushesRadiallyToEdge()
        {
            Assert.IsTrue(BossBodyPushRules.TryGetPushOut(new Vector3(0.6f, 3f, 0.8f), Vector3.zero, BLOCK,
                out Vector3 direction, out float distance));

            Assert.AreEqual(0.6f, direction.x, TOLERANCE);
            Assert.AreEqual(0f, direction.y, TOLERANCE);
            Assert.AreEqual(0.8f, direction.z, TOLERANCE);
            Assert.AreEqual(0.5f, distance, TOLERANCE);
        }

        [Test]
        public void TryGetPushOut_OnEdgeOrOutside_DoesNotPush()
        {
            Assert.IsFalse(BossBodyPushRules.TryGetPushOut(new Vector3(BLOCK, 0f, 0f), Vector3.zero, BLOCK, out _, out _));
            Assert.IsFalse(BossBodyPushRules.TryGetPushOut(new Vector3(0f, 0f, 3f), Vector3.zero, BLOCK, out _, out _));
        }

        [Test]
        public void TryGetPushOut_AtCenter_PushesForward()
        {
            Assert.IsTrue(BossBodyPushRules.TryGetPushOut(new Vector3(2f, 0f, 2f), new Vector3(2f, 5f, 2f), BLOCK,
                out Vector3 direction, out float distance));

            Assert.AreEqual(Vector3.forward, direction);
            Assert.AreEqual(BLOCK, distance, TOLERANCE);
        }

        [Test]
        public void TryGetPushOut_ZeroBlockDistance_DoesNotPush()
        {
            Assert.IsFalse(BossBodyPushRules.TryGetPushOut(Vector3.zero, Vector3.zero, 0f, out _, out _));
        }
    }
}
