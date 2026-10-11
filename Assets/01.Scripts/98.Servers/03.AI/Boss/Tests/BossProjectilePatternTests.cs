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
    // 월백 P04 패턴과 투사체 수명 관리자를 가짜 의존성으로 확인한다. 정의 값은 기획 표의 P04를 그대로 쓰되 가중치와 배율은 가짜 값이다
    // 실제 서버 틱, 실제 벽 검사, 복제, 화면은 거치지 않는다
    public class BossProjectilePatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string P04_ID       = "wolbaek_p04";
        private const string HARD_P04_ID  = "wolbaek_hard_p04";
        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  TEST_HREF    = 3541f;
        private const float  TELEGRAPH    = 0.9f;
        private const float  TRACKING     = 0.45f;
        private const float  TURN_RATE    = 120f;
        private const float  RECOVERY     = 0.5f;
        private const float  RANGE        = 35f;
        private const float  WIDTH        = 1f;
        private const float  SPEED        = 10f;
        private const float  DAMAGE_REF   = 0.18f;
        private const float  STEP         = 0.01f;
        private const float  TOLERANCE    = 0.02f;
        private const float  ANGLE_TOL    = 0.05f;
        private const double EPSILON      = 0.001;

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP04(bool guardable = true)
        {
            return new PatternDefinition(P04_ID, BossTestFixtures.BOSS_ID, 1, 20f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, TRACKING, TURN_RATE, 0f, RECOVERY, EnemyCatalogSO.AttackShapeSpec.Projectile(RANGE, WIDTH, SPEED),
                DAMAGE_REF, guardable, EBossActionState.Recover, RECOVERY, false);
        }

        private static double LaunchOf(PatternDefinition definition, double start)
        {
            return start + definition.TelegraphSeconds;
        }

        private static double RecoverEndOf(PatternDefinition definition, double start)
        {
            return LaunchOf(definition, start) + definition.ActiveSeconds + definition.RecoverySeconds;
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

        // z = WallZ 평면, 반경만큼 앞에서 막는다
        private sealed class PlaneWall : IBossWallQuery
        {
            public float WallZ = float.PositiveInfinity;
            public int   Calls;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                ++Calls;
                if ((float.IsPositiveInfinity(WallZ)) || (direction.z <= 0.0001f))
                {
                    return maxDistance;
                }

                return Mathf.Clamp((WallZ - bodyRadius - origin.z) / direction.z, 0f, maxDistance);
            }
        }

        private sealed class ProjQuery : ICombatTargetQuery
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

        // 요청 시각과 지정 결과를 기록한다
        private sealed class ProjResolver : IDamageResolver
        {
            private int _nextId = 1;

            public double       Time;
            public DamageResult Result = DamageResult.Applied(1f, false, false);

            public List<DamageRequest> Requests { get; } = new List<DamageRequest>();
            public List<double>        Times    { get; } = new List<double>();

            public DamageResult Apply(in DamageRequest request)
            {
                Requests.Add(request);
                Times.Add(Time);
                return Result;
            }

            public int IssueInstanceId()
            {
                int issued = _nextId;
                ++_nextId;
                return issued;
            }
        }

        private sealed class ProjListener : IBossProjectileListener, IBossProjectileTelegraphListener
        {
            public double Now;

            public int                          StartCount;
            public Vector3                      StartDirection;
            public float                        StartLength;
            public float                        StartWidth;
            public float                        StartSeconds;
            public List<float>                  AimLengths = new List<float>();
            public int                          LockCount;
            public Vector3                      LockDirection;
            public float                        LockLength;
            public List<EBossInterruptReason>   TelegraphCancels = new List<EBossInterruptReason>();

            public int                          LaunchCount;
            public Vector3                      LaunchOrigin;
            public Vector3                      LaunchDirection;
            public float                        LaunchSpeed;
            public float                        LaunchWidth;
            public float                        LaunchPathLength;
            public double                       LaunchTime;
            public int                          EndCount;
            public EBossProjectileEnd           EndKind;
            public Vector3                      EndPosition;
            public int                          EndHitTargetId;
            public double                       EndAt;

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds)
            {
                ++StartCount;
                StartDirection = direction;
                StartLength    = length;
                StartWidth     = width;
                StartSeconds   = telegraphSeconds;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                AimLengths.Add(length);
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                ++LockCount;
                LockDirection = direction;
                LockLength    = length;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                TelegraphCancels.Add(reason);
            }

            public void OnProjectileLaunched(int castInstanceId, int projectileId, Vector3 origin, Vector3 direction, float speed, float width,
                float pathLength, double launchServerTime)
            {
                ++LaunchCount;
                LaunchOrigin     = origin;
                LaunchDirection  = direction;
                LaunchSpeed      = speed;
                LaunchWidth      = width;
                LaunchPathLength = pathLength;
                LaunchTime       = launchServerTime;
            }

            public void OnProjectileEnded(int castInstanceId, int projectileId, EBossProjectileEnd end, Vector3 position, int hitTargetId)
            {
                ++EndCount;
                EndKind        = end;
                EndPosition    = position;
                EndHitTargetId = hitTargetId;
                EndAt          = Now;
            }
        }

        // 패턴 한 개를 공격 FSM에 올리고 지속물 관리자를 BossCombat처럼 공격 진행 뒤에 부른다
        private sealed class Rig
        {
            public FakeCombatTarget  Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Vector3.zero, BodyRadius = 2f };
            public FakeCombatTarget  Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public ProjQuery         Query    { get; } = new ProjQuery();
            public ProjResolver      Resolver { get; } = new ProjResolver();
            public ProjListener      Listener { get; } = new ProjListener();
            public PlaneWall         Wall     { get; } = new PlaneWall();
            public BossAttackFsm     Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate    Gate     { get; }
            public BossProjectileManager Manager { get; }
            public BossProjectilePattern Pattern { get; }
            public PatternDefinition Definition { get; }
            public BossPatternContext Context { get; }

            // 정의를 주면 팩토리가 그 정의로 실행 객체를 만든다
            public Rig(bool guardable = true, PatternDefinition definition = null)
            {
                Query.Targets.Add(Tracked);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = definition ?? MakeP04(guardable);
                Manager    = new BossProjectileManager(Query, Wall, Listener);
                Pattern    = (definition == null)
                    ? new BossProjectilePattern(TEST_HREF, Manager, Listener)
                    : (BossProjectilePattern)new WolbaekPatternFactory(TEST_HREF, null, null, null, null, null, Manager, Listener).Create(definition);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget Add(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget target = new FakeCombatTarget
                {
                    CombatId = id, Faction = ECombatFaction.Player, Position = position, IsSummon = isSummon
                };
                Query.Targets.Add(target);
                return target;
            }

            public void Start(double serverTime = 0.0)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }

            public void TickAt(double time)
            {
                Resolver.Time = time;
                Listener.Now  = time;
                Fsm.Tick(time);
                Manager.Tick(time);
            }

            public void TickRange(double from, double to, float step = STEP)
            {
                int count = (int)Math.Round((to - from) / step);
                for (int i = 1; i <= count; ++i)
                {
                    TickAt(from + (i * (double)step));
                }
            }

            public double Launch      => LaunchOf(Definition, 0.0);
            public double RecoverEnd  => RecoverEndOf(Definition, 0.0);

            public int Hits(int id)
            {
                int count = 0;
                for (int i = 0; i < Resolver.Requests.Count; ++i)
                {
                    if (Resolver.Requests[i].HitTargetId == id)
                    {
                        ++count;
                    }
                }

                return count;
            }
        }

        /****************************************
        *        Aim, Rotation And Lock
        ****************************************/

        // 타깃이 90도에 있어도 처음 0.45초 동안 120도/초로만 돌아 최대 54도이고 그 뒤 방향은 고정이다
        [Test]
        public void Tick_RotatesAtMostTurnRateUntilTrackingEnd_ThenLocks()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            rig.TickAt(0.2);
            Assert.AreEqual(24f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOL);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.TickAt(0.44);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.TickAt(TRACKING);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(54f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOL);
            Assert.AreEqual(1, rig.Listener.LockCount);
        }

        [Test]
        public void Aim_AfterLock_TargetMovingNeverRetracksNorGuidesProjectile()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();
            rig.TickRange(0.0, 0.5);
            Vector3 locked = rig.Pattern.AimDirection;

            rig.Tracked.Position = new Vector3(-10f, 0f, 0f);
            rig.TickRange(0.5, rig.Launch + 0.5);

            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Pattern.AimDirection), ANGLE_TOL);
            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Listener.LaunchDirection), ANGLE_TOL);
            Assert.AreEqual(1, rig.Listener.LockCount);
        }

        [Test]
        public void Tick_CoarseTickAcrossTrackingEnd_RotatesNoMoreThanBudget()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            rig.TickAt(0.8);

            Assert.AreEqual(54f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOL);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
        }

        [Test]
        public void Tick_TargetDiesOrDisappearsAfterStart_KeepsLastValidDirection()
        {
            Rig dead = new Rig();
            dead.Tracked.Position = new Vector3(10f, 0f, 0f);
            dead.Start();
            dead.TickRange(0.0, 0.2);
            dead.Tracked.LifeState = EParticipantLifeState.Dead;
            dead.TickRange(0.2, 0.8);
            Assert.AreEqual(24f, AimDegrees(dead.Pattern.AimDirection), ANGLE_TOL);

            Rig gone = new Rig();
            gone.Tracked.Position = new Vector3(10f, 0f, 0f);
            gone.Start();
            gone.TickRange(0.0, 0.1);
            gone.Query.Remove(gone.Tracked.CombatId);
            gone.TickRange(0.1, 0.8);
            Assert.AreEqual(12f, AimDegrees(gone.Pattern.AimDirection), ANGLE_TOL);
        }

        /****************************************
        *         Telegraph Equals Flight
        ****************************************/

        [Test]
        public void Begin_ReportsDefinitionWidthSecondsAndPathLength()
        {
            Rig rig = new Rig();
            rig.Start();

            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(WIDTH, rig.Listener.StartWidth);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds);
            Assert.AreEqual(RANGE, rig.Listener.StartLength);
        }

        // 벽이 가까우면 예고도 비행도 같은 줄어든 경로 길이다. 폭의 절반이 반경이다
        [Test]
        public void Wall_PathLengthShownInTelegraphEqualsFlightLength()
        {
            Rig rig = new Rig();
            rig.Wall.WallZ = 20f;
            rig.Start();
            Assert.AreEqual(19.5f, rig.Listener.StartLength, TOLERANCE);

            rig.TickRange(0.0, rig.Launch + 0.1);

            Assert.AreEqual(19.5f, rig.Listener.LockLength, TOLERANCE);
            Assert.AreEqual(19.5f, rig.Listener.LaunchPathLength, TOLERANCE);
        }

        /****************************************
        *            One Shot, Speed, Range
        ****************************************/

        [Test]
        public void Launch_HappensOnceAtTelegraphEnd_WithServerFixedOriginDirectionSpeedAndWidth()
        {
            Rig rig = new Rig();
            rig.Self.Position = new Vector3(2f, 0f, 3f);
            rig.Tracked.Position = new Vector3(2f, 0f, 9f);
            rig.Start();

            rig.TickRange(0.0, rig.Launch - 0.02);
            Assert.AreEqual(0, rig.Listener.LaunchCount);

            rig.TickRange(rig.Launch - 0.02, rig.RecoverEnd + 0.3);

            Assert.AreEqual(1, rig.Listener.LaunchCount);
            Assert.AreEqual(rig.Self.Position, rig.Listener.LaunchOrigin);
            Assert.AreEqual(SPEED, rig.Listener.LaunchSpeed);
            Assert.AreEqual(WIDTH, rig.Listener.LaunchWidth);
            Assert.AreEqual(rig.Launch, rig.Listener.LaunchTime, EPSILON / 1000.0);
            Assert.IsTrue(rig.Pattern.HasFired);
        }

        // 맞는 대상이 5m 앞에 있으면 속도 10m/초라 발사 0.5초 뒤에 맞는다
        [Test]
        public void Flight_ConstantSpeed_HitTimeEqualsDistanceOverSpeed()
        {
            Rig rig = new Rig();
            rig.Add(11, new Vector3(0f, 0f, 5f));
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            rig.Start();

            rig.TickRange(0.0, rig.Launch + 1.0);

            Assert.AreEqual(1, rig.Hits(11));
            Assert.AreEqual(rig.Launch + 0.5, rig.Resolver.Times[0], 0.011);
        }

        [Test]
        public void Flight_NoCollision_ExpiresAtMaxRangeAfterThreePointFiveSeconds()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 0f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Start();

            rig.TickRange(0.0, rig.Launch + 3.45);
            Assert.AreEqual(1, rig.Manager.ActiveCount);
            Assert.AreEqual(0, rig.Listener.EndCount);

            rig.TickRange(rig.Launch + 3.45, rig.Launch + 3.6);

            Assert.AreEqual(0, rig.Manager.ActiveCount);
            Assert.AreEqual(1, rig.Listener.EndCount);
            Assert.AreEqual(EBossProjectileEnd.Range, rig.Listener.EndKind);
            Assert.AreEqual(RANGE, rig.Listener.EndPosition.z, TOLERANCE);
            Assert.AreEqual(rig.Launch + 3.5, rig.Listener.EndAt, 0.011);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        /****************************************
        *          Collision And Priority
        ****************************************/

        [Test]
        public void Hit_NearestTargetOnPathOnly_OthersUntouchedAndProjectileEnds()
        {
            Rig rig = new Rig();
            rig.Add(11, new Vector3(0f, 0f, 8f));
            rig.Add(12, new Vector3(0.2f, 0f, 5f));
            rig.Add(13, new Vector3(0f, 0f, 12f));
            rig.Tracked.Position = new Vector3(0f, 0f, 20f);
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.5);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Hits(12));
            Assert.AreEqual(EBossProjectileEnd.Hit, rig.Listener.EndKind);
            Assert.AreEqual(12, rig.Listener.EndHitTargetId);
            Assert.AreEqual(5f, rig.Listener.EndPosition.z, TOLERANCE);
            Assert.AreEqual(0, rig.Manager.ActiveCount);
        }

        [Test]
        public void Hit_SameDistance_LowerCombatIdWins()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            rig.Add(40, new Vector3(-0.3f, 0f, 6f));
            rig.Add(20, new Vector3(0.3f, 0f, 6f));
            rig.Add(30, new Vector3(0f, 0f, 6f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.5);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(20, rig.Resolver.Requests[0].HitTargetId);
        }

        [Test]
        public void Hit_WidthIsOneMeter_CenterLineWithinHalfWidthOnly()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            rig.Add(11, new Vector3(0.6f, 0f, 4f));   // 밖
            rig.Add(12, new Vector3(-0.49f, 0f, 9f)); // 안
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.5);

            Assert.AreEqual(0, rig.Hits(11));
            Assert.AreEqual(1, rig.Hits(12));
        }

        [Test]
        public void Hit_TargetsBehindOriginAreIgnored()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            rig.Add(11, new Vector3(0f, 0f, -2f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.5);

            Assert.AreEqual(0, rig.Hits(11));
        }

        [Test]
        public void Hit_SummonCollides_DeadAndRemovedTargetsDoNot()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            FakeCombatTarget dead = rig.Add(11, new Vector3(0f, 0f, 3f));
            dead.LifeState = EParticipantLifeState.Dead;
            rig.Add(12, new Vector3(0f, 0f, 4f));
            rig.Query.Remove(12);
            rig.Add(13, new Vector3(0f, 0f, 7f), isSummon: true);
            rig.Add(14, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.5);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(13, rig.Resolver.Requests[0].HitTargetId);
        }

        // 벽이 먼저면 피해 없이 소멸하고 벽 뒤 대상은 맞지 않는다
        [Test]
        public void Wall_BeforeTarget_EndsWithoutDamageAndNothingBehindWallIsHit()
        {
            Rig rig = new Rig();
            rig.Wall.WallZ = 10f;
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            rig.Add(11, new Vector3(0f, 0f, 12f));
            rig.Add(12, new Vector3(0f, 0f, 20f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.5);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(EBossProjectileEnd.Wall, rig.Listener.EndKind);
            Assert.AreEqual(9.5f, rig.Listener.EndPosition.z, TOLERANCE);
            Assert.AreEqual(0, rig.Manager.ActiveCount);
        }

        [Test]
        public void Wall_TargetBeforeWall_StillHitAndTieGoesToWall()
        {
            Rig before = new Rig();
            before.Wall.WallZ = 10f;
            before.Tracked.Position = new Vector3(0f, 0f, 30f);
            before.Add(11, new Vector3(0f, 0f, 9.3f));
            before.Start();
            before.TickRange(0.0, before.RecoverEnd + 0.5);
            Assert.AreEqual(1, before.Hits(11));
            Assert.AreEqual(EBossProjectileEnd.Hit, before.Listener.EndKind);

            // 벽 앞 거리(9.5m)와 같은 거리의 대상은 벽이 이긴다
            Rig tie = new Rig();
            tie.Wall.WallZ = 10f;
            tie.Tracked.Position = new Vector3(0f, 0f, 30f);
            tie.Add(11, new Vector3(0f, 0f, 9.5f));
            tie.Start();
            tie.TickRange(0.0, tie.RecoverEnd + 0.5);
            Assert.AreEqual(0, tie.Hits(11));
            Assert.AreEqual(EBossProjectileEnd.Wall, tie.Listener.EndKind);
        }

        [Test]
        public void Wall_AdjacentToBoss_EndsAtOnceWithoutDamage()
        {
            Rig rig = new Rig();
            rig.Wall.WallZ = 0.4f; // 폭의 절반이 반경이라 경로 길이 0
            rig.Tracked.Position = new Vector3(0f, 0f, 0f);
            rig.Add(11, new Vector3(0f, 0f, 0.0f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.2);

            Assert.AreEqual(0f, rig.Listener.LaunchPathLength, TOLERANCE);
            Assert.AreEqual(EBossProjectileEnd.Wall, rig.Listener.EndKind);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        // 방어, 반격, 무적, 중복 판정으로 막혀도 한 번 판정하고 사라지며 뒤의 대상은 맞지 않는다
        [TestCase(EDamageOutcome.Guarded)]
        [TestCase(EDamageOutcome.Invulnerable)]
        [TestCase(EDamageOutcome.DuplicateHit)]
        public void Hit_BlockedOrIgnoredResult_ProjectileStillEndsAndNothingBehindIsHit(EDamageOutcome outcome)
        {
            Rig rig = new Rig();
            rig.Resolver.Result = (outcome == EDamageOutcome.Guarded) ? DamageResult.Guarded() : DamageResult.Ignored(outcome);
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            rig.Add(11, new Vector3(0f, 0f, 4f));
            rig.Add(12, new Vector3(0f, 0f, 6f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 1.0);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(11, rig.Resolver.Requests[0].HitTargetId);
            Assert.AreEqual(EBossProjectileEnd.Hit, rig.Listener.EndKind);
            Assert.AreEqual(0, rig.Manager.ActiveCount);
        }

        [Test]
        public void Hit_RequestFields_UseDefinitionDamageGateAndOneActionId()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 6f);
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.5);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            DamageRequest request = rig.Resolver.Requests[0];
            Assert.AreEqual(DAMAGE_REF * TEST_HREF, request.RawDamage, 0.001f);
            Assert.IsFalse(request.IsUnblockable);
            Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
            Assert.AreNotEqual(request.CastInstanceId, request.ActionInstanceId);
            Assert.AreEqual(rig.Self.CombatId, request.AttackerId);
            Assert.AreEqual(P04_ID, request.SourceDefinitionId);
            Assert.AreEqual(Vector3.zero, request.SourcePosition);
        }

        [Test]
        public void Hit_ClosedGate_DoesNotRequestDamageButProjectileEnds()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 6f);
            rig.Start();
            rig.TickRange(0.0, rig.Launch);

            rig.Gate.CloseForGood();
            rig.TickRange(rig.Launch, rig.Launch + 1.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Manager.ActiveCount);
        }

        /****************************************
        *             Coarse Ticks
        ****************************************/

        // 한 번의 큰 틱이 예고와 실행과 회복과 비행을 모두 건너뛰어도 한 발만 나가고 같은 대상을 한 번 맞힌다
        [Test]
        public void LargeTick_SingleJump_OneShotAndSameFirstTargetAsFineTicks()
        {
            Rig fine   = new Rig();
            Rig coarse = new Rig();
            foreach (Rig rig in new[] { fine, coarse })
            {
                rig.Tracked.Position = new Vector3(0f, 0f, 30f);
                rig.Add(11, new Vector3(0.3f, 0f, 7f));
                rig.Add(12, new Vector3(0f, 0f, 15f));
                rig.Start();
            }

            fine.TickRange(0.0, fine.RecoverEnd + 2.0, 0.005f);
            coarse.TickAt(100.0);

            Assert.AreEqual(1, coarse.Listener.LaunchCount);
            Assert.AreEqual(1, coarse.Resolver.Requests.Count);
            Assert.AreEqual(fine.Resolver.Requests[0].HitTargetId, coarse.Resolver.Requests[0].HitTargetId);
            Assert.AreEqual(11, coarse.Resolver.Requests[0].HitTargetId);
            Assert.AreEqual(EBossProjectileEnd.Hit, coarse.Listener.EndKind);
            Assert.IsTrue(coarse.Pattern.IsFinished);
        }

        [Test]
        public void LargeTick_NeverPassesThroughWall_EvenWhenSingleTickCoversWholePath()
        {
            Rig rig = new Rig();
            rig.Wall.WallZ = 12f;
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            rig.Add(11, new Vector3(0f, 0f, 20f));
            rig.Start();

            rig.TickAt(100.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(EBossProjectileEnd.Wall, rig.Listener.EndKind);
            Assert.AreEqual(1, rig.Listener.LaunchCount);
        }

        [Test]
        public void LargeTick_RandomIntervals_MatchFineTicks()
        {
            for (int seed = 1; seed <= 25; ++seed)
            {
                System.Random random = new System.Random(seed);
                Rig fine   = new Rig();
                Rig coarse = new Rig();
                float wall = (seed % 3 == 0) ? (6f + ((float)random.NextDouble() * 20f)) : float.PositiveInfinity;
                foreach (Rig rig in new[] { fine, coarse })
                {
                    rig.Wall.WallZ = wall;
                    rig.Tracked.Position = new Vector3(0f, 0f, 36f);
                }

                for (int i = 0; i < 10; ++i)
                {
                    Vector3 position = new Vector3(((float)random.NextDouble() * 2f) - 1f, 0f, (float)random.NextDouble() * 34f);
                    fine.Add(100 + i, position);
                    coarse.Add(100 + i, position);
                }

                fine.Start();
                coarse.Start();
                fine.TickRange(0.0, fine.Launch + 4.0, 0.005f);

                double time = 0.0;
                while (coarse.Manager.ActiveCount > 0 || coarse.Listener.LaunchCount == 0)
                {
                    time += 0.005 + (random.NextDouble() * 0.9);
                    coarse.TickAt(time);
                }

                Assert.AreEqual(fine.Listener.EndKind, coarse.Listener.EndKind, $"seed {seed}");
                Assert.AreEqual(fine.Resolver.Requests.Count, coarse.Resolver.Requests.Count, $"seed {seed}");
                Assert.LessOrEqual(coarse.Resolver.Requests.Count, 1, $"seed {seed}");
                if (coarse.Resolver.Requests.Count == 1)
                {
                    Assert.AreEqual(fine.Resolver.Requests[0].HitTargetId, coarse.Resolver.Requests[0].HitTargetId, $"seed {seed}");
                }
            }
        }

        /****************************************
        *      Pattern End Keeps Projectile
        ****************************************/

        // 회복이 끝나 패턴이 끝나도 날고 있는 투사체는 남는다
        [Test]
        public void Finish_AfterRecovery_KeepsFlyingProjectile()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 0f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.3);

            Assert.IsTrue(rig.Pattern.IsFinished);
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
            Assert.AreEqual(1, rig.Manager.ActiveCount);

            rig.Add(11, new Vector3(0f, 0f, 30f));
            rig.TickRange(rig.RecoverEnd + 0.3, rig.Launch + 3.2);
            Assert.AreEqual(1, rig.Hits(11), "패턴이 끝난 뒤에도 투사체가 날아가 맞힌다");
        }

        [Test]
        public void ManagerCancel_DirectControlReason_NeverRemovesFiredProjectile()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 0f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Start();
            rig.TickRange(0.0, rig.Launch + 0.2);

            rig.Manager.Cancel(EBossInterruptReason.DirectControl, rig.Context.CastInstanceId);
            rig.Manager.Cancel(EBossInterruptReason.DirectControl, CombatIds.NONE);

            Assert.AreEqual(1, rig.Manager.ActiveCount);
            Assert.AreEqual(0, rig.Listener.EndCount);
        }

        [Test]
        public void Cancel_DuringTelegraph_NoProjectileAndTelegraphCancelReported()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.TickRange(0.0, 0.5);

            rig.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            rig.TickAt(3.0);

            Assert.AreEqual(0, rig.Listener.LaunchCount);
            Assert.AreEqual(1, rig.Listener.TelegraphCancels.Count);
            Assert.AreEqual(0, rig.Manager.ActiveCount);
        }

        // 발사 뒤의 패턴 취소는 투사체를 건드리지 않고 발사 전 취소 알림도 내지 않는다
        [Test]
        public void PatternCancel_AfterFire_DoesNotTouchProjectileNorReportTelegraphCancel()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 0f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Start();
            rig.TickRange(0.0, rig.Launch + 0.2);

            rig.Fsm.Cancel(EBossInterruptReason.DirectControl);

            Assert.AreEqual(1, rig.Manager.ActiveCount);
            Assert.AreEqual(0, rig.Listener.TelegraphCancels.Count);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        public void ManagerCancel_StopReasons_RemoveProjectileAtOnceAndLaterTicksDoNothing(EBossInterruptReason reason)
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, 0f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Start();
            rig.TickRange(0.0, rig.Launch + 0.2);
            Assert.AreEqual(1, rig.Manager.ActiveCount);

            rig.Manager.Cancel(reason, CombatIds.NONE);
            rig.Add(11, new Vector3(0f, 0f, 8f));
            rig.TickRange(rig.Launch + 0.2, rig.Launch + 3.0);

            Assert.AreEqual(0, rig.Manager.ActiveCount);
            Assert.AreEqual(EBossProjectileEnd.Cancelled, rig.Listener.EndKind);
            Assert.AreEqual(1, rig.Listener.EndCount);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        /****************************************
        *         Manager Contracts
        ****************************************/

        [Test]
        public void Manager_RejectsInvalidSpecsAndOverCapacity()
        {
            ProjQuery query = new ProjQuery();
            ProjResolver resolver = new ProjResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            BossProjectileManager manager = new BossProjectileManager(query);

            BossProjectileSpec noSpeed = new BossProjectileSpec(5, 6, 1, P04_ID, Vector3.zero, Vector3.forward, 0f, 35f, 1f, 1f, EDamageFlags.None, 0.0, gate);
            BossProjectileSpec noGate  = new BossProjectileSpec(5, 6, 1, P04_ID, Vector3.zero, Vector3.forward, 10f, 35f, 1f, 1f, EDamageFlags.None, 0.0, null);
            BossProjectileSpec noDir   = new BossProjectileSpec(5, 6, 1, P04_ID, Vector3.zero, Vector3.zero, 10f, 35f, 1f, 1f, EDamageFlags.None, 0.0, gate);
            Assert.IsFalse(manager.TryLaunch(in noSpeed));
            Assert.IsFalse(manager.TryLaunch(in noGate));
            Assert.IsFalse(manager.TryLaunch(in noDir));

            int accepted = 0;
            for (int i = 1; i <= 12; ++i)
            {
                BossProjectileSpec spec = new BossProjectileSpec(i, i, 1, P04_ID, Vector3.zero, Vector3.forward, 10f, 35f, 1f, 1f,
                    EDamageFlags.None, 0.0, gate);
                if (manager.TryLaunch(in spec))
                {
                    ++accepted;
                }
            }

            Assert.Greater(accepted, 0);
            Assert.Less(accepted, 12);
            Assert.AreEqual(accepted, manager.ActiveCount);
        }

        [Test]
        public void Manager_BeforeLaunchTime_DoesNotMove()
        {
            Rig rig = new Rig();
            rig.Add(11, new Vector3(0f, 0f, 0.2f));
            BossProjectileSpec spec = new BossProjectileSpec(5, 6, 1, P04_ID, Vector3.zero, Vector3.forward, 10f, 35f, 1f, 1f,
                EDamageFlags.None, 10.0, rig.Gate);

            Assert.IsTrue(rig.Manager.TryLaunch(in spec));
            rig.Manager.Tick(9.0);
            rig.Manager.Tick(10.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Manager.ActiveCount);
        }

        /****************************************
        *          Through Boss Brain
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget  Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Vector3.zero, BodyRadius = 2f };
            public FakeCombatTarget  Player   { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 30f) };
            public ProjQuery         Query    { get; } = new ProjQuery();
            public ProjResolver      Resolver { get; } = new ProjResolver();
            public ProjListener      Listener { get; } = new ProjListener();
            public PlaneWall         Wall     { get; } = new PlaneWall();
            public FakeBossVitals    Vitals   { get; } = new FakeBossVitals();
            public FakeControlSource Control  { get; } = new FakeControlSource();
            public PatternDefinition Definition { get; } = MakeP04();
            public BossProjectileManager Manager { get; }
            public BossBrain         Brain    { get; }
            public List<BossDirectControlApplied> Applied { get; } = new List<BossDirectControlApplied>();

            public BrainRig()
            {
                Query.Targets.Add(Player);
                Manager = new BossProjectileManager(Query, Wall, Listener);

                FakeBossCatalog catalog = new FakeBossCatalog { Boss = BossTestFixtures.MakeBoss() };
                catalog.Patterns.Add(Definition);

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = catalog,
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Resolver,
                    Patterns = new WolbaekPatternFactory(TEST_HREF, null, null, null, null, null, Manager, Listener),
                    Effects  = Manager,
                    Control  = Control,
                    Random   = new FakeBossRandom()
                });
                Brain.DirectControlApplied += applied => Applied.Add(applied);
            }

            public void TickAt(double time)
            {
                Resolver.Time = time;
                Listener.Now  = time;
                Brain.Tick(time);
            }

            // 발사 뒤 투사체가 날고 있는 상태까지 진행한다
            public void RunToFlight(double flightSeconds)
            {
                Assert.IsTrue(Brain.Begin(0.0));
                TickAt(0.0);
                Assert.AreEqual(EBossAttackState.Telegraph, Brain.AttackState);
                TickAt(0.5);
                TickAt(LaunchOf(Definition, 0.0));
                TickAt(LaunchOf(Definition, 0.0) + flightSeconds);
                Assert.AreEqual(1, Listener.LaunchCount);
            }
        }

        [Test]
        public void Brain_FullCycle_ProjectileFiredAndPatternFinishedWhileProjectileKeepsFlying()
        {
            BrainRig rig = new BrainRig();
            rig.RunToFlight(0.2);
            Assert.AreEqual(1, rig.Manager.ActiveCount);

            rig.TickAt(RecoverEndOf(rig.Definition, 0.0) - EPSILON);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.TickAt(RecoverEndOf(rig.Definition, 0.0));
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.AreEqual(1, rig.Manager.ActiveCount, "패턴이 끝나도 투사체는 남는다");
        }

        // 회복 중 직접 제어는 이미 발사한 투사체를 취소하지 않는다
        [Test]
        public void Brain_DirectControlDuringRecovery_AppliesButKeepsProjectile()
        {
            BrainRig rig = new BrainRig();
            rig.RunToFlight(0.1);
            Assert.AreEqual(1, rig.Manager.ActiveCount);

            rig.Control.Next    = new BossDirectControl("test_bind", 0.5f);
            rig.Control.Pending = true;
            rig.TickAt(LaunchOf(rig.Definition, 0.0) + 0.2);

            Assert.AreEqual(1, rig.Applied.Count);
            Assert.IsFalse(rig.Applied[0].CancelledPattern);
            Assert.AreEqual(1, rig.Manager.ActiveCount);
            Assert.AreEqual(0, rig.Listener.EndCount);
        }

        // 제어 허용 구간은 발사 뒤 회복뿐이다, 예고 중 제어는 버려진다
        [Test]
        public void Brain_DirectControlDuringTelegraph_IsDiscarded()
        {
            BrainRig rig = new BrainRig();
            Assert.IsTrue(rig.Brain.Begin(0.0));
            rig.TickAt(0.0);

            rig.Control.Pending = true;
            rig.TickAt(0.5);

            Assert.AreEqual(0, rig.Applied.Count);
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
        }

        // 회복이 끝나 다음 패턴이 시작돼도 기존 투사체는 남고, 다음 투사체도 따로 날아간다
        [Test]
        public void Brain_NextPatternStartsWhileFirstProjectileStillFlying_BothFly()
        {
            BrainRig rig = new BrainRig();
            rig.RunToFlight(0.0);
            Assert.AreEqual(1, rig.Manager.ActiveCount);
            double recoverEnd = RecoverEndOf(rig.Definition, 0.0);

            rig.TickAt(recoverEnd);
            rig.TickAt(recoverEnd + 0.25);
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(1, rig.Manager.ActiveCount);

            rig.TickAt(recoverEnd + 0.25 + TELEGRAPH);
            Assert.AreEqual(2, rig.Listener.LaunchCount);
            Assert.AreEqual(2, rig.Manager.ActiveCount);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Brain_DeathPhaseTransitionCombatEnd_RemoveProjectileAtOnceAndBlockDamage(int kind)
        {
            BrainRig rig = new BrainRig();
            rig.RunToFlight(0.1);
            Assert.AreEqual(1, rig.Manager.ActiveCount);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);

            double stopAt = LaunchOf(rig.Definition, 0.0) + 0.15;
            if (kind == 0)
            {
                rig.Vitals.IsHealthZero = true;
            }
            else if (kind == 1)
            {
                rig.Vitals.HealthRate = 0.25f;
            }
            else
            {
                rig.Brain.RequestCombatEnd();
            }

            // 곧바로 맞을 위치에 대상을 둔다. 정리가 먼저 되므로 맞지 않아야 한다
            rig.Query.Targets.Add(new FakeCombatTarget { CombatId = 11, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) });
            rig.TickAt(stopAt);
            rig.TickAt(stopAt + 0.5);
            rig.TickAt(stopAt + 5.0);

            Assert.AreEqual(0, rig.Manager.ActiveCount);
            Assert.AreEqual(1, rig.Listener.EndCount);
            Assert.AreEqual(EBossProjectileEnd.Cancelled, rig.Listener.EndKind);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        // 이미 적용된 피해는 되돌리지 않는다
        [Test]
        public void Brain_AppliedDamageStays_WhenCombatEndsAfterHit()
        {
            BrainRig rig = new BrainRig();
            rig.Player.Position = new Vector3(0f, 0f, 3f);
            rig.RunToFlight(0.5);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);

            rig.Brain.RequestCombatEnd();
            rig.TickAt(LaunchOf(rig.Definition, 0.0) + 0.6);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(EBossProjectileEnd.Hit, rig.Listener.EndKind);
        }

        /****************************************
        *            Factory And Data
        ****************************************/

        [Test]
        public void Factory_P04WithLauncher_CreatesFreshInstanceEachCall_WithoutLauncherReturnsNull()
        {
            BossProjectileManager manager = new BossProjectileManager(new ProjQuery());
            WolbaekPatternFactory with    = new WolbaekPatternFactory(TEST_HREF, null, null, null, null, null, manager);
            WolbaekPatternFactory without = new WolbaekPatternFactory(TEST_HREF);

            IBossPattern first  = with.Create(MakeP04());
            IBossPattern second = with.Create(MakeP04());

            Assert.IsInstanceOf<BossProjectilePattern>(first);
            Assert.AreNotSame(first, second);
            Assert.IsNull(without.Create(MakeP04()));
        }

        [Test]
        public void Constructor_NullLauncherOrTargets_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BossProjectilePattern(TEST_HREF, null));
            Assert.Throws<ArgumentNullException>(() => new BossProjectileManager(null));
        }

        // 서버가 읽는 에셋 값이 기획 표의 P04와 같고, 선택 가중치는 그대로다
        [Test]
        public void CatalogAsset_P04_HasPlannedExecutionValues_AndKeepsSelectionWeights()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern(P04_ID, out PatternDefinition p04));

            Assert.AreEqual(EEnemyAttackShape.Projectile, p04.Shape.Shape);
            Assert.AreEqual(0.9f, p04.TelegraphSeconds);
            Assert.AreEqual(0.45f, p04.TrackingSeconds);
            Assert.AreEqual(120f, p04.TurnRate);
            Assert.AreEqual(0f, p04.ActiveSeconds);
            Assert.AreEqual(0.5f, p04.RecoverySeconds);
            Assert.AreEqual(35f, p04.Shape.Range);
            Assert.AreEqual(1f, p04.Shape.Width);
            Assert.AreEqual(10f, p04.Shape.ProjectileSpeed);
            Assert.AreEqual(0.18f, p04.DamageRef);
            Assert.IsTrue(p04.Guardable);
            Assert.AreEqual(EBossActionState.Recover, p04.ControlPhase);
            Assert.AreEqual(0.5f, p04.ControlWindowSeconds);
            Assert.IsFalse(p04.ControlCancelsPattern);
            Assert.AreEqual(3.5f, p04.Shape.Range / p04.Shape.ProjectileSpeed, 0.0001f);

            // 선택 가중치는 입력해 둔 그대로다
            Assert.AreEqual(20f, p04.BaseWeight);
            Assert.AreEqual(0.5f, p04.DistanceMultipliers[0]);
            Assert.AreEqual(1.3f, p04.DirectionMultipliers[0]);
            Assert.AreEqual(0.6f, p04.NearbyMultipliers[2]);
        }

        [Test]
        public void CatalogAsset_IsTuned_AndHasNoPatternErrors()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            List<string> errors = new List<string>();

            catalog.ValidateTuning("boss_wolbaek", errors);

            string all = string.Join("\n", errors);
            Assert.IsTrue(catalog.IsTuned("boss_wolbaek"));
            Assert.AreEqual(0, errors.Count, all);
        }

        /****************************************
        *            Wolbaek Hard P04
        ****************************************/

        private static PatternDefinition LoadAssetPattern(string id)
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern(id, out PatternDefinition pattern), id);
            return pattern;
        }

        // 하드 P04도 같은 투사체 실행을 만든다. 발사 창구가 없으면 이지와 같이 만들지 않고 만들 수 없다고 답한다
        [Test]
        public void Factory_HardP04_CreatesProjectileLikeEasyP04_AndNeedsLauncher()
        {
            PatternDefinition hard = LoadAssetPattern(HARD_P04_ID);
            BossProjectileManager manager = new BossProjectileManager(new ProjQuery());
            WolbaekPatternFactory with    = new WolbaekPatternFactory(TEST_HREF, null, null, null, null, null, manager);
            WolbaekPatternFactory without = new WolbaekPatternFactory(TEST_HREF);

            Assert.IsTrue(with.CanCreate(hard));
            Assert.IsInstanceOf<BossProjectilePattern>(with.Create(hard));
            Assert.AreNotSame(with.Create(hard), with.Create(hard));

            Assert.IsFalse(without.CanCreate(hard));
            Assert.IsNull(without.Create(hard));
            Assert.IsTrue(with.CanCreate(LoadAssetPattern(P04_ID)));
        }

        // 예고와 발사가 보여 주는 폭은 정의의 폭이다. 이지 P04는 1m 그대로다
        [TestCase(HARD_P04_ID, 1.2f)]
        [TestCase(P04_ID, 1f)]
        public void Launch_AssetP04_ReportsItsOwnWidth(string id, float expectedWidth)
        {
            Rig rig = new Rig(definition: LoadAssetPattern(id));
            rig.Start();
            Assert.AreEqual(expectedWidth, rig.Listener.StartWidth);

            rig.TickRange(0.0, rig.RecoverEnd + 0.3);

            Assert.AreEqual(expectedWidth, rig.Listener.LaunchWidth);
            Assert.AreEqual(RANGE, rig.Listener.StartLength);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds);
            Assert.AreEqual(SPEED, rig.Listener.LaunchSpeed);
        }

        // 서버 피격 폭은 예고 폭과 같다. 중심선에서 0.55m는 하드(절반 0.6m)에서만 맞고, 0.65m는 둘 다 빗나간다
        [TestCase(HARD_P04_ID, 0.45f, true)]
        [TestCase(HARD_P04_ID, 0.55f, true)]
        [TestCase(HARD_P04_ID, 0.65f, false)]
        [TestCase(P04_ID, 0.45f, true)]
        [TestCase(P04_ID, 0.55f, false)]
        [TestCase(P04_ID, 0.65f, false)]
        public void Hit_AssetP04_HitsOnlyInsideItsOwnHalfWidth(string id, float lateral, bool expectedHit)
        {
            Rig rig = new Rig(definition: LoadAssetPattern(id));
            rig.Tracked.Position = new Vector3(0f, 0f, 30f);
            rig.Add(11, new Vector3(lateral, 0f, 9f));
            rig.Add(12, new Vector3(-lateral, 0f, 9f));
            rig.Start();

            rig.TickRange(0.0, rig.RecoverEnd + 0.5);

            // 두 대상 중 먼저 닿는 한 명만 맞고 투사체는 사라지므로 맞은 수의 합으로 본다
            Assert.AreEqual(expectedHit ? 1 : 0, rig.Resolver.Requests.Count, id);
        }

        // 벽에 줄어드는 경로 길이도 폭의 절반만큼이라 예고와 비행이 같은 값이다. 벽 20m 앞에서 하드 19.4m, 이지 19.5m
        [TestCase(HARD_P04_ID, 19.4f)]
        [TestCase(P04_ID, 19.5f)]
        public void Wall_AssetP04_PathLengthFollowsItsOwnWidth(string id, float expectedLength)
        {
            Rig rig = new Rig(definition: LoadAssetPattern(id));
            rig.Wall.WallZ = 20f;
            rig.Start();
            Assert.AreEqual(expectedLength, rig.Listener.StartLength, TOLERANCE);

            rig.TickRange(0.0, rig.Launch + 0.1);

            Assert.AreEqual(expectedLength, rig.Listener.LockLength, TOLERANCE);
            Assert.AreEqual(expectedLength, rig.Listener.LaunchPathLength, TOLERANCE);
        }

        // 폭 말고는 이지 P04와 같은 피해 요청을 보낸다. 정의 ID만 각자 정의를 따른다
        [Test]
        public void Hit_HardP04_SendsSameDamageAsEasyWithOwnDefinitionId()
        {
            Rig hardRig = new Rig(definition: LoadAssetPattern(HARD_P04_ID));
            Rig easyRig = new Rig(definition: LoadAssetPattern(P04_ID));
            foreach (Rig rig in new[] { hardRig, easyRig })
            {
                rig.Tracked.Position = new Vector3(0f, 0f, 30f);
                rig.Add(11, new Vector3(0f, 0f, 9f));
                rig.Start();
                rig.TickRange(0.0, rig.RecoverEnd + 0.5);
            }

            DamageRequest hardRequest = hardRig.Resolver.Requests.Find(r => r.HitTargetId == 11);
            DamageRequest easyRequest = easyRig.Resolver.Requests.Find(r => r.HitTargetId == 11);
            Assert.AreEqual(easyRequest.RawDamage, hardRequest.RawDamage);
            Assert.AreEqual(easyRequest.Flags, hardRequest.Flags);
            Assert.AreEqual(HARD_P04_ID, hardRequest.SourceDefinitionId);
            Assert.AreEqual(P04_ID, easyRequest.SourceDefinitionId);
        }
    }
}
