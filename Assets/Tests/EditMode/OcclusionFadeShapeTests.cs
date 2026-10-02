// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class OcclusionFadeShapeTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 카메라는 초점 위 뒤쪽 10m에 두어 시선이 -z에서 +z로 내려다본다
        private static readonly Vector3 G_Camera = new Vector3(0f, 10f, -10f);
        private static readonly Vector3 G_Focus  = new Vector3(0f, 1f, 0f);

        private const float RADIUS         = 2f;
        private const float EDGE_SOFTNESS  = 0.5f;
        private const float TARGET_MARGIN  = 0.5f;
        private const float FLOOR_HEIGHT   = 0.3f;
        private const float HIGH_FLOOR     = 7f;
        private const float MIN_VISIBILITY = 0.2f;
        private const float HALF           = 0.5f;
        private const float TOLERANCE      = 0.0001f;

        // 중간 깊이의 원뿔 반지름은 1m, 안쪽 경계는 0.5m다
        private const float OUTSIDE_OFFSET = 1.5f;
        private const float EDGE_OFFSET    = 0.75f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void GetVisibility_PointOnSightLineBeforeFocus_ReturnsMinVisibility()
        {
            OcclusionFadeShape shape = CreateShape(EDGE_SOFTNESS, FLOOR_HEIGHT);

            float visibility = shape.GetVisibility(Midpoint());

            Assert.AreEqual(MIN_VISIBILITY, visibility, TOLERANCE);
        }

        [Test]
        public void GetVisibility_PointBehindFocus_ReturnsVisible()
        {
            OcclusionFadeShape shape     = CreateShape(EDGE_SOFTNESS, FLOOR_HEIGHT);
            Vector3            direction = (G_Focus - G_Camera).normalized;

            float visibility = shape.GetVisibility(G_Focus + direction);

            Assert.AreEqual(1f, visibility, TOLERANCE);
        }

        [Test]
        public void GetVisibility_PointInsideTargetMargin_ReturnsVisible()
        {
            OcclusionFadeShape shape     = CreateShape(EDGE_SOFTNESS, FLOOR_HEIGHT);
            Vector3            direction = (G_Focus - G_Camera).normalized;

            float visibility = shape.GetVisibility(G_Focus - (direction * (TARGET_MARGIN * HALF)));

            Assert.AreEqual(1f, visibility, TOLERANCE);
        }

        [Test]
        public void GetVisibility_PointBelowFloor_ReturnsVisible()
        {
            OcclusionFadeShape shape = CreateShape(EDGE_SOFTNESS, HIGH_FLOOR);

            float visibility = shape.GetVisibility(Midpoint());

            Assert.AreEqual(1f, visibility, TOLERANCE);
        }

        [Test]
        public void GetVisibility_PointOutsideCone_ReturnsVisible()
        {
            OcclusionFadeShape shape = CreateShape(EDGE_SOFTNESS, FLOOR_HEIGHT);

            float visibility = shape.GetVisibility(Midpoint() + (Vector3.right * OUTSIDE_OFFSET));

            Assert.AreEqual(1f, visibility, TOLERANCE);
        }

        [Test]
        public void GetVisibility_PointInSoftEdge_ReturnsBetweenMinAndVisible()
        {
            OcclusionFadeShape shape = CreateShape(EDGE_SOFTNESS, FLOOR_HEIGHT);

            float visibility = shape.GetVisibility(Midpoint() + (Vector3.right * EDGE_OFFSET));

            Assert.Greater(visibility, MIN_VISIBILITY);
            Assert.Less(visibility, 1f);
        }

        [Test]
        public void GetVisibility_ZeroEdgeSoftness_StaysFinite()
        {
            OcclusionFadeShape shape = CreateShape(0f, FLOOR_HEIGHT);

            float visibility = shape.GetVisibility(Midpoint());

            Assert.AreEqual(MIN_VISIBILITY, visibility, TOLERANCE);
        }

        [Test]
        public void IsFaded_ChannelCleared_ReturnsFalse()
        {
            OcclusionFadeChannel.Publish(CreateShape(EDGE_SOFTNESS, FLOOR_HEIGHT));
            OcclusionFadeChannel.Clear();

            Assert.IsFalse(OcclusionFadeChannel.IsFaded(Midpoint()));
        }

        [Test]
        public void IsFaded_PublishedPointInCone_ReturnsTrue()
        {
            OcclusionFadeChannel.Publish(CreateShape(EDGE_SOFTNESS, FLOOR_HEIGHT));

            try
            {
                Assert.IsTrue(OcclusionFadeChannel.IsFaded(Midpoint()));
                Assert.IsFalse(OcclusionFadeChannel.IsFaded(G_Focus + Vector3.forward));
            }
            finally
            {
                OcclusionFadeChannel.Clear();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private static OcclusionFadeShape CreateShape(float edgeSoftness, float floorHeight)
        {
            return new OcclusionFadeShape(G_Camera, G_Focus, RADIUS, edgeSoftness, TARGET_MARGIN, floorHeight, MIN_VISIBILITY);
        }

        private static Vector3 Midpoint()
        {
            return Vector3.Lerp(G_Camera, G_Focus, HALF);
        }
    }
}
