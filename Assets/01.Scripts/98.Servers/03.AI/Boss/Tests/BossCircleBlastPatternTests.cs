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
    // 예고 길이 1.2는 float라 double로 넘기면 1.2보다 약간 크다. 경계 시각은 FSM과 같은 식(시작 + (double)TelegraphSeconds)으로 구한다
    // 가중치와 배율은 이 테스트가 정한 가짜 값이며 기획 값이 아니다
    public class BossCircleBlastPatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string P02_ID       = "wolbaek_p02";
        private const string P03_ID       = "wolbaek_p03";
        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const float  TEST_HREF    = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  RADIUS       = 5f;
        private const float  TELEGRAPH    = 1.2f;
        private const float  RECOVERY     = 0.5f;
        private const double EPSILON      = 0.001;
        private const int    PROBE_ID     = 11;

        private static readonly Vector3 BossStart = new Vector3(2f, 0f, 3f);

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP03(float recovery = RECOVERY, bool guardable = true)
        {
            return new PatternDefinition(P03_ID, BossTestFixtures.BOSS_ID, 1, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, 0f, 0f, 0f, recovery, EnemyCatalogSO.AttackShapeSpec.Circle(RADIUS), 0.28f, guardable);
        }

        // FSM이 계산하는 예고 종료 경계와 같은 값이다
        private static double BoundaryOf(PatternDefinition definition, double startTime)
        {
            return startTime + definition.TelegraphSeconds;
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
        private sealed class CircleTestQuery : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Players { get; } = new List<FakeCombatTarget>();

            public int     RadiusCalls;
            public Vector3 LastCenter;
            public float   LastRadius;
            public ECombatFaction LastFaction;
            public bool    LastIncludeSummons;

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

        private sealed class CircleListener : IBossCircleTelegraphListener
        {
            public int                          StartCount;
            public int                          HitEventCount;
            public int                          LastHitCount = -1;
            public int                          LastCastId   = -1;
            public Vector3                      StartCenter;
            public float                        StartRadius;
            public float                        StartSeconds;
            public List<EBossInterruptReason>   Cancels = new List<EBossInterruptReason>();
            public List<string>                 Order   = new List<string>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds)
            {
                Order.Add("start");
                ++StartCount;
                LastCastId   = castInstanceId;
                StartCenter  = center;
                StartRadius  = radius;
                StartSeconds = telegraphSeconds;
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

        // 패턴 한 개를 공격 FSM에 직접 올린 최소 구성, 추적 대상은 보스 앞에 선다
        private sealed class Rig
        {
            public FakeCombatTarget     Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            public FakeCombatTarget     Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = BossStart + new Vector3(0f, 0f, 3f) };
            public CircleTestQuery      Query    { get; } = new CircleTestQuery();
            public FakeDamageResolver   Resolver { get; } = new FakeDamageResolver();
            public CircleListener       Listener { get; } = new CircleListener();
            public BossAttackFsm        Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate       Gate     { get; }
            public BossCircleBlastPattern Pattern { get; }
            public PatternDefinition    Definition { get; }
            public BossPatternContext   Context  { get; }

            public Rig(PatternDefinition definition = null)
            {
                Query.Players.Add(Tracked);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = definition ?? MakeP03();
                Pattern    = new BossCircleBlastPattern(TEST_HREF, Listener);
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

        // float 1.2f는 double 1.2보다 크므로 1.2 그대로의 시각에는 아직 예고 중이다
        [Test]
        public void Tick_AtDoubleOnePointTwo_StillTelegraphing()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            Assert.Greater(BoundaryOf(rig.Definition, 0.0), 1.2);
            rig.Fsm.Tick(1.2);

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
        public void Tick_AtBoundary_BuildsExpectedRequest()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            DamageRequest request = rig.Resolver.Requests[0];
            Assert.AreEqual(rig.Self.CombatId, request.AttackerId);
            Assert.AreEqual(rig.Tracked.CombatId, request.HitTargetId);
            Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
            Assert.AreEqual(P03_ID, request.SourceDefinitionId);
            Assert.AreEqual(0.28f * TEST_HREF, request.RawDamage, 0.0001f);
            Assert.AreEqual(EDamageFlags.None, request.Flags);
            Assert.IsTrue(request.HasValidIds);
            Assert.AreEqual(1, rig.Listener.LastHitCount);
        }

        [Test]
        public void Strike_NotGuardable_FlagsUnblockable()
        {
            Rig rig = new Rig(MakeP03(guardable: false));
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(EDamageFlags.Unblockable, rig.Resolver.Requests[0].Flags);
        }

        /****************************************
        *           Fixed Center
        ****************************************/

        [Test]
        public void Begin_SavesBossPositionAndNotifiesTelegraph()
        {
            Rig rig = new Rig();

            rig.Start(0.0);

            Assert.AreEqual(BossStart, rig.Pattern.Center);
            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(BossStart, rig.Listener.StartCenter);
            Assert.AreEqual(RADIUS, rig.Listener.StartRadius);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds);
            Assert.AreEqual(rig.Context.CastInstanceId, rig.Listener.LastCastId);
        }

        [Test]
        public void Tick_BossMovesDuringTelegraph_KeepsCenterEverywhere()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Self.Position = new Vector3(-20f, 4f, 30f);
            rig.Fsm.Tick(0.5);
            rig.Self.Position = new Vector3(40f, 0f, -7f);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(BossStart, rig.Pattern.Center);
            Assert.AreEqual(BossStart, rig.Listener.StartCenter);
            Assert.AreEqual(BossStart, rig.Query.LastCenter);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(BossStart, rig.Resolver.Requests[0].SourcePosition);
        }

        [Test]
        public void Strike_BossMoved_JudgesAroundSavedCenterNotCurrentBoss()
        {
            Rig rig = new Rig();
            FakeCombatTarget nearSaved   = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(4f, 0f, 0f));
            FakeCombatTarget nearCurrent = rig.AddPlayer(PROBE_ID + 1, new Vector3(30f, 0f, 30f) + new Vector3(1f, 0f, 0f));
            rig.Start(0.0);

            rig.Self.Position = new Vector3(30f, 0f, 30f);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, CountHits(rig.Resolver, nearSaved.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, nearCurrent.CombatId));
        }

        [Test]
        public void Tick_TargetMovesDuringTelegraph_DoesNotMoveCenter()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Tracked.Position = new Vector3(100f, 0f, 100f);
            rig.Fsm.Tick(0.75);

            Assert.AreEqual(BossStart, rig.Pattern.Center);
        }

        /****************************************
        *            Radius Boundaries
        ****************************************/

        // 보스 위치에서 +x 방향으로 distance만큼 떨어진 시험 대상 한 명
        [TestCase(0f, true)]
        [TestCase(4.99f, true)]
        [TestCase(5f, true)]
        [TestCase(5.01f, false)]
        [TestCase(12f, false)]
        public void Strike_ProbeAtDistance_HitsOnlyInsideRadiusInclusive(float distance, bool expectedHit)
        {
            Rig rig = new Rig();
            rig.AddPlayer(PROBE_ID, BossStart + new Vector3(distance, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(expectedHit ? 1 : 0, CountHits(rig.Resolver, PROBE_ID));
        }

        // 뒤쪽과 옆쪽도 같은 반경으로 맞는다, 부채꼴이 아니다
        [Test]
        public void Strike_AllDirectionsWithinRadius_AreHit()
        {
            Rig rig = new Rig();
            FakeCombatTarget back = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(0f, 0f, -4f));
            FakeCombatTarget side = rig.AddPlayer(PROBE_ID + 1, BossStart + new Vector3(-4f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, CountHits(rig.Resolver, back.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, side.CombatId));
        }

        // 수평 거리만 본다, 높이 차이는 거리에 넣지 않는다
        [Test]
        public void Strike_HeightDifference_IsIgnoredByHorizontalContract()
        {
            Rig rig = new Rig();
            FakeCombatTarget high = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(4f, 10f, 0f));
            FakeCombatTarget far  = rig.AddPlayer(PROBE_ID + 1, BossStart + new Vector3(5.5f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, CountHits(rig.Resolver, high.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, far.CombatId));
        }

        [Test]
        public void Strike_QueriesPlayerFactionWithSummonsAtDefinitionRadius()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, rig.Query.RadiusCalls);
            Assert.AreEqual(RADIUS, rig.Query.LastRadius);
            Assert.AreEqual(ECombatFaction.Player, rig.Query.LastFaction);
            Assert.IsTrue(rig.Query.LastIncludeSummons);
        }

        [Test]
        public void Strike_TargetMovesBeforeStrike_JudgedAtStrikePosition()
        {
            Rig rig = new Rig();
            FakeCombatTarget mover = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(20f, 0f, 0f));
            FakeCombatTarget leaver = rig.AddPlayer(PROBE_ID + 1, BossStart + new Vector3(2f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(0.5);
            mover.Position  = BossStart + new Vector3(3f, 0f, 0f);
            leaver.Position = BossStart + new Vector3(20f, 0f, 0f);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, CountHits(rig.Resolver, mover.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, leaver.CombatId));
        }

        [Test]
        public void Strike_DeadOrSummonedTargets_FollowQueryRules()
        {
            Rig rig = new Rig();
            FakeCombatTarget dead = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(1f, 0f, 0f));
            dead.LifeState = EParticipantLifeState.Dead;
            FakeCombatTarget summon = rig.AddPlayer(PROBE_ID + 1, BossStart + new Vector3(2f, 0f, 0f));
            summon.IsSummon = true;
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(0, CountHits(rig.Resolver, dead.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, summon.CombatId));
        }

        [Test]
        public void Strike_OriginalTargetDiesAfterTelegraphStart_StillStrikesOthers()
        {
            Rig rig = new Rig();
            FakeCombatTarget bystander = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(-3f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(0.5);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(0, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, bystander.CombatId));
            Assert.AreEqual(1, rig.Listener.HitEventCount);
        }

        [Test]
        public void Strike_OriginalTargetRemovedAfterTelegraphStart_StillStrikesOthers()
        {
            Rig rig = new Rig();
            FakeCombatTarget bystander = rig.AddPlayer(PROBE_ID, BossStart + new Vector3(-3f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(0.5);
            rig.Query.Players.Remove(rig.Tracked);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, CountHits(rig.Resolver, bystander.CombatId));
        }

        [Test]
        public void Strike_NobodyInRange_NotifiesHitWithZero()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = BossStart + new Vector3(50f, 0f, 0f);
            rig.Start(0.0);

            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
            Assert.AreEqual(0, rig.Listener.LastHitCount);
        }

        /****************************************
        *            Direct Control
        ****************************************/

        // 예고, 타격, 회복 어디에서도 직접 제어 구간이 열리지 않는다
        [Test]
        public void IsControlWindowOpen_NeverOpensDuringCast()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            double boundary = BoundaryOf(rig.Definition, 0.0);

            double[] times = { 0.0, 0.5, 1.0, boundary - EPSILON, boundary, boundary + 0.25, boundary + RECOVERY - EPSILON };
            for (int i = 0; i < times.Length; ++i)
            {
                rig.Fsm.Tick(times[i]);
                Assert.IsFalse(rig.Fsm.IsControlWindowOpen(times[i]), $"t={times[i]}");
            }
        }

        /****************************************
        *            One Hit Per Target
        ****************************************/

        [Test]
        public void Tick_ManyTicksAcrossWholeCast_AppliesOnceAndSharesActionId()
        {
            Rig rig = new Rig();
            rig.AddPlayer(PROBE_ID, BossStart + new Vector3(4f, 0f, 0f));
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
        public void Tick_OneHugeGap_AppliesOnceAndFinishes()
        {
            Rig rig = new Rig();
            rig.AddPlayer(PROBE_ID, BossStart + new Vector3(4f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, PROBE_ID));
            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
            Assert.IsTrue(rig.Pattern.IsFinished);

            rig.Fsm.Tick(200.0);

            Assert.AreEqual(2, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
        }

        [Test]
        public void EnterState_ExecuteCalledAgain_DoesNotApplyTwice()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));

            rig.Pattern.EnterState(EBossAttackState.Execute, 2.0);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
        }

        /****************************************
        *               Cancel
        ****************************************/

        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.Death)]
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
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(reason, rig.Listener.Cancels[0]);
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

            // 예약된 타격이 취소 뒤에 도착하는 상황을 직접 만든다
            rig.Pattern.EnterState(EBossAttackState.Execute, 1.25);
            rig.Pattern.Tick(EBossAttackState.Execute, 1.25);

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
        *           Notification Order
        ****************************************/

        [Test]
        public void Tick_FullCast_NotifiesStartThenHitOnly()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(0.5);
            rig.Fsm.Tick(BoundaryOf(rig.Definition, 0.0));
            rig.Fsm.Tick(5.0);

            CollectionAssert.AreEqual(new[] { "start", "hit" }, rig.Listener.Order);
        }

        [Test]
        public void Pattern_WithoutListener_StillStrikes()
        {
            FakeCombatTarget self = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            FakeCombatTarget player = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = BossStart + new Vector3(1f, 0f, 0f) };
            CircleTestQuery query = new CircleTestQuery();
            query.Players.Add(player);
            FakeDamageResolver resolver = new FakeDamageResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            PatternDefinition definition = MakeP03();
            BossPatternContext context = new BossPatternContext(definition, gate.IssueInstanceId(), player.CombatId, self, query, gate);
            BossAttackFsm fsm = new BossAttackFsm();

            Assert.IsTrue(fsm.Start(definition, new BossCircleBlastPattern(TEST_HREF), in context, 0.0));
            fsm.Tick(BoundaryOf(definition, 0.0));

            Assert.AreEqual(1, resolver.Requests.Count);
        }

        /****************************************
        *         Brain Integration
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget    Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            public FakeCombatTarget    Player   { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = BossStart + new Vector3(0f, 0f, 3f) };
            public CircleTestQuery     Query    { get; } = new CircleTestQuery();
            public FakeDamageResolver  Resolver { get; } = new FakeDamageResolver();
            public FakeBossVitals      Vitals   { get; } = new FakeBossVitals();
            public CircleListener      Listener { get; } = new CircleListener();
            public List<BossCircleBlastPattern> Created { get; } = new List<BossCircleBlastPattern>();
            public BossBrain           Brain    { get; }

            private sealed class RecordingFactory : IBossPatternFactory
            {
                private readonly WolbaekPatternFactory _inner;
                private readonly List<BossCircleBlastPattern> _created;

                public RecordingFactory(CircleListener listener, List<BossCircleBlastPattern> created)
                {
                    _inner   = new WolbaekPatternFactory(TEST_HREF, null, listener);
                    _created = created;
                }

                public IBossPattern Create(PatternDefinition definition)
                {
                    IBossPattern pattern = _inner.Create(definition);
                    if (pattern is BossCircleBlastPattern circle)
                    {
                        _created.Add(circle);
                    }

                    return pattern;
                }
            }

            public BrainRig()
            {
                Query.Players.Add(Player);
                FakeBossCatalog catalog = new FakeBossCatalog { Boss = BossTestFixtures.MakeBoss() };
                catalog.Patterns.Add(MakeP03());

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = catalog,
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Resolver,
                    Patterns = new RecordingFactory(Listener, Created),
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
            Assert.AreEqual(P03_ID, rig.Brain.ActivePatternId);

            rig.Brain.Tick(1.0);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);

            rig.Brain.Tick(1.25);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.Brain.Tick(1.5);
            Assert.AreEqual(EBossAttackState.Recover, rig.Brain.AttackState);

            rig.Brain.Tick(1.75);
            Assert.AreEqual(EBossAttackState.Ready, rig.Brain.AttackState);
            Assert.IsTrue(rig.Created[0].IsFinished);
            Assert.IsFalse(rig.Created[0].IsCancelled);

            rig.Brain.Tick(2.0);
            Assert.AreEqual(EBossAttackState.Telegraph, rig.Brain.AttackState);
            Assert.AreEqual(2, rig.Created.Count);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(2, rig.Listener.StartCount);
        }

        [Test]
        public void Brain_PhaseTransitionDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.Brain.Begin(0.0);
            rig.Brain.Tick(0.0);

            rig.Vitals.HealthRate = 0.25f;
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(1.25);
            rig.Brain.Tick(2.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Created[0].IsCancelled);
            Assert.AreEqual(EBossInterruptReason.PhaseTransition, rig.Created[0].CancelReason);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Brain_CombatEndDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.Brain.Begin(0.0);
            rig.Brain.Tick(0.0);

            rig.Brain.RequestCombatEnd();
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(1.25);
            rig.Brain.Tick(2.0);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Created[0].IsCancelled);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, rig.Created[0].CancelReason);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Brain_BossDeathDuringTelegraph_BlocksStrike()
        {
            BrainRig rig = new BrainRig();
            rig.Brain.Begin(0.0);
            rig.Brain.Tick(0.0);

            rig.Vitals.IsHealthZero = true;
            rig.Brain.Tick(0.5);
            rig.Brain.Tick(1.25);

            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.IsTrue(rig.Created[0].IsCancelled);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        /****************************************
        *         Factory And Data
        ****************************************/

        [Test]
        public void Factory_CreatesOnlyP02AndP03AndP06()
        {
            WolbaekPatternFactory factory = new WolbaekPatternFactory(TEST_HREF);

            Assert.IsInstanceOf<BossCircleBlastPattern>(factory.Create(MakeP03()));
            Assert.IsInstanceOf<BossFanBlastPattern>(factory.Create(
                new PatternDefinition(P02_ID, BossTestFixtures.BOSS_ID, 1, 15f,
                    new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                    1f, 0.5f, 120f, 0f, 0.5f, EnemyCatalogSO.AttackShapeSpec.Fan(6f, 100f), 0.24f, true)));

            Assert.IsInstanceOf<BossLineBlastPattern>(factory.Create(BossTestFixtures.MakePattern("wolbaek_p06")));

            string[] others = { "wolbaek_p01", "wolbaek_p04", "wolbaek_p05" };
            for (int i = 0; i < others.Length; ++i)
            {
                Assert.IsNull(factory.Create(BossTestFixtures.MakePattern(others[i])), others[i]);
            }

            Assert.IsNull(factory.Create(null));
        }

        [Test]
        public void Factory_CreatesFreshInstancePerCall()
        {
            WolbaekPatternFactory factory = new WolbaekPatternFactory(TEST_HREF);

            Assert.AreNotSame(factory.Create(MakeP03()), factory.Create(MakeP03()));
        }

        [Test]
        public void CatalogAsset_P03HasAgreedValuesAndNothingElseIsFilled()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            Assert.IsTrue(catalog.TryGetPattern(P03_ID, out PatternDefinition p03));

            Assert.AreEqual(1.2f, p03.TelegraphSeconds);
            Assert.AreEqual(0f, p03.TrackingSeconds);
            Assert.AreEqual(0f, p03.TurnRate);
            Assert.AreEqual(0f, p03.ActiveSeconds);
            Assert.AreEqual(EEnemyAttackShape.Circle, p03.Shape.Shape);
            Assert.AreEqual(5f, p03.Shape.Range);
            Assert.AreEqual(0.28f, p03.DamageRef);
            Assert.IsTrue(p03.Guardable);
            Assert.IsFalse(p03.HasControlWindow);

            // 임시값, 확정 밸런스가 아니다
            Assert.AreEqual(0.5f, p03.RecoverySeconds);

            // 이번 범위에서는 실행 수치만 입력했고 선택 가중치와 배율은 미입력이다
            Assert.AreEqual(0f, p03.BaseWeight);
            AssertAllZero(p03.DistanceMultipliers);
            AssertAllZero(p03.DirectionMultipliers);
            AssertAllZero(p03.NearbyMultipliers);
            AssertAllZero(p03.RecentUseMultipliers);

            Assert.IsFalse(catalog.IsTuned(WOLBAEK_ID));
        }

        [TestCase("wolbaek_p01")]
        [TestCase("wolbaek_p04")]
        [TestCase("wolbaek_p05")]
        public void CatalogAsset_OtherUnimplementedPatterns_StayUnset(string patternId)
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern(patternId, out PatternDefinition pattern));

            Assert.AreEqual(0f, pattern.TelegraphSeconds);
            Assert.AreEqual(0f, pattern.RecoverySeconds);
            Assert.AreEqual(0f, pattern.DamageRef);
            Assert.AreEqual(0f, pattern.BaseWeight);
        }

        private static void AssertAllZero(IReadOnlyList<float> values)
        {
            for (int i = 0; i < values.Count; ++i)
            {
                Assert.AreEqual(0f, values[i]);
            }
        }
    }
}
