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
    // 클라이언트 표현 모델(BossTelegraphCast)의 전이 규칙과 시간 계산을 확인하고, 실제 서버 패턴 네 개가 내는 알림을
    // 그대로 모델에 넘겨 표시 범위와 서버 피해 범위가 같은지 비교한다. ClientBridge는 네트워크 수신부가 할 변환의 예시이며
    // 전송 형식은 정하지 않는다. 이 테스트는 Mirror와 실제 서버, 화면 재생을 거치지 않는다
    public class BossTelegraphCastTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    BOSS_ID       = 1;
        private const float  TEST_HREF     = 3541f;
        private const float  BODY_RADIUS   = 2f;
        private const float  STEP          = 0.01f;
        private const float  TOLERANCE     = 0.002f;
        private const float  ANGLE_TOL     = 0.05f;
        private const float  DASH_SPEED    = 8f / 0.45f;
        private const double EPSILON       = 0.001;
        private const string P01_ID        = "wolbaek_p01";
        private const string P02_ID        = "wolbaek_p02";
        private const string P03_ID        = "wolbaek_p03";
        private const string P06_ID        = "wolbaek_p06";

        private static readonly Vector3 Origin = new Vector3(3f, 0f, -2f);

        /****************************************
        *               Helpers
        ****************************************/

        private static BossTelegraphStart FanStart(int castId = 7, double start = 10.0, float seconds = 1f)
        {
            return BossTelegraphStart.Fan(BOSS_ID, castId, P02_ID, Origin, Vector3.forward, 6f, 100f, start, seconds);
        }

        private static BossTelegraphStart DashStart(int castId = 7, double start = 10.0, float length = 8f)
        {
            return BossTelegraphStart.Dash(BOSS_ID, castId, P01_ID, Origin, Vector3.forward, length, 2f, start, 0.8f);
        }

        private static BossTelegraphStart LineStart(int castId = 7, double start = 10.0)
        {
            return BossTelegraphStart.Line(BOSS_ID, castId, P06_ID, Origin, Vector3.forward, 14f, 4f, start, 1.6f);
        }

        private static Vector3 Polar(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        private static float AimDegrees(Vector3 aim)
        {
            return Vector3.SignedAngle(Vector3.forward, aim, Vector3.up);
        }

        /****************************************
        *            Model Transitions
        ****************************************/

        [Test]
        public void Start_StoresServerValuesAsGiven()
        {
            BossTelegraphCast cast = new BossTelegraphCast(FanStart());

            Assert.AreEqual(EBossTelegraphShape.Fan, cast.Shape);
            Assert.AreEqual(6f, cast.Length);
            Assert.AreEqual(100f, cast.ArcAngle);
            Assert.AreEqual(Origin, cast.Origin);
            Assert.AreEqual(EBossTelegraphPhase.Telegraph, cast.Phase);
            Assert.IsFalse(cast.IsDirectionLocked);
            Assert.AreEqual(11.0, cast.TelegraphEndServerTime, EPSILON);
        }

        [Test]
        public void UpdateAim_BeforeLock_AppliesDirectionAndLength()
        {
            BossTelegraphCast cast = new BossTelegraphCast(DashStart());

            Assert.IsTrue(cast.TryUpdateAim(Polar(30f), 5f, 10.1));

            Assert.AreEqual(30f, AimDegrees(cast.Direction), ANGLE_TOL);
            Assert.AreEqual(5f, cast.Length);
        }

        [Test]
        public void UpdateAim_OlderServerTime_IsRejected()
        {
            BossTelegraphCast cast = new BossTelegraphCast(FanStart());
            Assert.IsTrue(cast.TryUpdateAim(Polar(30f), 6f, 10.3));

            Assert.IsFalse(cast.TryUpdateAim(Polar(10f), 6f, 10.2));

            Assert.AreEqual(30f, AimDegrees(cast.Direction), ANGLE_TOL);
        }

        [Test]
        public void UpdateAim_AfterLock_IsRejected()
        {
            BossTelegraphCast cast = new BossTelegraphCast(FanStart());
            Assert.IsTrue(cast.TryLockDirection(Polar(40f), 6f));

            Assert.IsFalse(cast.TryUpdateAim(Polar(80f), 6f, 10.9));
            Assert.IsFalse(cast.TryLockDirection(Polar(80f), 6f));

            Assert.AreEqual(40f, AimDegrees(cast.Direction), ANGLE_TOL);
            Assert.IsTrue(cast.IsDirectionLocked);
        }

        // 고정이 마지막 갱신보다 먼저 도착해도 고정이 이긴다
        [Test]
        public void Lock_ArrivingBeforeLastAim_StillWins()
        {
            BossTelegraphCast cast = new BossTelegraphCast(FanStart());

            Assert.IsTrue(cast.TryLockDirection(Polar(40f), 6f));
            Assert.IsFalse(cast.TryUpdateAim(Polar(35f), 6f, 10.45));

            Assert.AreEqual(40f, AimDegrees(cast.Direction), ANGLE_TOL);
        }

        [Test]
        public void Strike_FromTelegraph_MovesToExecuteOnce()
        {
            BossTelegraphCast cast = new BossTelegraphCast(FanStart());

            Assert.IsTrue(cast.TryStrike(11.0));
            Assert.IsFalse(cast.TryStrike(11.2));

            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase);
            Assert.IsTrue(cast.HasExecuted);
            Assert.IsTrue(cast.IsDirectionLocked);
            Assert.AreEqual(11.0, cast.ExecuteServerTime, EPSILON);
        }

        [Test]
        public void Strike_OnDash_IsRejectedAndDashUsesItsOwnEntry()
        {
            BossTelegraphCast cast = new BossTelegraphCast(DashStart());

            Assert.IsFalse(cast.TryStrike(10.8));
            Assert.IsTrue(cast.TryBeginDash(8f, 0.45f, DASH_SPEED, 10.8));
            Assert.IsFalse(cast.TryBeginDash(8f, 0.45f, DASH_SPEED, 10.9));

            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase);
        }

        [Test]
        public void BeginDash_OnNonDash_IsRejected()
        {
            BossTelegraphCast cast = new BossTelegraphCast(LineStart());

            Assert.IsFalse(cast.TryBeginDash(14f, 0.45f, 10f, 11.6));
            Assert.AreEqual(EBossTelegraphPhase.Telegraph, cast.Phase);
        }

        [Test]
        public void Cancel_BeforeStrike_EndsAndNothingRevivesIt()
        {
            BossTelegraphCast cast = new BossTelegraphCast(LineStart());

            Assert.IsTrue(cast.TryCancel());

            Assert.IsTrue(cast.IsEnded);
            Assert.IsFalse(cast.TryUpdateAim(Polar(10f), 14f, 11.0));
            Assert.IsFalse(cast.TryLockDirection(Polar(10f), 14f));
            Assert.IsFalse(cast.TryStrike(11.6));
            Assert.IsFalse(cast.TryFinish());
            Assert.IsFalse(cast.TryCancel());
            Assert.AreEqual(EBossTelegraphPhase.Cancelled, cast.Phase);
            Assert.IsFalse(cast.HasExecuted);
        }

        // 타격이 확정된 뒤의 취소는 서버가 내지 않는 알림이라 이미 확정한 표현을 지우지 않는다
        [Test]
        public void Cancel_AfterStrikeOnNonDash_IsRejected()
        {
            BossTelegraphCast cast = new BossTelegraphCast(LineStart());
            cast.TryStrike(11.6);

            Assert.IsFalse(cast.TryCancel());
            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase);
        }

        [Test]
        public void Cancel_DuringDash_Ends_ButStopAfterwardIsRejected()
        {
            BossTelegraphCast cast = new BossTelegraphCast(DashStart());
            cast.TryBeginDash(8f, 0.45f, DASH_SPEED, 10.8);

            Assert.IsTrue(cast.TryCancel());
            Assert.IsFalse(cast.TryStopDash(Origin + (Vector3.forward * 3f), false));
            Assert.AreEqual(EBossTelegraphPhase.Cancelled, cast.Phase);
        }

        [Test]
        public void Finish_AfterExecute_KeepsExecuteTimeForFadeOut()
        {
            BossTelegraphCast cast = new BossTelegraphCast(LineStart());
            cast.TryStrike(11.6);

            Assert.IsTrue(cast.TryFinish());

            Assert.AreEqual(EBossTelegraphPhase.Finished, cast.Phase);
            Assert.AreEqual(0.1f, cast.GetExecuteAge(11.7), 0.0001f);
            Assert.IsFalse(cast.TryCancel());
        }

        [Test]
        public void Expire_OnlyFromTelegraph()
        {
            BossTelegraphCast waiting = new BossTelegraphCast(FanStart());
            BossTelegraphCast struck  = new BossTelegraphCast(FanStart(8));
            struck.TryStrike(11.0);

            Assert.IsTrue(waiting.TryExpire());
            Assert.IsFalse(struck.TryExpire());
            Assert.AreEqual(EBossTelegraphPhase.Cancelled, waiting.Phase);
            Assert.AreEqual(EBossTelegraphPhase.Execute, struck.Phase);
        }

        /****************************************
        *            Time And Dash Math
        ****************************************/

        [Test]
        public void TelegraphProgress_UsesServerTime_SoLateReceiptContinues()
        {
            BossTelegraphCast cast = new BossTelegraphCast(FanStart(start: 10.0, seconds: 1f));

            Assert.AreEqual(0f, cast.GetTelegraphProgress(9.5), 0.0001f);
            Assert.AreEqual(0f, cast.GetTelegraphProgress(10.0), 0.0001f);
            Assert.AreEqual(0.6f, cast.GetTelegraphProgress(10.6), 0.0001f);
            Assert.AreEqual(1f, cast.GetTelegraphProgress(12.0), 0.0001f);
        }

        [Test]
        public void DashTraveled_IsConstantSpeed_AndClampedAtDistance()
        {
            BossTelegraphCast cast = new BossTelegraphCast(DashStart());
            cast.TryBeginDash(5f, 0.45f, DASH_SPEED, 10.8);

            Assert.AreEqual(0f, cast.GetDashTraveled(10.8), 0.0001f);
            Assert.AreEqual(DASH_SPEED * 0.1f, cast.GetDashTraveled(10.9), 0.001f);
            Assert.AreEqual(DASH_SPEED * 0.2f, cast.GetDashTraveled(11.0), 0.001f);
            Assert.Less(cast.GetDashTraveled(11.05), 5f);
            Assert.AreEqual(5f, cast.GetDashTraveled(11.1), 0.0001f);
            Assert.AreEqual(5f, cast.GetDashTraveled(99.0), 0.0001f);
        }

        // 벽 때문에 거리가 줄어도 속도는 서버가 보낸 값 그대로라 도착 시각이 앞당겨진다
        [Test]
        public void DashEndAge_WithWall_UsesServerSpeedNotShortenedDistance()
        {
            BossTelegraphCast cast = new BossTelegraphCast(DashStart());
            cast.TryBeginDash(5f, 0.45f, DASH_SPEED, 10.8);

            Assert.AreEqual(5f / DASH_SPEED, cast.GetDashEndAge(), 0.0001f);
            Assert.Less(cast.GetDashEndAge(), 0.45f);
        }

        [Test]
        public void StopDash_SnapsTraveledToServerStopDistance()
        {
            BossTelegraphCast cast = new BossTelegraphCast(DashStart());
            cast.TryBeginDash(8f, 0.45f, DASH_SPEED, 10.8);

            Assert.IsTrue(cast.TryStopDash(Origin + (Vector3.forward * 3f), true));
            Assert.IsFalse(cast.TryStopDash(Origin + (Vector3.forward * 4f), true));

            Assert.IsTrue(cast.IsDashStopped);
            Assert.IsTrue(cast.DashStoppedByWall);
            Assert.AreEqual(3f, cast.GetDashTraveled(10.85), 0.0001f);
            Assert.AreEqual(3f, cast.GetDashTraveled(99.0), 0.0001f);
            Assert.AreEqual(3f / DASH_SPEED, cast.GetDashEndAge(), 0.0001f);
        }

        [Test]
        public void Snapshot_RoundTrip_ContinuesFromSameServerTime()
        {
            BossTelegraphCast original = new BossTelegraphCast(DashStart());
            original.TryUpdateAim(Polar(25f), 6f, 10.2);
            original.TryLockDirection(Polar(40f), 6f);
            original.TryBeginDash(6f, 0.45f, DASH_SPEED, 10.8);

            BossTelegraphCast restored = BossTelegraphCast.FromSnapshot(original.ToSnapshot());

            Assert.AreEqual(original.Phase, restored.Phase);
            Assert.AreEqual(40f, AimDegrees(restored.Direction), ANGLE_TOL);
            Assert.AreEqual(6f, restored.Length);
            Assert.IsTrue(restored.IsDirectionLocked);
            Assert.IsTrue(restored.HasExecuted);
            for (double now = 10.8; now < 11.4; now += 0.05)
            {
                Assert.AreEqual(original.GetDashTraveled(now), restored.GetDashTraveled(now), 0.0001f);
            }
        }

        /****************************************
        *      Server Pattern To Client Model
        ****************************************/

        // 네트워크 수신부가 할 변환의 예시다. 서버 알림에 서버 시각을 붙여 모델에 넘긴다. 시각은 테스트가 틱 전에 정한다
        private sealed class ClientBridge : IBossFanTelegraphListener, IBossCircleTelegraphListener,
            IBossLineTelegraphListener, IBossDashTelegraphListener
        {
            public double Now;
            public readonly Dictionary<int, BossTelegraphCast> Casts = new Dictionary<int, BossTelegraphCast>();

            public BossTelegraphCast Only
            {
                get
                {
                    Assert.AreEqual(1, Casts.Count);
                    foreach (KeyValuePair<int, BossTelegraphCast> pair in Casts)
                    {
                        return pair.Value;
                    }

                    return null;
                }
            }

            void IBossFanTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
                float radius, float arcAngle, float telegraphSeconds)
            {
                Casts[castInstanceId] = new BossTelegraphCast(BossTelegraphStart.Fan(BOSS_ID, castInstanceId, P02_ID, origin,
                    direction, radius, arcAngle, Now, telegraphSeconds));
            }

            void IBossLineTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
                float length, float width, float telegraphSeconds)
            {
                Casts[castInstanceId] = new BossTelegraphCast(BossTelegraphStart.Line(BOSS_ID, castInstanceId, P06_ID, origin,
                    direction, length, width, Now, telegraphSeconds));
            }

            void IBossDashTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
                float length, float width, float telegraphSeconds)
            {
                Casts[castInstanceId] = new BossTelegraphCast(BossTelegraphStart.Dash(BOSS_ID, castInstanceId, P01_ID, origin,
                    direction, length, width, Now, telegraphSeconds));
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds)
            {
                Casts[castInstanceId] = new BossTelegraphCast(BossTelegraphStart.Circle(BOSS_ID, castInstanceId, P03_ID, center,
                    radius, Now, telegraphSeconds));
            }

            // 부채꼴과 직사각형은 길이가 바뀌지 않으므로 모델이 가진 길이를 그대로 쓴다
            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
                BossTelegraphCast cast = Casts[castInstanceId];
                cast.TryUpdateAim(direction, cast.Length, Now);
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                Casts[castInstanceId].TryUpdateAim(direction, length, Now);
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
                BossTelegraphCast cast = Casts[castInstanceId];
                cast.TryLockDirection(direction, cast.Length);
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                Casts[castInstanceId].TryLockDirection(direction, length);
            }

            public void OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance, float seconds,
                float speed, double startServerTime)
            {
                Casts[castInstanceId].TryBeginDash(distance, seconds, speed, startServerTime);
            }

            public void OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall)
            {
                Casts[castInstanceId].TryStopDash(position, stoppedByWall);
            }

            // 돌진은 OnHit을 이동 중 피해 알림으로만 쓰므로 모델에 넘기지 않는다. 나머지 세 패턴의 OnHit은 타격 확정이다
            public void OnHit(int castInstanceId, int hitCount)
            {
                BossTelegraphCast cast = Casts[castInstanceId];
                if (cast.Shape != EBossTelegraphShape.Dash)
                {
                    cast.TryStrike(Now);
                }
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Casts[castInstanceId].TryCancel();
            }
        }

        private sealed class GridQuery : ICombatTargetQuery
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
                    Vector3 offset = Targets[i].Position - center;
                    offset.y = 0f;
                    if ((Targets[i].LifeState == EParticipantLifeState.Alive) && (Targets[i].Faction == faction)
                        && (includeSummons || !Targets[i].IsSummon) && (offset.sqrMagnitude <= (radius * radius)))
                    {
                        results.Add(Targets[i]);
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

        // z축 벽 평면, 몸체 반경만큼 앞에서 멈춘다
        private sealed class WallMovement : IBossMovement
        {
            public FakeCombatTarget Self;
            public float            WallZ = float.PositiveInfinity;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                if ((float.IsPositiveInfinity(WallZ)) || (direction.z <= 0.0001f))
                {
                    return maxDistance;
                }

                return Mathf.Clamp((WallZ - bodyRadius - origin.z) / direction.z, 0f, maxDistance);
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                Vector3 delta = to - from;
                Vector3 reached = to;
                if ((!float.IsPositiveInfinity(WallZ)) && (delta.z > 0.0001f) && (to.z > (WallZ - bodyRadius)))
                {
                    float t = Mathf.Clamp01((WallZ - bodyRadius - from.z) / delta.z);
                    reached = from + (delta * t);
                }

                Self.Position = reached;
                return reached;
            }
        }

        // 실제 서버 패턴 하나를 공격 FSM에 올리고 알림을 모델로 흘린다
        private sealed class ServerRun
        {
            public FakeCombatTarget  Self    { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin, BodyRadius = BODY_RADIUS };
            public FakeCombatTarget  Tracked { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player };
            public GridQuery         Query   { get; } = new GridQuery();
            public FakeDamageResolver Resolver { get; } = new FakeDamageResolver();
            public ClientBridge      Bridge  { get; } = new ClientBridge();
            public WallMovement      Movement { get; } = new WallMovement();
            public BossAttackFsm     Fsm     { get; } = new BossAttackFsm();
            public BossDamageGate    Gate    { get; }
            public PatternDefinition Definition { get; }
            public BossPatternBase   Pattern { get; }

            public ServerRun(string patternId, float trackedDegrees)
            {
                Movement.Self = Self;
                Self.Forward  = Vector3.forward;
                Tracked.Position = Origin + (Polar(trackedDegrees) * 4f);
                Query.Targets.Add(Tracked);
                Gate = new BossDamageGate(Resolver);
                Gate.Open();

                switch (patternId)
                {
                    case P01_ID:
                        Definition = Make(patternId, 0.8f, 0.4f, 180f, 0.45f, 0.8f, EnemyCatalogSO.AttackShapeSpec.Dash(8f, 2f), 0.18f);
                        Pattern = new BossDashPattern(TEST_HREF, Movement, Bridge);
                        break;
                    case P02_ID:
                        Definition = Make(patternId, 1f, 0.5f, 120f, 0f, 0.5f, EnemyCatalogSO.AttackShapeSpec.Fan(6f, 100f), 0.24f);
                        Pattern = new BossFanBlastPattern(TEST_HREF, Bridge);
                        break;
                    case P03_ID:
                        Definition = Make(patternId, 1.2f, 0f, 0f, 0f, 0.5f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.28f);
                        Pattern = new BossCircleBlastPattern(TEST_HREF, Bridge);
                        break;
                    default:
                        Definition = Make(patternId, 1.6f, 0.6f, 90f, 0f, 0.5f, EnemyCatalogSO.AttackShapeSpec.Line(14f, 4f), 0.45f);
                        Pattern = new BossLineBlastPattern(TEST_HREF, Bridge);
                        break;
                }
            }

            private static PatternDefinition Make(string id, float telegraph, float tracking, float turn, float active,
                float recovery, EnemyCatalogSO.AttackShapeSpec shape, float damage)
            {
                return new PatternDefinition(id, BossTestFixtures.BOSS_ID, 1, 10f,
                    new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                    telegraph, tracking, turn, active, recovery, shape, damage, true);
            }

            public void Start(double serverTime)
            {
                Bridge.Now = serverTime;
                BossPatternContext context = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }

            public void TickTo(double from, double to)
            {
                int count = (int)Math.Round((to - from) / STEP);
                for (int i = 1; i <= count; ++i)
                {
                    double time = from + (i * (double)STEP);
                    Bridge.Now = time;
                    Fsm.Tick(time);
                }
            }

            // 격자 위 대상을 모두 올린다, 경계에 우연히 겹치지 않도록 간격을 불규칙하게 둔다
            public void FillGrid()
            {
                int id = 100;
                for (int i = 0; i < 26; ++i)
                {
                    for (int j = 0; j < 31; ++j)
                    {
                        FakeCombatTarget target = new FakeCombatTarget
                        {
                            CombatId = id, Faction = ECombatFaction.Player,
                            Position = Origin + new Vector3(-9.13f + (0.731f * i), 0f, -2.37f + (0.617f * j))
                        };
                        Query.Targets.Add(target);
                        ++id;
                    }
                }
            }

            public bool WasHit(int id)
            {
                for (int i = 0; i < Resolver.Requests.Count; ++i)
                {
                    if (Resolver.Requests[i].HitTargetId == id)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // 서버가 맞힌 대상 집합과 모델이 가진 값으로 그린 표시 범위 안의 집합이 같다
        private static void AssertDisplayedAreaEqualsServerHits(ServerRun run, BossTelegraphCast cast, float pathLength)
        {
            for (int i = 0; i < run.Query.Targets.Count; ++i)
            {
                FakeCombatTarget target = run.Query.Targets[i];
                bool shown = BossTelegraphRules.IsInside(cast.Shape, cast.Origin, cast.Direction, pathLength, cast.Width,
                    cast.ArcAngle, target.Position);
                Assert.AreEqual(shown, run.WasHit(target.CombatId), $"target {target.CombatId} at {target.Position}");
            }
        }

        [Test]
        public void ServerFan_ModelShapeAndDisplayedAreaMatchServerHits()
        {
            ServerRun run = new ServerRun(P02_ID, 80f);
            run.FillGrid();
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 1.0 + 0.1);

            BossTelegraphCast cast = run.Bridge.Only;
            Assert.AreEqual(EBossTelegraphShape.Fan, cast.Shape);
            Assert.AreEqual(6f, cast.Length);
            Assert.AreEqual(100f, cast.ArcAngle);
            Assert.IsTrue(cast.IsDirectionLocked);
            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase);
            Assert.AreEqual(60f, AimDegrees(cast.Direction), ANGLE_TOL); // 0.5초 x 120도/초
            Assert.AreEqual(11.0, cast.ExecuteServerTime, 0.02);
            AssertDisplayedAreaEqualsServerHits(run, cast, cast.Length);
        }

        [Test]
        public void ServerCircle_ModelShapeAndDisplayedAreaMatchServerHits()
        {
            ServerRun run = new ServerRun(P03_ID, 0f);
            run.FillGrid();
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 1.2 + 0.1);

            BossTelegraphCast cast = run.Bridge.Only;
            Assert.AreEqual(EBossTelegraphShape.Circle, cast.Shape);
            Assert.AreEqual(5f, cast.Length);
            Assert.AreEqual(Origin, cast.Origin);
            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase);
            AssertDisplayedAreaEqualsServerHits(run, cast, cast.Length);
        }

        [Test]
        public void ServerLine_ModelShapeAndDisplayedAreaMatchServerHits()
        {
            ServerRun run = new ServerRun(P06_ID, 30f);
            run.FillGrid();
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 1.6 + 0.1);

            BossTelegraphCast cast = run.Bridge.Only;
            Assert.AreEqual(EBossTelegraphShape.Line, cast.Shape);
            Assert.AreEqual(14f, cast.Length);
            Assert.AreEqual(4f, cast.Width);
            Assert.AreEqual(30f, AimDegrees(cast.Direction), ANGLE_TOL); // 0.6초 x 90도/초는 54도지만 타깃이 30도에 있어 30도까지만
            AssertDisplayedAreaEqualsServerHits(run, cast, cast.Length);
        }

        [Test]
        public void ServerDash_OpenField_ModelAndDisplayedAreaMatchServerHits()
        {
            ServerRun run = new ServerRun(P01_ID, 45f);
            run.FillGrid();
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 0.8 + 0.45 + 0.1);

            BossTelegraphCast cast = run.Bridge.Only;
            Assert.AreEqual(EBossTelegraphShape.Dash, cast.Shape);
            Assert.AreEqual(8f, cast.Length);
            Assert.AreEqual(2f, cast.Width);
            Assert.AreEqual(45f, AimDegrees(cast.Direction), ANGLE_TOL);
            Assert.AreEqual(DASH_SPEED, cast.DashSpeed, 0.0001f);
            Assert.AreEqual(10.8, cast.ExecuteServerTime, 0.0001);
            Assert.IsTrue(cast.IsDashStopped);
            Assert.IsFalse(cast.DashStoppedByWall);
            Assert.AreEqual(8f, cast.GetDashTraveled(99.0), TOLERANCE);
            AssertDisplayedAreaEqualsServerHits(run, cast, cast.Length);
        }

        // 벽이 가까우면 서버가 보낸 줄어든 길이가 표시 범위이고 그 너머 대상은 맞지 않는다
        [Test]
        public void ServerDash_NearWall_ModelShowsClearLengthAndDisplayedAreaMatchesServerHits()
        {
            ServerRun run = new ServerRun(P01_ID, 0f);
            run.Movement.WallZ = Origin.z + 5f;
            run.FillGrid();
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 0.8 + 0.45 + 0.1);

            BossTelegraphCast cast = run.Bridge.Only;
            Assert.AreEqual(3f, cast.Length, TOLERANCE);
            Assert.AreEqual(3f, cast.DashDistance, TOLERANCE);
            Assert.IsTrue(cast.DashStoppedByWall);
            Assert.AreEqual(3f, cast.GetDashTraveled(99.0), TOLERANCE);
            Assert.AreEqual(DASH_SPEED, cast.DashSpeed, 0.0001f);
            AssertDisplayedAreaEqualsServerHits(run, cast, cast.Length);
        }

        [Test]
        public void ServerDash_TrackingNearWall_LengthUpdatesFollowAimAndLockMatchesDash()
        {
            ServerRun run = new ServerRun(P01_ID, 70f);
            run.Movement.WallZ = Origin.z + 5f;
            run.Start(10.0);

            BossTelegraphCast cast = run.Bridge.Only;
            Assert.AreEqual(3f, cast.Length, TOLERANCE);

            run.TickTo(10.0, 10.0 + 0.2);
            float midLength = cast.Length;
            Assert.Greater(midLength, 3f, "방향이 돌면 벽까지 거리가 늘어난다");

            run.TickTo(10.2, 10.0 + 0.8 + 0.1);
            Assert.IsTrue(cast.IsDirectionLocked);
            Assert.AreEqual(cast.Length, cast.DashDistance, TOLERANCE);
            Assert.AreEqual(8f, cast.Length, TOLERANCE);
        }

        // 추적 구간 동안 모델의 방향이 서버 방향과 같다
        [Test]
        public void ServerDash_ModelDirectionEqualsServerDirectionEveryTick()
        {
            ServerRun run = new ServerRun(P01_ID, 70f);
            run.Start(10.0);
            BossDashPattern pattern = (BossDashPattern)run.Pattern;
            BossTelegraphCast cast = run.Bridge.Only;

            for (int i = 1; i <= 60; ++i)
            {
                double time = 10.0 + (i * (double)STEP);
                run.Bridge.Now = time;
                run.Fsm.Tick(time);
                Assert.AreEqual(0f, Vector3.Angle(pattern.AimDirection, cast.Direction), ANGLE_TOL, $"tick {i}");
            }
        }

        [Test]
        public void ServerDash_CancelledMidDash_ModelEndsAndIgnoresLaterNotifications()
        {
            ServerRun run = new ServerRun(P01_ID, 0f);
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 0.8 + 0.2);
            BossTelegraphCast cast = run.Bridge.Only;
            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase);

            run.Fsm.Cancel(EBossInterruptReason.Death);

            Assert.AreEqual(EBossTelegraphPhase.Cancelled, cast.Phase);
            Assert.IsFalse(cast.TryStopDash(Origin + (Vector3.forward * 2f), false));
        }

        [TestCase(P02_ID, EBossInterruptReason.PhaseTransition)]
        [TestCase(P03_ID, EBossInterruptReason.CombatEnd)]
        [TestCase(P06_ID, EBossInterruptReason.DirectControl)]
        public void ServerCancelDuringTelegraph_ModelCancelsAndStaysCancelled(string patternId, EBossInterruptReason reason)
        {
            ServerRun run = new ServerRun(patternId, 20f);
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 0.3);
            BossTelegraphCast cast = run.Bridge.Only;

            run.Fsm.Cancel(reason);
            run.Bridge.Now = 12.0;
            run.Fsm.Tick(12.0);

            Assert.AreEqual(EBossTelegraphPhase.Cancelled, cast.Phase);
            Assert.IsFalse(cast.HasExecuted);
            Assert.IsFalse(cast.TryStrike(12.0));
            Assert.AreEqual(0, run.Resolver.Requests.Count);
        }
    }
}
