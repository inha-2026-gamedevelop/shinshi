// System
using System;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 무철 조준 회전(MucheolYawMotion) 계산 검증. 수치는 모두 이 테스트가 주입하는 시험 데이터이며 밸런스 값이 아니다.
    // 요는 위에서 볼 때 +z에서 시계 방향이 양수(오른쪽)다
    public class MucheolYawMotionTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  YAW_TOLERANCE   = 0.01f;
        private const double ARRIVAL         = 0.4;   // 초, 방향 고정 시각
        private const double FINE_STEP       = 0.016; // 초, 한 프레임
        private const float  NOMINAL_RATE    = 150f;  // 도/초
        private const float  MIN_HORIZON     = 0.1f;  // 초
        private const float  PEAK_FACTOR     = 1.875f;
        private const float  HYSTERESIS      = 20f;

        /****************************************
        *               Helpers
        ****************************************/

        private static MucheolYawMotion MakeAt(float yaw)
        {
            MucheolYawMotion motion = new MucheolYawMotion();
            motion.Reset(yaw, 0.0);
            return motion;
        }

        private static void Run(MucheolYawMotion motion, float goal, double from, double to, double step)
        {
            int count = (int)Math.Round((to - from) / step);
            for (int i = 1; i <= count; ++i)
            {
                motion.Advance(from + (i * step), goal, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
            }
        }

        private static float Delta(float from, float to)
        {
            return Mathf.DeltaAngle(from, to);
        }

        /****************************************
        *            Reach At Arrival
        ****************************************/

        // 정지한 목표는 맞은편이든 아니든 도착 시각에 정확히 닿고 그때 속도는 0이다
        [TestCase(0f, 90f)]
        [TestCase(0f, -90f)]
        [TestCase(0f, 180f)]
        [TestCase(0f, 179f)]
        [TestCase(0f, -179f)]
        [TestCase(0f, 10f)]
        [TestCase(120f, -150f)]
        [TestCase(-170f, 170f)]
        public void Advance_StaticGoal_ReachesGoalAtArrivalWithZeroSpeed(float start, float goal)
        {
            MucheolYawMotion motion = MakeAt(start);

            Run(motion, goal, 0.0, ARRIVAL, FINE_STEP);
            motion.Advance(ARRIVAL, goal, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(0f, Delta(goal, motion.Yaw), YAW_TOLERANCE);
            Assert.AreEqual(0f, motion.Velocity, YAW_TOLERANCE);
            Assert.IsTrue(motion.IsAtRest);
        }

        // 한 번의 큰 틱으로 건너뛰어도 도착 시각 이후에는 목표에 닿아 있다
        [TestCase(180f)]
        [TestCase(-90f)]
        public void Advance_SingleHugeTick_ArrivesAtGoal(float goal)
        {
            MucheolYawMotion motion = MakeAt(0f);

            motion.Advance(10.0, goal, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(0f, Delta(goal, motion.Yaw), YAW_TOLERANCE);
            Assert.IsTrue(motion.IsAtRest);
        }

        // 시작과 끝이 급격하게 튀지 않는다: 첫 프레임의 속도는 최고 속도보다 훨씬 작고, 최고 속도는 이동각 / 시간 x 1.875다
        [Test]
        public void Advance_StaticGoal_StartsAndEndsGentlyWithExpectedPeakSpeed()
        {
            const float TARGET = 120f;
            MucheolYawMotion motion = MakeAt(0f);

            float peak       = 0f;
            float firstSpeed = 0f;
            float lastSpeed  = 0f;
            float lastYaw    = 0f;
            int   count      = (int)Math.Round(ARRIVAL / FINE_STEP);
            for (int i = 1; i <= count; ++i)
            {
                motion.Advance(i * FINE_STEP, TARGET, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
                float speed = Mathf.Abs(motion.Velocity);
                if (i == 1)
                {
                    firstSpeed = speed;
                }

                peak      = Mathf.Max(peak, speed);
                lastSpeed = speed;

                // 한 방향으로만 돈다
                Assert.GreaterOrEqual(Delta(lastYaw, motion.Yaw), -YAW_TOLERANCE, "되돌아 돈다");
                lastYaw = motion.Yaw;
            }

            float expectedPeak = PEAK_FACTOR * TARGET / (float)ARRIVAL;
            Assert.AreEqual(expectedPeak, peak, expectedPeak * 0.03f);
            Assert.Less(firstSpeed, peak * 0.1f, "시작 속도가 튄다");
            Assert.Less(lastSpeed, peak * 0.15f, "끝 속도가 튄다");
        }

        // 아주 작은 각도는 도착 시각을 다 쓰지 않고 기준 회전 속도로 가되 최소 시간보다 빠르지 않다
        [Test]
        public void Advance_SmallAngle_UsesMinimumHorizonNotTheWholeWindow()
        {
            MucheolYawMotion motion = MakeAt(0f);

            Run(motion, 5f, 0.0, MIN_HORIZON + 0.02, FINE_STEP / 2.0);

            Assert.AreEqual(5f, motion.Yaw, YAW_TOLERANCE);
            Assert.IsTrue(motion.IsAtRest);
        }

        /****************************************
        *            Shortest Side
        ****************************************/

        // 최단 경로의 방향으로 돈다(오른쪽 +, 왼쪽 -), 각도 감김을 넘어도 마찬가지다
        [TestCase(0f, 100f, 1)]
        [TestCase(0f, -100f, -1)]
        [TestCase(0f, 170f, 1)]
        [TestCase(0f, -170f, -1)]
        [TestCase(170f, -170f, 1)]
        [TestCase(-170f, 170f, -1)]
        [TestCase(90f, -80f, -1)]
        [TestCase(-90f, 80f, 1)]
        public void Advance_ChoosesTheShorterSide(float start, float goal, int expectedSide)
        {
            MucheolYawMotion motion = MakeAt(start);

            motion.Advance(FINE_STEP * 3.0, goal, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(expectedSide, motion.Side);
            Assert.AreEqual(expectedSide, Math.Sign(Delta(start, motion.Yaw)));
        }

        // 정확히 맞은편이면 시작 각도와 상관없이 늘 오른쪽으로 돈다
        [TestCase(0f, 180f)]
        [TestCase(0f, -180f)]
        [TestCase(90f, -90f)]
        [TestCase(-45f, 135f)]
        [TestCase(170f, -10f)]
        public void Advance_ExactlyOpposite_AlwaysTurnsRight(float start, float goal)
        {
            MucheolYawMotion motion = MakeAt(start);

            motion.Advance(FINE_STEP * 3.0, goal, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(1, motion.Side);
            Assert.Greater(Delta(start, motion.Yaw), 0f);
        }

        // 맞은편 목표를 따라 돌던 도중 목표가 정확히 맞은편 쪽으로 다시 와도 돌던 방향이 유지된다
        [Test]
        public void Advance_OppositeWhileTurning_KeepsTheTurningSide()
        {
            MucheolYawMotion motion = MakeAt(0f);

            // 왼쪽으로 돌기 시작한다
            Run(motion, -120f, 0.0, 0.1, FINE_STEP);
            Assert.AreEqual(-1, motion.Side);
            float yawBefore = motion.Yaw;

            // 목표가 지금 요의 정반대로 간다
            float opposite = yawBefore + 180f;
            motion.Advance(0.1 + FINE_STEP, opposite, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(-1, motion.Side, "정반대에서 돌던 방향이 뒤집혔다");
        }

        /****************************************
        *            Rear Boundary
        ****************************************/

        // 이미 오른쪽으로 돌던 중 목표가 뒤쪽 경계를 넘어 반대편(왼쪽 최단)이 되어도 한도 안이면 방향을 뒤집지 않는다
        [Test]
        public void Advance_TargetCrossesRearBoundary_WithinHysteresis_DoesNotReverse()
        {
            MucheolYawMotion motion = MakeAt(0f);

            Run(motion, 170f, 0.0, 0.1, FINE_STEP);
            Assert.AreEqual(1, motion.Side);

            // 목표가 지금 요의 왼쪽 170도(뒤쪽 경계 너머)로 넘어간다. 오른쪽으로 계속 가면 190도라 한도(180 + 히스테리시스) 안이다
            float yawBefore = motion.Yaw;
            float crossed   = yawBefore - 170f;
            motion.Advance(0.1 + FINE_STEP, crossed, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
            motion.Advance(0.1 + (FINE_STEP * 2.0), crossed, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(1, motion.Side);
            Assert.Greater(Delta(yawBefore, motion.Yaw), 0f, "경계를 넘자 되돌아 돈다");
        }

        // 한도를 넘어 긴 길이 되면 짧은 쪽으로 바꾼다
        [Test]
        public void Advance_TargetFarBeyondRearBoundary_ReversesToTheShorterSide()
        {
            MucheolYawMotion motion = MakeAt(0f);

            Run(motion, 90f, 0.0, 0.1, FINE_STEP);
            Assert.AreEqual(1, motion.Side);

            // 지금 요에서 오른쪽 길이가 180 + 히스테리시스를 넘는 -100으로 간다
            Run(motion, -100f, 0.1, ARRIVAL, FINE_STEP);
            motion.Advance(ARRIVAL, -100f, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(0f, Delta(-100f, motion.Yaw), YAW_TOLERANCE);
        }

        // 목표가 뒤쪽 경계에서 처음 몇 프레임 동안 프레임마다 왔다 갔다 해도 회전 방향이 뒤집히지 않고 떨지 않는다
        [Test]
        public void Advance_TargetJittersAcrossRearBoundary_NeverFlipsDirection()
        {
            const int JITTER_FRAMES = 6;

            MucheolYawMotion motion = MakeAt(0f);

            float previous  = 0f;
            int   firstSide = 0;
            int   reversals = 0;
            int   count     = (int)Math.Round(ARRIVAL / FINE_STEP);
            for (int i = 1; i <= count; ++i)
            {
                float goal = ((i > JITTER_FRAMES) || ((i % 2) == 0)) ? 178f : -178f;
                motion.Advance(i * FINE_STEP, goal, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

                int stepSide = (Mathf.Abs(Delta(previous, motion.Yaw)) > YAW_TOLERANCE) ? Math.Sign(Delta(previous, motion.Yaw)) : 0;
                if (firstSide == 0)
                {
                    firstSide = stepSide;
                }
                else if ((stepSide != 0) && (stepSide != firstSide))
                {
                    ++reversals;
                }

                previous = motion.Yaw;
            }

            Assert.AreEqual(0, reversals, "요가 되돌아 도는 프레임이 있다");
            Assert.AreNotEqual(0, firstSide);
        }

        /****************************************
        *            Tick Independence
        ****************************************/

        // 같은 정지 목표라면 틱 크기와 상관없이 같은 곡선 위의 같은 값이 나온다
        [Test]
        public void Advance_StaticGoal_TickSizeDoesNotChangeTheCurve()
        {
            MucheolYawMotion fine   = MakeAt(0f);
            MucheolYawMotion coarse = MakeAt(0f);

            Run(fine, 160f, 0.0, 0.2, 0.002);
            coarse.Advance(0.1, 160f, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
            coarse.Advance(0.2, 160f, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(fine.Yaw, coarse.Yaw, YAW_TOLERANCE);
            Assert.AreEqual(fine.Velocity, coarse.Velocity, 0.5f);
        }

        // 시각이 되감기거나 그대로면 아무 일도 없다
        [Test]
        public void Advance_NonIncreasingTime_DoesNothing()
        {
            MucheolYawMotion motion = MakeAt(30f);

            motion.Advance(0.0, 120f, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
            motion.Advance(-1.0, 120f, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

            Assert.AreEqual(30f, motion.Yaw, YAW_TOLERANCE);
        }

        /****************************************
        *            Moving Goal
        ****************************************/

        // 도는 도중 목표가 갑자기 바뀌어도 속도가 끊기지 않고 이어진다(한 프레임의 속도 변화는 최고 속도보다 훨씬 작다)
        [Test]
        public void Advance_GoalJumpsMidTurn_VelocityStaysContinuous()
        {
            MucheolYawMotion motion = MakeAt(0f);
            Run(motion, 90f, 0.0, 0.15, FINE_STEP / 2.0);

            float before = motion.Velocity;
            motion.Advance(0.15 + (FINE_STEP / 2.0), -60f, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
            float after = motion.Velocity;

            Assert.Greater(Mathf.Abs(before), 100f, "시험 전제: 도는 중이다");
            Assert.Less(Mathf.Abs(after - before), Mathf.Abs(before) * 0.2f, "목표가 바뀌자 속도가 튀었다");
        }

        // 움직이는 목표를 쫓다가 도착 시각 직전(0.12초 전)에 갱신을 멈추면 매 프레임 속도 변화가 작고, 도착 시각에는 마지막 목표에 정확히 닿는다.
        // 도착 순간까지 계속 움직이는 목표를 쫓는 경우는 시험하지 않는다: 유한한 속도로 그렇게 하면서 부드러울 수는 없어서 목표 갱신 중단 구간을 둔다
        [Test]
        public void Advance_MovingGoalThenFrozen_SmoothUntilArrivalThenExact()
        {
            const double FREEZE_AT = ARRIVAL - 0.12;

            MucheolYawMotion motion = MakeAt(0f);

            float previousSpeed = 0f;
            float maxSpeedStep  = 0f;
            float peak          = 0f;
            float goal          = 0f;
            int   count         = (int)Math.Round(ARRIVAL / FINE_STEP);
            for (int i = 1; i <= count; ++i)
            {
                double time = i * FINE_STEP;
                goal = 20f + (140f * (float)(Math.Min(time, FREEZE_AT) / ARRIVAL)); // 20도에서 시작해 일정하게 움직이다 멈춘다
                motion.Advance(time, goal, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);

                float speed = motion.Velocity;
                maxSpeedStep = Mathf.Max(maxSpeedStep, Mathf.Abs(speed - previousSpeed));
                peak         = Mathf.Max(peak, Mathf.Abs(speed));
                previousSpeed = speed;
            }

            Assert.AreEqual(0f, Delta(goal, motion.Yaw), YAW_TOLERANCE);
            Assert.Less(maxSpeedStep, peak * 0.5f, "한 프레임에 속도가 크게 튄다");
        }

        /****************************************
        *            Reset
        ****************************************/

        [Test]
        public void Reset_ClearsSpeedAndSideMemory()
        {
            MucheolYawMotion motion = MakeAt(0f);
            Run(motion, -120f, 0.0, 0.1, FINE_STEP);
            Assert.AreEqual(-1, motion.Side);

            motion.Reset(0f, 1.0);

            Assert.AreEqual(0, motion.Side);
            Assert.AreEqual(0f, motion.Velocity, YAW_TOLERANCE);
            Assert.IsTrue(motion.IsAtRest);

            // 기억이 지워졌으니 맞은편은 다시 오른쪽이다
            motion.Advance(1.0 + FINE_STEP, 180f, 1.0 + ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
            Assert.AreEqual(1, motion.Side);
        }

        // 히스테리시스 상수가 문서와 같은지 한 곳에서 보이게 한다(바뀌면 이 시험이 알려 준다)
        [Test]
        public void Hysteresis_IsTwentyDegrees()
        {
            MucheolYawMotion motion = MakeAt(0f);
            Run(motion, 100f, 0.0, 0.05, FINE_STEP / 2.0);
            float yaw = motion.Yaw;

            // 오른쪽으로 돌던 도중 목표의 오른쪽 길이가 한도 직전(180 + 히스테리시스 - 여유)인 곳으로 간다
            float justInside = yaw + 180f + HYSTERESIS - 2f;
            motion.Advance(0.05 + FINE_STEP, justInside, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
            Assert.AreEqual(1, motion.Side, "한도 안인데 방향을 바꿨다");

            // 한도 직후로 가면 짧은 쪽으로 바꾼다
            float justOutside = motion.Yaw + 180f + HYSTERESIS + 2f;
            motion.Advance(0.05 + (FINE_STEP * 2.0), justOutside, ARRIVAL, NOMINAL_RATE, MIN_HORIZON);
            Assert.AreEqual(-1, motion.Side, "한도를 넘었는데 방향을 유지했다");
        }
    }
}
