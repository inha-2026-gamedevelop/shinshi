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
    // 가짜 이동 창구와 가짜 지형(z축 방향 벽 평면)으로 돌진 패턴 한 개를 공격 FSM에 올려 확인한다
    // 정의 값은 기획 표의 P01 값을 그대로 쓰되 가중치와 배율은 이 테스트가 정한 가짜 값이다
    // 이 테스트는 서버 틱, 실제 NavMesh와 물리 벽, 복제, VFX를 거치지 않는다
    public class BossDashPatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string P01_ID       = "wolbaek_p01";
        private const string HARD_P01_ID  = "wolbaek_hard_p01";
        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  TEST_HREF    = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  RANGE        = 8f;
        private const float  WIDTH        = 2f;
        private const float  TELEGRAPH    = 0.8f;
        private const float  TRACKING     = 0.4f;
        private const float  TURN_RATE    = 180f;
        private const float  ACTIVE       = 0.45f;
        private const float  RECOVERY     = 0.8f;
        private const float  DAMAGE_REF   = 0.18f;
        private const float  BODY_RADIUS  = 2f;
        private const float  POSITION_TOLERANCE = 0.002f;
        private const float  ANGLE_TOLERANCE    = 0.05f;
        private const double EPSILON      = 0.001;
        private const float  STEP         = 0.01f;

        private static readonly Vector3 Origin = Vector3.zero;

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP01(float range = RANGE, float width = WIDTH, float telegraph = TELEGRAPH,
            float tracking = TRACKING, float turnRate = TURN_RATE, float active = ACTIVE, float recovery = RECOVERY,
            bool guardable = true)
        {
            return new PatternDefinition(P01_ID, BossTestFixtures.BOSS_ID, 1, 20f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                telegraph, tracking, turnRate, active, recovery, EnemyCatalogSO.AttackShapeSpec.Dash(range, width),
                DAMAGE_REF, guardable);
        }

        // FSM이 계산하는 경계와 같은 식이다
        private static double ExecuteStartOf(PatternDefinition definition, double startTime)
        {
            return startTime + definition.TelegraphSeconds;
        }

        private static double ExecuteEndOf(PatternDefinition definition, double startTime)
        {
            return ExecuteStartOf(definition, startTime) + definition.ActiveSeconds;
        }

        private static double RecoverEndOf(PatternDefinition definition, double startTime)
        {
            return ExecuteEndOf(definition, startTime) + definition.RecoverySeconds;
        }

        private static float AimDegrees(Vector3 aim)
        {
            return Vector3.SignedAngle(Vector3.forward, aim, Vector3.up);
        }

        private static int CountHits(List<DamageRequest> requests, int targetId)
        {
            int count = 0;
            for (int i = 0; i < requests.Count; ++i)
            {
                if (requests[i].HitTargetId == targetId)
                {
                    ++count;
                }
            }

            return count;
        }

        // 벽은 z = WallZ 평면이다. 몸체 반경만큼 앞에서 멈춘다
        private sealed class DashMovement : IBossMovement
        {
            public FakeCombatTarget Self;
            public float            WallZ       = float.PositiveInfinity; // 예고도 아는 벽
            public float            HiddenWallZ = float.PositiveInfinity; // 이동할 때만 막는 벽, 검사 뒤에 생긴 장애물 모사
            public int              MoveCalls;
            public int              ClearCalls;
            public List<float>      Radii = new List<float>();

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                ++ClearCalls;
                Radii.Add(bodyRadius);
                return Mathf.Clamp(LimitAlong(origin, direction, WallZ, bodyRadius, maxDistance), 0f, maxDistance);
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                ++MoveCalls;
                Radii.Add(bodyRadius);

                Vector3 delta    = to - from;
                float   distance = delta.magnitude;
                Vector3 reached  = to;

                if (distance > 0f)
                {
                    Vector3 direction = delta / distance;
                    float   allowed   = Mathf.Min(
                        LimitAlong(from, direction, WallZ, bodyRadius, distance),
                        LimitAlong(from, direction, HiddenWallZ, bodyRadius, distance));
                    reached = from + (direction * Mathf.Clamp(allowed, 0f, distance));
                }

                Self.Position = reached;
                return reached;
            }

            private static float LimitAlong(Vector3 origin, Vector3 direction, float wallZ, float bodyRadius, float maxDistance)
            {
                if ((float.IsPositiveInfinity(wallZ)) || (direction.z <= 0.0001f))
                {
                    return maxDistance;
                }

                return (wallZ - bodyRadius - origin.z) / direction.z;
            }
        }

        private sealed class DashQuery : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Targets { get; } = new List<FakeCombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if ((Targets[i].LifeState == EParticipantLifeState.Alive) && (!Targets[i].IsSummon))
                    {
                        results.Add(Targets[i]);
                    }
                }

                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    FakeCombatTarget candidate = Targets[i];
                    Vector3 offset = candidate.Position - center;
                    offset.y = 0f;

                    if ((candidate.LifeState == EParticipantLifeState.Alive) && (candidate.Faction == faction)
                        && (includeSummons || !candidate.IsSummon) && (offset.sqrMagnitude <= (radius * radius)))
                    {
                        results.Add(candidate);
                    }
                }

                return results.Count;
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return GetLivingInRadius(center, radius, faction, includeSummons, new List<ICombatTarget>());
            }

            public bool TryGet(int combatId, out ICombatTarget target)
            {
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if (Targets[i].CombatId == combatId)
                    {
                        target = Targets[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }

            public void Remove(int combatId)
            {
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if (Targets[i].CombatId == combatId)
                    {
                        Targets.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        // 지정한 결과를 돌려주고 요청을 기록한다. 방어, 반격, 무적 결과가 나와도 패턴이 다시 요청하지 않는지 본다
        private sealed class ScriptedDamageResolver : IDamageResolver
        {
            private int _nextId = 1;

            public DamageResult Result { get; set; } = DamageResult.Applied(1f, false, false);

            public List<DamageRequest> Requests { get; } = new List<DamageRequest>();

            public DamageResult Apply(in DamageRequest request)
            {
                Requests.Add(request);
                return Result;
            }

            public int IssueInstanceId()
            {
                int issued = _nextId;
                ++_nextId;
                return issued;
            }
        }

        private sealed class DashListener : IBossDashTelegraphListener
        {
            public int                        StartCount;
            public Vector3                    StartOrigin;
            public Vector3                    StartDirection;
            public float                      StartLength;
            public float                      StartWidth;
            public float                      StartSeconds;
            public List<Vector3>              AimDirections = new List<Vector3>();
            public List<float>                AimLengths    = new List<float>();
            public int                        LockCount;
            public Vector3                    LockDirection;
            public float                      LockLength;
            public int                        DashStartCount;
            public float                      DashDistance;
            public float                      DashSeconds;
            public float                      DashSpeed;
            public double                     DashStartTime;
            public int                        StopCount;
            public Vector3                    StopPosition;
            public bool                       StopByWall;
            public int                        HitTotal;
            public List<EBossInterruptReason> Cancels = new List<EBossInterruptReason>();
            public List<string>               Order   = new List<string>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds)
            {
                Order.Add("start");
                ++StartCount;
                StartOrigin    = origin;
                StartDirection = direction;
                StartLength    = length;
                StartWidth     = width;
                StartSeconds   = telegraphSeconds;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                Order.Add("aim");
                AimDirections.Add(direction);
                AimLengths.Add(length);
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                Order.Add("lock");
                ++LockCount;
                LockDirection = direction;
                LockLength    = length;
            }

            public void OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance, float seconds, float speed,
                double startServerTime)
            {
                Order.Add("dash");
                ++DashStartCount;
                DashStartTime = startServerTime;
                DashDistance = distance;
                DashSeconds  = seconds;
                DashSpeed    = speed;
            }

            public void OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall)
            {
                Order.Add("stop");
                ++StopCount;
                StopPosition = position;
                StopByWall   = stoppedByWall;
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                Order.Add("hit");
                HitTotal += hitCount;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Order.Add("cancel");
                Cancels.Add(reason);
            }
        }

        // 패턴 한 개를 공격 FSM에 직접 올린 최소 구성. 월백은 원점에서 +z를 보고 추적 대상은 앞 3m에 선다
        private sealed class Rig
        {
            public FakeCombatTarget       Self       { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin, BodyRadius = BODY_RADIUS };
            public FakeCombatTarget       Tracked    { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public DashQuery              Query      { get; } = new DashQuery();
            public ScriptedDamageResolver Resolver   { get; } = new ScriptedDamageResolver();
            public DashListener           Listener   { get; } = new DashListener();
            public DashMovement           Movement   { get; } = new DashMovement();
            public BossAttackFsm          Fsm        { get; } = new BossAttackFsm();
            public BossDamageGate         Gate       { get; }
            public BossDashPattern        Pattern    { get; }
            public PatternDefinition      Definition { get; }
            public BossPatternContext     Context    { get; }

            // create가 있으면 그 팩토리 경로로 패턴을 만든다. 팩토리가 만드는 실제 패턴의 기본 조준 동작을 확인하려는 용도다
            public Rig(PatternDefinition definition = null, bool snapAimToTarget = false,
                Func<DashMovement, DashListener, IBossPattern> create = null)
            {
                Query.Targets.Add(Tracked);
                Movement.Self = Self;
                Gate          = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = definition ?? MakeP01();
                Pattern    = (create == null)
                    ? new BossDashPattern(TEST_HREF, Movement, Listener, snapAimToTarget)
                    : (BossDashPattern)create(Movement, Listener);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget player = new FakeCombatTarget
                {
                    CombatId = id, Faction = ECombatFaction.Player, Position = position, IsSummon = isSummon
                };
                Query.Targets.Add(player);
                return player;
            }

            public void Start(double serverTime = 0.0)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }

            // from 다음 틱부터 to까지 일정 간격으로 틱한다
            public void TickRange(double from, double to, float step = STEP)
            {
                int count = (int)Math.Round((to - from) / step);
                for (int i = 1; i <= count; ++i)
                {
                    Fsm.Tick(from + (i * (double)step));
                }
            }

            public double ExecuteStart => ExecuteStartOf(Definition, 0.0);
            public double ExecuteEnd   => ExecuteEndOf(Definition, 0.0);
            public double RecoverEnd   => RecoverEndOf(Definition, 0.0);
        }

        /****************************************
        *          Telegraph Rotation
        ****************************************/

        // 타깃이 오른쪽 90도에 있어도 처음 0.4초 동안 180도/초로만 돌아 최대 72도다
        [Test]
        public void Tick_RotatesAtMostTurnRate_AndLocksAtTrackingEnd()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            rig.Fsm.Tick(0.2);
            Assert.AreEqual(36f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.Fsm.Tick(0.39);
            Assert.AreEqual(70.2f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            // 추적 끝 경계는 FSM과 같은 식(시작 + (double)길이)이다
            rig.Fsm.Tick(TRACKING);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(72f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);

            rig.Fsm.Tick(0.6);
            Assert.AreEqual(72f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.LockCount);
            Assert.AreEqual(72f, AimDegrees(rig.Listener.LockDirection), ANGLE_TOLERANCE);
        }

        // 기본 조준은 그대로다. 대상이 뒤에 있어도 예고는 기존 정면에서 시작하고 추적 시간 x 회전 속도까지만 돈다
        [Test]
        public void Default_TargetBehind_StartsForwardAndTurnsOnlyWithinBudget()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, -10f);
            rig.Start();

            Assert.AreEqual(0f, Vector3.Angle(Vector3.forward, rig.Listener.StartDirection), ANGLE_TOLERANCE);

            rig.TickRange(0.0, 0.7);

            Assert.AreEqual(72f, Vector3.Angle(Vector3.forward, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(72f, Vector3.Angle(Vector3.forward, rig.Listener.LockDirection), ANGLE_TOLERANCE);
        }

        // 월백 P01과 무철 1페이즈 P03은 팩토리가 부드러운 조준을 켜 최단 경로로 돌아 고정 시각에 대상 방향에 닿는다.
        // 옵트인을 켜지 않은 기본 생성이 회전 속도 x 추적 시간(72도)까지만 도는 것은 위 시험이 확인한다
        [TestCase(false, 180f)]
        [TestCase(true, 180f)]
        public void Factory_WolbaekAndMucheolP03_ReachTargetByLock(bool isMucheol, float expectedDegrees)
        {
            PatternDefinition definition = isMucheol
                ? new PatternDefinition(MucheolPatternFactory.P03_ID, MucheolPatternFactory.BOSS_ID, 1, 15f,
                    new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                    TELEGRAPH, TRACKING, TURN_RATE, ACTIVE, RECOVERY, EnemyCatalogSO.AttackShapeSpec.Dash(RANGE, WIDTH),
                    DAMAGE_REF, true)
                : MakeP01();

            Rig rig = new Rig(definition, false, (movement, listener) => isMucheol
                ? new MucheolPatternFactory(TEST_HREF, null, movement, listener).Create(definition)
                : new WolbaekPatternFactory(TEST_HREF, null, null, null, listener, movement).Create(definition));
            rig.Tracked.Position = new Vector3(0f, 0f, -10f);
            rig.Start();

            Assert.AreEqual(0f, Vector3.Angle(Vector3.forward, rig.Listener.StartDirection), ANGLE_TOLERANCE);

            rig.TickRange(0.0, 0.7);

            Assert.AreEqual(expectedDegrees, Vector3.Angle(Vector3.forward, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
        }

        // 옵션을 켠 돌진은 예고를 시작하는 순간 대상 쪽을 보고, 어느 방향이든 제한 없이 따라가다 고정한다
        [Test]
        public void SnapAim_TargetBehind_StartsAndLocksTowardTheTarget()
        {
            Rig rig = new Rig(null, true);
            rig.Tracked.Position = new Vector3(0f, 0f, -10f);
            rig.Start();

            Assert.AreEqual(0f, Vector3.Angle(Vector3.back, rig.Listener.StartDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(0, rig.Listener.AimDirections.Count, "예고 시작 알림이 방향을 싣는다");

            rig.TickRange(0.0, 0.1);
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.TickRange(0.1, 0.7);

            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, rig.Listener.LockDirection), ANGLE_TOLERANCE);
        }

        [Test]
        public void Tick_AfterLock_TargetMovingDoesNotRetrack()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();
            rig.TickRange(0.0, 0.45);
            Vector3 locked = rig.Pattern.AimDirection;
            int aimEvents = rig.Listener.AimDirections.Count;

            rig.Tracked.Position = new Vector3(-10f, 0f, 0f);
            rig.TickRange(0.45, 0.79);

            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(aimEvents, rig.Listener.AimDirections.Count);
            Assert.AreEqual(1, rig.Listener.LockCount);
        }

        // 틱이 거칠어도 총 회전은 회전 속도 x 추적 시간을 넘지 않는다
        [Test]
        public void Tick_CoarseTickAcrossTrackingEnd_RotatesNoMoreThanTrackingBudget()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            rig.Fsm.Tick(0.7);

            Assert.AreEqual(72f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
        }

        [Test]
        public void Tick_TargetDiesAfterStart_KeepsLastValidDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            rig.TickRange(0.0, 0.2);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Tracked.Position  = new Vector3(0f, 0f, 10f);
            rig.TickRange(0.2, 0.7);

            Assert.AreEqual(36f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(36f, AimDegrees(rig.Listener.LockDirection), ANGLE_TOLERANCE);
        }

        [Test]
        public void Tick_TargetNotFoundAfterStart_KeepsLastValidDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            rig.TickRange(0.0, 0.1);
            rig.Query.Remove(rig.Tracked.CombatId);
            rig.TickRange(0.1, 0.7);

            Assert.AreEqual(18f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
        }

        /****************************************
        *      Telegraph Shape And Wall Length
        ****************************************/

        [Test]
        public void Begin_ReportsDefinitionLengthWidthAndSeconds()
        {
            Rig rig = new Rig();
            rig.Start();

            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(RANGE, rig.Listener.StartLength);
            Assert.AreEqual(WIDTH, rig.Listener.StartWidth);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds);
            Assert.AreEqual(0f, Vector3.Distance(Vector3.forward, rig.Listener.StartDirection), 0.0001f);
        }

        // 몸체 반경 2m이므로 z=5의 벽 앞 3m까지만 보인다
        [Test]
        public void Begin_NearWall_ShowsOnlyDistanceBodyCanTravel()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 5f;
            rig.Start();

            Assert.AreEqual(3f, rig.Listener.StartLength, POSITION_TOLERANCE);
            Assert.AreEqual(3f, rig.Pattern.ClearLength, POSITION_TOLERANCE);
            Assert.AreEqual(BODY_RADIUS, rig.Movement.Radii[0]);
        }

        // 조준이 돌면 벽까지의 거리도 달라지므로 방향 알림이 길이를 함께 보내고 고정 길이가 실제 이동 거리가 된다
        [Test]
        public void Tick_AimChange_ReportsLengthMatchingNewAim_AndDashUsesLockedLength()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ   = 5f;
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();
            Assert.AreEqual(3f, rig.Listener.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Assert.IsTrue(rig.Listener.AimLengths.Count > 0);
            for (int i = 1; i < rig.Listener.AimLengths.Count; ++i)
            {
                Assert.GreaterOrEqual(rig.Listener.AimLengths[i], rig.Listener.AimLengths[i - 1] - POSITION_TOLERANCE);
            }

            Assert.AreEqual(RANGE, rig.Listener.LockLength, POSITION_TOLERANCE);
            Assert.AreEqual(rig.Listener.LockLength, rig.Listener.DashDistance, POSITION_TOLERANCE);
            Assert.AreEqual(rig.Listener.LockLength, rig.Pattern.TraveledDistance, POSITION_TOLERANCE);
        }

        // 예고 직사각형 안팎과 실제 피해 대상이 격자 위 모든 점에서 일치한다. 벽으로 줄어든 길이도 같다
        [Test]
        public void Dash_HitSetMatchesTelegraphRectangle_AtEveryGridPoint()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 5f;
            rig.Tracked.Position = new Vector3(0f, 0f, 1.5f);

            int nextId = 100;
            for (int forward = 0; forward < 22; ++forward)
            {
                for (int lateral = 0; lateral < 10; ++lateral)
                {
                    rig.AddPlayer(nextId, new Vector3(-2.25f + (0.5f * lateral), 0f, -1.25f + (0.5f * forward)));
                    ++nextId;
                }
            }

            rig.Start();
            rig.TickRange(0.0, rig.RecoverEnd + 0.1);

            float length = rig.Listener.LockLength;
            Assert.AreEqual(3f, length, POSITION_TOLERANCE);
            for (int i = 0; i < rig.Query.Targets.Count; ++i)
            {
                FakeCombatTarget target = rig.Query.Targets[i];
                bool inside = (target.Position.z >= 0f) && (target.Position.z <= length)
                    && (Mathf.Abs(target.Position.x) <= (WIDTH * 0.5f));
                Assert.AreEqual(inside ? 1 : 0, CountHits(rig.Resolver.Requests, target.CombatId),
                    $"target {target.CombatId} at {target.Position}");
            }
        }

        /****************************************
        *        Movement Distance And Time
        ****************************************/

        [Test]
        public void Dash_OpenField_TravelsDefinitionDistanceInActiveSeconds()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteStart);
            Assert.AreEqual(0f, rig.Self.Position.z, POSITION_TOLERANCE);

            rig.Fsm.Tick(rig.ExecuteStart + (ACTIVE * 0.5));
            Assert.AreEqual(RANGE * 0.5f, rig.Self.Position.z, POSITION_TOLERANCE);

            rig.Fsm.Tick(rig.ExecuteEnd);
            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.DashStartCount);
            Assert.AreEqual(RANGE, rig.Listener.DashDistance);
            Assert.AreEqual(ACTIVE, rig.Listener.DashSeconds);
            Assert.AreEqual(RANGE / ACTIVE, rig.Listener.DashSpeed, 0.0001f);
            Assert.AreEqual(rig.ExecuteStart, rig.Listener.DashStartTime, EPSILON / 1000.0);
            Assert.AreEqual(1, rig.Listener.StopCount);
            Assert.IsFalse(rig.Listener.StopByWall);

            rig.TickRange(rig.ExecuteEnd, rig.RecoverEnd);
            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void Dash_Speed_IsConstantAndEqualsDistanceOverActiveSeconds()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.TickRange(0.0, rig.ExecuteStart + STEP);

            float expected = RANGE / ACTIVE;
            Assert.AreEqual(17.78f, expected, 0.01f);

            float previous = rig.Self.Position.z;
            double time    = rig.ExecuteStart + STEP;
            for (int i = 0; i < 40; ++i)
            {
                time += STEP;
                rig.Fsm.Tick(time);
                float speed = (rig.Self.Position.z - previous) / STEP;
                Assert.AreEqual(expected, speed, 0.1f, $"tick {i}");
                previous = rig.Self.Position.z;
            }
        }

        // 거리와 시간은 모두 정의에서 읽는다. 다른 값을 넣어도 그 값을 따른다
        [Test]
        public void Dash_UsesDefinitionValues_NotHardcodedOnes()
        {
            Rig rig = new Rig(MakeP01(range: 6f, active: 0.3f, telegraph: 0.5f, recovery: 0.2f));
            rig.Start();

            rig.Fsm.Tick(rig.ExecuteStart + 0.15);
            Assert.AreEqual(3f, rig.Self.Position.z, POSITION_TOLERANCE);

            rig.Fsm.Tick(rig.ExecuteEnd);
            Assert.AreEqual(6f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(6f, rig.Listener.StartLength);
        }

        [Test]
        public void Dash_MovesStraightAlongLockedDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Vector3 moved = rig.Self.Position - Origin;
            Assert.AreEqual(RANGE, moved.magnitude, POSITION_TOLERANCE);
            Assert.AreEqual(0f, Vector3.Angle(rig.Listener.LockDirection, moved), ANGLE_TOLERANCE);
        }

        /****************************************
        *       Targets, Hits And Dedup
        ****************************************/

        [Test]
        public void Dash_ReachingTarget_DoesNotStop()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, rig.Tracked.CombatId));
            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void Dash_PlayersAndSummons_DoNotBlockAndBothTakeDamage()
        {
            Rig rig = new Rig();
            rig.AddPlayer(11, new Vector3(0.3f, 0f, 2f));
            rig.AddPlayer(12, new Vector3(-0.3f, 0f, 4f), isSummon: true);
            rig.AddPlayer(13, new Vector3(0f, 0f, 0.5f));
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 11));
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 12));
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 13));
            Assert.AreEqual(4, rig.Resolver.Requests.Count);
        }

        // 지나간 구간만 판정한다. 아직 지나지 않은 경로의 대상은 맞지 않는다
        [Test]
        public void Dash_OnlyTargetsOnPassedSegmentAreHit()
        {
            Rig rig = new Rig();
            rig.AddPlayer(11, new Vector3(0f, 0f, 1f));
            rig.AddPlayer(12, new Vector3(0f, 0f, 6f));
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteStart);
            rig.Fsm.Tick(rig.ExecuteStart + 0.1125); // 2m 지점
            Assert.AreEqual(2f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 11));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 12));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, rig.Tracked.CombatId));

            rig.Fsm.Tick(rig.ExecuteStart + 0.3375); // 6m 지점
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 12));
        }

        [Test]
        public void Dash_ArrivalPositionOnlyIsNotTheOnlyCheck_TargetPassedMidTickIsHit()
        {
            Rig rig = new Rig();
            rig.AddPlayer(11, new Vector3(0f, 0f, 3f));
            rig.Start();

            rig.Fsm.Tick(0.7);
            rig.Fsm.Tick(rig.ExecuteStart + 0.3); // 한 틱에 0m에서 약 5.3m까지

            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 11));
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, rig.Tracked.CombatId));
        }

        [Test]
        public void Dash_EachTargetHitOnce_AcrossManyTicks()
        {
            Rig rig = new Rig();
            rig.AddPlayer(11, new Vector3(0.5f, 0f, 2f));
            rig.AddPlayer(12, new Vector3(-0.9f, 0f, 5f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.1, 0.005f);

            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 11));
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 12));
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, rig.Tracked.CombatId));
            Assert.AreEqual(3, rig.Resolver.Requests.Count);
        }

        // 방어, 반격, 무적으로 막힌 결과도 이번 돌진에서 처리한 대상이다. 다음 틱에 다시 요청하지 않는다
        [TestCase(EDamageOutcome.Guarded)]
        [TestCase(EDamageOutcome.Invulnerable)]
        [TestCase(EDamageOutcome.DuplicateHit)]
        public void Dash_BlockedOrIgnoredResult_IsNotRetriedOnLaterTicks(EDamageOutcome outcome)
        {
            Rig rig = new Rig();
            rig.Resolver.Result = (outcome == EDamageOutcome.Guarded) ? DamageResult.Guarded() : DamageResult.Ignored(outcome);
            rig.AddPlayer(11, new Vector3(0f, 0f, 2f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.1);

            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 11));
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, rig.Tracked.CombatId));
        }

        [Test]
        public void Dash_RequestFields_UseDefinitionDamageAndSharedActionId()
        {
            Rig rig = new Rig();
            rig.AddPlayer(11, new Vector3(0f, 0f, 2f));
            rig.AddPlayer(12, new Vector3(0f, 0f, 6f));
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Assert.AreEqual(3, rig.Resolver.Requests.Count);
            int actionId = rig.Resolver.Requests[0].ActionInstanceId;
            for (int i = 0; i < rig.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = rig.Resolver.Requests[i];
                Assert.AreEqual(DAMAGE_REF * TEST_HREF, request.RawDamage, 0.001f);
                Assert.IsFalse(request.IsUnblockable);
                Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
                Assert.AreEqual(actionId, request.ActionInstanceId);
                Assert.AreNotEqual(request.CastInstanceId, request.ActionInstanceId);
                Assert.AreEqual(rig.Self.CombatId, request.AttackerId);
                Assert.AreEqual(P01_ID, request.SourceDefinitionId);
                Assert.AreEqual(0f, Vector3.Distance(Origin, request.SourcePosition), 0.0001f);
            }
        }

        [Test]
        public void Dash_NotGuardable_SendsUnblockableFlag()
        {
            Rig rig = new Rig(MakeP01(guardable: false));
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Resolver.Requests[0].IsUnblockable);
        }

        [Test]
        public void Dash_DoesNotHitDeadTargets_OrFactionEnemy()
        {
            Rig rig = new Rig();
            FakeCombatTarget dead = rig.AddPlayer(11, new Vector3(0f, 0f, 2f));
            dead.LifeState = EParticipantLifeState.Dead;
            FakeCombatTarget ally = rig.AddPlayer(12, new Vector3(0f, 0f, 3f));
            ally.Faction = ECombatFaction.Enemy;
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 11));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 12));
        }

        /****************************************
        *               Walls
        ****************************************/

        [Test]
        public void Wall_StopsBeforeBodyEntersWall()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 5f;
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Assert.AreEqual(3f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.LessOrEqual(rig.Self.Position.z + BODY_RADIUS, 5f + POSITION_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.StopCount);
            Assert.IsTrue(rig.Listener.StopByWall);
            Assert.AreEqual(3f, rig.Listener.StopPosition.z, POSITION_TOLERANCE);
        }

        [Test]
        public void Wall_BlocksDamageBeyondStopPosition()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 5f;
            rig.Tracked.Position = new Vector3(0f, 0f, 1f);
            rig.AddPlayer(11, new Vector3(0f, 0f, 2.9f));
            rig.AddPlayer(12, new Vector3(0f, 0f, 3.5f));
            rig.AddPlayer(13, new Vector3(0f, 0f, 6f));
            rig.AddPlayer(14, new Vector3(0f, 0f, 9f), isSummon: true);
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.1);

            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 11));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 12));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 13));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 14));
        }

        // 벽에서 일찍 서도 원래 실행 끝 시각까지 실행이고 그 뒤 회복 시간이 그대로 이어진다
        [Test]
        public void Wall_EarlyStop_KeepsExecuteAndRecoveryTimes()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 4f; // 2m에서 정지, 약 0.1125초 만에 닿는다
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteStart);
            rig.Fsm.Tick(rig.ExecuteStart + 0.2);
            Assert.AreEqual(2f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.IsTrue(rig.Pattern.IsStopped);
            Assert.AreEqual(EBossAttackState.Execute, rig.Fsm.State);

            rig.Fsm.Tick(rig.ExecuteEnd - EPSILON);
            Assert.AreEqual(EBossAttackState.Execute, rig.Fsm.State);

            rig.Fsm.Tick(rig.ExecuteEnd);
            Assert.AreEqual(EBossAttackState.Recover, rig.Fsm.State);

            rig.Fsm.Tick(rig.RecoverEnd - EPSILON);
            Assert.AreEqual(EBossAttackState.Recover, rig.Fsm.State);
            Assert.IsFalse(rig.Pattern.IsFinished);

            rig.Fsm.Tick(rig.RecoverEnd);
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
            Assert.IsTrue(rig.Pattern.IsFinished);
            Assert.AreEqual(2f, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void Wall_AfterStop_NoContactDamageEvenIfTargetsEnterBodyArea()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ   = 4f;
            rig.Tracked.Position = new Vector3(0f, 0f, 20f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteStart + 0.2);
            Assert.IsTrue(rig.Pattern.IsStopped);

            rig.AddPlayer(11, new Vector3(0f, 0f, 2.5f)); // 정지 위치 너머
            rig.AddPlayer(12, new Vector3(0f, 0f, 1f));   // 이미 지나온 구간
            rig.AddPlayer(13, new Vector3(0f, 0f, 2f));   // 정지 위치 바로 그 자리
            rig.TickRange(rig.ExecuteStart + 0.2, rig.RecoverEnd + 0.1);

            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 11));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 12));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 13));
        }

        [Test]
        public void Wall_ZeroClearance_NoMoveNoHitAndTimesKept()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 2f; // 몸체 반경 2m, 이미 벽에 닿아 있다
            rig.AddPlayer(11, new Vector3(0f, 0f, 0f));
            rig.Start();
            Assert.AreEqual(0f, rig.Listener.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, rig.ExecuteEnd + 0.01);

            Assert.AreEqual(0f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(EBossAttackState.Recover, rig.Fsm.State);
            Assert.IsTrue(rig.Listener.StopByWall);
            Assert.AreEqual(1, rig.Listener.StopCount);
        }

        // 예고가 모르는 장애물이 이동을 먼저 막으면 그 지점이 새 최대 거리고, 그 너머에는 피해가 없다
        [Test]
        public void Wall_MidwayBlockByMover_StopsThereAndBlocksDamageBeyond()
        {
            Rig rig = new Rig();
            rig.Movement.HiddenWallZ = 4f; // 2m에서 막힌다
            rig.Tracked.Position = new Vector3(0f, 0f, 1f);
            rig.AddPlayer(11, new Vector3(0f, 0f, 3f));
            rig.Start();
            Assert.AreEqual(RANGE, rig.Listener.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, rig.RecoverEnd + 0.1);

            Assert.AreEqual(2f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(2f, rig.Pattern.TraveledDistance, POSITION_TOLERANCE);
            Assert.IsTrue(rig.Listener.StopByWall);
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, rig.Tracked.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 11));
        }

        [Test]
        public void Movement_ReceivesBossBodyRadius()
        {
            Rig rig = new Rig();
            rig.Self.BodyRadius = 1.5f;
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteEnd + 0.05);

            Assert.IsTrue(rig.Movement.MoveCalls > 0);
            for (int i = 0; i < rig.Movement.Radii.Count; ++i)
            {
                Assert.AreEqual(1.5f, rig.Movement.Radii[i]);
            }
        }

        /****************************************
        *            Coarse Ticks
        ****************************************/

        // 한 번의 큰 틱이 예고, 실행, 회복을 모두 건너뛰어도 이동 거리와 타격은 잘게 쪼갠 틱과 같다
        [Test]
        public void LargeTick_SingleJumpOverAllStages_MatchesFineTicks()
        {
            Rig fine   = new Rig();
            Rig coarse = new Rig();
            foreach (Rig rig in new[] { fine, coarse })
            {
                rig.Movement.WallZ = 100f;
                rig.AddPlayer(11, new Vector3(0.4f, 0f, 2f));
                rig.AddPlayer(12, new Vector3(-0.8f, 0f, 5f));
                rig.AddPlayer(13, new Vector3(0f, 0f, 7.9f), isSummon: true);
                rig.AddPlayer(14, new Vector3(0f, 0f, 8.5f));
                rig.Start();
            }

            fine.TickRange(0.0, fine.RecoverEnd + 0.1, 0.005f);
            coarse.Fsm.Tick(10.0);

            Assert.AreEqual(RANGE, coarse.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(fine.Self.Position.z, coarse.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossAttackState.Ready, coarse.Fsm.State);
            Assert.IsTrue(coarse.Pattern.IsFinished);

            int[] ids = { 10, 11, 12, 13, 14 };
            for (int i = 0; i < ids.Length; ++i)
            {
                Assert.AreEqual(CountHits(fine.Resolver.Requests, ids[i]), CountHits(coarse.Resolver.Requests, ids[i]), $"id {ids[i]}");
                Assert.LessOrEqual(CountHits(coarse.Resolver.Requests, ids[i]), 1);
            }

            Assert.AreEqual(0, CountHits(coarse.Resolver.Requests, 14));
        }

        // 틱이 예고 안에서 실행 안으로 건너뛰어도 이동은 틱 시각이 아니라 예고 끝 경계부터 센다
        [Test]
        public void LargeTick_JumpFromTelegraphIntoExecute_CountsMovementFromBoundary()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.Fsm.Tick(0.7);
            rig.Fsm.Tick(rig.ExecuteStart + 0.15);

            Assert.AreEqual(RANGE * (0.15f / ACTIVE), rig.Self.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void LargeTick_RandomIntervals_NeverOvershootOrDuplicate()
        {
            for (int seed = 1; seed <= 25; ++seed)
            {
                System.Random random = new System.Random(seed);
                Rig rig = new Rig();
                rig.Movement.WallZ = (seed % 2 == 0) ? 6f : float.PositiveInfinity;
                for (int i = 0; i < 12; ++i)
                {
                    rig.AddPlayer(100 + i, new Vector3(((float)random.NextDouble() * 3f) - 1.5f, 0f, (float)random.NextDouble() * 10f));
                }

                rig.Start();
                float limit    = (seed % 2 == 0) ? 4f : RANGE;
                float previous = 0f;
                double time    = 0.0;
                while (rig.Fsm.State != EBossAttackState.Ready)
                {
                    time += 0.005 + (random.NextDouble() * 0.7);
                    rig.Fsm.Tick(time);

                    float z = rig.Self.Position.z;
                    Assert.GreaterOrEqual(z, previous - 0.0001f, $"seed {seed} moved backward");
                    Assert.LessOrEqual(z, limit + POSITION_TOLERANCE, $"seed {seed} overshoot");
                    previous = z;
                }

                Assert.AreEqual(limit, rig.Self.Position.z, POSITION_TOLERANCE, $"seed {seed} missed distance");
                for (int i = 0; i < rig.Query.Targets.Count; ++i)
                {
                    Assert.LessOrEqual(CountHits(rig.Resolver.Requests, rig.Query.Targets[i].CombatId), 1, $"seed {seed}");
                }
            }
        }

        /****************************************
        *        Cancel, Finish And Late Calls
        ****************************************/

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        public void Cancel_DuringExecute_StopsMovementAndDamageEvenWithLateCalls(EBossInterruptReason reason)
        {
            Rig rig = new Rig();
            rig.AddPlayer(11, new Vector3(0f, 0f, 1f));
            rig.AddPlayer(12, new Vector3(0f, 0f, 6f));
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteStart);
            rig.Fsm.Tick(rig.ExecuteStart + 0.1125);
            float stoppedZ = rig.Self.Position.z;
            int   requests = rig.Resolver.Requests.Count;
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 11));

            rig.Fsm.Cancel(reason);
            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(reason, rig.Pattern.CancelReason);

            // FSM이 Ready로 돌아갔어도 예약 호출이 패턴에 직접 들어오는 경우를 흉내 낸다
            int moveCalls = rig.Movement.MoveCalls;
            rig.Pattern.Tick(EBossAttackState.Execute, rig.ExecuteEnd);
            rig.Pattern.Tick(EBossAttackState.Execute, 99.0);
            rig.Pattern.EnterState(EBossAttackState.Recover, rig.ExecuteEnd);
            rig.Pattern.EnterState(EBossAttackState.Execute, 99.0);
            rig.Fsm.Tick(99.0);

            Assert.AreEqual(stoppedZ, rig.Self.Position.z);
            Assert.AreEqual(moveCalls, rig.Movement.MoveCalls);
            Assert.AreEqual(requests, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, CountHits(rig.Resolver.Requests, 12));
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(reason, rig.Listener.Cancels[0]);
            Assert.AreEqual(1, CountHits(rig.Resolver.Requests, 11), "이미 적용된 피해는 그대로다");
        }

        [Test]
        public void Cancel_DuringTelegraph_NeverMoves()
        {
            Rig rig = new Rig();
            rig.AddPlayer(11, new Vector3(0f, 0f, 1f));
            rig.Start();

            rig.Fsm.Tick(0.3);
            rig.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            rig.Pattern.EnterState(EBossAttackState.Execute, rig.ExecuteStart);
            rig.Pattern.Tick(EBossAttackState.Execute, 5.0);

            Assert.AreEqual(0, rig.Movement.MoveCalls);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0f, rig.Self.Position.z);
        }

        [Test]
        public void Finish_ThenLateCalls_DoNotMoveOrHit()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.TickRange(0.0, rig.RecoverEnd + 0.1);
            Assert.IsTrue(rig.Pattern.IsFinished);

            int requests  = rig.Resolver.Requests.Count;
            int moveCalls = rig.Movement.MoveCalls;
            rig.AddPlayer(11, new Vector3(0f, 0f, 4f));
            rig.Pattern.EnterState(EBossAttackState.Execute, 50.0);
            rig.Pattern.Tick(EBossAttackState.Execute, 99.0);

            Assert.AreEqual(moveCalls, rig.Movement.MoveCalls);
            Assert.AreEqual(requests, rig.Resolver.Requests.Count);
        }

        // 게이트가 먼저 닫힌 상태에서 이어지는 틱도 피해를 내지 않는다
        [Test]
        public void ClosedGate_BlocksFurtherDamage()
        {
            Rig rig = new Rig();
            rig.AddPlayer(11, new Vector3(0f, 0f, 6f));
            rig.Start();
            rig.TickRange(0.0, rig.ExecuteStart);

            rig.Gate.CloseForGood();
            rig.TickRange(rig.ExecuteStart, rig.ExecuteEnd + 0.05);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        /****************************************
        *          Through Boss Brain
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget       Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin, BodyRadius = BODY_RADIUS };
            public FakeCombatTarget       Near     { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 1f) };
            public FakeCombatTarget       Far      { get; } = new FakeCombatTarget { CombatId = 11, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 6f) };
            public DashQuery              Query    { get; } = new DashQuery();
            public ScriptedDamageResolver Resolver { get; } = new ScriptedDamageResolver();
            public DashMovement           Movement { get; } = new DashMovement();
            public DashListener           Listener { get; } = new DashListener();
            public FakeBossVitals         Vitals   { get; } = new FakeBossVitals();
            public FakeControlSource      Control  { get; } = new FakeControlSource();
            public FakeEffectLifetime     Effects  { get; } = new FakeEffectLifetime();
            public PatternDefinition      Definition { get; } = MakeP01();
            public BossBrain              Brain    { get; }

            public BrainRig()
            {
                Query.Targets.Add(Near);
                Query.Targets.Add(Far);
                Movement.Self = Self;

                FakeBossCatalog catalog = new FakeBossCatalog { Boss = BossTestFixtures.MakeBoss() };
                catalog.Patterns.Add(Definition);

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = catalog,
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Resolver,
                    Patterns = new WolbaekPatternFactory(TEST_HREF, null, null, null, Listener, Movement),
                    Effects  = Effects,
                    Control  = Control,
                    Random   = new FakeBossRandom()
                });
            }

            // 돌진 실행 중간까지 진행한다, 가까운 대상 한 명만 이미 맞은 상태다
            public void RunToMidExecute()
            {
                Assert.IsTrue(Brain.Begin(0.0));
                Brain.Tick(0.0);
                Assert.AreEqual(EBossAttackState.Telegraph, Brain.AttackState);
                Assert.AreEqual(P01_ID, Brain.ActivePatternId);

                Brain.Tick(0.5);
                Brain.Tick(ExecuteStartOf(Definition, 0.0));
                Brain.Tick(ExecuteStartOf(Definition, 0.0) + 0.1125);
                Assert.AreEqual(EBossAttackState.Execute, Brain.AttackState);
                Assert.AreEqual(2f, Self.Position.z, POSITION_TOLERANCE);
                Assert.AreEqual(1, CountHits(Resolver.Requests, 10));
            }

            public void AssertFrozenAfter(Action interruptAndTick)
            {
                interruptAndTick();

                float z        = Self.Position.z;
                int   requests = Resolver.Requests.Count;
                Brain.Tick(ExecuteEndOf(Definition, 0.0));
                Brain.Tick(RecoverEndOf(Definition, 0.0) + 5.0);

                Assert.AreEqual(z, Self.Position.z);
                Assert.AreEqual(requests, Resolver.Requests.Count);
                Assert.AreEqual(0, CountHits(Resolver.Requests, 11));
                Assert.AreEqual(1, CountHits(Resolver.Requests, 10), "이미 적용된 피해는 그대로다");
                Assert.AreEqual(1, Listener.Cancels.Count);
            }
        }

        [Test]
        public void Brain_FullCycle_DashesAndReturnsToReadyAfterRecovery()
        {
            BrainRig rig = new BrainRig();
            rig.Far.Position = new Vector3(0f, 0f, 20f);
            rig.RunToMidExecute();

            rig.Brain.Tick(ExecuteEndOf(rig.Definition, 0.0));
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);
            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);

            rig.Brain.Tick(RecoverEndOf(rig.Definition, 0.0) - EPSILON);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.Brain.Tick(RecoverEndOf(rig.Definition, 0.0));
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
        }

        [Test]
        public void Brain_PhaseTransitionDuringExecute_StopsMovementAndDamage()
        {
            BrainRig rig = new BrainRig();
            rig.RunToMidExecute();

            rig.AssertFrozenAfter(() =>
            {
                rig.Vitals.HealthRate = 0.25f;
                rig.Brain.Tick(ExecuteStartOf(rig.Definition, 0.0) + 0.15);
            });
            Assert.AreEqual(EBossInterruptReason.PhaseTransition, rig.Listener.Cancels[0]);
        }

        [Test]
        public void Brain_CombatEndDuringExecute_StopsMovementAndDamage()
        {
            BrainRig rig = new BrainRig();
            rig.RunToMidExecute();

            rig.AssertFrozenAfter(() =>
            {
                rig.Brain.RequestCombatEnd();
                rig.Brain.Tick(ExecuteStartOf(rig.Definition, 0.0) + 0.15);
            });
            Assert.AreEqual(EBossInterruptReason.CombatEnd, rig.Listener.Cancels[0]);
            Assert.IsFalse(rig.Brain.IsBossDeathRecorded);
        }

        [Test]
        public void Brain_BossDeathDuringExecute_StopsMovementAndDamage()
        {
            BrainRig rig = new BrainRig();
            rig.RunToMidExecute();

            rig.AssertFrozenAfter(() =>
            {
                rig.Vitals.IsHealthZero = true;
                rig.Brain.Tick(ExecuteStartOf(rig.Definition, 0.0) + 0.15);
            });
            Assert.AreEqual(EBossInterruptReason.Death, rig.Listener.Cancels[0]);
            Assert.IsTrue(rig.Brain.IsBossDeathRecorded);
        }

        /****************************************
        *            Factory And Data
        ****************************************/

        [Test]
        public void Factory_P01WithMovement_CreatesFreshDashInstanceEachCall()
        {
            WolbaekPatternFactory factory = new WolbaekPatternFactory(TEST_HREF, null, null, null, null, new DashMovement());
            PatternDefinition definition = MakeP01();

            IBossPattern first  = factory.Create(definition);
            IBossPattern second = factory.Create(definition);

            Assert.IsInstanceOf<BossDashPattern>(first);
            Assert.IsInstanceOf<BossDashPattern>(second);
            Assert.AreNotSame(first, second);
        }

        [Test]
        public void Factory_P01WithoutMovement_ReturnsNull_AndOthersStillWork()
        {
            WolbaekPatternFactory factory = new WolbaekPatternFactory(TEST_HREF);

            Assert.IsNull(factory.Create(MakeP01()));
            Assert.IsInstanceOf<BossFanBlastPattern>(factory.Create(BossTestFixtures.MakePattern("wolbaek_p02")));
            Assert.IsNull(factory.Create(null));
        }

        [Test]
        public void Constructor_NullMovement_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BossDashPattern(TEST_HREF, null));
        }

        // 서버가 읽는 에셋 값이 기획 표의 P01과 같고 돌진 속도가 거리 / 실행 시간이다. 가중치와 배율은 입력하지 않았다
        [Test]
        public void CatalogAsset_P01_HasPlannedExecutionValues()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern(P01_ID, out PatternDefinition p01));

            Assert.AreEqual(EEnemyAttackShape.Dash, p01.Shape.Shape);
            Assert.AreEqual(0.8f, p01.TelegraphSeconds);
            Assert.AreEqual(0.4f, p01.TrackingSeconds);
            Assert.AreEqual(180f, p01.TurnRate);
            Assert.AreEqual(0.45f, p01.ActiveSeconds);
            Assert.AreEqual(0.8f, p01.RecoverySeconds);
            Assert.AreEqual(8f, p01.Shape.Range);
            Assert.AreEqual(2f, p01.Shape.Width);
            Assert.AreEqual(0.18f, p01.DamageRef);
            Assert.IsTrue(p01.Guardable);
            Assert.IsFalse(p01.HasControlWindow);
            Assert.AreEqual(17.78f, p01.Shape.Range / p01.ActiveSeconds, 0.01f);
        }

        /****************************************
        *            Wolbaek Hard P01
        ****************************************/

        private static PatternDefinition LoadAssetPattern(string id)
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern(id, out PatternDefinition pattern), id);
            return pattern;
        }

        // 하드 P01도 같은 돌진 실행을 만든다. 이동 창구가 없으면 이지와 같이 만들지 않고 만들 수 없다고 답한다
        [Test]
        public void Factory_HardP01_CreatesDashLikeEasyP01_AndNeedsMovement()
        {
            PatternDefinition hard = LoadAssetPattern(HARD_P01_ID);
            WolbaekPatternFactory withMovement = new WolbaekPatternFactory(TEST_HREF, null, null, null, null, new DashMovement());
            WolbaekPatternFactory without      = new WolbaekPatternFactory(TEST_HREF);

            Assert.IsTrue(withMovement.CanCreate(hard));
            Assert.IsInstanceOf<BossDashPattern>(withMovement.Create(hard));
            Assert.AreNotSame(withMovement.Create(hard), withMovement.Create(hard));

            Assert.IsFalse(without.CanCreate(hard));
            Assert.IsNull(without.Create(hard));
            Assert.IsTrue(withMovement.CanCreate(LoadAssetPattern(P01_ID)));
        }

        // 예고가 보여 주는 폭은 정의의 폭이다. 이지 P01은 2m 그대로다
        [TestCase(HARD_P01_ID, 2.25f)]
        [TestCase(P01_ID, 2f)]
        public void Begin_AssetP01_ReportsItsOwnTelegraphWidth(string id, float expectedWidth)
        {
            PatternDefinition definition = LoadAssetPattern(id);
            Rig rig = new Rig(definition, false, (movement, listener) =>
                new WolbaekPatternFactory(TEST_HREF, null, null, null, listener, movement).Create(definition));
            rig.Start();

            Assert.AreEqual(expectedWidth, rig.Listener.StartWidth);
            Assert.AreEqual(RANGE, rig.Listener.StartLength);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds);
        }

        // 서버 피격 폭이 예고 폭과 같다. 좌우 1.1m는 하드(절반 1.125m)에서만 맞고, 1.15m는 둘 다 빗나간다
        [TestCase(HARD_P01_ID, 2.25f)]
        [TestCase(P01_ID, 2f)]
        public void Dash_AssetP01_HitsOnlyInsideTelegraphWidth(string id, float expectedWidth)
        {
            PatternDefinition definition = LoadAssetPattern(id);
            Rig rig = new Rig(definition, false, (movement, listener) =>
                new WolbaekPatternFactory(TEST_HREF, null, null, null, listener, movement).Create(definition));

            float[] laterals = { 0f, 0.9f, 1.0f, 1.1f, 1.15f, 1.3f };
            int nextId = 100;
            var idsByLateral = new List<KeyValuePair<float, int>>();
            for (int i = 0; i < laterals.Length; ++i)
            {
                foreach (float sign in new[] { 1f, -1f })
                {
                    rig.AddPlayer(nextId, new Vector3(sign * laterals[i], 0f, 2f));
                    idsByLateral.Add(new KeyValuePair<float, int>(laterals[i], nextId));
                    ++nextId;
                }
            }

            rig.Start();
            Assert.AreEqual(expectedWidth, rig.Listener.StartWidth);
            rig.TickRange(0.0, rig.RecoverEnd + 0.1);

            for (int i = 0; i < idsByLateral.Count; ++i)
            {
                bool inside = idsByLateral[i].Key <= (expectedWidth * 0.5f);
                Assert.AreEqual(inside ? 1 : 0, CountHits(rig.Resolver.Requests, idsByLateral[i].Value),
                    $"{id} lateral {idsByLateral[i].Key}");
            }
        }

        // 폭 말고는 이지 P01과 같은 피해 요청을 보낸다. 피해 계수, 방어 가능 여부, 정의 ID만 정의를 따른다
        [Test]
        public void Dash_HardP01_SendsSameDamageAsEasyWithOwnDefinitionId()
        {
            PatternDefinition hard = LoadAssetPattern(HARD_P01_ID);
            PatternDefinition easy = LoadAssetPattern(P01_ID);
            Rig hardRig = new Rig(hard);
            Rig easyRig = new Rig(easy);
            hardRig.AddPlayer(100, new Vector3(0f, 0f, 2f));
            easyRig.AddPlayer(100, new Vector3(0f, 0f, 2f));

            hardRig.Start();
            easyRig.Start();
            hardRig.TickRange(0.0, hardRig.RecoverEnd + 0.1);
            easyRig.TickRange(0.0, easyRig.RecoverEnd + 0.1);

            DamageRequest hardRequest = hardRig.Resolver.Requests.Find(r => r.HitTargetId == 100);
            DamageRequest easyRequest = easyRig.Resolver.Requests.Find(r => r.HitTargetId == 100);
            Assert.AreEqual(easyRequest.RawDamage, hardRequest.RawDamage);
            Assert.AreEqual(easyRequest.Flags, hardRequest.Flags);
            Assert.AreEqual(HARD_P01_ID, hardRequest.SourceDefinitionId);
            Assert.AreEqual(P01_ID, easyRequest.SourceDefinitionId);
        }
    }
}
