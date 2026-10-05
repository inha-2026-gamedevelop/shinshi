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
    // 무철 P03(어깨 돌진) 실행 검증, 시작 수치는 플레이 테스트 전 임시값이라 운영 카탈로그가 아니라 이 테스트의 데이터를 쓴다
    // 가짜 이동 창구와 가짜 지형(z축 방향 벽 평면)으로 팩토리가 만든 패턴을 공격 FSM에 올려 확인한다
    // 시간은 0.01초 간격으로 틱해 경계 부동소수 오차가 판정을 뒤집지 않게 하고, 경계 값 검사는 경계에서 떨어진 위치만 쓴다
    public class MucheolShoulderDashTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TEST_HREF   = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  BODY_RADIUS = 2f;
        private const float  POSITION_TOLERANCE = 0.002f;
        private const float  ANGLE_TOLERANCE    = 0.05f;
        private const double TIME_TOLERANCE     = 0.001;
        private const float  STEP        = 0.01f;
        private const int    PROBE_ID    = 11;

        // 임시 시작 수치
        private const float RANGE      = 8f;
        private const float WIDTH      = 2.5f;
        private const float WEIGHT     = 15f;
        private const float TELEGRAPH  = 0.9f;
        private const float TRACKING   = 0.4f;
        private const float TURN_RATE  = 150f;
        private const float ACTIVE     = 0.5f;
        private const float RECOVERY   = 0.8f;
        private const float DAMAGE_REF = 0.22f;

        private static readonly Vector3 Origin = Vector3.zero;

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP03()
        {
            return new PatternDefinition(MucheolPatternFactory.P03_ID, MucheolPatternFactory.BOSS_ID, 1, WEIGHT,
                new[] { 0f, 1f, 0f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, TRACKING, TURN_RATE, ACTIVE, RECOVERY, EnemyCatalogSO.AttackShapeSpec.Dash(RANGE, WIDTH),
                DAMAGE_REF, true);
        }

        private static float AimDegrees(Vector3 aim)
        {
            return Vector3.SignedAngle(Vector3.forward, aim, Vector3.up);
        }

        private static int CountHits(FakeDamageResolver resolver, int targetId)
        {
            int count = 0;
            for (int i = 0; i < resolver.Requests.Count; ++i)
            {
                if (resolver.Requests[i].HitTargetId == targetId)
                {
                    ++count;
                }
            }

            return count;
        }

        // 벽은 z = WallZ 평면이다. 몸체 반경만큼 앞에서 멈춘다
        private sealed class ShoulderMovement : IBossMovement
        {
            public FakeCombatTarget Self;
            public float            WallZ = float.PositiveInfinity;
            public int              MoveCalls;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return Mathf.Clamp(LimitAlong(origin, direction, bodyRadius, maxDistance), 0f, maxDistance);
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                ++MoveCalls;

                Vector3 delta    = to - from;
                float   distance = delta.magnitude;
                Vector3 reached  = to;

                if (distance > 0f)
                {
                    Vector3 direction = delta / distance;
                    reached = from + (direction * Mathf.Clamp(LimitAlong(from, direction, bodyRadius, distance), 0f, distance));
                }

                Self.Position = reached;
                return reached;
            }

            private float LimitAlong(Vector3 origin, Vector3 direction, float bodyRadius, float maxDistance)
            {
                if ((float.IsPositiveInfinity(WallZ)) || (direction.z <= 0.0001f))
                {
                    return maxDistance;
                }

                return (WallZ - bodyRadius - origin.z) / direction.z;
            }
        }

        private sealed class ShoulderQuery : ICombatTargetQuery
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
        }

        private sealed class ShoulderListener : IBossDashTelegraphListener
        {
            public int                        StartCount;
            public float                      StartLength;
            public float                      StartWidth;
            public int                        LockCount;
            public Vector3                    LockDirection;
            public int                        AimCount;
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

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds)
            {
                ++StartCount;
                StartLength = length;
                StartWidth  = width;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                ++AimCount;
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                ++LockCount;
                LockDirection = direction;
            }

            public void OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance, float seconds, float speed,
                double startServerTime)
            {
                ++DashStartCount;
                DashDistance  = distance;
                DashSeconds   = seconds;
                DashSpeed     = speed;
                DashStartTime = startServerTime;
            }

            public void OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall)
            {
                ++StopCount;
                StopPosition = position;
                StopByWall   = stoppedByWall;
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                HitTotal += hitCount;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Cancels.Add(reason);
            }
        }

        // 팩토리가 만든 패턴을 공격 FSM에 올린 최소 구성. 보스는 원점에서 +z를 보고 추적 대상은 앞 3m에 선다
        private sealed class Rig
        {
            public FakeCombatTarget   Self       { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin, BodyRadius = BODY_RADIUS };
            public FakeCombatTarget   Tracked    { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public ShoulderQuery      Query      { get; } = new ShoulderQuery();
            public FakeDamageResolver Resolver   { get; } = new FakeDamageResolver();
            public ShoulderListener   Listener   { get; } = new ShoulderListener();
            public ShoulderMovement   Movement   { get; } = new ShoulderMovement();
            public BossAttackFsm      Fsm        { get; } = new BossAttackFsm();
            public BossDamageGate     Gate       { get; }
            public BossDashPattern    Pattern    { get; }
            public PatternDefinition  Definition { get; }
            public BossPatternContext Context    { get; }

            public double ExecuteStart => (double)Definition.TelegraphSeconds;
            public double ExecuteEnd   => ExecuteStart + Definition.ActiveSeconds;
            public double RecoverEnd   => ExecuteEnd + Definition.RecoverySeconds;

            public Rig()
            {
                Query.Targets.Add(Tracked);
                Movement.Self = Self;
                Gate          = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = MakeP03();
                Pattern    = (BossDashPattern)new MucheolPatternFactory(TEST_HREF, null, Movement, Listener).Create(Definition);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Targets.Add(player);
                return player;
            }

            public void Start(double serverTime = 0.0)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }

            // from 다음 틱부터 to까지 일정 간격으로 틱한다
            public void TickRange(double from, double to)
            {
                int count = (int)Math.Round((to - from) / STEP);
                for (int i = 1; i <= count; ++i)
                {
                    Fsm.Tick(from + (i * (double)STEP));
                }
            }
        }

        /****************************************
        *               Factory
        ****************************************/

        [Test]
        public void Create_WithMovement_ReturnsDashPattern()
        {
            Rig rig = new Rig();
            var factory = new MucheolPatternFactory(TEST_HREF, null, rig.Movement, rig.Listener);

            Assert.IsTrue(factory.CanCreate(rig.Definition));
            Assert.IsInstanceOf<BossDashPattern>(factory.Create(rig.Definition));
        }

        [Test]
        public void Create_WithoutMovement_CannotCreateAndReturnsNull()
        {
            var factory = new MucheolPatternFactory(TEST_HREF, new FanListenerStub(), null, null);
            PatternDefinition definition = MakeP03();

            Assert.IsFalse(factory.CanCreate(definition));
            Assert.IsNull(factory.Create(definition));
        }

        // 이동 창구를 넘겨도 P01, P02는 부채꼴 패턴 그대로다
        [TestCase(MucheolPatternFactory.P01_ID)]
        [TestCase(MucheolPatternFactory.P02_ID)]
        public void Create_FanPatternsWithMovement_StillReturnFanPattern(string patternId)
        {
            Rig rig = new Rig();
            var factory = new MucheolPatternFactory(TEST_HREF, null, rig.Movement, rig.Listener);
            PatternDefinition definition = new PatternDefinition(patternId, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);

            Assert.IsTrue(factory.CanCreate(definition));
            Assert.IsInstanceOf<BossFanBlastPattern>(factory.Create(definition));
        }

        private sealed class FanListenerStub : IBossFanTelegraphListener
        {
            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius,
                float arcAngle, float telegraphSeconds)
            {
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
            }
        }

        /****************************************
        *               Movement
        ****************************************/

        [Test]
        public void Begin_ReportsDefinitionLengthAndWidth()
        {
            Rig rig = new Rig();
            rig.Start();

            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(RANGE, rig.Listener.StartLength, POSITION_TOLERANCE);
            Assert.AreEqual(WIDTH, rig.Listener.StartWidth, POSITION_TOLERANCE);
        }

        [Test]
        public void Tick_BeforeTelegraphEnds_DoesNotMoveOrHit()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.TickRange(0.0, TELEGRAPH - 0.1);

            Assert.AreEqual(0, rig.Movement.MoveCalls);
            Assert.AreEqual(0f, Vector3.Distance(Origin, rig.Self.Position), POSITION_TOLERANCE);
            Assert.AreEqual(0, rig.Listener.DashStartCount);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        [Test]
        public void Tick_ThroughExecute_MovesBossEightMetersAtConstantSpeed()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.TickRange(0.0, rig.ExecuteStart + (ACTIVE * 0.5f));
            Assert.AreEqual(RANGE * 0.5f, rig.Self.Position.z, 0.1f);

            rig.TickRange(rig.ExecuteStart + (ACTIVE * 0.5f), rig.RecoverEnd + 0.1);

            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.DashStartCount);
            Assert.AreEqual(RANGE, rig.Listener.DashDistance, POSITION_TOLERANCE);
            Assert.AreEqual(ACTIVE, rig.Listener.DashSeconds, POSITION_TOLERANCE);
            Assert.AreEqual(RANGE / ACTIVE, rig.Listener.DashSpeed, 0.01f);
            Assert.AreEqual(rig.ExecuteStart, rig.Listener.DashStartTime, TIME_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.StopCount);
            Assert.IsFalse(rig.Listener.StopByWall);
            Assert.AreEqual(RANGE, rig.Listener.StopPosition.z, POSITION_TOLERANCE);
            Assert.IsTrue(rig.Pattern.IsFinished);
        }

        // 벽은 z=6이고 몸체 반경이 2m라 4m에서 멈춘다. 예고 길이도 같은 4m다
        [Test]
        public void Tick_WallAhead_StopsInFrontOfWallAndSkipsTargetsBeyond()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 6f;
            FakeCombatTarget beyondStop = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, 5f));
            rig.Start();

            Assert.AreEqual(4f, rig.Listener.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, rig.RecoverEnd + 0.1);

            Assert.AreEqual(4f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.StopCount);
            Assert.IsTrue(rig.Listener.StopByWall);
            Assert.AreEqual(4f, rig.Listener.StopPosition.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, beyondStop.CombatId));
        }

        /****************************************
        *            Hit Area And Count
        ****************************************/

        // 판정 폭 2.5m라 중심선에서 좌우 1.25m까지다, 시작점 뒤와 끝점 너머는 맞지 않는다
        [Test]
        public void Tick_ThroughExecute_HitsOnlyTargetsInsideWidthAndLength()
        {
            Rig rig = new Rig();
            FakeCombatTarget insideRight = rig.AddPlayer(PROBE_ID, new Vector3(1f, 0f, 4f));
            FakeCombatTarget insideLeft  = rig.AddPlayer(PROBE_ID + 1, new Vector3(-1f, 0f, 6f));
            FakeCombatTarget tooWide     = rig.AddPlayer(PROBE_ID + 2, new Vector3(1.5f, 0f, 4f));
            FakeCombatTarget behind      = rig.AddPlayer(PROBE_ID + 3, new Vector3(0f, 0f, -1f));
            FakeCombatTarget beyondEnd   = rig.AddPlayer(PROBE_ID + 4, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.1);

            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, insideRight.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, insideLeft.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, tooWide.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, behind.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, beyondEnd.CombatId));
            Assert.AreEqual(3, rig.Listener.HitTotal);
        }

        [Test]
        public void Tick_ThroughExecute_AppliesExpectedRequestOncePerTarget()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.1);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            DamageRequest request = rig.Resolver.Requests[0];
            Assert.AreEqual(rig.Self.CombatId, request.AttackerId);
            Assert.AreEqual(rig.Tracked.CombatId, request.HitTargetId);
            Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
            Assert.AreEqual(MucheolPatternFactory.P03_ID, request.SourceDefinitionId);
            Assert.AreEqual(DAMAGE_REF * TEST_HREF, request.RawDamage, 0.001f);
            Assert.AreEqual(EDamageFlags.None, request.Flags);
            Assert.IsTrue(request.HasValidIds);
        }

        /****************************************
        *            Direction Lock
        ****************************************/

        // 타깃이 오른쪽 90도에 있어도 처음 0.4초 동안 초당 150도로만 돌아 최대 60도다
        [Test]
        public void Tick_RotatesAtMostTurnRate_AndLocksAfterTrackingEnd()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            rig.TickRange(0.0, 0.2);
            Assert.AreEqual(TURN_RATE * 0.2f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.TickRange(0.2, 0.5);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(TURN_RATE * TRACKING, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.LockCount);
        }

        [Test]
        public void Tick_AfterLock_TargetMovingDoesNotChangeDirectionOrDashPath()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();
            rig.TickRange(0.0, 0.5);
            Vector3 locked = rig.Pattern.AimDirection;
            int aimEvents = rig.Listener.AimCount;

            rig.Tracked.Position = new Vector3(-10f, 0f, 0f);
            rig.TickRange(0.5, rig.RecoverEnd + 0.1);

            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(aimEvents, rig.Listener.AimCount);
            Assert.AreEqual(1, rig.Listener.LockCount);

            // 60도 방향으로 8m 간 위치다
            Vector3 expected = locked * RANGE;
            Assert.AreEqual(expected.x, rig.Self.Position.x, POSITION_TOLERANCE);
            Assert.AreEqual(expected.z, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        /****************************************
        *               Cancel
        ****************************************/

        [Test]
        public void Cancel_DuringTelegraph_NeverMovesOrHits()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.TickRange(0.0, 0.5);

            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.TickRange(0.5, rig.RecoverEnd + 0.1);

            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(0, rig.Movement.MoveCalls);
            Assert.AreEqual(0f, Vector3.Distance(Origin, rig.Self.Position), POSITION_TOLERANCE);
            Assert.AreEqual(0, rig.Listener.DashStartCount);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.Death, rig.Listener.Cancels[0]);
        }

        [Test]
        public void Cancel_DuringDash_StopsMovementAndSkipsRemainingTargets()
        {
            Rig rig = new Rig();
            FakeCombatTarget ahead = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, 6f));
            rig.Start();

            // 실행 시작 0.2초 뒤에는 3.2m까지 갔고 추적 대상(3m)만 지나갔다
            rig.TickRange(0.0, rig.ExecuteStart + 0.2);
            rig.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            Vector3 stoppedAt = rig.Self.Position;
            int movesAtCancel = rig.Movement.MoveCalls;

            rig.TickRange(rig.ExecuteStart + 0.2, rig.RecoverEnd + 0.1);

            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.Greater(stoppedAt.z, 3f);
            Assert.Less(stoppedAt.z, 6f);
            Assert.AreEqual(stoppedAt.z, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(movesAtCancel, rig.Movement.MoveCalls);
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, ahead.CombatId));
            Assert.AreEqual(0, rig.Listener.StopCount);
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, rig.Listener.Cancels[0]);
        }
    }
}
