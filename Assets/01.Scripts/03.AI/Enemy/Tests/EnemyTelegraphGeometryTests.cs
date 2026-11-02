// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Tests
{
    // 예고 채움 면이 진행도만큼 직선은 앞으로, 부채꼴과 원형은 바깥으로 자라는지 본다. 렌더러와 메시는 거치지 않는다
    public class EnemyTelegraphGeometryTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float RANGE           = 8f;   // m
        private const float WIDTH           = 2f;   // m, 직선 폭
        private const float FAN_ANGLE       = 90f;  // 도
        private const float HALF            = 0.5f;
        private const float TOLERANCE       = 0.0001f;
        private const float LOOSE_TOLERANCE = 0.01f; // 각도, 넓이처럼 오차가 쌓이는 값

        private static readonly Vector3 ORIGIN    = new Vector3(3f, 1f, -2f);
        private static readonly Vector3 DIRECTION = Vector3.right;

        /****************************************
        *                Fields
        ****************************************/

        private Vector3[] _vertices;
        private int[]     _indices;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _vertices = new Vector3[EnemyTelegraphGeometry.MAX_AREA_VERTICES];
            _indices  = new int[EnemyTelegraphGeometry.MAX_AREA_INDICES];
        }

        [TestCase(EEnemyAttackShape.Dash)]
        [TestCase(EEnemyAttackShape.Line)]
        [TestCase(EEnemyAttackShape.Fan)]
        [TestCase(EEnemyAttackShape.Circle)]
        [TestCase(EEnemyAttackShape.TargetArea)]
        public void FillArea_ZeroOrNegativeProgress_ReturnsZero(EEnemyAttackShape shape)
        {
            EnemyTelegraphCue cue = MakeCue(shape, WIDTH);

            Assert.AreEqual(0, EnemyTelegraphGeometry.FillArea(_vertices, in cue, 0f));
            Assert.AreEqual(0, EnemyTelegraphGeometry.FillArea(_vertices, in cue, -1f));
        }

        [Test]
        public void FillArea_InvalidInput_ReturnsZero()
        {
            EnemyTelegraphCue cue     = MakeCue(EEnemyAttackShape.Dash, WIDTH);
            EnemyTelegraphCue noRange = new EnemyTelegraphCue(1, EEnemyAttackShape.Dash, ORIGIN, DIRECTION, 0f, WIDTH, 0.0, 1.0);

            Assert.AreEqual(0, EnemyTelegraphGeometry.FillArea(null, in cue, 1f));
            Assert.AreEqual(0, EnemyTelegraphGeometry.FillArea(new Vector3[EnemyTelegraphGeometry.RECT_AREA_VERTICES], in cue, 1f));
            Assert.AreEqual(0, EnemyTelegraphGeometry.FillArea(_vertices, in noRange, 1f));
        }

        [Test]
        public void FillArea_DashHalfProgress_ReachesHalfLengthForward()
        {
            EnemyTelegraphCue cue = MakeCue(EEnemyAttackShape.Dash, WIDTH);

            int count = EnemyTelegraphGeometry.FillArea(_vertices, in cue, HALF);

            Assert.AreEqual(EnemyTelegraphGeometry.RECT_AREA_VERTICES, count);
            Vector3 start = ORIGIN + (Vector3.up * EnemyTelegraphGeometry.FILL_LIFT);
            float   reach = RANGE * HALF;
            float   side  = WIDTH * HALF;

            // 뒤쪽 두 점은 시작점에, 앞쪽 두 점은 방향으로 길이 절반 앞에 있다
            AssertNear(start + new Vector3(0f, 0f, -side), _vertices[0]);
            AssertNear(start + new Vector3(reach, 0f, -side), _vertices[1]);
            AssertNear(start + new Vector3(reach, 0f, side), _vertices[2]);
            AssertNear(start + new Vector3(0f, 0f, side), _vertices[3]);
        }

        [Test]
        public void FillArea_ProgressAboveOne_ClampsToFullLength()
        {
            EnemyTelegraphCue cue = MakeCue(EEnemyAttackShape.Line, WIDTH);

            EnemyTelegraphGeometry.FillArea(_vertices, in cue, 2f);

            Assert.AreEqual(RANGE, Vector3.Distance(_vertices[0], _vertices[1]), TOLERANCE);
        }

        [Test]
        public void FillArea_ZeroDirection_FallsBackToForward()
        {
            EnemyTelegraphCue cue = new EnemyTelegraphCue(1, EEnemyAttackShape.Dash, ORIGIN, Vector3.zero, RANGE, WIDTH, 0.0, 1.0);

            EnemyTelegraphGeometry.FillArea(_vertices, in cue, 1f);

            AssertNear(Vector3.forward, (_vertices[1] - _vertices[0]).normalized);
        }

        [TestCase(EEnemyAttackShape.Circle, 0.25f)]
        [TestCase(EEnemyAttackShape.Circle, 0.5f)]
        [TestCase(EEnemyAttackShape.TargetArea, 0.75f)]
        [TestCase(EEnemyAttackShape.Fan, 0.5f)]
        public void FillArea_FanAndCircle_RadiusScalesWithProgress(EEnemyAttackShape shape, float progress)
        {
            float             width = (shape == EEnemyAttackShape.Fan) ? FAN_ANGLE : WIDTH;
            EnemyTelegraphCue cue   = MakeCue(shape, width);

            int count = EnemyTelegraphGeometry.FillArea(_vertices, in cue, progress);

            Assert.AreEqual(EnemyTelegraphGeometry.FAN_AREA_VERTICES, count);
            Vector3 center = ORIGIN + (Vector3.up * EnemyTelegraphGeometry.FILL_LIFT);
            AssertNear(center, _vertices[0]);
            for (int i = 1; i < count; ++i)
            {
                Assert.AreEqual(RANGE * progress, Vector3.Distance(center, _vertices[i]), TOLERANCE);
                Assert.AreEqual(center.y, _vertices[i].y, TOLERANCE);
            }
        }

        [Test]
        public void FillArea_Circle_ClosesFullTurn()
        {
            EnemyTelegraphCue cue = MakeCue(EEnemyAttackShape.Circle, WIDTH);

            int count = EnemyTelegraphGeometry.FillArea(_vertices, in cue, 1f);

            // 360도를 나눈 마지막 점이 첫 가장자리 점과 겹쳐야 원이 닫힌다
            Assert.Less(Vector3.Distance(_vertices[1], _vertices[count - 1]), LOOSE_TOLERANCE);
        }

        [Test]
        public void FillArea_Fan_EdgesSpanArcAngle()
        {
            EnemyTelegraphCue cue = MakeCue(EEnemyAttackShape.Fan, FAN_ANGLE);

            int count = EnemyTelegraphGeometry.FillArea(_vertices, in cue, 1f);

            Vector3 left  = _vertices[1] - _vertices[0];
            Vector3 right = _vertices[count - 1] - _vertices[0];
            Assert.AreEqual(FAN_ANGLE, Vector3.Angle(left, right), LOOSE_TOLERANCE);
        }

        [TestCase(true, EnemyTelegraphGeometry.RECT_AREA_INDICES, EnemyTelegraphGeometry.RECT_AREA_VERTICES)]
        [TestCase(false, EnemyTelegraphGeometry.FAN_AREA_INDICES, EnemyTelegraphGeometry.FAN_AREA_VERTICES)]
        public void FillAreaIndices_CountAndRange_MatchVertices(bool isRectangle, int expectedIndices, int vertexCount)
        {
            int count = EnemyTelegraphGeometry.FillAreaIndices(_indices, isRectangle);

            Assert.AreEqual(expectedIndices, count);
            Assert.AreEqual(0, count % 3);
            for (int i = 0; i < count; ++i)
            {
                Assert.GreaterOrEqual(_indices[i], 0);
                Assert.Less(_indices[i], vertexCount);
            }
        }

        [Test]
        public void FillAreaIndices_SmallBuffer_ReturnsZero()
        {
            Assert.AreEqual(0, EnemyTelegraphGeometry.FillAreaIndices(null, true));
            Assert.AreEqual(0, EnemyTelegraphGeometry.FillAreaIndices(new int[EnemyTelegraphGeometry.RECT_AREA_INDICES], true));
        }

        [Test]
        public void FillAreaIndices_Rectangle_CoversWholeQuad()
        {
            EnemyTelegraphCue cue = MakeCue(EEnemyAttackShape.Dash, WIDTH);
            EnemyTelegraphGeometry.FillArea(_vertices, in cue, 1f);
            int count = EnemyTelegraphGeometry.FillAreaIndices(_indices, true);

            // 두 삼각형 넓이의 합이 직사각형 넓이와 같아야 빈틈이나 겹침이 없다
            float area = 0f;
            for (int i = 0; i < count; i += 3)
            {
                Vector3 a = _vertices[_indices[i]];
                Vector3 b = _vertices[_indices[i + 1]];
                Vector3 c = _vertices[_indices[i + 2]];
                area += Vector3.Cross(b - a, c - a).magnitude * HALF;
            }

            Assert.AreEqual(RANGE * WIDTH, area, LOOSE_TOLERANCE);
        }

        [Test]
        public void UsesRectangleArea_MatchesShape()
        {
            Assert.IsTrue(EnemyTelegraphGeometry.UsesRectangleArea(EEnemyAttackShape.Dash));
            Assert.IsTrue(EnemyTelegraphGeometry.UsesRectangleArea(EEnemyAttackShape.Line));
            Assert.IsTrue(EnemyTelegraphGeometry.UsesRectangleArea(EEnemyAttackShape.Projectile));
            Assert.IsFalse(EnemyTelegraphGeometry.UsesRectangleArea(EEnemyAttackShape.Fan));
            Assert.IsFalse(EnemyTelegraphGeometry.UsesRectangleArea(EEnemyAttackShape.Circle));
            Assert.IsFalse(EnemyTelegraphGeometry.UsesRectangleArea(EEnemyAttackShape.TargetArea));
        }

        [Test]
        public void IsAreaFrontVertex_FrontIsProgressEnd()
        {
            Assert.IsFalse(EnemyTelegraphGeometry.IsAreaFrontVertex(true, 0));
            Assert.IsTrue(EnemyTelegraphGeometry.IsAreaFrontVertex(true, 1));
            Assert.IsTrue(EnemyTelegraphGeometry.IsAreaFrontVertex(true, 2));
            Assert.IsFalse(EnemyTelegraphGeometry.IsAreaFrontVertex(true, 3));
            Assert.IsFalse(EnemyTelegraphGeometry.IsAreaFrontVertex(false, 0));
            Assert.IsTrue(EnemyTelegraphGeometry.IsAreaFrontVertex(false, 1));
        }

        private static EnemyTelegraphCue MakeCue(EEnemyAttackShape shape, float width)
        {
            return new EnemyTelegraphCue(1, shape, ORIGIN, DIRECTION, RANGE, width, 0.0, 1.0);
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), TOLERANCE, $"expected {expected}, actual {actual}");
        }
    }
}
