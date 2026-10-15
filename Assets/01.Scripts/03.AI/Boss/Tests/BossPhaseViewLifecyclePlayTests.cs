// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 프레임이 실제로 흘러야 확인되는 표시 뷰의 수명 동작만 플레이 모드에서 검증한다. 나머지 표시 규칙은 EditMode 시험이 맡는다
    public class BossPhaseViewLifecyclePlayTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double START = 100.0;
        private const int    FRAMES = 5;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject        _root;
        private BossHardPhaseView _hard;
        private double            _clock;

        /****************************************
        *               Set Up
        ****************************************/

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("BossPhaseViewLifecyclePlayTests");
            _hard = _root.AddComponent<BossHardPhaseView>();
            _clock = START;
            _hard.SetServerClock(() => _clock);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }

        /****************************************
        *               Tests
        ****************************************/

        [UnityTest]
        public IEnumerator TrackingArea_Moving_FollowsServerCenterWithoutOvershoot()
        {
            _hard.TrackingArea.BeginMoving(1, 7, 3, Vector3.zero, 6f, START, 4f, 2.5f);
            _hard.TrackingArea.UpdateMoving(7, new Vector3(10f, 0f, 0f), START + 0.1);

            for (int i = 0; i < FRAMES; ++i)
            {
                yield return null;
            }

            _hard.TrackingArea.TryGetState(7, out Vector3 center, out _, out _, out _);
            Assert.That(center.x, Is.GreaterThan(0f));
            Assert.That(center.x, Is.LessThanOrEqualTo(10f));
        }

        [UnityTest]
        public IEnumerator DisabledView_ClearsItsShapes()
        {
            _hard.CircleBurst.Begin(1, new[] { Vector3.zero, Vector3.one }, new[] { 3f, 3f }, START, 2f);
            yield return null;
            Assert.That(_hard.ShapePool.LeasedCount, Is.EqualTo(2));

            _hard.CircleBurst.enabled = false;

            Assert.That(_hard.ShapePool.LeasedCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator UpdateLoop_WithoutEntries_SwitchesTheViewOff()
        {
            _hard.CircleBurst.Begin(1, new[] { Vector3.zero }, new[] { 3f }, START, 2f);
            yield return null;
            Assert.That(_hard.CircleBurst.enabled, Is.True);

            _hard.CircleBurst.Cancel(1);
            yield return null;

            Assert.That(_hard.CircleBurst.enabled, Is.False);
        }
    }
}
