// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Tests
{
    // 고정 평행 빔의 배치와 포함 판정, 그리고 표현 메시가 같은 영역인지 확인한다. 서버와 클라이언트가 같은 기하를 쓰는지 보는 시험이다
    public class BossBeamGeometryTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float TOLERANCE   = 0.001f;
        private const float RADIUS      = 18f;
        private const float OFFSET      = 5f;
        private const float WIDTH       = 2f;
        private const int   COUNT       = 2;

        private static readonly Vector3 Origin = Vector3.zero;
        private static readonly Vector3 OffCenter = new Vector3(50f, 0f, -30f);

        /****************************************
        *               Helpers
        ****************************************/

        private static void AssertNear(Vector3 expected, Vector3 actual, string message)
        {
            Assert.AreEqual(0f, (expected - actual).magnitude, TOLERANCE, message + " expected " + expected + " actual " + actual);
        }

        private static BossBeamShape Beam(Vector3 center, Vector3 direction, int index)
        {
            return BossBeamGeometry.Place(center, RADIUS, direction, index, COUNT, OFFSET, WIDTH);
        }

        /****************************************
        *               Placement
        ****************************************/

        [Test]
        public void Place_TwoBeams_AreFiveMetersLeftAndRightWithEightMeterGap()
        {
            BossBeamShape left  = Beam(Origin, Vector3.forward, 0);
            BossBeamShape right = Beam(Origin, Vector3.forward, 1);

            Assert.AreEqual(-5f, left.LateralOffset, TOLERANCE);
            Assert.AreEqual(5f, right.LateralOffset, TOLERANCE);
            Assert.AreEqual(2f, left.Width, TOLERANCE);
            AssertNear(new Vector3(-5f, 0f, 0f), left.LineCenter, "정면(+z)의 왼쪽은 -x다");
            AssertNear(new Vector3(5f, 0f, 0f), right.LineCenter, "정면(+z)의 오른쪽은 +x다");

            // 가운데 안전 간격은 안쪽 변 사이 거리다, 중심선 10m에서 폭 2m 두 줄을 빼면 8m다
            float gap = (right.LateralOffset - (right.Width * 0.5f)) - (left.LateralOffset + (left.Width * 0.5f));
            Assert.AreEqual(8f, gap, TOLERANCE);
        }

        [Test]
        public void Place_NonOriginArena_UsesArenaCenterAndKeepsDirection()
        {
            BossBeamShape left  = Beam(OffCenter, Vector3.right, 0);
            BossBeamShape right = Beam(OffCenter, Vector3.right, 1);

            Assert.AreEqual(OffCenter, left.ArenaCenter);
            Assert.AreEqual(RADIUS, left.ArenaRadius, TOLERANCE);

            // 방향이 +x이면 오른쪽은 -z다
            AssertNear(OffCenter + new Vector3(0f, 0f, 5f), left.LineCenter, "왼쪽 빔");
            AssertNear(OffCenter + new Vector3(0f, 0f, -5f), right.LineCenter, "오른쪽 빔");
            AssertNear(Vector3.right, right.Direction, "방향");
        }

        [Test]
        public void Place_FlattensDirectionAndFallsBackWhenItHasNoHorizontalLength()
        {
            BossBeamShape tilted = Beam(Origin, new Vector3(0f, 5f, 3f), 1);
            Assert.AreEqual(0f, tilted.Direction.y, TOLERANCE);
            Assert.AreEqual(1f, tilted.Direction.magnitude, TOLERANCE);

            BossBeamShape vertical = Beam(Origin, Vector3.up, 1);
            Assert.AreEqual(Vector3.forward, vertical.Direction, "수평 길이가 없으면 정면으로 대신한다");
        }

        [Test]
        public void Place_ThreeBeams_AreCenteredWithMiddleOnTheAxis()
        {
            BossBeamShape first  = BossBeamGeometry.Place(Origin, RADIUS, Vector3.forward, 0, 3, OFFSET, WIDTH);
            BossBeamShape middle = BossBeamGeometry.Place(Origin, RADIUS, Vector3.forward, 1, 3, OFFSET, WIDTH);
            BossBeamShape last   = BossBeamGeometry.Place(Origin, RADIUS, Vector3.forward, 2, 3, OFFSET, WIDTH);

            Assert.AreEqual(-10f, first.LateralOffset, TOLERANCE);
            Assert.AreEqual(0f, middle.LateralOffset, TOLERANCE);
            Assert.AreEqual(10f, last.LateralOffset, TOLERANCE);
        }

        /****************************************
        *               Containment
        ****************************************/

        [Test]
        public void Contains_InsideBeam_OutsideGapAndOuterAreSafe()
        {
            BossBeamShape right = Beam(Origin, Vector3.forward, 1);

            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(5f, 0f, 0f)), "중심선");
            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(5f, 0f, 12f)), "중심선 위 멀리");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(0f, 0f, 0f)), "가운데 안전 구역");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(-5f, 0f, 0f)), "다른 빔 중심선은 이 빔이 아니다");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(8f, 0f, 0f)), "바깥쪽 안전 구역");
        }

        [Test]
        public void Contains_StripEdgesAreInclusive()
        {
            BossBeamShape right = Beam(Origin, Vector3.forward, 1);

            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(4f, 0f, 3f)), "안쪽 변");
            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(6f, 0f, 3f)), "바깥쪽 변");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(3.98f, 0f, 3f)));
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(6.02f, 0f, 3f)));
        }

        [Test]
        public void Contains_IgnoresHeight()
        {
            BossBeamShape right = Beam(Origin, Vector3.forward, 1);
            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(5f, 40f, 3f)));
        }

        [Test]
        public void Contains_ArenaBoundaryIsInclusiveAndBeyondIsOutside()
        {
            BossBeamShape right = Beam(Origin, Vector3.forward, 1);
            float onBoundary = Mathf.Sqrt((RADIUS * RADIUS) - 25f);

            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(5f, 0f, onBoundary)), "전장 경계 위");
            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(5f, 0f, -onBoundary)), "반대쪽 경계 위");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(5f, 0f, onBoundary + 0.05f)), "전장 밖은 빔도 닿지 않는다");
        }

        // 피해 영역은 띠 전체와 전장 원의 교집합이다. 중심선만 자르면 안쪽 변 쪽에서 더 안으로 들어간 점이 빠진다
        [Test]
        public void Contains_UsesWholeStripIntersectionNotOnlyTheCenterLineChord()
        {
            BossBeamShape right = Beam(Origin, Vector3.forward, 1);
            Assert.IsTrue(BossBeamGeometry.TryGetCenterLineChord(in right, out Vector3 start, out Vector3 end));

            float centerLineHalf = (end - start).magnitude * 0.5f;
            float innerEdgeHalf  = Mathf.Sqrt((RADIUS * RADIUS) - 16f);
            Assert.Greater(innerEdgeHalf, centerLineHalf, "안쪽 변이 중심선보다 전장 중심에 가까워 더 길다");

            float z = (centerLineHalf + innerEdgeHalf) * 0.5f;
            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(4f, 0f, z)), "중심선 끝 밖이지만 띠와 전장 안");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(6f, 0f, z)), "바깥쪽 변은 같은 z에서 전장 밖이다");
        }

        [Test]
        public void Contains_NonOriginArenaAndRotatedDirection()
        {
            // 방향 +x, 전장 중심 (50, -30): 오른쪽 빔은 z = -30 - 5 = -35 줄이다
            BossBeamShape right = Beam(OffCenter, Vector3.right, 1);

            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(60f, 0f, -35f)));
            Assert.IsTrue(BossBeamGeometry.Contains(in right, new Vector3(50f, 0f, -34f)), "안쪽 변");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(60f, 0f, -30f)), "가운데 안전 구역");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(0f, 0f, 0f)), "원점 기준으로 판정하지 않는다");
            Assert.IsFalse(BossBeamGeometry.Contains(in right, new Vector3(80f, 0f, -35f)), "전장 밖");
        }

        [Test]
        public void Contains_BeamsAreIndependent_NoPointIsInBoth()
        {
            BossBeamShape left  = Beam(Origin, Vector3.forward, 0);
            BossBeamShape right = Beam(Origin, Vector3.forward, 1);

            for (float x = -12f; x <= 12f; x += 0.25f)
            {
                Vector3 point = new Vector3(x, 0f, 2f);
                Assert.IsFalse(BossBeamGeometry.Contains(in left, point) && BossBeamGeometry.Contains(in right, point), "x=" + x);
            }
        }

        [Test]
        public void TryGetCenterLineChord_ReturnsFalseWhenLineMissesTheArena()
        {
            BossBeamShape far = BossBeamGeometry.Place(Origin, RADIUS, Vector3.forward, 1, COUNT, 30f, WIDTH);
            Assert.IsFalse(BossBeamGeometry.TryGetCenterLineChord(in far, out Vector3 start, out Vector3 end));
        }

        /****************************************
        *      Presentation Mesh Matches Server Area
        ****************************************/

        // 표현 메시의 모든 정점이 서버 판정 영역 안(경계 포함)이고, 앞뒤 끝 정점은 전장 경계 위에 있다
        [Test]
        public void BeamMesh_VerticesLieInsideTheServerAreaAndEndsTouchTheArenaBoundary()
        {
            BossBeamShape right = Beam(OffCenter, new Vector3(1f, 0f, 1f), 1);

            GameObject go = new GameObject("BeamMeshTest");
            try
            {
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                BeamGroundVFXController visual = go.AddComponent<BeamGroundVFXController>();
                visual.Show(right.ArenaCenter, right.ArenaRadius, right.Direction, right.LateralOffset, right.Width);

                Assert.IsTrue(visual.HasArea);
                Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.Greater(mesh.vertexCount, 4);

                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; ++i)
                {
                    Vector3 world = go.transform.TransformPoint(vertices[i]);
                    Assert.IsTrue(BossBeamGeometry.Contains(in right, world), "정점 " + i + " " + world);

                    Vector3 offset = world - right.ArenaCenter;
                    offset.y = 0f;
                    Assert.AreEqual(RADIUS, offset.magnitude, TOLERANCE, "앞뒤 끝 정점은 전장 경계 위다 " + i);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BeamMesh_StripOutsideTheArena_HasNoArea()
        {
            GameObject go = new GameObject("BeamMeshOutside");
            try
            {
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                BeamGroundVFXController visual = go.AddComponent<BeamGroundVFXController>();
                visual.Show(Origin, RADIUS, Vector3.forward, 30f, WIDTH);

                Assert.IsFalse(visual.HasArea);
                Assert.IsFalse(go.GetComponent<MeshRenderer>().enabled);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BeamMesh_StripPartlyOutside_IsClippedToTheArena()
        {
            GameObject go = new GameObject("BeamMeshPartial");
            try
            {
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                BeamGroundVFXController visual = go.AddComponent<BeamGroundVFXController>();
                BossBeamShape edge = BossBeamGeometry.Place(Origin, RADIUS, Vector3.forward, 1, COUNT, 17.5f, WIDTH);
                visual.Show(edge.ArenaCenter, edge.ArenaRadius, edge.Direction, edge.LateralOffset, edge.Width);

                Assert.IsTrue(visual.HasArea);
                Vector3[] vertices = go.GetComponent<MeshFilter>().sharedMesh.vertices;
                for (int i = 0; i < vertices.Length; ++i)
                {
                    Assert.IsTrue(BossBeamGeometry.Contains(in edge, go.transform.TransformPoint(vertices[i])), "정점 " + i);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
