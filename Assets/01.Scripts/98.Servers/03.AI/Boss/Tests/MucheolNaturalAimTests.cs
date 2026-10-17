// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 무철 방향성 공격의 최단 경로 부드러운 조준 검증. 실제 팩토리가 만든 패턴을 공격 FSM에 올려 확인한다.
    // 정의는 MucheolDevCatalog의 임시 시작값을 그대로 쓴다. 각도는 위에서 볼 때 +z에서 시계 방향이 양수(오른쪽)다
    public class MucheolNaturalAimTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TEST_HREF      = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  ARENA_DIAMETER = 44f;
        private const float  YAW_TOLERANCE  = 0.3f;
        private const double STEP           = 0.02;  // 초, 한 프레임
        private const double AFTER          = 0.05;  // 초, 경계를 넘긴 여유
        private const float  TARGET_DISTANCE = 10f;
        private const float  BYSTANDER_DISTANCE = 3f;
        private const float  PEAK_MARGIN    = 1.15f;
        private const float  PEAK_FACTOR    = 1.875f;
        private const float  SETTLE_MAX     = 0.15f;
        private const float  SETTLE_RATIO   = 0.3f;
        private const int    TARGET_ID      = 10;
        private const int    OTHER_ID       = 11;
        private const int    NEAR_A_ID      = 21;
        private const int    NEAR_B_ID      = 22;

        private static readonly string[] G_NaturalKeys = { "ph1_p01", "ph1_p02", "ph1_p03", "ph1_p05", "ph2_p01", "ph2_p02", "ph2_p05" };
        private static readonly string[] G_SnapKeys    = { "ph1_p06", "ph2_p03", "ph2_p06" };
        private static readonly float[]  G_TargetAngles = { 0f, 45f, 90f, -90f, 170f, -170f, 180f };

        /****************************************
        *               Helpers
        ****************************************/

        private static float YawOf(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static float FreezeSeconds(float tracking)
        {
            return tracking - Mathf.Min(SETTLE_MAX, tracking * SETTLE_RATIO);
        }

        /// <summary> 조준 방향 알림을 기록하는 가짜, 부채꼴, 돌진, 콤보, 순차 타격 알림을 한 곳에서 받는다 </summary>
        private sealed class AimRecorder : IBossFanTelegraphListener, IBossDashTelegraphListener, IBossComboTelegraphListener,
            IMucheolSequenceTelegraphListener
        {
            public int     StartCount;
            public Vector3 StartDirection;
            public int     LockCount;
            public Vector3 LockDirection;
            public int     CancelCount;
            public int     HitEvents;
            public int     AimChangeCount;

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float a, float b, float telegraphSeconds)
            {
                ++StartCount;
                StartDirection = direction;
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
                IReadOnlyList<MucheolComboHit> hits)
            {
                ++StartCount;
                StartDirection = direction;
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
                IReadOnlyList<MucheolSequenceStep> steps)
            {
                ++StartCount;
                StartDirection = direction;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
                ++AimChangeCount;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                ++AimChangeCount;
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
                ++LockCount;
                LockDirection = direction;
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                ++LockCount;
                LockDirection = direction;
            }

            public void OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance, float seconds, float speed,
                double startServerTime)
            {
            }

            public void OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall, double stopServerTime)
            {
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                ++HitEvents;
            }

            public void OnHit(int castInstanceId, int hitIndex, int hitCount)
            {
                ++HitEvents;
            }

            public void OnStepHit(int castInstanceId, int stepIndex, int hitCount)
            {
                ++HitEvents;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                ++CancelCount;
            }
        }

        // 공격 FSM에 올린 최소 구성. 보스는 원점에서 +z를 보고, 추적 대상(10번)은 원점에서 10m 거리에 선다
        private sealed class Rig
        {
            public FakeCombatTarget   Self       { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget   Tracked    { get; } = new FakeCombatTarget { CombatId = TARGET_ID, Faction = ECombatFaction.Player };
            public Phase2Query        Query      { get; } = new Phase2Query();
            public FakeDamageResolver Resolver   { get; } = new FakeDamageResolver();
            public AimRecorder        Recorder   { get; } = new AimRecorder();
            public DashRecorder       DashLog    { get; } = new DashRecorder();
            public Phase2Movement     Movement   { get; } = new Phase2Movement();
            public BossAttackFsm      Fsm        { get; } = new BossAttackFsm();
            public BossDamageGate     Gate       { get; }
            public PatternDefinition  Definition { get; }
            public IBossPattern       Pattern    { get; }
            public BossPatternContext Context    { get; }

            public List<double> Times { get; } = new List<double>();
            public List<float>  Yaws  { get; } = new List<float>();

            public double TrackingSeconds => Definition.TrackingSeconds;

            public Rig(string key, float targetAngle = 0f)
            {
                Tracked.Position = Phase2Helpers.Polar(targetAngle, TARGET_DISTANCE);
                Query.Players.Add(Tracked);
                Movement.Self = Self;
                Gate          = new BossDamageGate(Resolver);
                Gate.Open();

                MucheolDevCatalog catalog = new MucheolDevCatalog(ARENA_DIAMETER, true);
                MucheolPatternFactory phase1 = new MucheolPatternFactory(TEST_HREF, Recorder, Movement, Recorder, null,
                    new MucheolComboSpec(new MucheolComboHit(5f, 120f, 0.14f, 0f), new MucheolComboHit(5f, 70f, 0.24f, 0.5f)), Recorder,
                    new MucheolChargeSpec(2.5f));
                MucheolPhase2PatternFactory phase2 = new MucheolPhase2PatternFactory(TEST_HREF, MucheolDevCatalog.CreatePhaseTwoSpec(),
                    new FakeBossVitals(), catalog, Movement, Recorder, DashLog);

                switch (key)
                {
                    case "ph1_p01":
                        Definition = catalog.Find(MucheolPatternFactory.P01_ID);
                        Pattern    = phase1.Create(Definition);
                        break;
                    case "ph1_p02":
                        Definition = catalog.Find(MucheolPatternFactory.P02_ID);
                        Pattern    = phase1.Create(Definition);
                        break;
                    case "ph1_p03":
                        Definition = catalog.Find(MucheolPatternFactory.P03_ID);
                        Pattern    = phase1.Create(Definition);
                        break;
                    case "ph1_p05":
                        Definition = catalog.Find(MucheolPatternFactory.P05_ID);
                        Pattern    = phase1.Create(Definition);
                        break;
                    case "ph1_p06":
                        Definition = catalog.Find(MucheolPatternFactory.P06_ID);
                        Pattern    = phase1.Create(Definition);
                        break;
                    case "ph2_p01":
                        Definition = catalog.Find(MucheolPhase2PatternFactory.P01_ID);
                        Pattern    = phase2.Create(Definition);
                        break;
                    case "ph2_p02":
                        Definition = catalog.Find(MucheolPhase2PatternFactory.P02_ID);
                        Pattern    = phase2.Create(Definition);
                        break;
                    case "ph2_p03":
                        Definition = catalog.Find(MucheolPhase2PatternFactory.P03_ID);
                        Pattern    = phase2.Create(Definition);
                        break;
                    case "ph2_p05":
                        Definition = catalog.Find(MucheolPhase2PatternFactory.P05_ID);
                        Pattern    = phase2.Create(Definition);
                        break;
                    case "ph2_p06":
                        Definition = catalog.Find(MucheolPhase2PatternFactory.P06_ID);
                        Pattern    = phase2.Create(Definition);
                        break;
                    default:
                        throw new ArgumentException(key);
                }

                Assert.IsNotNull(Pattern, key);
                Context = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public Vector3 Aim
            {
                get
                {
                    switch (Pattern)
                    {
                        case BossFanBlastPattern fan:
                            return fan.AimDirection;
                        case BossDashPattern dash:
                            return dash.AimDirection;
                        case MucheolComboPattern combo:
                            return combo.AimDirection;
                        case MucheolSequencePattern sequence:
                            return sequence.AimDirection;
                        case MucheolChargePattern charge:
                            return charge.AimDirection;
                        case MucheolMultiDashPattern multi:
                            return multi.AimDirection;
                        default:
                            throw new InvalidOperationException(Pattern.GetType().Name);
                    }
                }
            }

            public float AimYaw => YawOf(Aim);

            public FakeCombatTarget AddPlayer(int id, Vector3 position)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Players.Add(player);
                return player;
            }

            public void Start(double serverTime = 0.0)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
                Sample(serverTime);
            }

            // from 다음 틱부터 to까지 한 프레임씩 틱하며 매번 조준 방향을 기록한다. 매 틱 앞에서 onFrame이 있으면 불러 대상을 움직인다
            public void TickRange(double from, double to, Action<double> onFrame = null)
            {
                int count = (int)Math.Round((to - from) / STEP);
                for (int i = 1; i <= count; ++i)
                {
                    double time = from + (i * STEP);
                    onFrame?.Invoke(time);
                    Fsm.Tick(time);
                    Sample(time);
                }
            }

            public void TickOnce(double time)
            {
                Fsm.Tick(time);
                Sample(time);
            }

            private void Sample(double time)
            {
                Times.Add(time);
                Yaws.Add(AimYaw);
            }

            public int Count(int targetId)
            {
                return Phase2Helpers.RequestsFor(Resolver, targetId).Count;
            }
        }

        /****************************************
        *       Shortest Path And Lock Target
        ****************************************/

        // 정면, 좌우 측면, 정반대, 뒤쪽 경계 양쪽 어디에 있어도 최단 경로로 돌아 방향 고정 시각까지 대상 방향에 닿고, 한 번만 고정한다.
        // 한 방향으로만 돌고(되돌지 않고) 필요한 각도보다 더 돌지 않으며, 맞은편은 오른쪽이다
        [Test]
        public void Natural_TargetAtAngle_TurnsTheShortestWayAndLocksOnTheTarget(
            [ValueSource(nameof(G_NaturalKeys))] string key, [ValueSource(nameof(G_TargetAngles))] float angle)
        {
            Rig rig = new Rig(key, angle);
            rig.Start();
            Assert.AreEqual(0f, rig.AimYaw, YAW_TOLERANCE, "시작 방향은 보스의 현재 방향이다");

            float tracking = (float)rig.TrackingSeconds;
            rig.TickRange(0.0, tracking + AFTER);

            int expectedSide = (Mathf.Abs(angle) >= 180f - 0.001f) ? 1 : (int)Mathf.Sign(angle);
            float walked = 0f;
            float peakStep = 0f;
            for (int i = 1; i < rig.Yaws.Count; ++i)
            {
                float step = Mathf.DeltaAngle(rig.Yaws[i - 1], rig.Yaws[i]);
                if (angle != 0f)
                {
                    Assert.GreaterOrEqual(step * expectedSide, -0.01f, $"{key} {angle}도: {i}번째 프레임에서 되돌아 돈다");
                }

                walked += Mathf.Abs(step);
                peakStep = Mathf.Max(peakStep, Mathf.Abs(step));
            }

            Assert.AreEqual(Mathf.Abs(angle), walked, 1f, $"{key} {angle}도: 필요한 각도만큼만 돌아야 한다");
            Assert.AreEqual(0f, Mathf.DeltaAngle(angle, rig.AimYaw), YAW_TOLERANCE, $"{key} {angle}도: 고정 방향이 대상 방향이 아니다");

            float limit = Mathf.Max(rig.Definition.TurnRate, PEAK_FACTOR * Mathf.Abs(angle) / tracking) * (float)STEP * PEAK_MARGIN;
            Assert.LessOrEqual(peakStep, limit + 0.01f, $"{key} {angle}도: 한 프레임에 너무 많이 돈다");

            Assert.AreEqual(1, rig.Recorder.LockCount);
            Assert.AreEqual(0f, Vector3.Angle(rig.Aim, rig.Recorder.LockDirection), YAW_TOLERANCE);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.forward, rig.Recorder.StartDirection), YAW_TOLERANCE);
        }

        // 도착은 방향 고정 시각 안이다: 고정 시각의 첫 틱에서 이미 대상 방향이다(고정 시각을 바꾸지 않는다)
        [Test]
        public void Natural_AimReachesTheTargetByTheLockTime([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig rig = new Rig(key, 180f);
            rig.Start();

            float tracking = (float)rig.TrackingSeconds;
            rig.TickRange(0.0, tracking - (float)STEP * 1.5f);
            Assert.IsFalse(IsLocked(rig), "고정 시각 전이다");

            rig.TickOnce(tracking);

            Assert.IsTrue(IsLocked(rig));
            Assert.AreEqual(0f, Mathf.DeltaAngle(180f, rig.AimYaw), YAW_TOLERANCE);
        }

        private static bool IsLocked(Rig rig)
        {
            return rig.Recorder.LockCount > 0;
        }

        /****************************************
        *          Moving And Frozen Target
        ****************************************/

        // 대상이 뒤쪽 경계(정반대 근처)에서 처음 몇 프레임 동안 좌우를 오가도 회전 방향이 뒤집히지 않고 떨지 않으며, 자리를 잡으면 거기서 고정한다
        [Test]
        public void Natural_TargetJittersAcrossTheRearBoundary_NeverFlipsDirection([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            const int JITTER_FRAMES = 5;

            Rig rig = new Rig(key, 175f);
            rig.Start();

            rig.TickRange(0.0, rig.TrackingSeconds + AFTER, time =>
            {
                int  frame = (int)Math.Round(time / STEP);
                bool left  = (frame < JITTER_FRAMES) && ((frame % 2) == 0);
                rig.Tracked.Position = Phase2Helpers.Polar(left ? -175f : 175f, TARGET_DISTANCE);
            });

            int firstSide = 0;
            for (int i = 1; i < rig.Yaws.Count; ++i)
            {
                float step = Mathf.DeltaAngle(rig.Yaws[i - 1], rig.Yaws[i]);
                if (Mathf.Abs(step) <= 0.01f)
                {
                    continue;
                }

                int side = (int)Mathf.Sign(step);
                firstSide = (firstSide == 0) ? side : firstSide;
                Assert.AreEqual(firstSide, side, $"{key}: {i}번째 프레임에서 회전 방향이 바뀐다");
            }

            Assert.AreNotEqual(0, firstSide);
            Assert.AreEqual(0f, Mathf.DeltaAngle(175f, rig.AimYaw), YAW_TOLERANCE, key);
            Assert.AreEqual(1, rig.Recorder.LockCount);
        }

        // 목표 갱신을 멈춘 뒤(고정 직전 구간)에는 대상이 반대편으로 가도 마지막으로 읽은 방향에서 고정된다
        [Test]
        public void Natural_TargetMovesAfterTheFreezePoint_LockedAimIgnoresIt([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig rig = new Rig(key, 90f);
            rig.Start();

            float tracking = (float)rig.TrackingSeconds;
            double afterFreeze = FreezeSeconds(tracking) + (STEP * 2.0);
            rig.TickRange(0.0, afterFreeze);

            // 목표 갱신을 멈춘 뒤에 대상이 반대편으로 간다
            rig.Tracked.Position = Phase2Helpers.Polar(-90f, TARGET_DISTANCE);
            rig.TickRange(afterFreeze, tracking + AFTER);

            Assert.AreEqual(0f, Mathf.DeltaAngle(90f, rig.AimYaw), YAW_TOLERANCE, $"{key}: 멈춘 뒤의 대상 위치를 따라갔다");
            Assert.AreEqual(1, rig.Recorder.LockCount);
        }

        // 목표 갱신을 멈추기 전에 대상이 움직이면 그 위치를 따라가 마지막으로 읽은 위치에서 고정된다
        [Test]
        public void Natural_TargetMovesBeforeTheFreezePoint_LockedAimFollowsIt([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig rig = new Rig(key, 90f);
            rig.Start();

            rig.TickRange(0.0, STEP * 3.0);
            rig.Tracked.Position = Phase2Helpers.Polar(-60f, TARGET_DISTANCE);
            rig.TickRange(STEP * 3.0, rig.TrackingSeconds + AFTER);

            Assert.AreEqual(0f, Mathf.DeltaAngle(-60f, rig.AimYaw), YAW_TOLERANCE, key);
        }

        // 대상이 계속 움직여도 고정 방향은 목표 갱신을 멈춘 첫 틱에서 읽은 방향이고, 방향이 한 프레임에 튀지 않는다
        [Test]
        public void Natural_TargetKeepsMoving_LockedAimIsTheLastSampleAndNeverJumps([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            const float ORBIT_RATE = 120f; // 도/초
            Rig rig = new Rig(key, 0f);
            rig.Start();

            float  tracking   = (float)rig.TrackingSeconds;
            double freezeAt   = FreezeSeconds(tracking);
            double sampleTime = -1.0;
            rig.TickRange(0.0, tracking + AFTER, time =>
            {
                if ((sampleTime < 0.0) && (time >= freezeAt))
                {
                    sampleTime = time; // 이 틱의 대상 위치가 마지막으로 읽히는 위치다
                }

                float orbit = (sampleTime < 0.0 || time <= sampleTime) ? ORBIT_RATE * (float)time : ORBIT_RATE * (float)sampleTime + 90f;
                rig.Tracked.Position = Phase2Helpers.Polar(orbit, TARGET_DISTANCE);
            });

            float expected = ORBIT_RATE * (float)sampleTime;
            Assert.AreEqual(0f, Mathf.DeltaAngle(expected, rig.AimYaw), YAW_TOLERANCE, $"{key}: 고정 방향이 마지막으로 읽은 방향이 아니다");

            float peakStep = 0f;
            float previousStep = 0f;
            float peakStepChange = 0f;
            for (int i = 1; i < rig.Yaws.Count; ++i)
            {
                float step = Mathf.DeltaAngle(rig.Yaws[i - 1], rig.Yaws[i]);
                peakStep       = Mathf.Max(peakStep, Mathf.Abs(step));
                peakStepChange = Mathf.Max(peakStepChange, Mathf.Abs(step - previousStep));
                previousStep   = step;
            }

            Assert.Less(peakStepChange, peakStep * 0.8f, $"{key}: 프레임 사이 회전량이 튄다");
        }

        // 고정 뒤에는 대상이 움직여도 공격 방향이 바뀌지 않는다
        [Test]
        public void Natural_AfterLock_TargetMovingNeverChangesTheDirection([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig rig = new Rig(key, 60f);
            rig.Start();
            rig.TickRange(0.0, rig.TrackingSeconds + AFTER);
            Vector3 locked = rig.Aim;
            int aimEvents = rig.Recorder.AimChangeCount;

            rig.Tracked.Position = Phase2Helpers.Polar(-120f, TARGET_DISTANCE);
            rig.TickRange(rig.TrackingSeconds + AFTER, rig.Definition.TelegraphSeconds + 0.2);

            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Aim), 0.01f);
            Assert.AreEqual(aimEvents, rig.Recorder.AimChangeCount);
            Assert.AreEqual(1, rig.Recorder.LockCount);
        }

        /****************************************
        *        Target Lost And Cancel
        ****************************************/

        // 도는 도중 대상이 죽거나 사라지면 마지막으로 읽은 방향으로 마저 돌고, 다른 플레이어로 바꾸지 않는다
        [Test]
        public void Natural_TargetDiesOrLeavesMidTurn_KeepsTheLastValidDirectionAndNeverSwitches(
            [ValueSource(nameof(G_NaturalKeys))] string key, [Values(false, true)] bool removeInsteadOfKill)
        {
            Rig rig = new Rig(key, 90f);
            rig.AddPlayer(OTHER_ID, Phase2Helpers.Polar(-90f, TARGET_DISTANCE));
            rig.Start();

            rig.TickRange(0.0, STEP * 3.0);
            if (removeInsteadOfKill)
            {
                rig.Query.Players.Remove(rig.Tracked);
            }
            else
            {
                rig.Tracked.LifeState = EParticipantLifeState.Dead;
            }

            rig.TickRange(STEP * 3.0, rig.TrackingSeconds + AFTER);

            Assert.AreEqual(0f, Mathf.DeltaAngle(90f, rig.AimYaw), YAW_TOLERANCE, $"{key}: 마지막 방향이 아니다");
            for (int i = 1; i < rig.Yaws.Count; ++i)
            {
                Assert.GreaterOrEqual(Mathf.DeltaAngle(rig.Yaws[i - 1], rig.Yaws[i]), -0.01f, $"{key}: 다른 플레이어 쪽으로 돈다");
            }

            Assert.AreEqual(1, rig.Recorder.LockCount);
        }

        // 시작할 때부터 대상이 없으면 돌지 않고 보스의 현재 방향에서 고정한다
        [Test]
        public void Natural_TargetGoneBeforeStart_KeepsTheCurrentFacing([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig rig = new Rig(key, 90f);
            rig.AddPlayer(OTHER_ID, Phase2Helpers.Polar(-90f, TARGET_DISTANCE));
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Start();

            rig.TickRange(0.0, rig.TrackingSeconds + AFTER);

            Assert.AreEqual(0f, rig.AimYaw, YAW_TOLERANCE, key);
            Assert.AreEqual(0, rig.Recorder.AimChangeCount);
            Assert.AreEqual(1, rig.Recorder.LockCount);
        }

        // 취소하면 조준도 타격도 더 나오지 않는다
        [Test]
        public void Natural_CancelDuringTracking_StopsAimingAndHitting([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig rig = new Rig(key, 120f);
            FakeCombatTarget near = rig.AddPlayer(NEAR_A_ID, Phase2Helpers.Polar(120f, BYSTANDER_DISTANCE));
            rig.Start();

            rig.TickRange(0.0, STEP * 3.0);
            rig.Fsm.Cancel(EBossInterruptReason.Death);
            Vector3 aimAtCancel = rig.Aim;
            int aimEvents = rig.Recorder.AimChangeCount;

            rig.TickRange(STEP * 3.0, rig.Definition.TelegraphSeconds + rig.Definition.ActiveSeconds + rig.Definition.RecoverySeconds + 0.2);

            Assert.AreEqual(1, rig.Recorder.CancelCount);
            Assert.AreEqual(0f, Vector3.Angle(aimAtCancel, rig.Aim), 0.01f);
            Assert.AreEqual(aimEvents, rig.Recorder.AimChangeCount);
            Assert.AreEqual(0, rig.Recorder.LockCount);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Count(near.CombatId));
        }

        // 한 틱이 예고 전체를 건너뛰어도 고정은 마지막으로 읽은 방향이고 한 번만 나온다
        [Test]
        public void Natural_HugeTick_LocksOnTheSampledTargetDirectionOnce([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig rig = new Rig(key, 135f);
            rig.Start();
            rig.Tracked.Position = Phase2Helpers.Polar(-100f, TARGET_DISTANCE);

            rig.TickOnce(rig.Definition.TelegraphSeconds + AFTER);

            Assert.AreEqual(0f, Mathf.DeltaAngle(-100f, rig.AimYaw), YAW_TOLERANCE, key);
            Assert.AreEqual(1, rig.Recorder.LockCount);
            Assert.AreEqual(0f, Vector3.Angle(rig.Aim, rig.Recorder.LockDirection), YAW_TOLERANCE);
        }

        // 큰 틱과 작은 틱은 정지한 대상에서 같은 고정 방향을 낸다
        [Test]
        public void Natural_StaticTarget_FineAndCoarseTicksLockTheSameDirection([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig fine   = new Rig(key, -150f);
            Rig coarse = new Rig(key, -150f);
            fine.Start();
            coarse.Start();

            fine.TickRange(0.0, fine.TrackingSeconds + AFTER);
            coarse.TickOnce(coarse.TrackingSeconds * 0.5);
            coarse.TickOnce(coarse.TrackingSeconds + AFTER);

            Assert.AreEqual(0f, Vector3.Angle(fine.Aim, coarse.Aim), YAW_TOLERANCE, key);
        }

        /****************************************
        *        Telegraph, Aim And Hit Agree
        ****************************************/

        // 부채꼴: 고정 방향에 선 플레이어만 맞고 반대편에 선 플레이어는 맞지 않는다(예고 방향과 타격 판정이 같다)
        [TestCase("ph1_p01")]
        [TestCase("ph1_p02")]
        [TestCase("ph1_p05")]
        public void Fan_HitsOnlyTheLockedDirection(string key)
        {
            const float TARGET_DEGREES = -110f;
            Rig rig = new Rig(key, TARGET_DEGREES);
            FakeCombatTarget onAim    = rig.AddPlayer(NEAR_A_ID, Phase2Helpers.Polar(TARGET_DEGREES, BYSTANDER_DISTANCE));
            FakeCombatTarget opposite = rig.AddPlayer(NEAR_B_ID, Phase2Helpers.Polar(TARGET_DEGREES + 180f, BYSTANDER_DISTANCE));
            rig.Start();

            rig.TickRange(0.0, rig.Definition.TelegraphSeconds + rig.Definition.ActiveSeconds + 1.0);

            Assert.AreEqual(0f, Mathf.DeltaAngle(TARGET_DEGREES, rig.AimYaw), YAW_TOLERANCE);
            Assert.GreaterOrEqual(rig.Count(onAim.CombatId), 1, key);
            Assert.AreEqual(0, rig.Count(opposite.CombatId), key);
        }

        // 순차 타격: 첫 조준(기준 방향)은 대상 쪽으로 돌고, 각 타의 상대 각도는 그대로 기준 방향에 더해진다.
        // 2페이즈 P01은 기준 방향 -30도 구역을 먼저, +30도 구역을 나중에 친다
        [Test]
        public void Sequence_StepOffsetsStayRelativeToTheNewBaseDirection()
        {
            const float TARGET_DEGREES = 140f;
            Rig rig = new Rig("ph2_p01", TARGET_DEGREES);
            FakeCombatTarget left  = rig.AddPlayer(NEAR_A_ID, Phase2Helpers.Polar(TARGET_DEGREES - 20f, BYSTANDER_DISTANCE));
            FakeCombatTarget right = rig.AddPlayer(NEAR_B_ID, Phase2Helpers.Polar(TARGET_DEGREES + 20f, BYSTANDER_DISTANCE));
            rig.Start();

            rig.TickRange(0.0, rig.Definition.TelegraphSeconds + rig.Definition.ActiveSeconds + 1.0);

            Assert.AreEqual(0f, Mathf.DeltaAngle(TARGET_DEGREES, rig.AimYaw), YAW_TOLERANCE, "기준 방향은 대상 방향이다");
            Assert.AreEqual(1, rig.Count(left.CombatId), "왼쪽 구역(기준 -30도)을 한 번 친다");
            Assert.AreEqual(1, rig.Count(right.CombatId), "오른쪽 구역(기준 +30도)을 한 번 친다");
        }

        // 돌진: 고정 방향의 경로 위에 선 플레이어만 맞고 반대편은 맞지 않으며, 보스는 고정 방향으로 간다
        [Test]
        public void Dash_MovesAndHitsAlongTheLockedDirection()
        {
            const float TARGET_DEGREES = 130f;
            Rig rig = new Rig("ph1_p03", TARGET_DEGREES);
            FakeCombatTarget onPath   = rig.AddPlayer(NEAR_A_ID, Phase2Helpers.Polar(TARGET_DEGREES, 4f));
            FakeCombatTarget opposite = rig.AddPlayer(NEAR_B_ID, Phase2Helpers.Polar(TARGET_DEGREES + 180f, 4f));
            rig.Start();

            rig.TickRange(0.0, rig.Definition.TelegraphSeconds + rig.Definition.ActiveSeconds + 0.3);

            Assert.AreEqual(0f, Mathf.DeltaAngle(TARGET_DEGREES, rig.AimYaw), YAW_TOLERANCE);
            Assert.AreEqual(0f, Vector3.Angle(rig.Aim, rig.Recorder.LockDirection), YAW_TOLERANCE);
            Assert.AreEqual(0f, Mathf.DeltaAngle(TARGET_DEGREES, YawOf(rig.Self.Position)), 0.5f, "보스가 고정 방향으로 가지 않았다");
            Assert.AreEqual(1, rig.Count(onPath.CombatId));
            Assert.AreEqual(0, rig.Count(opposite.CombatId));
        }

        /****************************************
        *      Dash Contract (Snap Is Unchanged)
        ****************************************/

        // 돌진(1페이즈 P06, 2페이즈 P03, P06)은 예고 시작부터 선택된 대상 방향을 그대로 알리고 고정 시각까지 따라가다 고정한다.
        // 부드러운 회전은 서버 방향이 아니라 몸의 표현이 맡는다
        [Test]
        public void Snap_DashPatterns_TelegraphExactTargetDirectionFromTheStart(
            [ValueSource(nameof(G_SnapKeys))] string key, [ValueSource(nameof(G_TargetAngles))] float angle)
        {
            Rig rig = new Rig(key, angle);
            rig.Start();

            Assert.AreEqual(0f, Mathf.DeltaAngle(angle, rig.AimYaw), YAW_TOLERANCE, $"{key} {angle}도: 예고 시작 방향");

            rig.TickRange(0.0, rig.Definition.TelegraphSeconds + AFTER);

            Assert.AreEqual(0f, Mathf.DeltaAngle(angle, rig.AimYaw), YAW_TOLERANCE, $"{key} {angle}도: 고정 방향");
        }

        /****************************************
        *          Scope And Regression
        ****************************************/

        // 부드러운 조준은 정의의 추적 시간(방향 고정 시각)을 도착 시각으로 쓴다. 방향이 있는 패턴 정의에 추적 시간이 있어야 돌 수 있으므로
        // 개발 카탈로그의 값이 0이 되면 조용히 돌지 않게 되는 것을 이 테스트가 알려 준다. 고정 시각은 정의의 값 그대로다
        [Test]
        public void DevCatalog_DirectionalPatterns_HaveATrackingWindowShorterThanTheTelegraph([ValueSource(nameof(G_NaturalKeys))] string key)
        {
            Rig rig = new Rig(key, 90f);

            Assert.Greater(rig.Definition.TrackingSeconds, 0f, key);
            Assert.Less(rig.Definition.TrackingSeconds, rig.Definition.TelegraphSeconds, key);

            rig.Start();
            rig.TickRange(0.0, rig.TrackingSeconds - (STEP * 1.5));
            Assert.AreEqual(0, rig.Recorder.LockCount, "정의의 추적 시간 전에 고정됐다");
            rig.TickOnce(rig.TrackingSeconds);
            Assert.AreEqual(1, rig.Recorder.LockCount, "정의의 추적 시간에 고정되지 않았다");
        }

        // 바로 조준과 부드러운 조준은 서로 다른 규칙이라 같이 켤 수 없다
        [Test]
        public void BossDashPattern_SnapAndNaturalTogether_Throws()
        {
            Assert.Throws<ArgumentException>(() => new BossDashPattern(TEST_HREF, new Phase2Movement(), null, true, true));
        }

        // 월백 팩토리가 만든 부채꼴은 부드러운 조준으로 대상 방향에 닿고, 옵트인 없이 직접 만들면 기존대로 회전 속도 x 추적 시간까지만 돈다
        [TestCase(true, 180f)]
        [TestCase(false, 54f)]
        public void Wolbaek_FanNaturalAimIsOptIn_FactoryReachesTargetDefaultKeepsLimitedRotation(bool viaFactory, float expectedDegrees)
        {
            PatternDefinition definition = new PatternDefinition(WolbaekPatternFactory.P02_ID, "boss_wolbaek", 1, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                1.0f, 0.6f, 90f, 0f, 1f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 70f), 0.3f, true);

            FakeCombatTarget self    = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            FakeCombatTarget tracked = new FakeCombatTarget
            {
                CombatId = TARGET_ID, Faction = ECombatFaction.Player, Position = Phase2Helpers.Polar(180f, TARGET_DISTANCE)
            };
            Phase2Query query = new Phase2Query();
            query.Players.Add(tracked);
            FakeDamageResolver resolver = new FakeDamageResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            AimRecorder recorder = new AimRecorder();

            BossFanBlastPattern pattern = viaFactory
                ? (BossFanBlastPattern)new WolbaekPatternFactory(TEST_HREF, recorder).Create(definition)
                : new BossFanBlastPattern(TEST_HREF, recorder);
            BossPatternContext context = new BossPatternContext(definition, gate.IssueInstanceId(), tracked.CombatId, self, query, gate);
            BossAttackFsm fsm = new BossAttackFsm();
            Assert.IsTrue(fsm.Start(definition, pattern, in context, 0.0));

            int count = (int)Math.Round((definition.TelegraphSeconds + AFTER) / STEP);
            for (int i = 1; i <= count; ++i)
            {
                fsm.Tick(i * STEP);
            }

            Assert.AreEqual(expectedDegrees, Vector3.Angle(Vector3.forward, pattern.AimDirection), YAW_TOLERANCE);
        }
    }
}
