// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
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
    // 월백 방향성 공격(1페이즈 P01 돌진, P02 부채꼴, P04 투사체, P06 직선과 하드 1페이즈 정의, 2페이즈 P05 투사체)의 최단 경로 부드러운 조준 검증.
    // 에셋의 실제 정의와 실제 월백 팩토리가 만든 패턴을 공격 FSM에 올려 확인한다. 가짜 대상 조회와 가짜 피해 처리기를 쓰며 실제 서버 틱, 복제, 화면은 거치지 않는다.
    // 각도는 위에서 볼 때 +z에서 시계 방향이 양수(오른쪽)다
    public class BossWolbaekNaturalAimTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  HREF         = 1000f;
        private const float  YAW_TOLERANCE = 0.3f;
        private const double STEP         = 0.02;   // 초, 한 프레임
        private const double AFTER        = 0.05;   // 초, 경계를 넘긴 여유
        private const float  TARGET_DISTANCE = 10f;
        private const float  BYSTANDER_DISTANCE = 3f;
        private const float  PEAK_MARGIN  = 1.15f;
        private const float  PEAK_FACTOR  = 1.875f;
        private const float  SETTLE_MAX   = 0.15f;
        private const float  SETTLE_RATIO = 0.3f;
        private const int    TARGET_ID    = 10;
        private const int    NEAR_ID      = 21;
        private const int    OPPOSITE_ID  = 22;

        private static readonly string[] G_Keys =
        {
            "wolbaek_p01", "wolbaek_p02", "wolbaek_p04", "wolbaek_p06",
            "wolbaek_hard_p01", "wolbaek_hard_p02",
            "wolbaek_ph2_p05"
        };

        private static readonly float[] G_TargetAngles = { 0f, 45f, 90f, -90f, 170f, -170f, 180f };
        private static readonly float[] G_StartYaws    = { 0f, 135f, -100f };

        /****************************************
        *               Fakes
        ****************************************/

        // 돌진, 부채꼴, 투사체, 직선 알림을 한 곳에서 받는다. 네 알림은 시작 알림의 형태가 같다
        private sealed class AimRecorder : IBossFanTelegraphListener, IBossDashTelegraphListener, IBossLineTelegraphListener,
            IBossProjectileTelegraphListener
        {
            public int           StartCount;
            public Vector3       StartDirection;
            public int           LockCount;
            public Vector3       LockDirection;
            public int           CancelCount;
            public Vector3       DashStartDirection;
            public int           DashStartCount;
            public List<Vector3> AimEvents = new List<Vector3>();

            // 몸체 방향은 리스너가 받은 마지막 방향으로 따라간다(BossServer와 로컬 브리지의 Face 호출과 같은 경로)
            public Vector3 BodyFacing;

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float a, float b, float telegraphSeconds)
            {
                ++StartCount;
                StartDirection = direction;
                BodyFacing     = direction;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
                AimEvents.Add(direction);
                BodyFacing = direction;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                AimEvents.Add(direction);
                BodyFacing = direction;
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
                ++LockCount;
                LockDirection = direction;
                BodyFacing    = direction;
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                ++LockCount;
                LockDirection = direction;
                BodyFacing    = direction;
            }

            public void OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance, float seconds, float speed,
                double startServerTime)
            {
                ++DashStartCount;
                DashStartDirection = direction;
            }

            public void OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall)
            {
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                ++CancelCount;
            }
        }

        private sealed class FakeLauncher : IBossProjectileLauncher
        {
            public List<Vector3> Directions = new List<Vector3>();
            public List<Vector3> Origins    = new List<Vector3>();

            public float MeasurePath(Vector3 origin, Vector3 direction, float maxDistance, float width)
            {
                return maxDistance;
            }

            public bool TryLaunch(in BossProjectileSpec spec)
            {
                Directions.Add(spec.Direction);
                Origins.Add(spec.Origin);
                return true;
            }
        }

        // 공격 FSM에 올린 최소 구성. 보스는 원점에서 startYaw 방향을 보고, 추적 대상(10번)은 원점에서 10m 거리에 선다
        private sealed class Rig
        {
            public FakeCombatTarget   Self       { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget   Tracked    { get; } = new FakeCombatTarget { CombatId = TARGET_ID, Faction = ECombatFaction.Player };
            public Phase2Query        Query      { get; } = new Phase2Query();
            public FakeDamageResolver Resolver   { get; } = new FakeDamageResolver();
            public AimRecorder        Recorder   { get; } = new AimRecorder();
            public FakeLauncher       Launcher   { get; } = new FakeLauncher();
            public Phase2Movement     Movement   { get; } = new Phase2Movement();
            public BossAttackFsm      Fsm        { get; } = new BossAttackFsm();
            public BossDamageGate     Gate       { get; }
            public PatternDefinition  Definition { get; }
            public IBossPattern       Pattern    { get; }
            public BossPatternContext Context    { get; }
            public float              StartYaw   { get; }

            public List<double> Times { get; } = new List<double>();
            public List<float>  Yaws  { get; } = new List<float>();

            public double TrackingSeconds => Definition.TrackingSeconds;
            public double TelegraphSeconds => Definition.TelegraphSeconds;

            // useFactory가 거짓이면 같은 정의로 옵트인 없이 직접 만든다, 기본 동작이 그대로인지 보려는 것이다
            public Rig(string id, float targetAngle = 0f, float startYaw = 0f, bool useFactory = true)
            {
                StartYaw         = startYaw;
                Self.Forward     = Phase2Helpers.Polar(startYaw, 1f);
                Tracked.Position = Phase2Helpers.Polar(targetAngle, TARGET_DISTANCE);
                Query.Players.Add(Tracked);
                Movement.Self = Self;
                Gate          = new BossDamageGate(Resolver);
                Gate.Open();

                Assert.IsTrue(LoadCatalog().TryGetPattern(id, out PatternDefinition definition), id);
                Definition = definition;

                if (useFactory)
                {
                    WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, fanListener: Recorder, lineListener: Recorder,
                        dashListener: Recorder, movement: Movement, projectiles: Launcher, projectileListener: Recorder);
                    Pattern = factory.Create(Definition);
                }
                else
                {
                    Pattern = CreateWithoutNaturalAim(id);
                }

                Assert.IsNotNull(Pattern, id);
                Context = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            private IBossPattern CreateWithoutNaturalAim(string id)
            {
                switch (Kind(id))
                {
                    case EKind.Dash:
                        return new BossDashPattern(HREF, Movement, Recorder);
                    case EKind.Fan:
                        return new BossFanBlastPattern(HREF, Recorder);
                    case EKind.Projectile:
                        return new BossProjectilePattern(HREF, Launcher, Recorder);
                    default:
                        return new BossLineBlastPattern(HREF, Recorder);
                }
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
                        case BossProjectilePattern projectile:
                            return projectile.AimDirection;
                        case BossLineBlastPattern line:
                            return line.AimDirection;
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
            public void TickRange(double from, double to, Action<double> onFrame = null, double step = STEP)
            {
                int count = (int)Math.Round((to - from) / step);
                for (int i = 1; i <= count; ++i)
                {
                    double time = from + (i * step);
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
        }

        private enum EKind
        {
            Dash,
            Fan,
            Projectile,
            Line
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static BossPatternCatalogSO LoadCatalog()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        private static EKind Kind(string id)
        {
            if (id.EndsWith("p01"))
            {
                return EKind.Dash;
            }

            if (id.EndsWith("p02"))
            {
                return EKind.Fan;
            }

            return id.EndsWith("p06") ? EKind.Line : EKind.Projectile;
        }

        private static float YawOf(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static double FreezeTime(Rig rig)
        {
            double tracking = rig.Definition.TrackingSeconds;
            return tracking - Math.Min(SETTLE_MAX, tracking * SETTLE_RATIO);
        }

        private static float ExpectedWalk(float startYaw, float targetYaw)
        {
            return Mathf.Abs(Mathf.DeltaAngle(startYaw, targetYaw));
        }

        /****************************************
        *       Shortest Path And Lock Target
        ****************************************/

        // 이전 공격이 어느 쪽을 보고 있었든(0도, 뒤쪽 135도, -100도) 새 대상이 정면, 측면, 정반대 어디에 있어도 최단 경로로 돌아
        // 방향 고정 시각까지 대상 방향에 닿는다. 되돌지 않고 필요한 각도보다 더 돌지 않으며 한 프레임에 순간 이동하지 않는다
        [Test]
        public void Natural_TargetAtAngle_TurnsTheShortestWayAndLocksOnTheTarget(
            [ValueSource(nameof(G_Keys))] string id, [ValueSource(nameof(G_TargetAngles))] float angle,
            [ValueSource(nameof(G_StartYaws))] float startYaw)
        {
            Rig rig = new Rig(id, angle, startYaw);
            rig.Start();
            Assert.AreEqual(0f, Mathf.DeltaAngle(startYaw, rig.AimYaw), YAW_TOLERANCE, "시작 방향은 이전 공격의 마지막 방향(보스의 현재 방향)이다");
            Assert.AreEqual(0f, Vector3.Angle(Phase2Helpers.Polar(startYaw, 1f), rig.Recorder.StartDirection), YAW_TOLERANCE);

            float tracking = (float)rig.TrackingSeconds;
            rig.TickRange(0.0, tracking + AFTER);

            float expectedWalk = ExpectedWalk(startYaw, angle);
            float signedDelta = Mathf.DeltaAngle(startYaw, angle);
            bool isOpposite = Mathf.Abs(Mathf.Abs(signedDelta) - 180f) < 0.001f;

            int side = 0;
            float walked = 0f;
            float peakStep = 0f;
            for (int i = 1; i < rig.Yaws.Count; ++i)
            {
                float step = Mathf.DeltaAngle(rig.Yaws[i - 1], rig.Yaws[i]);
                if (Mathf.Abs(step) > 0.01f)
                {
                    int stepSide = (int)Mathf.Sign(step);
                    side = (side == 0) ? stepSide : side;
                    Assert.AreEqual(side, stepSide, $"{id} {startYaw}->{angle}도: {i}번째 프레임에서 회전 방향이 바뀐다");
                }

                walked += Mathf.Abs(step);
                peakStep = Mathf.Max(peakStep, Mathf.Abs(step));
            }

            if ((!isOpposite) && (expectedWalk > 0.01f))
            {
                Assert.AreEqual((int)Mathf.Sign(signedDelta), side, $"{id} {startYaw}->{angle}도: 가까운 쪽으로 돌지 않는다");
            }

            Assert.AreEqual(expectedWalk, walked, 1f, $"{id} {startYaw}->{angle}도: 필요한 각도만큼만 돌아야 한다");
            Assert.AreEqual(0f, Mathf.DeltaAngle(angle, rig.AimYaw), YAW_TOLERANCE, $"{id} {startYaw}->{angle}도: 고정 방향이 대상 방향이 아니다");

            float limit = Mathf.Max(rig.Definition.TurnRate, PEAK_FACTOR * expectedWalk / tracking) * (float)STEP * PEAK_MARGIN;
            Assert.LessOrEqual(peakStep, limit + 0.01f, $"{id} {startYaw}->{angle}도: 한 프레임에 너무 많이 돈다");

            Assert.AreEqual(1, rig.Recorder.LockCount);
            Assert.AreEqual(0f, Vector3.Angle(rig.Aim, rig.Recorder.LockDirection), YAW_TOLERANCE, "고정 알림 방향이 서버 조준 방향과 같다");
            Assert.AreEqual(0f, Vector3.Angle(rig.Aim, rig.Recorder.BodyFacing), YAW_TOLERANCE, "리스너가 받은 몸체 방향이 서버 조준 방향과 같다");
            Assert.AreEqual(0, rig.Recorder.CancelCount);
        }

        // 마지막으로 알린 조준 방향이 항상 서버가 쥔 조준 방향이다, 알리지 않은 채 방향이 바뀌지 않는다
        [Test]
        public void Natural_EveryAimNotificationMatchesTheServerAim([ValueSource(nameof(G_Keys))] string id)
        {
            Rig rig = new Rig(id, 150f, 0f);
            rig.Start();

            rig.TickRange(0.0, rig.TrackingSeconds + AFTER);

            Assert.Greater(rig.Recorder.AimEvents.Count, 1, "여러 프레임에 걸쳐 나눠 알린다");
            Vector3 lastEvent = rig.Recorder.AimEvents[rig.Recorder.AimEvents.Count - 1];
            Assert.AreEqual(0f, Vector3.Angle(lastEvent, rig.Aim), YAW_TOLERANCE);
            for (int i = 0; i < rig.Recorder.AimEvents.Count; ++i)
            {
                Assert.AreEqual(1f, rig.Recorder.AimEvents[i].magnitude, 0.001f, "수평 단위 벡터다");
                Assert.AreEqual(0f, rig.Recorder.AimEvents[i].y, 0.0001f);
            }
        }

        // 도착은 방향 고정 시각 안이다: 고정 시각의 첫 틱에서 이미 대상 방향이다
        [Test]
        public void Natural_AimReachesTheTargetByTheLockTime([ValueSource(nameof(G_Keys))] string id)
        {
            Rig rig = new Rig(id, 180f);
            rig.Start();

            float tracking = (float)rig.TrackingSeconds;
            rig.TickRange(0.0, tracking - (float)STEP * 1.5f);
            Assert.AreEqual(0, rig.Recorder.LockCount, "고정 시각 전이다");

            rig.TickOnce(tracking);

            Assert.AreEqual(1, rig.Recorder.LockCount);
            Assert.AreEqual(0f, Mathf.DeltaAngle(180f, rig.AimYaw), YAW_TOLERANCE);
        }

        /****************************************
        *   Moving, Frozen, Lost Target
        ****************************************/

        // 정반대 근처 경계에서 처음 몇 프레임 좌우를 오가도 회전 방향이 뒤집히지 않는다
        [Test]
        public void Natural_TargetJittersAcrossTheRearBoundary_NeverFlipsDirection([ValueSource(nameof(G_Keys))] string id)
        {
            const int JITTER_FRAMES = 5;

            Rig rig = new Rig(id, 175f);
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
                Assert.AreEqual(firstSide, side, $"{id}: {i}번째 프레임에서 회전 방향이 바뀐다");
            }

            Assert.AreNotEqual(0, firstSide);
            Assert.AreEqual(0f, Mathf.DeltaAngle(175f, rig.AimYaw), YAW_TOLERANCE, id);
            Assert.AreEqual(1, rig.Recorder.LockCount);
        }

        // 추적 구간에서 움직이는 대상은 따라가고, 목표 갱신을 멈춘 뒤에는 마지막으로 읽은 방향에서 고정한다
        [Test]
        public void Natural_MovingTarget_FollowsDuringTrackingOnly([ValueSource(nameof(G_Keys))] string id)
        {
            const float DEGREES_PER_SECOND = 50f;

            Rig rig = new Rig(id, 0f);
            rig.Start();

            double freeze = FreezeTime(rig);
            float angleAtFreeze = float.NaN;
            rig.TickRange(0.0, rig.TrackingSeconds + AFTER, time =>
            {
                float angle = (float)(time * DEGREES_PER_SECOND);
                rig.Tracked.Position = Phase2Helpers.Polar(angle, TARGET_DISTANCE);
                if (float.IsNaN(angleAtFreeze) && (time >= freeze))
                {
                    angleAtFreeze = angle;
                }
            });

            Assert.IsFalse(float.IsNaN(angleAtFreeze), id);
            Assert.AreEqual(0f, Mathf.DeltaAngle(angleAtFreeze, rig.AimYaw), 2f, $"{id}: 고정 방향은 목표 갱신을 멈춘 시점의 대상 방향이다");
            Assert.AreEqual(1, rig.Recorder.LockCount);
        }

        // 방향 고정 뒤에는 대상이 움직여도 조준, 알림, 몸체 방향이 바뀌지 않는다
        [Test]
        public void Natural_TargetMovesAfterLock_AimStaysLocked([ValueSource(nameof(G_Keys))] string id)
        {
            Rig rig = new Rig(id, 90f);
            rig.Start();
            rig.TickRange(0.0, rig.TrackingSeconds + AFTER);
            Assert.AreEqual(1, rig.Recorder.LockCount);
            Vector3 locked = rig.Aim;
            int events = rig.Recorder.AimEvents.Count;

            rig.Tracked.Position = Phase2Helpers.Polar(-90f, TARGET_DISTANCE);
            rig.TickRange(rig.Times[rig.Times.Count - 1], rig.TelegraphSeconds - 0.01);

            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Aim), 0.001f);
            Assert.AreEqual(events, rig.Recorder.AimEvents.Count, "고정 뒤에는 조준 알림이 더 나가지 않는다");
            Assert.AreEqual(1, rig.Recorder.LockCount);
            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Recorder.BodyFacing), 0.001f);
        }

        // 대상이 추적 중에 죽거나 사라지면 마지막으로 읽은 방향을 유지해 고정하고, 다른 플레이어로 바꾸지 않는다
        [Test]
        public void Natural_TargetDiesOrVanishesWhileTracking_KeepsLastValidDirection(
            [ValueSource(nameof(G_Keys))] string id, [Values(true, false)] bool dies)
        {
            Rig rig = new Rig(id, 120f);
            FakeCombatTarget other = rig.AddPlayer(NEAR_ID, Phase2Helpers.Polar(-60f, TARGET_DISTANCE));
            rig.Start();

            bool gone = false;
            rig.TickRange(0.0, rig.TrackingSeconds + AFTER, time =>
            {
                if ((!gone) && (time >= 0.1))
                {
                    gone = true;
                    if (dies)
                    {
                        rig.Tracked.LifeState = EParticipantLifeState.Dead;
                    }
                    else
                    {
                        rig.Query.Players.Remove(rig.Tracked);
                    }

                    rig.Tracked.Position = Phase2Helpers.Polar(-120f, TARGET_DISTANCE);
                }
            });

            Assert.IsTrue(gone);
            Assert.AreEqual(0f, Mathf.DeltaAngle(120f, rig.AimYaw), YAW_TOLERANCE, $"{id}: 마지막 유효 방향이 아니다");
            Assert.AreEqual(1, rig.Recorder.LockCount);
            Assert.IsNotNull(other);
        }

        // 시작부터 죽은 대상이면 방향을 바꾸지 않고 현재 방향으로 고정한다
        [Test]
        public void Natural_TargetAlreadyDead_KeepsTheCurrentDirection([ValueSource(nameof(G_Keys))] string id)
        {
            Rig rig = new Rig(id, 150f, 40f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Start();

            rig.TickRange(0.0, rig.TrackingSeconds + AFTER);

            Assert.AreEqual(0f, Mathf.DeltaAngle(40f, rig.AimYaw), YAW_TOLERANCE);
            Assert.AreEqual(1, rig.Recorder.LockCount);
            Assert.AreEqual(0, rig.Recorder.AimEvents.Count);
        }

        /****************************************
        *            Tick Sizes
        ****************************************/

        // 틱 간격이 아주 작아도(1/128초) 크게 건너뛰어도(한 번에 예고 끝까지) 같은 방향에 닿고 한 번만 고정한다
        [Test]
        public void Natural_TinyAndHugeTickSteps_ReachTheSameTarget([ValueSource(nameof(G_Keys))] string id)
        {
            Rig tiny = new Rig(id, 150f, -30f);
            tiny.Start();
            tiny.TickRange(0.0, tiny.TrackingSeconds + AFTER, step: 1.0 / 128.0);
            Assert.AreEqual(0f, Mathf.DeltaAngle(150f, tiny.AimYaw), YAW_TOLERANCE, id);
            Assert.AreEqual(1, tiny.Recorder.LockCount);

            float walked = 0f;
            for (int i = 1; i < tiny.Yaws.Count; ++i)
            {
                walked += Mathf.Abs(Mathf.DeltaAngle(tiny.Yaws[i - 1], tiny.Yaws[i]));
            }

            Assert.AreEqual(ExpectedWalk(-30f, 150f), walked, 1f, $"{id}: 잔 틱에서도 필요한 각도만 돈다");

            Rig huge = new Rig(id, 150f, -30f);
            huge.Start();
            huge.TickOnce(huge.TelegraphSeconds);
            Assert.AreEqual(0f, Mathf.DeltaAngle(150f, huge.AimYaw), YAW_TOLERANCE, id);
            Assert.AreEqual(1, huge.Recorder.LockCount);
        }

        /****************************************
        *   Judgement Follows The Locked Aim
        ****************************************/

        // 서버의 실제 판정 방향이 고정 방향이다. 고정 뒤 대상이 반대편으로 가도 판정은 고정 방향으로 나간다
        [Test]
        public void Natural_JudgementUsesTheLockedDirection([ValueSource(nameof(G_Keys))] string id)
        {
            const float TARGET_ANGLE = 100f;

            Rig rig = new Rig(id, TARGET_ANGLE, -20f);
            FakeCombatTarget near = rig.AddPlayer(NEAR_ID, Phase2Helpers.Polar(TARGET_ANGLE, BYSTANDER_DISTANCE));
            FakeCombatTarget opposite = rig.AddPlayer(OPPOSITE_ID, Phase2Helpers.Polar(TARGET_ANGLE + 180f, BYSTANDER_DISTANCE));
            rig.Start();
            rig.TickRange(0.0, rig.TrackingSeconds + AFTER);
            Assert.AreEqual(0f, Mathf.DeltaAngle(TARGET_ANGLE, rig.AimYaw), YAW_TOLERANCE);

            rig.Tracked.Position = Phase2Helpers.Polar(-80f, TARGET_DISTANCE);
            // 예고 끝 경계는 float 값을 double로 넓힌 시각이라 마지막 틱이 그 경계를 확실히 넘도록 두 프레임을 더 돈다
            rig.TickRange(rig.Times[rig.Times.Count - 1], rig.TelegraphSeconds + (2.0 * STEP));

            switch (Kind(id))
            {
                case EKind.Fan:
                case EKind.Line:
                    Assert.AreEqual(1, Phase2Helpers.RequestsFor(rig.Resolver, near.CombatId).Count, $"{id}: 고정 방향 쪽 대상이 맞아야 한다");
                    Assert.AreEqual(0, Phase2Helpers.RequestsFor(rig.Resolver, opposite.CombatId).Count, $"{id}: 반대쪽 대상은 맞지 않는다");
                    break;
                case EKind.Projectile:
                    Assert.Greater(rig.Launcher.Directions.Count, 0);
                    for (int i = 0; i < rig.Launcher.Directions.Count; ++i)
                    {
                        Assert.AreEqual(0f, Vector3.Angle(rig.Aim, rig.Launcher.Directions[i]), YAW_TOLERANCE, $"{id}: {i}번째 발의 방향");
                        Assert.AreEqual(0f, Mathf.DeltaAngle(TARGET_ANGLE, YawOf(rig.Launcher.Directions[i])), YAW_TOLERANCE);
                    }

                    break;
                case EKind.Dash:
                    Assert.AreEqual(1, rig.Recorder.DashStartCount);
                    Assert.AreEqual(0f, Mathf.DeltaAngle(TARGET_ANGLE, YawOf(rig.Recorder.DashStartDirection)), YAW_TOLERANCE, id);
                    break;
            }

            Assert.AreEqual(0f, Vector3.Angle(rig.Aim, rig.Recorder.LockDirection), YAW_TOLERANCE);
            Assert.AreEqual(0f, Vector3.Angle(rig.Aim, rig.Recorder.BodyFacing), YAW_TOLERANCE);
        }

        // 다음 패턴은 이번 패턴이 고정한 마지막 방향에서 출발한다(보스 몸체가 리스너로 받은 방향이 다음 시작 방향이다)
        [Test]
        public void Natural_NextCastStartsFromTheLastLockedDirection()
        {
            Rig first = new Rig("wolbaek_p02", 150f, 0f);
            first.Start();
            first.TickRange(0.0, first.TrackingSeconds + AFTER);
            Vector3 lastFacing = first.Recorder.BodyFacing;
            Assert.AreEqual(0f, Mathf.DeltaAngle(150f, YawOf(lastFacing)), YAW_TOLERANCE);

            Rig second = new Rig("wolbaek_p06", -170f, YawOf(lastFacing));
            second.Start();

            Assert.AreEqual(0f, Mathf.DeltaAngle(150f, second.AimYaw), YAW_TOLERANCE, "이전 방향에서 출발한다");
            second.TickRange(0.0, second.TrackingSeconds + AFTER);
            Assert.AreEqual(0f, Mathf.DeltaAngle(-170f, second.AimYaw), YAW_TOLERANCE, "맞은편 가까운 대상도 닿는다");
        }

        /****************************************
        *      Defaults Stay Limited-Turn
        ****************************************/

        // 옵트인을 켜지 않은 기본 생성은 기존대로 회전 속도 x 추적 시간까지만 돈다, 다른 보스의 기본 동작이다
        [Test]
        public void Default_WithoutNaturalAim_StillTurnsOnlyTurnRateTimesTracking([ValueSource(nameof(G_Keys))] string id)
        {
            Rig rig = new Rig(id, 180f, 0f, useFactory: false);
            rig.Start();

            rig.TickRange(0.0, rig.TrackingSeconds + AFTER);

            float limit = rig.Definition.TurnRate * (float)rig.TrackingSeconds;
            Assert.Less(limit, 179f, "시험 전제: 제한 회전각이 반대편에 못 닿는다");
            Assert.AreEqual(limit, Mathf.Abs(Mathf.DeltaAngle(0f, rig.AimYaw)), 0.5f, id);
            Assert.AreEqual(1, rig.Recorder.LockCount);
        }

        // 월백 팩토리가 만든 패턴은 모두 반대편에 닿는다, 옵트인이 팩토리에서 켜져 있다
        [Test]
        public void Factory_EveryWolbaekDirectionalPattern_ReachesTheOppositeSide([ValueSource(nameof(G_Keys))] string id)
        {
            Rig rig = new Rig(id, 180f);
            rig.Start();

            rig.TickRange(0.0, rig.TrackingSeconds + AFTER);

            float limit = rig.Definition.TurnRate * (float)rig.TrackingSeconds;
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, rig.AimYaw)), limit + 1f, id);
        }

        // 조준 회전이 없는 월백 패턴(원형 폭발, 장판, 반월 연쇄)에는 추적 부드러운 조준을 만들지 않는다
        [Test]
        public void Factory_NonAimingPatterns_AreNotAimTrackers()
        {
            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF);
            string[] ids = { "wolbaek_p03", "wolbaek_hard_p03", "wolbaek_ph2_p03", "wolbaek_ph2_p06" };

            for (int i = 0; i < ids.Length; ++i)
            {
                Assert.IsTrue(LoadCatalog().TryGetPattern(ids[i], out PatternDefinition definition), ids[i]);
                IBossPattern pattern = factory.Create(definition);
                Assert.IsNotNull(pattern, ids[i]);
                Assert.IsNotInstanceOf<BossDashPattern>(pattern, ids[i]);
                Assert.IsNotInstanceOf<BossFanBlastPattern>(pattern, ids[i]);
                Assert.IsNotInstanceOf<BossProjectilePattern>(pattern, ids[i]);
                Assert.IsNotInstanceOf<BossLineBlastPattern>(pattern, ids[i]);
            }
        }
    }
}
