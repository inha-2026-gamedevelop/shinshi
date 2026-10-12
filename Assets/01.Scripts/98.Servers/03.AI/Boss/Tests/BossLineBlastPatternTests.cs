// System
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
    // 예고 1.6, 추적 0.6, 제어 구간 0.4는 float라 double로 넘기면 약간 크다. 경계 시각은 FSM과 같은 식(시작 + (double)길이)으로 구한다
    // 가중치와 배율은 이 테스트가 정한 가짜 값이며 기획 값이 아니다
    public class BossLineBlastPatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string P06_ID       = "wolbaek_p06";
        private const string HARD_P06_ID  = "wolbaek_hard_p06";
        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const float  TEST_HREF    = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  LENGTH       = 14f;
        private const float  WIDTH        = 4f;
        private const float  TELEGRAPH    = 1.6f;
        private const float  TRACKING     = 0.6f;
        private const float  TURN_RATE    = 90f;
        private const float  RECOVERY     = 0.5f;
        private const float  CONTROL_SEC  = 0.4f;
        private const float  ANGLE_TOLERANCE = 0.01f;
        private const double EPSILON      = 0.001;
        private const int    PROBE_ID     = 11;

        private static readonly Vector3 BossStart = new Vector3(2f, 0f, 3f);

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP06(float recovery = RECOVERY, bool guardable = false)
        {
            return new PatternDefinition(P06_ID, BossTestFixtures.BOSS_ID, 1, 8f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, TRACKING, TURN_RATE, 0f, recovery, EnemyCatalogSO.AttackShapeSpec.Line(LENGTH, WIDTH), 0.45f, guardable,
                EBossActionState.Telegraph, CONTROL_SEC, true);
        }

        // FSM이 계산하는 경계와 같은 식이다
        private static double BoundaryOf(PatternDefinition definition, double startTime)
        {
            return startTime + definition.TelegraphSeconds;
        }

        private static double TrackEndOf(PatternDefinition definition, double startTime)
        {
            return startTime + definition.TrackingSeconds;
        }

        private static double WindowStartOf(PatternDefinition definition, double startTime)
        {
            return BoundaryOf(definition, startTime) - definition.ControlWindowSeconds;
        }

        private static Vector3 Polar(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }

        // 조준 방향이 degrees일 때 보스 기준 전방 forward, 좌우 lateral에 있는 월드 위치
        private static Vector3 LocalToWorld(float aimDegrees, float lateral, float forward)
        {
            return BossStart + Polar(aimDegrees, forward) + Polar(aimDegrees + 90f, lateral);
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

        // 조회 계약(수평 거리, 경계 포함)대로 동작하고 마지막 호출 인자를 기록한다
        private sealed class LineTestQuery : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Players { get; } = new List<FakeCombatTarget>();

            public int            RadiusCalls;
            public Vector3        LastCenter;
            public float          LastRadius;
            public ECombatFaction LastFaction;
            public bool           LastIncludeSummons;

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Players.Count; ++i)
                {
                    if (Players[i].LifeState == EParticipantLifeState.Alive)
                    {
                        results.Add(Players[i]);
                    }
                }

                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                ++RadiusCalls;
                LastCenter         = center;
                LastRadius         = radius;
                LastFaction        = faction;
                LastIncludeSummons = includeSummons;

                results.Clear();
                for (int i = 0; i < Players.Count; ++i)
                {
                    FakeCombatTarget candidate = Players[i];
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
                for (int i = 0; i < Players.Count; ++i)
                {
                    if (Players[i].CombatId == combatId)
                    {
                        target = Players[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }
        }

        private sealed class LineListener : IBossLineTelegraphListener
        {
            public int                          StartCount;
            public int                          LockCount;
            public int                          HitEventCount;
            public int                          LastHitCount = -1;
            public int                          LastCastId   = -1;
            public Vector3                      StartOrigin;
            public Vector3                      StartDirection;
            public float                        StartLength;
            public float                        StartWidth;
            public float                        StartSeconds;
            public Vector3                      LockedDirection;
            public List<EBossInterruptReason>   Cancels    = new List<EBossInterruptReason>();
            public List<Vector3>                AimChanges = new List<Vector3>();
            public List<int>                    AimCastIds = new List<int>();
            public List<string>                 Order      = new List<string>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds)
            {
                Order.Add("start");
                ++StartCount;
                LastCastId     = castInstanceId;
                StartOrigin    = origin;
                StartDirection = direction;
                StartLength    = length;
                StartWidth     = width;
                StartSeconds   = telegraphSeconds;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
                Order.Add("aim");
                AimChanges.Add(direction);
                AimCastIds.Add(castInstanceId);
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
                Order.Add("lock");
                ++LockCount;
                LockedDirection = direction;
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                Order.Add("hit");
                ++HitEventCount;
                LastHitCount = hitCount;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Order.Add("cancel");
                Cancels.Add(reason);
            }
        }

        // 패턴 한 개를 공격 FSM에 직접 올린 최소 구성, 추적 대상은 보스 앞 3m에 선다
        private sealed class Rig
        {
            public FakeCombatTarget     Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            public FakeCombatTarget     Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = BossStart + new Vector3(0f, 0f, 3f) };
            public LineTestQuery        Query    { get; } = new LineTestQuery();
            public FakeDamageResolver   Resolver { get; } = new FakeDamageResolver();
            public LineListener         Listener { get; } = new LineListener();
            public BossAttackFsm        Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate       Gate     { get; }
            public BossLineBlastPattern Pattern  { get; }
            public PatternDefinition    Definition { get; }
            public BossPatternContext   Context  { get; }

            // createPattern을 주면 실행 객체를 그 방법(팩토리)으로 만든다
            public Rig(PatternDefinition definition = null, System.Func<LineListener, BossLineBlastPattern> createPattern = null)
            {
                Query.Players.Add(Tracked);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = definition ?? MakeP06();
                Pattern    = (createPattern == null) ? new BossLineBlastPattern(TEST_HREF, Listener) : createPattern(Listener);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Players.Add(player);
                return player;
            }

            // 보스가 이미 aimDegrees 방향을 보고 추적 대상도 그 방향에 있어 회전 없이 고정된다
            public void FaceAndTrack(float aimDegrees)
            {
                Self.Forward     = Polar(aimDegrees, 1f);
                Tracked.Position = BossStart + Polar(aimDegrees, 3f);
            }

            public void Start(double serverTime)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }
        }

        /****************************************
        *         Telegraph And Timing
        ****************************************/

        [Test]
        public void Tick_BeforeTelegraphEnds_AppliesNoDamage()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(0.0);
            rig.Fsm.Tick(0.5);
            rig.Fsm.Tick(1.0);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0) - EPSILON);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Fsm.State);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Tick_AtFsmComputedBoundary_AppliesExactlyOneHit()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            double boundary = BoundaryOf(rig.Definition, 0.0);

            rig.Fsm.Tick(boundary - EPSILON);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);

            rig.Fsm.Tick(boundary);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
            Assert.AreEqual(EBossAttackState.Recover, rig.Fsm.State);
        }

        // float 1.6f는 double 1.6보다 크므로 1.6 그대로의 시각에는 아직 예고 중이다
        [Test]
        public void Tick_AtDoubleOnePointSix_StillTelegraphing()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            Assert.Greater(BoundaryOf(rig.Definition, 0.0), 1.6);
            rig.Fsm.Tick(1.6);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Fsm.State);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        [Test]
        public void Tick_NonZeroStartTime_UsesStartPlusTelegraph()
        {
            Rig rig = new Rig();
            rig.Start(10.25);
            double boundary = BoundaryOf(rig.Definition, 10.25);

            rig.Fsm.Tick(boundary - EPSILON);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);

            rig.Fsm.Tick(boundary);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
        }

        [Test]
        public void Tick_AtBoundary_BuildsRawDamageAndUnblockableRequest()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            DamageRequest request = rig.Resolver.Requests[0];
            Assert.AreEqual(rig.Self.CombatId, request.AttackerId);
            Assert.AreEqual(rig.Tracked.CombatId, request.HitTargetId);
            Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
            Assert.AreEqual(P06_ID, request.SourceDefinitionId);
            Assert.AreEqual(0.45f * TEST_HREF, request.RawDamage, 0.0001f);
            Assert.AreEqual(EDamageFlags.Unblockable, request.Flags);
            Assert.AreEqual(BossStart, request.SourcePosition);
            Assert.IsTrue(request.HasValidIds);
            Assert.AreEqual(1, rig.Listener.LastHitCount);
        }

        [Test]
        public void Tick_RawDamageUsesInjectedHref_NotAConstant()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Rig other = new Rig();
            BossLineBlastPattern doubled = new BossLineBlastPattern(TEST_HREF * 2f);
            BossPatternContext context = other.Context;
            Assert.IsTrue(other.Fsm.Start(other.Definition, doubled, in context, 0.0));
            other.Fsm.Tick(BoundaryOf(other.Definition, 0.0));

            Assert.AreEqual(rig.Resolver.Requests[0].RawDamage * 2f, other.Resolver.Requests[0].RawDamage, 0.0001f);
        }

        /****************************************
        *        Tracking And Direction Lock
        ****************************************/

        [Test]
        public void Tick_DuringTracking_RotatesAtMost90DegreesPerSecond()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f); // 정면 대비 90도
            rig.Start(0.0);
            Vector3 initial = rig.Self.Forward;

            rig.Fsm.Tick(0.25);

            Assert.AreEqual(22.5f, Vector3.Angle(initial, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.Fsm.Tick(0.5);

            Assert.AreEqual(45f, Vector3.Angle(initial, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
        }

        // 0.6초 x 90도/초 = 54도에서 멈춘다, 타깃은 더 멀리 있어도 넘지 않는다
        [Test]
        public void Tick_AtTrackEnd_LocksAfterTotalTurnOf54Degrees()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(TrackEndOf(rig.Definition, 0.0) - EPSILON);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.Fsm.Tick(TrackEndOf(rig.Definition, 0.0));

            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(54f, Vector3.Angle(rig.Self.Forward, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.LockCount);
            Assert.AreEqual(rig.Pattern.AimDirection, rig.Listener.LockedDirection);
        }

        [Test]
        public void Tick_TargetCloserThanTurnLimit_FacesTargetWithoutOvershoot()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + Polar(20f, 3f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);

            Assert.AreEqual(0f, Vector3.Angle(Polar(20f, 3f), rig.Pattern.AimDirection), ANGLE_TOLERANCE);
        }

        [Test]
        public void Tick_AfterLock_TargetMovesAndStrikeUsesLockedDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(TrackEndOf(rig.Definition, 0.0));
            Vector3 locked = rig.Pattern.AimDirection;

            // 고정된 방향(보스 앞 54도) 위에 선 구경꾼은 맞고, 타깃이 옮겨 간 쪽은 맞지 않는다
            FakeCombatTarget onLocked = rig.AddPlayer(PROBE_ID, BossStart + (locked * 8f));
            rig.Tracked.Position = BossStart + new Vector3(-3f, 0f, 0f);
            rig.Fsm.Tick(1.0);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(locked, rig.Pattern.AimDirection);
            Assert.AreEqual(1, rig.Listener.LockCount);
            Assert.AreEqual(1, CountHits(rig.Resolver, onLocked.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, rig.Tracked.CombatId));
        }

        [Test]
        public void Tick_TargetDiesAfterLock_StillStrikesFixedDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(TrackEndOf(rig.Definition, 0.0));
            Vector3 locked = rig.Pattern.AimDirection;
            FakeCombatTarget onLocked = rig.AddPlayer(PROBE_ID, BossStart + (locked * 8f));
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, CountHits(rig.Resolver, onLocked.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(locked, rig.Listener.LockedDirection);
        }

        [Test]
        public void Tick_TargetDiesDuringTracking_KeepsCurrentDirectionAndStillStrikes()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);
            Vector3 before = rig.Pattern.AimDirection;
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Fsm.Tick(0.5);

            Assert.AreEqual(before, rig.Pattern.AimDirection);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(before, rig.Pattern.AimDirection);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
            Assert.AreEqual(EBossAttackState.Recover, rig.Fsm.State);
        }

        [Test]
        public void Tick_TargetRemovedDuringTracking_KeepsCurrentDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);
            Vector3 before = rig.Pattern.AimDirection;
            rig.Query.Players.Remove(rig.Tracked);
            rig.Fsm.Tick(0.5);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(before, rig.Pattern.AimDirection);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
        }

        [Test]
        public void Tick_SingleLargeGap_DoesNotExceedTotalTurnLimit()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + Polar(170f, 3f);
            rig.Start(0.0);

            rig.Fsm.Tick(1.0);

            Assert.AreEqual(54f, Vector3.Angle(rig.Self.Forward, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(0, CountHits(rig.Resolver, rig.Tracked.CombatId));
        }

        [Test]
        public void Tick_OneHugeGap_NotifiesAimBeforeLockAndHit()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(100.0);

            CollectionAssert.AreEqual(new[] { "start", "aim", "lock", "hit" }, rig.Listener.Order);
            Assert.AreEqual(54f, Vector3.Angle(rig.Self.Forward, rig.Listener.AimChanges[0]), ANGLE_TOLERANCE);
            Assert.AreEqual(rig.Listener.AimChanges[0], rig.Listener.LockedDirection);
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
        }

        // 틱이 추적 종료와 예고 종료 사이에 한 번만 오는 경우도 방향 갱신, 고정, 타격 순서를 지킨다
        [Test]
        public void Tick_FirstTickAfterTrackEndBeforeBoundary_LocksBeforeStrike()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(1.0);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            CollectionAssert.AreEqual(new[] { "start", "aim", "lock", "hit" }, rig.Listener.Order);
        }

        /****************************************
        *          Aim Change Notification
        ****************************************/

        [Test]
        public void Begin_NotifiesOriginDirectionLengthWidthAndSeconds()
        {
            Rig rig = new Rig();

            rig.Start(0.0);

            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(BossStart, rig.Listener.StartOrigin);
            Assert.AreEqual(Vector3.forward, rig.Listener.StartDirection);
            Assert.AreEqual(LENGTH, rig.Listener.StartLength);
            Assert.AreEqual(WIDTH, rig.Listener.StartWidth);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds);
            Assert.AreEqual(rig.Context.CastInstanceId, rig.Listener.LastCastId);
        }

        [Test]
        public void Tick_AimChangesDuringTracking_NotifiesCastIdAndDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);

            Assert.AreEqual(1, rig.Listener.AimChanges.Count);
            Assert.AreEqual(rig.Context.CastInstanceId, rig.Listener.AimCastIds[0]);
            Assert.AreEqual(rig.Pattern.AimDirection, rig.Listener.AimChanges[0]);
            Assert.AreEqual(22.5f, Vector3.Angle(rig.Self.Forward, rig.Listener.AimChanges[0]), ANGLE_TOLERANCE);
            Assert.AreEqual(0f, rig.Listener.AimChanges[0].y);
        }

        [Test]
        public void Tick_AimUnchanged_DoesNotNotify()
        {
            Rig rig = new Rig(); // 추적 대상이 정면이라 방향이 그대로다
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);
            rig.Fsm.Tick(TrackEndOf(rig.Definition, 0.0));

            Assert.AreEqual(0, rig.Listener.AimChanges.Count);
            Assert.AreEqual(1, rig.Listener.LockCount);
        }

        [Test]
        public void Tick_AfterLock_NoMoreAimNotifications()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);
            rig.Fsm.Tick(TrackEndOf(rig.Definition, 0.0));
            int before = rig.Listener.AimChanges.Count;

            rig.Tracked.Position = BossStart + new Vector3(-3f, 0f, 0f);
            rig.Fsm.Tick(1.0);
            rig.Fsm.Tick(1.5);

            Assert.AreEqual(before, rig.Listener.AimChanges.Count);
            Assert.AreEqual(1, rig.Listener.LockCount);
        }

        [Test]
        public void Tick_AfterCancel_NoAimNotifications()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);
            rig.Fsm.Tick(0.25);
            int before = rig.Listener.AimChanges.Count;

            rig.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            rig.Pattern.Tick(EBossAttackState.Telegraph, 0.5);
            rig.Fsm.Tick(0.5);

            Assert.AreEqual(before, rig.Listener.AimChanges.Count);
            Assert.AreEqual(0, rig.Listener.LockCount);
        }

        [Test]
        public void Tick_AfterFinish_NoAimNotifications()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(3f, 0f, 0f);
            rig.Start(0.0);
            rig.Fsm.Tick(100.0);
            int before = rig.Listener.AimChanges.Count;

            rig.Tracked.Position = BossStart + new Vector3(-3f, 0f, 0f);
            rig.Pattern.Tick(EBossAttackState.Telegraph, 101.0);

            Assert.IsTrue(rig.Pattern.IsFinished);
            Assert.AreEqual(before, rig.Listener.AimChanges.Count);
        }

        /****************************************
        *          Rectangle Boundaries
        ****************************************/

        // 보스 앞을 +z로 본 로컬 좌표: (좌우, 전방, 맞아야 하는가). 길이 14, 전체 폭 4, 경계는 포함, 뒤쪽은 제외
        private static readonly (float lateral, float forward, bool hit)[] RectangleCases =
        {
            (0f,     0f,     true),   // 시작점
            (0f,     7f,     true),   // 중앙
            (0f,     14f,    true),   // 앞 경계
            (0f,     14.05f, false),
            (2f,     7f,     true),   // 우측 경계
            (-2f,    7f,     true),   // 좌측 경계
            (2.05f,  7f,     false),
            (-2.05f, 7f,     false),
            (2f,     14f,    true),   // 먼 모서리
            (-2f,    14f,    true),
            (2.05f,  14f,    false),
            (2f,     14.05f, false),
            (2f,     0f,     true),   // 가까운 모서리
            (-2f,    0f,     true),
            (2f,     -0.05f, false),
            (0f,     -0.05f, false),  // 뒤쪽 경계 바로 밖
            (0f,     -5f,    false),
            (1f,     -14f,   false),
            (1.99f,  13.99f, true),   // 모서리 안쪽
            (6f,     7f,     false),
            (3f,     15f,    false)   // 원 조회 반경(약 14.14) 안이어도 직사각형 밖
        };

        [Test]
        public void Strike_ProbesAroundRectangle_HitOnlyInsideWithInclusiveEdges()
        {
            for (int i = 0; i < RectangleCases.Length; ++i)
            {
                (float lateral, float forward, bool hit) = RectangleCases[i];

                Rig rig = new Rig();
                rig.Tracked.Position = BossStart + new Vector3(0f, 0f, 3f); // 정면, 회전 없음
                rig.AddPlayer(PROBE_ID, LocalToWorld(0f, lateral, forward));
                rig.Start(0.0);

                rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

                Assert.AreEqual(hit ? 1 : 0, CountHits(rig.Resolver, PROBE_ID), $"lateral={lateral}, forward={forward}");
            }
        }

        [Test]
        public void Strike_RotatedAim_JudgesInAimLocalSpace()
        {
            float[] angles = { 30f, 45f, 90f, 135f, 180f, 200f, 315f };

            for (int a = 0; a < angles.Length; ++a)
            {
                for (int i = 0; i < RectangleCases.Length; ++i)
                {
                    (float lateral, float forward, bool hit) = RectangleCases[i];

                    Rig rig = new Rig();
                    rig.FaceAndTrack(angles[a]);
                    rig.AddPlayer(PROBE_ID, LocalToWorld(angles[a], lateral, forward));
                    rig.Start(0.0);

                    rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

                    Assert.Less((Polar(angles[a], 1f) - rig.Pattern.AimDirection).magnitude, 0.0001f);
                    Assert.AreEqual(hit ? 1 : 0, CountHits(rig.Resolver, PROBE_ID), $"aim={angles[a]}, lateral={lateral}, forward={forward}");
                }
            }
        }

        // 보스가 +x를 볼 때 원래 정면(+z) 쪽 점은 직사각형 밖이다
        [Test]
        public void Strike_RotatedAim_PointOnOldForwardAxisIsOutside()
        {
            Rig rig = new Rig();
            rig.FaceAndTrack(90f);
            rig.AddPlayer(PROBE_ID, BossStart + new Vector3(0f, 0f, 7f));
            rig.AddPlayer(PROBE_ID + 1, BossStart + new Vector3(7f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(0, CountHits(rig.Resolver, PROBE_ID));
            Assert.AreEqual(1, CountHits(rig.Resolver, PROBE_ID + 1));
        }

        // 높이 차이는 판정에 넣지 않는다
        [Test]
        public void Strike_HeightDifference_IsIgnored()
        {
            Rig rig = new Rig();
            FakeCombatTarget high = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(0f, 10f, 7f));
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, CountHits(rig.Resolver, high.CombatId));
        }

        /****************************************
        *              Query Radius
        ****************************************/

        [Test]
        public void QueryRadius_CoversFarCornersOfRectangle()
        {
            float cornerDistance = Mathf.Sqrt((LENGTH * LENGTH) + ((WIDTH * 0.5f) * (WIDTH * 0.5f)));
            float radius = BossLineBlastPattern.QueryRadius(LENGTH, WIDTH);

            Assert.GreaterOrEqual(radius, cornerDistance);
            Assert.Greater(radius, LENGTH);
            Assert.Less(radius, cornerDistance + 0.01f);
        }

        [Test]
        public void Strike_QueriesSavedOriginWithCornerRadiusPlayerFactionAndSummons()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, rig.Query.RadiusCalls);
            Assert.AreEqual(BossStart, rig.Query.LastCenter);
            Assert.AreEqual(BossLineBlastPattern.QueryRadius(LENGTH, WIDTH), rig.Query.LastRadius);
            Assert.AreEqual(ECombatFaction.Player, rig.Query.LastFaction);
            Assert.IsTrue(rig.Query.LastIncludeSummons);
        }

        // 엄격한 거리 계약(제곱 거리 <= 반경 제곱)에서도 먼 모서리가 후보에 든다
        [Test]
        public void Strike_FarCornerIsCandidateUnderStrictDistanceContract()
        {
            Rig rig = new Rig();
            FakeCombatTarget corner = rig.AddPlayer(PROBE_ID, LocalToWorld(0f, 2f, 14f));
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, CountHits(rig.Resolver, corner.CombatId));
        }

        /****************************************
        *            Strike Origin
        ****************************************/

        [Test]
        public void Strike_BossMovedDuringTelegraph_UsesBossPositionAtStrike()
        {
            Rig rig = new Rig();
            Vector3 moved = new Vector3(30f, 0f, 30f);
            FakeCombatTarget nearOld = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(0f, 0f, 7f));
            FakeCombatTarget nearNew = rig.AddPlayer(PROBE_ID + 1, moved + new Vector3(0f, 0f, 7f));
            rig.Tracked.Position = moved + new Vector3(0f, 0f, 3f);
            rig.Start(0.0);

            rig.Self.Position = moved;
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(moved, rig.Query.LastCenter);
            Assert.AreEqual(moved, rig.Resolver.Requests[0].SourcePosition);
            Assert.AreEqual(0, CountHits(rig.Resolver, nearOld.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, nearNew.CombatId));
        }

        /****************************************
        *            One Hit Per Target
        ****************************************/

        [Test]
        public void Strike_DeadTargetExcluded_SummonIncluded()
        {
            Rig rig = new Rig();
            FakeCombatTarget dead = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(0f, 0f, 5f));
            dead.LifeState = EParticipantLifeState.Dead;
            FakeCombatTarget summon = rig.AddPlayer(PROBE_ID + 1, BossStart + new Vector3(1f, 0f, 6f));
            summon.IsSummon = true;
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(0, CountHits(rig.Resolver, dead.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, summon.CombatId));
        }

        [Test]
        public void Tick_ManyTicksAcrossWholeCast_AppliesOnceAndSharesActionId()
        {
            Rig rig = new Rig();
            rig.AddPlayer(PROBE_ID, BossStart + new Vector3(1f, 0f, 9f));
            rig.Start(0.0);
            double boundary = BoundaryOf(rig.Definition, 0.0);

            double[] times = { 0.0, 0.5, 1.0, boundary, boundary, boundary + 0.25, boundary + 0.25, boundary + 0.5, boundary + 1.0, boundary + 2.0 };
            for (int i = 0; i < times.Length; ++i)
            {
                rig.Fsm.Tick(times[i]);
            }

            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, PROBE_ID));
            Assert.AreEqual(rig.Resolver.Requests[0].ActionInstanceId, rig.Resolver.Requests[1].ActionInstanceId);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
        }

        [Test]
        public void EnterState_ExecuteCalledAgain_DoesNotApplyTwice()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            rig.Pattern.EnterState(EBossAttackState.Execute, 5.0);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
        }

        [Test]
        public void Strike_NobodyInside_NotifiesHitWithZero()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(50f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
            Assert.AreEqual(0, rig.Listener.LastHitCount);
        }

        [Test]
        public void Pattern_WithoutListener_StillStrikes()
        {
            Rig rig = new Rig();
            BossLineBlastPattern silent = new BossLineBlastPattern(TEST_HREF);
            BossPatternContext context = rig.Context;

            Assert.IsTrue(rig.Fsm.Start(rig.Definition, silent, in context, 0.0));
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
        }

        /****************************************
        *               Cancel
        ****************************************/

        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.DirectControl)]
        public void Cancel_DuringTelegraph_BlocksLaterStrike(EBossInterruptReason reason)
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(0.5);

            rig.Fsm.Cancel(reason);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));
            rig.Fsm.Tick(5.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(reason, rig.Listener.Cancels[0]);
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
            CollectionAssert.AreEqual(new[] { "start", "cancel" }, rig.Listener.Order);
        }

        [Test]
        public void Cancel_ThenStaleExecuteReservation_DoesNotReachResolverOrNotify()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(0.75);
            rig.Fsm.Cancel(EBossInterruptReason.CombatEnd);

            rig.Pattern.EnterState(EBossAttackState.Execute, 1.6);
            rig.Pattern.Tick(EBossAttackState.Execute, 1.6);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Strike_GateClosedBeforeHit_AppliesNoDamage()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(0.5);

            rig.Gate.CloseForGood();
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        [Test]
        public void Cancel_AfterStrike_KeepsAppliedDamageAndSendsNothingMore()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.Fsm.Tick(5.0);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
        }

        /****************************************
        *       Control Window (FSM Level)
        ****************************************/

        [Test]
        public void IsControlWindowOpen_OnlyLastPartOfTelegraph()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            double windowStart = WindowStartOf(rig.Definition, 0.0);

            Assert.IsFalse(rig.Fsm.IsControlWindowOpen(0.0));
            Assert.IsFalse(rig.Fsm.IsControlWindowOpen(1.0));
            Assert.IsFalse(rig.Fsm.IsControlWindowOpen(windowStart - EPSILON));
            Assert.IsTrue(rig.Fsm.IsControlWindowOpen(windowStart));
            Assert.IsTrue(rig.Fsm.IsControlWindowOpen(windowStart + 0.1));
            Assert.IsTrue(rig.Fsm.IsControlWindowOpen(BoundaryOf(rig.Definition, 0.0) - EPSILON));
        }

        [Test]
        public void IsControlWindowOpen_NeverOpensAfterTelegraph()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            double boundary = BoundaryOf(rig.Definition, 0.0);

            rig.Fsm.Tick(boundary);

            Assert.AreEqual(EBossAttackState.Recover, rig.Fsm.State);
            Assert.IsFalse(rig.Fsm.IsControlWindowOpen(boundary + 0.1));
            Assert.IsFalse(rig.Fsm.IsControlWindowOpen(boundary + RECOVERY - EPSILON));
        }

        /****************************************
        *         Brain Integration
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget    Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            public FakeCombatTarget    Player   { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = BossStart + new Vector3(0f, 0f, 3f) };
            public LineTestQuery       Query    { get; } = new LineTestQuery();
            public FakeDamageResolver  Resolver { get; } = new FakeDamageResolver();
            public FakeBossVitals      Vitals   { get; } = new FakeBossVitals();
            public FakeControlSource   Control  { get; } = new FakeControlSource();
            public FakeEffectLifetime  Effects  { get; } = new FakeEffectLifetime();
            public LineListener        Listener { get; } = new LineListener();
            public PatternDefinition   Definition { get; } = MakeP06();
            public List<BossLineBlastPattern>      Created  { get; } = new List<BossLineBlastPattern>();
            public List<BossDirectControlApplied>  Applied  { get; } = new List<BossDirectControlApplied>();
            public BossBrain           Brain    { get; }

            private sealed class RecordingFactory : IBossPatternFactory
            {
                private readonly WolbaekPatternFactory _inner;
                private readonly List<BossLineBlastPattern> _created;

                public RecordingFactory(LineListener listener, List<BossLineBlastPattern> created)
                {
                    _inner   = new WolbaekPatternFactory(TEST_HREF, null, null, listener);
                    _created = created;
                }

                public IBossPattern Create(PatternDefinition definition)
                {
                    IBossPattern pattern = _inner.Create(definition);
                    if (pattern is BossLineBlastPattern line)
                    {
                        _created.Add(line);
                    }

                    return pattern;
                }
            }

            public BrainRig()
            {
                Query.Players.Add(Player);
                FakeBossCatalog catalog = new FakeBossCatalog { Boss = BossTestFixtures.MakeBoss() };
                catalog.Patterns.Add(Definition);

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = catalog,
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Resolver,
                    Patterns = new RecordingFactory(Listener, Created),
                    Effects  = Effects,
                    Control  = Control,
                    Random   = new FakeBossRandom()
                });
                Brain.DirectControlApplied += applied => Applied.Add(applied);
            }

            // 예고를 시작한 상태까지 진행한다
            public void BeginCast(double serverTime)
            {
                Assert.IsTrue(Brain.Begin(serverTime));
                Brain.Tick(serverTime);
                Assert.AreEqual(EBossAttackState.Telegraph, Brain.AttackState);
            }

            public void SendControl(double serverTime, float requestedSeconds = 0.5f)
            {
                Control.Next    = new BossDirectControl("test_bind", requestedSeconds);
                Control.Pending = true;
                Brain.Tick(serverTime);
            }
        }

        [Test]
        public void Brain_FullCycle_ReturnsToReadyAfterRecoveryAndSelectsAgain()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);
            Assert.AreEqual(P06_ID, rig.Brain.ActivePatternId);
            double boundary = BoundaryOf(rig.Definition, 0.0);

            rig.Brain.Tick(boundary - EPSILON);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);

            rig.Brain.Tick(boundary);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.Brain.Tick(boundary + RECOVERY - EPSILON);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.Brain.Tick(boundary + RECOVERY);
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.IsTrue(rig.Created[0].IsFinished);
            Assert.IsFalse(rig.Created[0].IsCancelled);

            rig.Brain.Tick(boundary + RECOVERY + 0.25);
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(2, rig.Created.Count);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(2, rig.Listener.StartCount);
        }

        [Test]
        public void Brain_PhaseTransitionDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);

            rig.Vitals.HealthRate = 0.25f;
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(BoundaryOf(rig.Definition, 0.0));
            rig.Brain.Tick(3.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Created[0].IsCancelled);
            Assert.AreEqual(EBossInterruptReason.PhaseTransition, rig.Created[0].CancelReason);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Brain_CombatEndDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);

            rig.Brain.RequestCombatEnd();
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(BoundaryOf(rig.Definition, 0.0));
            rig.Brain.Tick(3.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Created[0].IsCancelled);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, rig.Created[0].CancelReason);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Brain_BossDeathDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);

            rig.Vitals.IsHealthZero = true;
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Created[0].IsCancelled);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        /****************************************
        *            Direct Control
        ****************************************/

        // 구간 시작 직전의 제어는 버려지고 효과가 없으며, 같은 시전의 시작 경계 제어는 받아들여진다
        [Test]
        public void Brain_ControlJustBeforeWindow_IsDiscardedThenWindowStartCancels()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);
            double windowStart = WindowStartOf(rig.Definition, 0.0);

            rig.SendControl(windowStart - EPSILON);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.IsFalse(rig.Created[0].IsCancelled);
            Assert.AreEqual(0, rig.Applied.Count);
            Assert.AreEqual(0, rig.Effects.Cancels.Count);

            rig.SendControl(windowStart);

            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.IsTrue(rig.Created[0].IsCancelled);
            Assert.AreEqual(1, rig.Applied.Count);
        }

        [Test]
        public void Brain_ControlEarlyInTelegraph_DoesNotCancel()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);

            rig.SendControl(0.25);
            rig.SendControl(1.0);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(0, rig.Applied.Count);
        }

        [Test]
        public void Brain_ControlInsideWindow_CancelsAndBlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);
            double boundary = BoundaryOf(rig.Definition, 0.0);

            rig.SendControl(boundary - 0.1);

            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.IsTrue(rig.Created[0].IsCancelled);
            Assert.AreEqual(EBossInterruptReason.DirectControl, rig.Created[0].CancelReason);
            CollectionAssert.AreEqual(new[] { EBossInterruptReason.DirectControl }, rig.Listener.Cancels);
            CollectionAssert.AreEqual(new[] { "start", "cancel" }, rig.Listener.Order);

            // 같은 시전의 늦은 타격 예약이 도착해도 나가지 않는다
            rig.Created[0].EnterState(EBossAttackState.Execute, boundary);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Brain_ControlAtWindowStart_CancelsThisAttackOnly()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);
            double windowStart = WindowStartOf(rig.Definition, 0.0);
            int castId = rig.Listener.LastCastId;

            rig.SendControl(windowStart, 5f);

            Assert.AreEqual(1, rig.Applied.Count);
            Assert.IsTrue(rig.Applied[0].CancelledPattern);
            Assert.AreEqual(P06_ID, rig.Applied[0].PatternId);
            Assert.AreEqual(1f, rig.Applied[0].AppliedSeconds, 0.0001f); // 보스 정의의 최대 1초로 잘린다
            Assert.AreEqual(1, rig.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.DirectControl, rig.Effects.Cancels[0].Key);
            Assert.AreEqual(castId, rig.Effects.Cancels[0].Value);
        }

        [Test]
        public void Brain_AfterControlCancel_NoStrikeFromThatCast()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);
            double windowStart = WindowStartOf(rig.Definition, 0.0);
            double boundary = BoundaryOf(rig.Definition, 0.0);

            rig.SendControl(windowStart);
            rig.Brain.Tick(boundary - EPSILON);

            // 취소 뒤 다음 선택이 시작돼도 첫 시전은 타격하지 않는다. 두 번째 시전은 아직 예고 중이다
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
            Assert.IsTrue(rig.Created[0].IsCancelled);
        }

        // 면역 2초 동안 온 제어는 버려져 다음 시전은 취소되지 않고 타격한다
        [Test]
        public void Brain_ControlDuringImmunity_IsIgnoredAndNextCastStrikes()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);
            double windowStart = WindowStartOf(rig.Definition, 0.0);

            rig.SendControl(windowStart);
            Assert.AreEqual(1, rig.Applied.Count);

            // 다음 시전의 제어 구간은 면역(적용 시각 + 2초) 안에서 열린다
            double secondStart = windowStart + 0.05;
            rig.Brain.Tick(secondStart);
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(2, rig.Created.Count);

            double secondWindow = WindowStartOf(rig.Definition, secondStart);
            Assert.Less(secondWindow + 0.1, windowStart + 2.0);

            rig.SendControl(secondWindow + 0.1);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(1, rig.Applied.Count);
            Assert.IsFalse(rig.Created[1].IsCancelled);

            rig.Brain.Tick(BoundaryOf(rig.Definition, secondStart));
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
        }

        [Test]
        public void Brain_ControlAfterImmunityExpires_CancelsAgain()
        {
            BrainRig rig = new BrainRig();
            rig.BeginCast(0.0);
            double windowStart = WindowStartOf(rig.Definition, 0.0);

            rig.SendControl(windowStart);
            Assert.AreEqual(1, rig.Applied.Count);

            // 면역은 적용 시각 + 2초까지라, 그 뒤의 제어 구간에서 온 제어는 다시 받는다
            double secondStart = 2.0;
            rig.Brain.Tick(secondStart);
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);

            rig.SendControl(WindowStartOf(rig.Definition, secondStart) + 0.05);

            Assert.AreEqual(2, rig.Applied.Count);
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.IsTrue(rig.Created[1].IsCancelled);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        // 시전 중이 아닐 때 도착한 제어는 버려지고 다음 시전으로 넘어가지 않는다
        [Test]
        public void Brain_ControlWhileReady_IsDiscardedAndNotCarriedToNextCast()
        {
            BrainRig rig = new BrainRig();
            Assert.IsTrue(rig.Brain.Begin(0.0));

            rig.SendControl(0.0); // 제어가 먼저 소비되고, 같은 틱에 첫 예고가 시작된다
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(0, rig.Applied.Count);

            rig.Brain.Tick(WindowStartOf(rig.Definition, 0.0) + 0.05);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(0, rig.Applied.Count);
            Assert.IsFalse(rig.Created[0].IsCancelled);
        }

        /****************************************
        *         Factory And Data
        ****************************************/

        [Test]
        public void Factory_P06CreatesFreshLineInstanceEachCall()
        {
            WolbaekPatternFactory factory = new WolbaekPatternFactory(TEST_HREF);

            IBossPattern first  = factory.Create(MakeP06());
            IBossPattern second = factory.Create(MakeP06());

            Assert.IsInstanceOf<BossLineBlastPattern>(first);
            Assert.AreNotSame(first, second);
        }

        [Test]
        public void CatalogAsset_P06HasAgreedValuesAndBossIsTuned()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            Assert.IsTrue(catalog.TryGetPattern(P06_ID, out PatternDefinition p06));

            Assert.AreEqual(1.6f, p06.TelegraphSeconds);
            Assert.AreEqual(0.6f, p06.TrackingSeconds);
            Assert.AreEqual(90f, p06.TurnRate);
            Assert.AreEqual(0f, p06.ActiveSeconds);
            Assert.AreEqual(EEnemyAttackShape.Line, p06.Shape.Shape);
            Assert.AreEqual(14f, p06.Shape.Range);
            Assert.AreEqual(4f, p06.Shape.Width);
            Assert.AreEqual(0.45f, p06.DamageRef);
            Assert.IsFalse(p06.Guardable);
            Assert.AreEqual(EBossActionState.Telegraph, p06.ControlPhase);
            Assert.AreEqual(0.4f, p06.ControlWindowSeconds);
            Assert.IsTrue(p06.ControlCancelsPattern);
            Assert.IsTrue(p06.HasControlWindow);

            // 임시값, 확정 밸런스가 아니다
            Assert.AreEqual(0.5f, p06.RecoverySeconds);

            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));
        }

        [Test]
        public void CatalogAsset_BossSettings_AreTheDocumentedOnes()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossPatternCatalogSO.BossDefinition boss));

            Assert.AreEqual(1f, boss.DirectControlMaxSeconds);
            Assert.AreEqual(2f, boss.DirectControlImmuneSeconds);

            // 전장 지름, 시작 체력 배수, 전환 비율은 기획 문서의 시작값이다
            Assert.AreEqual(36f, boss.ArenaDiameter);
            Assert.AreEqual(700f, boss.HealthRef);
            Assert.AreEqual(0.5f, boss.PhaseTransitionHealthRate);
        }

        [Test]
        public void CatalogAsset_P02AndP03AreUnchanged()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern("wolbaek_p02", out PatternDefinition p02));
            Assert.IsTrue(catalog.TryGetPattern("wolbaek_p03", out PatternDefinition p03));

            Assert.AreEqual(1f, p02.TelegraphSeconds);
            Assert.AreEqual(0.5f, p02.TrackingSeconds);
            Assert.AreEqual(120f, p02.TurnRate);
            Assert.AreEqual(6f, p02.Shape.Range);
            Assert.AreEqual(100f, p02.Shape.ArcAngle);
            Assert.AreEqual(0.24f, p02.DamageRef);
            Assert.AreEqual(0.5f, p02.RecoverySeconds);
            Assert.IsFalse(p02.HasControlWindow);

            Assert.AreEqual(1.2f, p03.TelegraphSeconds);
            Assert.AreEqual(5f, p03.Shape.Range);
            Assert.AreEqual(0.28f, p03.DamageRef);
            Assert.AreEqual(0.5f, p03.RecoverySeconds);
            Assert.IsFalse(p03.HasControlWindow);
        }

        /****************************************
        *            Wolbaek Hard P06
        ****************************************/

        private static PatternDefinition LoadAssetPattern(string id)
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern(id, out PatternDefinition pattern), id);
            return pattern;
        }

        // 실제 에셋 정의를 팩토리가 만든 직선 폭발 실행에 올린다
        private static Rig MakeAssetRig(string id)
        {
            PatternDefinition definition = LoadAssetPattern(id);
            return new Rig(definition, listener =>
                (BossLineBlastPattern)new WolbaekPatternFactory(TEST_HREF, null, null, listener).Create(definition));
        }

        // 하드 P06도 같은 직선 폭발 실행을 만든다. 이동 창구 같은 추가 창구는 필요 없다
        [Test]
        public void Factory_HardP06_CreatesLineLikeEasyP06()
        {
            PatternDefinition hard = LoadAssetPattern(HARD_P06_ID);
            WolbaekPatternFactory factory = new WolbaekPatternFactory(TEST_HREF);

            Assert.IsTrue(factory.CanCreate(hard));
            Assert.IsInstanceOf<BossLineBlastPattern>(factory.Create(hard));
            Assert.AreNotSame(factory.Create(hard), factory.Create(hard));
            Assert.IsTrue(factory.CanCreate(LoadAssetPattern(P06_ID)));
        }

        // 예고가 보여 주는 폭은 정의의 폭이다. 길이와 예고 시간은 이지와 같고 이지 P06은 4m 그대로다
        [TestCase(HARD_P06_ID, 4.5f)]
        [TestCase(P06_ID, 4f)]
        public void Begin_AssetP06_ReportsItsOwnWidth(string id, float expectedWidth)
        {
            Rig rig = MakeAssetRig(id);
            rig.Start(0.0);

            Assert.AreEqual(expectedWidth, rig.Listener.StartWidth);
            Assert.AreEqual(LENGTH, rig.Listener.StartLength);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds);
        }

        // 서버 피격 폭은 예고 폭과 같다. 좌우 2.1m, 2.2m는 하드(절반 2.25m)에서만 맞고, 2.3m는 둘 다 빗나간다. 먼 모서리도 같다
        [TestCase(HARD_P06_ID, 2f, 7f, true)]
        [TestCase(HARD_P06_ID, 2.1f, 7f, true)]
        [TestCase(HARD_P06_ID, 2.2f, 7f, true)]
        [TestCase(HARD_P06_ID, -2.2f, 7f, true)]
        [TestCase(HARD_P06_ID, 2.25f, 7f, true)]
        [TestCase(HARD_P06_ID, 2.3f, 7f, false)]
        [TestCase(HARD_P06_ID, -2.3f, 7f, false)]
        [TestCase(HARD_P06_ID, 2.2f, 14f, true)]
        [TestCase(HARD_P06_ID, 2.2f, 14.05f, false)]
        [TestCase(P06_ID, 2f, 7f, true)]
        [TestCase(P06_ID, 2.05f, 7f, false)]
        [TestCase(P06_ID, 2.1f, 7f, false)]
        [TestCase(P06_ID, 2.2f, 7f, false)]
        [TestCase(P06_ID, -2.2f, 7f, false)]
        [TestCase(P06_ID, 2.2f, 14f, false)]
        public void Strike_AssetP06_HitsOnlyInsideItsOwnWidth(string id, float lateral, float forward, bool expectedHit)
        {
            Rig rig = MakeAssetRig(id);
            rig.AddPlayer(PROBE_ID, LocalToWorld(0f, lateral, forward));
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(expectedHit ? 1 : 0, CountHits(rig.Resolver, PROBE_ID), $"{id} lateral={lateral}, forward={forward}");
        }

        // 조회 반경은 정의의 폭에 맞춰 먼 모서리를 덮는다
        [TestCase(HARD_P06_ID, 4.5f)]
        [TestCase(P06_ID, 4f)]
        public void Strike_AssetP06_QueryRadiusCoversItsOwnFarCorner(string id, float width)
        {
            Rig rig = MakeAssetRig(id);
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            float cornerDistance = Mathf.Sqrt((LENGTH * LENGTH) + ((width * 0.5f) * (width * 0.5f)));
            Assert.AreEqual(BossLineBlastPattern.QueryRadius(LENGTH, width), rig.Query.LastRadius);
            Assert.GreaterOrEqual(rig.Query.LastRadius, cornerDistance);
        }

        // 폭 말고는 이지 P06과 같은 피해 요청을 보낸다. 정의 ID만 각자 정의를 따른다
        [Test]
        public void Strike_HardP06_SendsSameDamageAsEasyWithOwnDefinitionId()
        {
            Rig hardRig = MakeAssetRig(HARD_P06_ID);
            Rig easyRig = MakeAssetRig(P06_ID);
            hardRig.Start(0.0);
            easyRig.Start(0.0);

            hardRig.Fsm.Tick(BoundaryOf(hardRig.Definition, 0.0));
            easyRig.Fsm.Tick(BoundaryOf(easyRig.Definition, 0.0));

            DamageRequest hardRequest = hardRig.Resolver.Requests.Find(r => r.HitTargetId == hardRig.Tracked.CombatId);
            DamageRequest easyRequest = easyRig.Resolver.Requests.Find(r => r.HitTargetId == easyRig.Tracked.CombatId);
            Assert.AreEqual(easyRequest.RawDamage, hardRequest.RawDamage);
            Assert.AreEqual(easyRequest.Flags, hardRequest.Flags);
            Assert.AreEqual(HARD_P06_ID, hardRequest.SourceDefinitionId);
            Assert.AreEqual(P06_ID, easyRequest.SourceDefinitionId);
        }
    }
}
