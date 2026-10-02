// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public class PathFollowerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   CAPACITY  = 4;
        private const float TOLERANCE = 0.0001f;
        private const float LONG_STEP = 100f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Step_AlongCorners_MovesByDistanceAndTurnsAtCorner()
        {
            PathFollower path = CreateLPath();

            Vector3 first  = path.Step(Vector3.zero, 2f, out Vector3 firstDirection, out bool movedFirst);
            Vector3 second = path.Step(first, 3f, out Vector3 secondDirection, out _);

            Assert.IsTrue(movedFirst);
            Assert.AreEqual(2f, first.x, TOLERANCE);
            Assert.Less(Vector3.Distance(Vector3.right, firstDirection), TOLERANCE);
            Assert.AreEqual(3f, second.x, TOLERANCE);
            Assert.AreEqual(2f, second.z, TOLERANCE);
            Assert.Less(Vector3.Distance(Vector3.forward, secondDirection), TOLERANCE);
            Assert.IsTrue(path.IsMoving);
        }

        [Test]
        public void Step_PastEnd_StopsAtLastCorner()
        {
            PathFollower path = CreateLPath();

            Vector3 end = path.Step(Vector3.zero, LONG_STEP, out _, out _);

            Assert.AreEqual(new Vector3(3f, 0f, 4f), end);
            Assert.IsFalse(path.IsMoving);
        }

        [Test]
        public void Stop_WhileMoving_HaltsMovement()
        {
            PathFollower path = CreateLPath();
            path.Stop();

            Vector3 position = path.Step(Vector3.zero, LONG_STEP, out _, out bool moved);

            Assert.IsFalse(moved);
            Assert.AreEqual(Vector3.zero, position);
        }

        /****************************************
        *                Helpers
        ****************************************/

        // 0번은 출발점, (3,0,0)에서 꺾여 (3,0,4)로 간다
        private static PathFollower CreateLPath()
        {
            var path = new PathFollower(CAPACITY);
            path.CornerBuffer[0] = Vector3.zero;
            path.CornerBuffer[1] = new Vector3(3f, 0f, 0f);
            path.CornerBuffer[2] = new Vector3(3f, 0f, 4f);
            path.Begin(3);
            return path;
        }
    }
}
