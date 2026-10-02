// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class MoveRequestThrottleTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  NEAR_OFFSET   = 0.05f;
        private const float  DESTINATION_X = 5f;
        private const float  BEYOND_LIMIT  = 1f;
        private const double START_TIME    = 10.0;
        private const double JUST_BEFORE   = 0.001;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CheckDestination_BeyondLimit_ReturnsTooFar()
        {
            MoveRequestThrottle throttle = CreateThrottle();
            var                 far      = new Vector3(Constants.Player.MAX_MOVE_DESTINATION_DISTANCE + BEYOND_LIMIT, 0f, 0f);

            Assert.AreEqual(EMoveAdmission.TooFar, throttle.CheckDestination(Vector3.zero, far, false));
        }

        [Test]
        public void CheckDestination_AtLimit_IsAccepted()
        {
            MoveRequestThrottle throttle = CreateThrottle();
            var                 edge     = new Vector3(Constants.Player.MAX_MOVE_DESTINATION_DISTANCE, 0f, 0f);

            Assert.AreEqual(EMoveAdmission.Accepted, throttle.CheckDestination(Vector3.zero, edge, false));
        }

        [Test]
        public void CheckDestination_SameAsActiveWhileMoving_ReturnsSameDestination()
        {
            MoveRequestThrottle throttle    = CreateThrottle();
            var                 destination = new Vector3(DESTINATION_X, 0f, 0f);
            throttle.MarkAttempt(START_TIME, true, destination);

            var nearby = new Vector3(DESTINATION_X + NEAR_OFFSET, 0f, 0f);
            Assert.AreEqual(EMoveAdmission.SameDestination, throttle.CheckDestination(Vector3.zero, nearby, true));

            // 도착해 멈췄으면 같은 곳이라도 다시 받는다
            Assert.AreEqual(EMoveAdmission.Accepted, throttle.CheckDestination(Vector3.zero, nearby, false));
        }

        [Test]
        public void IsRepathReady_InsideMinInterval_ReturnsFalse()
        {
            MoveRequestThrottle throttle = CreateThrottle();
            throttle.MarkAttempt(START_TIME, true, Vector3.zero);
            double readyAt = START_TIME + Constants.Player.MOVE_REPATH_MIN_INTERVAL_SECONDS;

            Assert.IsFalse(throttle.IsRepathReady(readyAt - JUST_BEFORE));
            Assert.IsTrue(throttle.IsRepathReady(readyAt));
        }

        [Test]
        public void MarkAttempt_FailedPath_StillConsumesInterval()
        {
            MoveRequestThrottle throttle = CreateThrottle();
            throttle.MarkAttempt(START_TIME, false, Vector3.zero);

            Assert.IsFalse(throttle.IsRepathReady(START_TIME));
            Assert.IsFalse(throttle.HasActiveDestination);
        }

        [Test]
        public void PathRequestBudget_PerTickLimit_DefersRest()
        {
            var budget = new PathRequestBudget(Constants.Player.PATH_REQUESTS_PER_TICK);
            budget.BeginTick();

            for (int i = 0; i < Constants.Player.PATH_REQUESTS_PER_TICK; ++i)
            {
                Assert.IsTrue(budget.TryConsume());
            }

            Assert.IsFalse(budget.TryConsume());

            budget.BeginTick();
            Assert.IsTrue(budget.TryConsume());
        }

        [Test]
        public void PathRequestBudget_BeforeFirstTick_HasNoBudget()
        {
            var budget = new PathRequestBudget(Constants.Player.PATH_REQUESTS_PER_TICK);

            Assert.IsFalse(budget.TryConsume());
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static MoveRequestThrottle CreateThrottle()
        {
            return new MoveRequestThrottle(Constants.Player.MAX_MOVE_DESTINATION_DISTANCE,
                Constants.Player.MOVE_REPATH_MIN_INTERVAL_SECONDS, Constants.Player.MOVE_SAME_DESTINATION_EPSILON_SQR);
        }
    }
}
