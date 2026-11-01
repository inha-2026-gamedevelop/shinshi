// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    // 무너진 전장 가두기와 밖 판정의 계산만 본다. 서버 틱 순서와 낙사 연결은 런타임 검증이 맡는다
    public class ArenaEdgeRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float ARENA_RADIUS = 18f;
        private const float BODY_RADIUS  = 0.5f;
        private const float LIMIT        = 17.5f;
        private const float TOLERANCE    = 0.01f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void GetLimit_SubtractsBodyRadius()
        {
            Assert.AreEqual(LIMIT, ArenaEdgeRules.GetLimit(ARENA_RADIUS, BODY_RADIUS), TOLERANCE);
        }

        [Test]
        public void Clamp_Inside_KeepsPosition()
        {
            var position = new Vector3(3f, 1f, 4f);
            Assert.AreEqual(position, ArenaEdgeRules.Clamp(position, Vector3.zero, ARENA_RADIUS, BODY_RADIUS));
        }

        [Test]
        public void Clamp_Outside_ReturnsRadiallyToLimitAndKeepsHeight()
        {
            Vector3 clamped = ArenaEdgeRules.Clamp(new Vector3(20f, 2f, 0f), Vector3.zero, ARENA_RADIUS, BODY_RADIUS);

            Assert.AreEqual(LIMIT, clamped.x, TOLERANCE);
            Assert.AreEqual(2f, clamped.y, TOLERANCE);
            Assert.AreEqual(0f, clamped.z, TOLERANCE);
            Assert.IsFalse(ArenaEdgeRules.IsBeyond(clamped, Vector3.zero, ARENA_RADIUS, BODY_RADIUS));
        }

        [Test]
        public void Clamp_UsesOffsetCenter()
        {
            var     center  = new Vector3(10f, 0f, -5f);
            Vector3 clamped = ArenaEdgeRules.Clamp(center + new Vector3(0f, 0f, 30f), center, ARENA_RADIUS, BODY_RADIUS);

            Assert.AreEqual(10f, clamped.x, TOLERANCE);
            Assert.AreEqual(-5f + LIMIT, clamped.z, TOLERANCE);
        }

        [Test]
        public void IsBeyond_OnLimit_IsInside()
        {
            Assert.IsFalse(ArenaEdgeRules.IsBeyond(new Vector3(LIMIT, 0f, 0f), Vector3.zero, ARENA_RADIUS, BODY_RADIUS));
        }

        [Test]
        public void IsBeyond_PastLimit_IsOutside()
        {
            Assert.IsTrue(ArenaEdgeRules.IsBeyond(new Vector3(LIMIT + 0.5f, 0f, 0f), Vector3.zero, ARENA_RADIUS, BODY_RADIUS));
        }

        [Test]
        public void IsBeyond_NonFinite_IsOutside()
        {
            Assert.IsTrue(ArenaEdgeRules.IsBeyond(new Vector3(float.NaN, 0f, 0f), Vector3.zero, ARENA_RADIUS, BODY_RADIUS));
        }
    }
}
