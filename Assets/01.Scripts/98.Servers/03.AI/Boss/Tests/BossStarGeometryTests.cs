// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 별형 회전 빔의 배치와 회전과 포함 판정을 확인한다. 서버 판정과 표현이 같은 기하를 쓰는지 보는 시험이다
    public class BossStarGeometryTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float TOLERANCE = 0.001f;
        private const float RING      = 14f;
        private const float WIDTH     = 1.5f;
        private const float ARENA     = 18f;
        private const float DEGREES   = 20f;
        private const float DURATION  = 6f;
        private const float APOTHEM   = 4.326f; // 14 x cos(72도), 중심에서 각 빔 중심선까지 거리

        private static readonly Vector3 OffCenter = new Vector3(40f, 0f, -25f);

        /****************************************
        *               Helpers
        ****************************************/

        private static BossStarShape Star(Vector3 center, Vector3 direction, bool right = true, float arena = ARENA)
        {
            return BossStarGeometry.Place(center, arena, direction, RING, 5, 2, WIDTH, DEGREES, right);
        }

        // 중심에서 기준 방향으로부터 오른쪽으로 degrees도, 거리 radius인 점
        private static Vector3 Polar(in BossStarShape shape, float degrees, float radius)
        {
            return shape.ArenaCenter + ((Quaternion.AngleAxis(degrees, Vector3.up) * shape.BaseDirection) * radius);
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, string message)
        {
            Assert.AreEqual(0f, (expected - actual).magnitude, TOLERANCE, message + " expected " + expected + " actual " + actual);
        }

        /****************************************
        *               Placement
        ****************************************/

        [Test]
        public void Place_VerticesAreOnTheRingEvenlyAndVertexZeroPointsAtTheBaseDirection()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);

            AssertNear(OffCenter + (Vector3.forward * RING), BossStarGeometry.GetVertex(in shape, 0, 0f), "꼭짓점 0");
            for (int i = 0; i < 5; ++i)
            {
                Vector3 vertex = BossStarGeometry.GetVertex(in shape, i, 0f);
                Assert.AreEqual(RING, (vertex - OffCenter).magnitude, TOLERANCE, "꼭짓점 " + i);
                AssertNear(Polar(in shape, i * 72f, RING), vertex, "각도 " + i);
            }
        }

        [Test]
        public void Place_BeamsConnectEveryOtherVertex_ZeroToTwoThroughFourToOne()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);
            int[] expectedEnd = { 2, 3, 4, 0, 1 };

            for (int beam = 0; beam < 5; ++beam)
            {
                BossStarGeometry.GetBeam(in shape, beam, 0f, out Vector3 start, out Vector3 end);
                AssertNear(BossStarGeometry.GetVertex(in shape, beam, 0f), start, "시작 " + beam);
                AssertNear(BossStarGeometry.GetVertex(in shape, expectedEnd[beam], 0f), end, "끝 " + beam);
            }
        }

        [Test]
        public void Place_FlattensDirectionAndFallsBackToWorldForward()
        {
            BossStarShape tilted = Star(OffCenter, new Vector3(0f, 5f, 3f));
            Assert.AreEqual(0f, tilted.BaseDirection.y, TOLERANCE);
            Assert.AreEqual(1f, tilted.BaseDirection.magnitude, TOLERANCE);

            BossStarShape vertical = Star(OffCenter, Vector3.up);
            AssertNear(Vector3.forward, vertical.BaseDirection, "수평 길이가 없으면 월드 정면");
        }

        /****************************************
        *               Rotation
        ****************************************/

        [Test]
        public void RotationAt_IsSignedByDirectionAndClampedToTheDuration()
        {
            BossStarShape right = Star(OffCenter, Vector3.forward, true);
            BossStarShape left  = Star(OffCenter, Vector3.forward, false);

            Assert.AreEqual(0f, BossStarGeometry.RotationAt(in right, -1.0, DURATION), TOLERANCE, "활성 전");
            Assert.AreEqual(20f, BossStarGeometry.RotationAt(in right, 1.0, DURATION), TOLERANCE);
            Assert.AreEqual(-20f, BossStarGeometry.RotationAt(in left, 1.0, DURATION), TOLERANCE);
            Assert.AreEqual(120f, BossStarGeometry.RotationAt(in right, 6.0, DURATION), TOLERANCE, "6초에 120도");
            Assert.AreEqual(120f, BossStarGeometry.RotationAt(in right, 30.0, DURATION), TOLERANCE, "유지 시간 뒤에는 멈춘다");
        }

        [Test]
        public void RotationAt_IsComputedFromTimeSoLongRunsAccumulateNoError()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);
            double elapsed = 0.0;
            for (int i = 0; i < 360; ++i)
            {
                elapsed += 1.0 / 60.0;
            }

            Assert.AreEqual(BossStarGeometry.RotationAt(in shape, 6.0, DURATION), BossStarGeometry.RotationAt(in shape, elapsed, DURATION), 0.001f);
        }

        [Test]
        public void GetVertex_RotatesRightAsAWholeAndKeepsTheShape()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);

            for (int i = 0; i < 5; ++i)
            {
                AssertNear(Polar(in shape, (i * 72f) + 30f, RING), BossStarGeometry.GetVertex(in shape, i, 30f), "회전 " + i);
            }

            BossStarGeometry.GetBeam(in shape, 0, 0f, out Vector3 a0, out Vector3 b0);
            BossStarGeometry.GetBeam(in shape, 0, 47f, out Vector3 a1, out Vector3 b1);
            Assert.AreEqual((b0 - a0).magnitude, (b1 - a1).magnitude, TOLERANCE, "빔 길이는 회전해도 그대로");
        }

        /****************************************
        *               Containment
        ****************************************/

        [Test]
        public void Contains_BeamMidpointIsInsideAndItsWidthEdgesAreInclusive()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);

            // 빔 0(꼭짓점 0-2)의 중심선까지 거리는 apothem, 가운데 방향은 기준에서 72도
            Assert.IsTrue(BossStarGeometry.Contains(in shape, Polar(in shape, 72f, APOTHEM), 0f), "중심선 가운데");
            Assert.IsTrue(BossStarGeometry.Contains(in shape, Polar(in shape, 72f, APOTHEM - 0.74f), 0f), "안쪽 변 안");
            Assert.IsTrue(BossStarGeometry.Contains(in shape, Polar(in shape, 72f, APOTHEM + 0.74f), 0f), "바깥쪽 변 안");
            Assert.IsFalse(BossStarGeometry.Contains(in shape, Polar(in shape, 72f, APOTHEM - 0.80f), 0f), "안쪽 변 밖");
            Assert.IsFalse(BossStarGeometry.Contains(in shape, Polar(in shape, 72f, APOTHEM + 0.80f), 0f), "바깥쪽 변 밖");
        }

        [Test]
        public void Contains_CentralSafeAreaHasAboutThreePointSixMetersRadius()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);

            Assert.IsFalse(BossStarGeometry.Contains(in shape, OffCenter, 0f), "정중앙");
            for (int degrees = 0; degrees < 360; degrees += 5)
            {
                Assert.IsFalse(BossStarGeometry.Contains(in shape, Polar(in shape, degrees, 3.5f), 0f), "반지름 3.5m 각도 " + degrees);
            }

            // 빔 중심선 방향으로 3.65m(= apothem - 폭의 절반 + 여유)는 빔에 걸린다
            Assert.IsTrue(BossStarGeometry.Contains(in shape, Polar(in shape, 72f, 3.65f), 0f));
        }

        [Test]
        public void Contains_BeamEndsStopAtTheVertices()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);

            Assert.IsTrue(BossStarGeometry.Contains(in shape, Polar(in shape, 0f, RING), 0f), "꼭짓점(여우불) 위치는 포함");
            Assert.IsFalse(BossStarGeometry.Contains(in shape, Polar(in shape, 0f, RING + 1f), 0f), "꼭짓점 바깥");
        }

        [Test]
        public void Contains_ArenaClipsBeamsAtItsBoundary()
        {
            BossStarShape small = Star(OffCenter, Vector3.forward, true, 10f);
            Assert.IsFalse(BossStarGeometry.Contains(in small, Polar(in small, 0f, 12f), 0f), "전장 밖의 빔 끝");
            Assert.IsTrue(BossStarGeometry.Contains(in small, Polar(in small, 72f, APOTHEM), 0f));
        }

        [Test]
        public void Contains_CrossingPointIsOneYesNotTwo_AndFiveFoldSymmetryHolds()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);

            // 안쪽 오각형의 꼭짓점(두 빔이 만나는 곳)은 기준에서 36도, 반지름 apothem / cos(36도)
            Vector3 crossing = Polar(in shape, 36f, APOTHEM / Mathf.Cos(36f * Mathf.Deg2Rad));
            Assert.IsTrue(BossStarGeometry.Contains(in shape, crossing, 0f));

            Vector3 probe = Polar(in shape, 72f, APOTHEM + 0.3f);
            Assert.AreEqual(BossStarGeometry.Contains(in shape, probe, 0f), BossStarGeometry.Contains(in shape, probe, 72f), "72도 돌면 같은 모양");
            Assert.AreEqual(BossStarGeometry.Contains(in shape, crossing, 0f), BossStarGeometry.Contains(in shape, crossing, 144f));
        }

        [Test]
        public void Contains_RotatingTheStarAndThePointTogetherKeepsTheResult()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);

            for (float rotation = -120f; rotation <= 120f; rotation += 15f)
            {
                Assert.IsTrue(BossStarGeometry.Contains(in shape, Polar(in shape, 72f + rotation, APOTHEM), rotation), "회전 " + rotation);
                Assert.IsFalse(BossStarGeometry.Contains(in shape, Polar(in shape, 72f + rotation, 2f), rotation), "중앙 안전 " + rotation);
            }
        }

        [Test]
        public void Contains_LeftAndRightRotationDiffer_ForAPointOffTheSymmetryAxis()
        {
            BossStarShape right = Star(OffCenter, Vector3.forward, true);
            BossStarShape left  = Star(OffCenter, Vector3.forward, false);

            // 별은 72도마다 같은 모양이라 어떤 점은 두 방향에서 같은 횟수로 맞는다. 횟수가 다른 점을 찾아 방향이 판정에 반영되는지 본다
            bool found = false;
            for (int degrees = 0; (degrees < 360) && (!found); ++degrees)
            {
                for (float radius = 6f; radius <= 13.5f; radius += 0.25f)
                {
                    Vector3 point = Polar(in right, degrees, radius);
                    int rightHits = CountHits(in right, point);
                    int leftHits  = CountHits(in left, point);
                    if (System.Math.Abs(rightHits - leftHits) >= 2)
                    {
                        found = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(found, "방향에 따라 맞는 횟수가 달라지는 점이 있다");
        }

        // 24회차(0.25초 간격)에서 점이 별 안에 있는 횟수
        private static int CountHits(in BossStarShape shape, Vector3 point)
        {
            int hits = 0;
            for (int i = 0; i < 24; ++i)
            {
                float rotation = BossStarGeometry.RotationAt(in shape, i * 0.25, DURATION);
                hits += BossStarGeometry.Contains(in shape, point, rotation) ? 1 : 0;
            }

            return hits;
        }

        [Test]
        public void Contains_IgnoresHeight()
        {
            BossStarShape shape = Star(OffCenter, Vector3.forward);
            Vector3 point = Polar(in shape, 72f, APOTHEM) + (Vector3.up * 30f);
            Assert.IsTrue(BossStarGeometry.Contains(in shape, point, 0f));
        }
    }
}
