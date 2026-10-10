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
    // 시각은 이진 분수(0.25 단위)로만 넘겨 부동소수 오차가 경계 판정에 끼지 않게 한다
    // 가중치와 배율은 이 테스트가 정한 가짜 값이며 기획 값이 아니다
    public class BossFanBlastPatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string P02_ID       = "wolbaek_p02";
        private const string HARD_P02_ID  = "wolbaek_hard_p02";
        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const float  TEST_HREF    = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  ANGLE_TOLERANCE = 0.01f;
        private const int    PROBE_ID     = 11;

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP02(float recovery = 0.5f)
        {
            return new PatternDefinition(P02_ID, BossTestFixtures.BOSS_ID, 1, 15f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                1f, 0.5f, 120f, 0f, recovery, EnemyCatalogSO.AttackShapeSpec.Fan(6f, 100f), 0.24f, true);
        }

        private static Vector3 Polar(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
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

        private sealed class FanTestQuery : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Players { get; } = new List<FakeCombatTarget>();

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

        private sealed class FanListener : IBossFanTelegraphListener
        {
            public int                          StartCount;
            public float                        StartRadius;
            public float                        StartArc;
            public int                          LockCount;
            public int                          HitEventCount;
            public int                          LastHitCount = -1;
            public Vector3                      LockedDirection;
            public Vector3                      StartDirection;
            public List<EBossInterruptReason>   Cancels = new List<EBossInterruptReason>();
            public List<Vector3>                AimChanges = new List<Vector3>();
            public List<int>                    AimCastIds = new List<int>();
            public List<string>                 Order = new List<string>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius,
                float arcAngle, float telegraphSeconds)
            {
                Order.Add("start");
                ++StartCount;
                StartDirection = direction;
                StartRadius    = radius;
                StartArc       = arcAngle;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
                AimChanges.Add(direction);
                AimCastIds.Add(castInstanceId);
                Order.Add("aim");
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
                Cancels.Add(reason);
            }
        }

        // 패턴 한 개를 공격 FSM에 직접 올린 최소 구성, 추적 대상은 보스 앞 3m에 선다
        private sealed class Rig
        {
            public FakeCombatTarget     Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget     Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public FanTestQuery         Query    { get; } = new FanTestQuery();
            public FakeDamageResolver   Resolver { get; } = new FakeDamageResolver();
            public FanListener          Listener { get; } = new FanListener();
            public BossAttackFsm        Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate       Gate     { get; }
            public BossFanBlastPattern  Pattern  { get; }
            public PatternDefinition    Definition { get; }
            public BossPatternContext   Context  { get; }

            // create가 있으면 그 팩토리 경로로 패턴을 만든다. 정의가 팩토리를 거쳐 실제 부채꼴 실행이 되는지 확인하려는 용도다
            public Rig(PatternDefinition definition = null, System.Func<FanListener, BossFanBlastPattern> create = null)
            {
                Query.Players.Add(Tracked);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = definition ?? MakeP02();
                Pattern    = (create == null) ? new BossFanBlastPattern(TEST_HREF, Listener) : create(Listener);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Players.Add(player);
                return player;
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
            rig.Fsm.Tick(0.75);
            rig.Fsm.Tick(0.9375);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Fsm.State);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Tick_AtTelegraphEnd_AppliesOneHitWithExpectedRequest()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(0.5);
            rig.Fsm.Tick(1.0);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            DamageRequest request = rig.Resolver.Requests[0];
            Assert.AreEqual(rig.Self.CombatId, request.AttackerId);
            Assert.AreEqual(rig.Tracked.CombatId, request.HitTargetId);
            Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
            Assert.AreEqual(P02_ID, request.SourceDefinitionId);
            Assert.AreEqual(0.24f * TEST_HREF, request.RawDamage, 0.0001f);
            Assert.AreEqual(EDamageFlags.None, request.Flags);
            Assert.AreEqual(rig.Self.Position, request.SourcePosition);
            Assert.IsTrue(request.HasValidIds);
            Assert.AreEqual(1, rig.Listener.LastHitCount);
        }

        /****************************************
        *        Tracking And Direction Lock
        ****************************************/

        [Test]
        public void Tick_DuringTracking_RotatesAtMost120DegreesPerSecond()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f); // 정면 대비 90도
            rig.Start(0.0);
            Vector3 initial = rig.Self.Forward;

            rig.Fsm.Tick(0.25);

            Assert.AreEqual(30f, Vector3.Angle(initial, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.Fsm.Tick(0.5);

            Assert.AreEqual(60f, Vector3.Angle(initial, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
        }

        [Test]
        public void Tick_TargetCloserThanTurnLimit_FacesTargetWithoutOvershoot()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = Polar(20f, 3f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);

            Vector3 toTarget = rig.Tracked.Position;
            Assert.AreEqual(0f, Vector3.Angle(toTarget, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
        }

        [Test]
        public void Tick_AfterTrackingWindow_LocksDirectionAndIgnoresTargetMovement()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.5);
            Vector3 locked = rig.Pattern.AimDirection;
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(1, rig.Listener.LockCount);

            rig.Tracked.Position = new Vector3(-3f, 0f, 0f);
            rig.Fsm.Tick(0.75);
            rig.Fsm.Tick(0.9375);

            Assert.AreEqual(locked, rig.Pattern.AimDirection);
            Assert.AreEqual(1, rig.Listener.LockCount);
            Assert.AreEqual(locked, rig.Listener.LockedDirection);
        }

        [Test]
        public void Tick_TargetDiesAfterLock_StillStrikesFixedDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            FakeCombatTarget bystander = rig.AddPlayer(PROBE_ID, Polar(60f, 4f));
            rig.Start(0.0);

            rig.Fsm.Tick(0.5);
            rig.Tracked.Position  = new Vector3(-3f, 0f, 0f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Fsm.Tick(1.0);

            Assert.AreEqual(0, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, bystander.CombatId));
        }

        [Test]
        public void Tick_TargetDiesDuringTracking_KeepsCurrentDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);
            Vector3 before = rig.Pattern.AimDirection;
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Fsm.Tick(0.5);

            Assert.AreEqual(before, rig.Pattern.AimDirection);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
        }

        [Test]
        public void Tick_SingleLargeGap_DoesNotExceedTotalTurnLimit()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = Polar(170f, 3f);
            rig.Start(0.0);
            Vector3 initial = rig.Self.Forward;

            rig.Fsm.Tick(1.0);

            Assert.AreEqual(60f, Vector3.Angle(initial, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(0, CountHits(rig.Resolver, rig.Tracked.CombatId));
        }

        /****************************************
        *          Aim Change Notification
        ****************************************/

        [Test]
        public void Tick_AimChangesDuringTracking_NotifiesCastIdAndDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);

            Assert.AreEqual(1, rig.Listener.AimChanges.Count);
            Assert.AreEqual(rig.Context.CastInstanceId, rig.Listener.AimCastIds[0]);
            Assert.AreEqual(rig.Pattern.AimDirection, rig.Listener.AimChanges[0]);
            Assert.AreEqual(30f, Vector3.Angle(rig.Self.Forward, rig.Listener.AimChanges[0]), ANGLE_TOLERANCE);
            Assert.AreEqual(0f, rig.Listener.AimChanges[0].y);
        }

        [Test]
        public void Tick_AimUnchanged_DoesNotNotify()
        {
            Rig rig = new Rig(); // 추적 대상이 정면이라 방향이 그대로다
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);
            rig.Fsm.Tick(0.5);

            Assert.AreEqual(0, rig.Listener.AimChanges.Count);
            Assert.AreEqual(1, rig.Listener.LockCount);
        }

        [Test]
        public void Tick_AfterLock_NoMoreAimNotifications()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            rig.Start(0.0);
            rig.Fsm.Tick(0.5);
            int before = rig.Listener.AimChanges.Count;

            rig.Tracked.Position = new Vector3(-3f, 0f, 0f);
            rig.Fsm.Tick(0.75);
            rig.Fsm.Tick(2.0);

            Assert.AreEqual(before, rig.Listener.AimChanges.Count);
        }

        [Test]
        public void Tick_AfterCancel_NoAimNotifications()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            rig.Start(0.0);
            rig.Fsm.Tick(0.25);
            int before = rig.Listener.AimChanges.Count;

            rig.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            rig.Pattern.Tick(EBossAttackState.Telegraph, 0.5);
            rig.Fsm.Tick(0.5);

            Assert.AreEqual(before, rig.Listener.AimChanges.Count);
        }

        [Test]
        public void Tick_AfterFinish_NoAimNotifications()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            rig.Start(0.0);
            rig.Fsm.Tick(100.0);
            int before = rig.Listener.AimChanges.Count;

            rig.Tracked.Position = new Vector3(-3f, 0f, 0f);
            rig.Pattern.Tick(EBossAttackState.Telegraph, 101.0);

            Assert.IsTrue(rig.Pattern.IsFinished);
            Assert.AreEqual(before, rig.Listener.AimChanges.Count);
        }

        [Test]
        public void Tick_OneHugeGap_NotifiesAimBeforeLockAndHit()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(100.0);

            CollectionAssert.AreEqual(new[] { "start", "aim", "lock", "hit" }, rig.Listener.Order);
            Assert.AreEqual(60f, Vector3.Angle(rig.Self.Forward, rig.Listener.AimChanges[0]), ANGLE_TOLERANCE);
            Assert.AreEqual(rig.Listener.AimChanges[0], rig.Listener.LockedDirection);
        }

        [Test]
        public void Tick_NormalTicks_KeepsStartAimLockHitOrder()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(3f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(0.25);
            rig.Fsm.Tick(0.5);
            rig.Fsm.Tick(1.0);

            CollectionAssert.AreEqual(new[] { "start", "aim", "aim", "lock", "hit" }, rig.Listener.Order);
        }

        /****************************************
        *            Fan Boundaries
        ****************************************/

        // 추적 대상은 정면 3m에 있어 방향이 +z로 유지되고, 시험 대상 한 명의 위치만 바꿔 본다
        [TestCase(0f, 5.99f, true)]
        [TestCase(0f, 6f, true)]
        [TestCase(0f, 6.01f, false)]
        [TestCase(49.9f, 5f, true)]
        [TestCase(50f, 5f, true)]
        [TestCase(50.1f, 5f, false)]
        [TestCase(-50f, 5f, true)]
        [TestCase(-50.1f, 5f, false)]
        [TestCase(90f, 3f, false)]
        [TestCase(180f, 1f, false)]
        [TestCase(0f, 0f, true)]
        public void Strike_ProbeAtPosition_HitsOnlyInsideDistanceAndAngle(float degrees, float distance, bool expectedHit)
        {
            Rig rig = new Rig();
            rig.AddPlayer(PROBE_ID, Polar(degrees, distance));
            rig.Start(0.0);

            rig.Fsm.Tick(1.0);

            Assert.AreEqual(expectedHit ? 1 : 0, CountHits(rig.Resolver, PROBE_ID));
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
        }

        [Test]
        public void Strike_DeadOrSummonedTargets_FollowQueryRules()
        {
            Rig rig = new Rig();
            FakeCombatTarget dead = rig.AddPlayer(PROBE_ID, Polar(0f, 4f));
            dead.LifeState = EParticipantLifeState.Dead;
            FakeCombatTarget summon = rig.AddPlayer(PROBE_ID + 1, Polar(10f, 4f));
            summon.IsSummon = true;
            rig.Start(0.0);

            rig.Fsm.Tick(1.0);

            Assert.AreEqual(0, CountHits(rig.Resolver, dead.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, summon.CombatId));
        }

        /****************************************
        *            One Hit Per Target
        ****************************************/

        [Test]
        public void Tick_ManyTicksAcrossWholeCast_AppliesOnceAndSharesActionId()
        {
            Rig rig = new Rig();
            rig.AddPlayer(PROBE_ID, Polar(10f, 4f));
            rig.Start(0.0);

            double[] times = { 0.0, 0.5, 1.0, 1.0, 1.0, 1.25, 1.5, 1.5, 2.0, 2.25 };
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
        public void Tick_OneHugeGap_AppliesOnceAndFinishes()
        {
            Rig rig = new Rig();
            rig.AddPlayer(PROBE_ID, Polar(10f, 4f));
            rig.Start(0.0);

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, PROBE_ID));
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
            Assert.IsTrue(rig.Pattern.IsFinished);

            rig.Fsm.Tick(200.0);

            Assert.AreEqual(2, rig.Resolver.Requests.Count);
        }

        [Test]
        public void EnterState_ExecuteCalledAgain_DoesNotApplyTwice()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(1.0);

            rig.Pattern.EnterState(EBossAttackState.Execute, 1.0);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
        }

        /****************************************
        *               Cancel
        ****************************************/

        [Test]
        public void Cancel_DuringTelegraph_BlocksLaterStrike()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(0.5);

            rig.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            rig.Fsm.Tick(1.0);
            rig.Fsm.Tick(2.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.PhaseTransition, rig.Listener.Cancels[0]);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Cancel_ThenStaleExecuteReservation_DoesNotReachResolver()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(0.75);
            rig.Fsm.Cancel(EBossInterruptReason.CombatEnd);

            // 예약된 타격이 취소 뒤에 도착하는 상황을 직접 만든다
            rig.Pattern.EnterState(EBossAttackState.Execute, 1.0);
            rig.Pattern.Tick(EBossAttackState.Execute, 1.0);

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
            rig.Fsm.Tick(1.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
        }

        [Test]
        public void Cancel_AfterStrike_KeepsAppliedDamageAndSendsNothingMore()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(1.0);

            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.Fsm.Tick(2.0);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
        }

        /****************************************
        *         Brain Integration
        ****************************************/

        private sealed class RecordingFactory : IBossPatternFactory
        {
            private readonly WolbaekPatternFactory _inner = new WolbaekPatternFactory(TEST_HREF);

            public List<BossFanBlastPattern> Created { get; } = new List<BossFanBlastPattern>();

            public IBossPattern Create(PatternDefinition definition)
            {
                IBossPattern pattern = _inner.Create(definition);
                if (pattern is BossFanBlastPattern fan)
                {
                    Created.Add(fan);
                }

                return pattern;
            }
        }

        private sealed class BrainRig
        {
            public FakeCombatTarget    Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget    Player   { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public FanTestQuery        Query    { get; } = new FanTestQuery();
            public FakeDamageResolver  Resolver { get; } = new FakeDamageResolver();
            public FakeBossVitals      Vitals   { get; } = new FakeBossVitals();
            public RecordingFactory    Factory  { get; } = new RecordingFactory();
            public BossBrain           Brain    { get; }

            public BrainRig()
            {
                Query.Players.Add(Player);
                FakeBossCatalog catalog = new FakeBossCatalog { Boss = BossTestFixtures.MakeBoss() };
                catalog.Patterns.Add(MakeP02());

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = catalog,
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Resolver,
                    Patterns = Factory,
                    Effects  = new FakeEffectLifetime(),
                    Control  = new FakeControlSource(),
                    Random   = new FakeBossRandom()
                });
            }
        }

        [Test]
        public void Brain_FullCycle_ReturnsToAttackFsmAndSelectsAgain()
        {
            BrainRig rig = new BrainRig();
            Assert.IsTrue(rig.Brain.Begin(0.0));

            rig.Brain.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(P02_ID, rig.Brain.ActivePatternId);

            rig.Brain.Tick(1.0);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.Brain.Tick(1.25);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.Brain.Tick(1.5);
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.IsTrue(rig.Factory.Created[0].IsFinished);
            Assert.IsFalse(rig.Factory.Created[0].IsCancelled);

            rig.Brain.Tick(1.75);
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(2, rig.Factory.Created.Count);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
        }

        [Test]
        public void Brain_RecoveryUsesDefinitionValue()
        {
            BrainRig rig = new BrainRig();
            rig.Brain.Begin(0.0);
            rig.Brain.Tick(0.0);
            rig.Brain.Tick(1.0);

            rig.Brain.Tick(1.4375);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.Brain.Tick(1.5);
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
        }

        [Test]
        public void Brain_PhaseTransitionDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.Brain.Begin(0.0);
            rig.Brain.Tick(0.0);

            rig.Vitals.HealthRate = 0.25f;
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(1.0);
            rig.Brain.Tick(2.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Factory.Created[0].IsCancelled);
            Assert.AreEqual(EBossInterruptReason.PhaseTransition, rig.Factory.Created[0].CancelReason);
        }

        [Test]
        public void Brain_CombatEndDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.Brain.Begin(0.0);
            rig.Brain.Tick(0.0);

            rig.Brain.RequestCombatEnd();
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(1.0);
            rig.Brain.Tick(2.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Factory.Created[0].IsCancelled);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, rig.Factory.Created[0].CancelReason);
        }

        [Test]
        public void Brain_BossDeathDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.Brain.Begin(0.0);
            rig.Brain.Tick(0.0);

            rig.Vitals.IsHealthZero = true;
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(1.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Factory.Created[0].IsCancelled);
        }

        /****************************************
        *         Factory And Data
        ****************************************/

        [Test]
        public void Factory_P02_CreatesFanPattern()
        {
            WolbaekPatternFactory factory = new WolbaekPatternFactory(TEST_HREF);

            Assert.IsInstanceOf<BossFanBlastPattern>(factory.Create(MakeP02()));
            Assert.IsNull(factory.Create(BossTestFixtures.MakePattern("wolbaek_p01")));
            Assert.IsNull(factory.Create(null));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void Factory_NonPositiveHref_Throws(float href)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekPatternFactory(href));
        }

        [Test]
        public void CatalogAsset_P02HasConfirmedValuesAndBossIsTuned()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            Assert.IsTrue(catalog.TryGetPattern(P02_ID, out PatternDefinition p02));

            Assert.AreEqual(1f, p02.TelegraphSeconds);
            Assert.AreEqual(0.5f, p02.TrackingSeconds);
            Assert.AreEqual(120f, p02.TurnRate);
            Assert.AreEqual(0f, p02.ActiveSeconds);
            Assert.AreEqual(EEnemyAttackShape.Fan, p02.Shape.Shape);
            Assert.AreEqual(6f, p02.Shape.Range);
            Assert.AreEqual(100f, p02.Shape.ArcAngle);
            Assert.AreEqual(0.24f, p02.DamageRef);
            Assert.IsTrue(p02.Guardable);
            Assert.IsFalse(p02.HasControlWindow);

            // 임시값, 확정 밸런스가 아니다
            Assert.AreEqual(0.5f, p02.RecoverySeconds);

            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));
        }

        /****************************************
        *            Wolbaek Hard P02
        ****************************************/

        private static PatternDefinition LoadAssetPattern(string id)
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern(id, out PatternDefinition pattern), id);
            return pattern;
        }

        // 실제 에셋 정의를 팩토리가 만든 부채꼴 실행에 올린다
        private static Rig MakeAssetRig(string id)
        {
            PatternDefinition definition = LoadAssetPattern(id);
            return new Rig(definition, listener =>
                (BossFanBlastPattern)new WolbaekPatternFactory(TEST_HREF, listener).Create(definition));
        }

        // 하드 P02도 같은 부채꼴 실행을 만든다. 이동 창구 같은 추가 창구는 필요 없다
        [Test]
        public void Factory_HardP02_CreatesFanLikeEasyP02()
        {
            PatternDefinition hard = LoadAssetPattern(HARD_P02_ID);
            WolbaekPatternFactory factory = new WolbaekPatternFactory(TEST_HREF);

            Assert.IsTrue(factory.CanCreate(hard));
            Assert.IsInstanceOf<BossFanBlastPattern>(factory.Create(hard));
            Assert.AreNotSame(factory.Create(hard), factory.Create(hard));
            Assert.IsTrue(factory.CanCreate(LoadAssetPattern(P02_ID)));
        }

        // 예고가 보여 주는 사거리와 각도는 정의 값이다. 이지 P02는 6m, 100도 그대로다
        [TestCase(HARD_P02_ID, 7f, 110f)]
        [TestCase(P02_ID, 6f, 100f)]
        public void Begin_AssetP02_ReportsItsOwnRangeAndArc(string id, float expectedRange, float expectedArc)
        {
            Rig rig = MakeAssetRig(id);
            rig.Start(0.0);

            Assert.AreEqual(expectedRange, rig.Listener.StartRadius);
            Assert.AreEqual(expectedArc, rig.Listener.StartArc);
            Assert.AreEqual(1f, rig.Definition.TelegraphSeconds);
        }

        // 두 난이도가 갈리는 위치를 포함한다. (0도, 6.5m)와 (+-52도, 5m)는 하드에서만 맞고, 7.01m와 55.1도는 둘 다 빗나간다
        [TestCase(HARD_P02_ID, 0f, 6.99f, true)]
        [TestCase(HARD_P02_ID, 0f, 7f, true)]
        [TestCase(HARD_P02_ID, 0f, 7.01f, false)]
        [TestCase(HARD_P02_ID, 0f, 6.5f, true)]
        [TestCase(HARD_P02_ID, 52f, 5f, true)]
        [TestCase(HARD_P02_ID, -52f, 5f, true)]
        [TestCase(HARD_P02_ID, 54.9f, 5f, true)]
        [TestCase(HARD_P02_ID, 55f, 5f, true)]
        [TestCase(HARD_P02_ID, 55.1f, 5f, false)]
        [TestCase(HARD_P02_ID, -55f, 5f, true)]
        [TestCase(HARD_P02_ID, -55.1f, 5f, false)]
        [TestCase(HARD_P02_ID, 60f, 5f, false)]
        [TestCase(P02_ID, 0f, 6f, true)]
        [TestCase(P02_ID, 0f, 6.01f, false)]
        [TestCase(P02_ID, 0f, 6.5f, false)]
        [TestCase(P02_ID, 50f, 5f, true)]
        [TestCase(P02_ID, 50.1f, 5f, false)]
        [TestCase(P02_ID, 52f, 5f, false)]
        [TestCase(P02_ID, -52f, 5f, false)]
        public void Strike_AssetP02_HitsOnlyInsideItsOwnSector(string id, float degrees, float distance, bool expectedHit)
        {
            Rig rig = MakeAssetRig(id);
            rig.AddPlayer(PROBE_ID, Polar(degrees, distance));
            rig.Start(0.0);

            rig.Fsm.Tick(1.0);

            Assert.AreEqual(expectedHit ? 1 : 0, CountHits(rig.Resolver, PROBE_ID));
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
        }

        // 사거리와 각도 말고는 이지 P02와 같은 피해 요청을 보낸다
        [Test]
        public void Strike_HardP02_SendsSameDamageAsEasyWithOwnDefinitionId()
        {
            Rig hardRig = MakeAssetRig(HARD_P02_ID);
            Rig easyRig = MakeAssetRig(P02_ID);
            hardRig.Start(0.0);
            easyRig.Start(0.0);

            hardRig.Fsm.Tick(1.0);
            easyRig.Fsm.Tick(1.0);

            DamageRequest hardRequest = hardRig.Resolver.Requests.Find(r => r.HitTargetId == hardRig.Tracked.CombatId);
            DamageRequest easyRequest = easyRig.Resolver.Requests.Find(r => r.HitTargetId == easyRig.Tracked.CombatId);
            Assert.AreEqual(easyRequest.RawDamage, hardRequest.RawDamage);
            Assert.AreEqual(easyRequest.Flags, hardRequest.Flags);
            Assert.AreEqual(HARD_P02_ID, hardRequest.SourceDefinitionId);
            Assert.AreEqual(P02_ID, easyRequest.SourceDefinitionId);
        }
    }
}
