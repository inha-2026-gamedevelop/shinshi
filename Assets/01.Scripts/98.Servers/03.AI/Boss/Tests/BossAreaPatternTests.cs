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
    // 월백 P05 패턴과 장판 관리자를 가짜 의존성으로 확인한다. 정의 값은 기획 표의 P05를 그대로 쓰되 가중치와 배율은 가짜 값이다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 틱은 BossCombat처럼 공격 진행 뒤에 장판 관리자를 부르는 순서로 흉내 낸다
    public class BossAreaPatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string P05_ID       = "wolbaek_p05";
        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  TEST_HREF    = 3541f;
        private const float  TELEGRAPH    = 1f;
        private const float  RECOVERY     = 0.5f;
        private const float  RADIUS       = 3f;
        private const float  DURATION     = 6f;
        private const float  INTERVAL     = 1f;
        private const int    MAX_AREAS    = 3;
        private const float  DAMAGE_REF   = 0.08f;
        private const float  STEP         = 0.01f;
        private const double EPSILON      = 0.001;

        private static readonly Vector3 AreaCenter = new Vector3(4f, 0f, 5f);

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP05()
        {
            return new PatternDefinition(P05_ID, BossTestFixtures.BOSS_ID, 1, 15f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, 0f, 0f, 0f, RECOVERY, EnemyCatalogSO.AttackShapeSpec.TargetArea(RADIUS), DAMAGE_REF, false,
                EBossActionState.SelectTarget, 0f, false, DURATION, INTERVAL, MAX_AREAS);
        }

        private sealed class AreaQuery : ICombatTargetQuery
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

        private sealed class AreaResolver : IDamageResolver
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

        private struct TickRecord
        {
            public int    CastId;
            public int    Index;
            public double RoundTime;
            public int    HitCount;
        }

        private sealed class AreaListener : IBossAreaListener, IBossAreaTelegraphListener
        {
            public int      TelegraphStarts;
            public Vector3  TelegraphCenter;
            public float    TelegraphRadius;
            public float    TelegraphSeconds;
            public List<EBossInterruptReason> TelegraphCancels = new List<EBossInterruptReason>();

            public List<int>        Activated  = new List<int>();
            public Vector3          ActivatedCenter;
            public float            ActivatedRadius;
            public float            ActivatedDuration;
            public float            ActivatedInterval;
            public double           ActivatedAt;
            public List<TickRecord> Ticks      = new List<TickRecord>();
            public List<int>        Ended      = new List<int>();
            public EBossAreaEnd     LastEnd;
            public double           LastEndAt;

            public void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds)
            {
                ++TelegraphStarts;
                TelegraphCenter  = center;
                TelegraphRadius  = radius;
                TelegraphSeconds = telegraphSeconds;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                TelegraphCancels.Add(reason);
            }

            public void OnAreaActivated(int castInstanceId, Vector3 center, float radius, float durationSeconds, float tickInterval,
                double activateServerTime)
            {
                Activated.Add(castInstanceId);
                ActivatedCenter   = center;
                ActivatedRadius   = radius;
                ActivatedDuration = durationSeconds;
                ActivatedInterval = tickInterval;
                ActivatedAt       = activateServerTime;
            }

            public void OnAreaTick(int castInstanceId, int hitIndex, double roundServerTime, int hitCount)
            {
                Ticks.Add(new TickRecord { CastId = castInstanceId, Index = hitIndex, RoundTime = roundServerTime, HitCount = hitCount });
            }

            public void OnAreaEnded(int castInstanceId, EBossAreaEnd end, double endServerTime)
            {
                Ended.Add(castInstanceId);
                LastEnd   = end;
                LastEndAt = endServerTime;
            }
        }

        // 한 보스를 흉내 낸다, 장판 관리자는 여러 시전이 공유한다
        private sealed class World
        {
            public FakeCombatTarget Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Vector3.zero, BodyRadius = 2f };
            public AreaQuery        Query    { get; } = new AreaQuery();
            public AreaResolver     Resolver { get; } = new AreaResolver();
            public AreaListener     Listener { get; } = new AreaListener();
            public BossDamageGate   Gate     { get; }
            public BossAreaManager  Manager  { get; }
            public PatternDefinition Definition { get; } = MakeP05();
            public List<Cast>       Casts    { get; } = new List<Cast>();

            public World()
            {
                Gate    = new BossDamageGate(Resolver);
                Gate.Open();
                Manager = new BossAreaManager(Query, Listener);
            }

            public FakeCombatTarget Add(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position, IsSummon = isSummon };
                Query.Targets.Add(target);
                return target;
            }

            // 시전 하나를 시작한다, 타깃 번호로 예고 시작 순간의 중심이 정해진다
            public Cast StartCast(int targetId, double serverTime)
            {
                Cast cast = new Cast(this, targetId);
                Casts.Add(cast);
                cast.Start(serverTime);
                return cast;
            }

            public void TickAt(double time)
            {
                Resolver.Time = time;
                for (int i = 0; i < Casts.Count; ++i)
                {
                    Casts[i].Fsm.Tick(time);
                }

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

            public int Hits(int targetId)
            {
                int count = 0;
                for (int i = 0; i < Resolver.Requests.Count; ++i)
                {
                    if (Resolver.Requests[i].HitTargetId == targetId)
                    {
                        ++count;
                    }
                }

                return count;
            }
        }

        private sealed class Cast
        {
            public BossAttackFsm     Fsm     { get; } = new BossAttackFsm();
            public BossAreaPattern   Pattern { get; }
            public BossPatternContext Context { get; private set; }

            private readonly World _world;
            private readonly int   _targetId;

            public Cast(World world, int targetId)
            {
                _world    = world;
                _targetId = targetId;
                Pattern   = new BossAreaPattern(TEST_HREF, world.Manager, world.Listener);
            }

            public void Start(double serverTime)
            {
                Context = new BossPatternContext(_world.Definition, _world.Gate.IssueInstanceId(), _targetId, _world.Self, _world.Query, _world.Gate);
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(_world.Definition, Pattern, in context, serverTime));
            }
        }

        private static double ActivateOf(double start)
        {
            return start + (double)TELEGRAPH;
        }

        /****************************************
        *         Position Fixed At Telegraph
        ****************************************/

        // 예고 시작 순간 타깃 위치를 고정하고 이후 타깃이 움직이거나 죽어도 장판 중심은 바뀌지 않는다
        [Test]
        public void Center_FixedAtTelegraphStart_TargetMovingOrDyingDoesNotMoveIt()
        {
            World world = new World();
            FakeCombatTarget tracked = world.Add(10, AreaCenter);
            FakeCombatTarget bystander = world.Add(11, AreaCenter + new Vector3(1f, 0f, 0f));
            Cast cast = world.StartCast(10, 0.0);

            Assert.AreEqual(AreaCenter, cast.Pattern.Center);
            Assert.AreEqual(AreaCenter, world.Listener.TelegraphCenter);
            Assert.AreEqual(RADIUS, world.Listener.TelegraphRadius);
            Assert.AreEqual(TELEGRAPH, world.Listener.TelegraphSeconds);

            world.TickRange(0.0, 0.4);
            tracked.Position = new Vector3(30f, 0f, 30f);
            world.TickRange(0.4, 0.8);
            tracked.LifeState = EParticipantLifeState.Dead;
            world.TickRange(0.8, 1.2);

            Assert.AreEqual(AreaCenter, cast.Pattern.Center);
            Assert.AreEqual(AreaCenter, world.Listener.ActivatedCenter);
            Assert.AreEqual(RADIUS, world.Listener.ActivatedRadius);
            Assert.AreEqual(1, world.Hits(11), "원래 자리에 있던 대상은 장판에 맞는다");
            Assert.AreEqual(0, world.Hits(10));
        }

        [Test]
        public void Center_TargetNotFoundAtStart_FallsBackToBossPosition()
        {
            World world = new World();
            world.Self.Position = new Vector3(2f, 0f, 2f);

            Cast cast = world.StartCast(999, 0.0);

            Assert.AreEqual(world.Self.Position, cast.Pattern.Center);
        }

        /****************************************
        *       Activation And First Strike
        ****************************************/

        // 예고가 끝나는 경계에 활성되고, 그 전에는 피해가 없으며, 활성 순간이 첫 타격이다
        [Test]
        public void Activation_AtTelegraphBoundary_FirstStrikeAtActivation_NoDamageBefore()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);
            double activate = ActivateOf(0.0);

            world.TickRange(0.0, activate - 0.02);
            Assert.AreEqual(0, world.Listener.Activated.Count);
            Assert.AreEqual(0, world.Resolver.Requests.Count);

            world.TickRange(activate - 0.02, activate + 0.03);

            Assert.AreEqual(1, world.Listener.Activated.Count);
            Assert.AreEqual(activate, world.Listener.ActivatedAt, EPSILON / 1000.0);
            Assert.AreEqual(DURATION, world.Listener.ActivatedDuration);
            Assert.AreEqual(INTERVAL, world.Listener.ActivatedInterval);
            Assert.AreEqual(1, world.Hits(10), "활성 순간 한 번만 맞는다");
            Assert.AreEqual(activate, world.Resolver.Times[0], 0.011);
            Assert.AreEqual(0, world.Listener.Ticks[0].Index);
            Assert.AreEqual(activate, world.Listener.Ticks[0].RoundTime, EPSILON / 1000.0);
        }

        // 패턴 진입(활성)과 관리자 Tick 두 곳에서 첫 타격이 겹치지 않는다
        [Test]
        public void FirstStrike_NotDuplicatedBetweenPatternEntryAndManagerTick()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);

            world.TickAt(ActivateOf(0.0));
            world.TickAt(ActivateOf(0.0));
            world.TickAt(ActivateOf(0.0) + 0.001);

            Assert.AreEqual(1, world.Hits(10));
            Assert.AreEqual(1, world.Listener.Ticks.Count);
        }

        /****************************************
        *            Six Rounds, No Tail
        ****************************************/

        [Test]
        public void Rounds_SixHitsAtZeroToFiveSeconds_NoHitAtSixSecondEnd()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);
            double activate = ActivateOf(0.0);

            world.TickRange(0.0, activate + 7.5);

            Assert.AreEqual(6, world.Hits(10));
            Assert.AreEqual(6, world.Listener.Ticks.Count);
            for (int i = 0; i < 6; ++i)
            {
                Assert.AreEqual(i, world.Listener.Ticks[i].Index);
                Assert.AreEqual(activate + i, world.Listener.Ticks[i].RoundTime, EPSILON / 1000.0);
                Assert.AreEqual(activate + i, world.Resolver.Times[i], 0.011);
            }

            Assert.AreEqual(1, world.Listener.Ended.Count);
            Assert.AreEqual(EBossAreaEnd.Expired, world.Listener.LastEnd);
            Assert.AreEqual(activate + DURATION, world.Listener.LastEndAt, EPSILON / 1000.0);
            Assert.AreEqual(0, world.Manager.ActiveCount);
        }

        [Test]
        public void Rounds_AtExactEndTime_ExpiresWithoutExtraHit_AndJustBeforeStillActive()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);
            double activate = ActivateOf(0.0);

            world.TickAt(activate);
            world.TickAt(activate + 5.0);
            world.TickAt(activate + 5.999);
            Assert.AreEqual(1, world.Manager.ActiveCount);
            Assert.AreEqual(6, world.Hits(10));

            world.TickAt(activate + 6.0);

            Assert.AreEqual(0, world.Manager.ActiveCount);
            Assert.AreEqual(6, world.Hits(10));
            Assert.AreEqual(EBossAreaEnd.Expired, world.Listener.LastEnd);
        }

        [TestCase(6f, 1f, 6)]
        [TestCase(6f, 0.7f, 9)]
        [TestCase(5.5f, 1f, 6)]
        [TestCase(6f, 2f, 3)]
        [TestCase(1f, 1f, 1)]
        [TestCase(0.5f, 1f, 1)]
        public void ComputeHitCount_IntervalsBelowDuration(float duration, float interval, int expected)
        {
            Assert.AreEqual(expected, BossAreaManager.ComputeHitCount(duration, interval));
        }

        // 서버 틱 간격이 큰 장판은 정의에서 읽은 주기를 따른다, 하드코딩이 없다
        [Test]
        public void Rounds_FollowDefinitionIntervalAndDuration()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            BossAreaSpec spec = new BossAreaSpec(5, 1, P05_ID, AreaCenter, RADIUS, 2f, 0.5f, 10f, EDamageFlags.Unblockable, 10.0, world.Gate);
            Assert.IsTrue(world.Manager.TryReserve(5, P05_ID, MAX_AREAS));
            Assert.IsTrue(world.Manager.TryActivate(in spec));

            world.TickRange(10.0, 13.0);

            Assert.AreEqual(4, world.Hits(10));
        }

        /****************************************
        *         Re-query Every Round
        ****************************************/

        [Test]
        public void Rounds_RequeryEachRound_EnteringAndLeavingChangesWhoIsHit()
        {
            World world = new World();
            FakeCombatTarget walker = world.Add(10, AreaCenter + new Vector3(10f, 0f, 0f));
            world.Add(11, AreaCenter);
            world.StartCast(11, 0.0);
            double activate = ActivateOf(0.0);

            world.TickRange(0.0, activate + 1.5);
            walker.Position = AreaCenter;
            world.TickRange(activate + 1.5, activate + 3.5);
            walker.Position = AreaCenter + new Vector3(10f, 0f, 0f);
            world.TickRange(activate + 3.5, activate + 7.0);

            Assert.AreEqual(6, world.Hits(11));
            Assert.AreEqual(2, world.Hits(10), "회차 2와 3에서만 안에 있었다");
        }

        [Test]
        public void Rounds_PerTargetOncePerRound_NotOncePerArea_ActionIdPerRoundCastIdShared()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.Add(11, AreaCenter + new Vector3(0.5f, 0f, 0f));
            Cast cast = world.StartCast(10, 0.0);

            world.TickRange(0.0, ActivateOf(0.0) + 7.0);

            Assert.AreEqual(12, world.Resolver.Requests.Count);
            HashSet<int> actionIds = new HashSet<int>();
            for (int i = 0; i < world.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = world.Resolver.Requests[i];
                Assert.AreEqual(cast.Context.CastInstanceId, request.CastInstanceId);
                Assert.AreNotEqual(request.CastInstanceId, request.ActionInstanceId);
                actionIds.Add(request.ActionInstanceId);
            }

            Assert.AreEqual(6, actionIds.Count, "회차마다 새 동작 번호, 같은 회차의 대상끼리는 같은 번호");
            for (int round = 0; round < 6; ++round)
            {
                Assert.AreEqual(world.Resolver.Requests[round * 2].ActionInstanceId, world.Resolver.Requests[(round * 2) + 1].ActionInstanceId);
            }
        }

        [Test]
        public void Rounds_RadiusBoundaryInclusive_BodyRadiusNotUsed_SummonsAndLivingIncluded()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            FakeCombatTarget edge = world.Add(11, AreaCenter + new Vector3(RADIUS, 0f, 0f));
            edge.BodyRadius = 5f;
            world.Add(12, AreaCenter + new Vector3(RADIUS + 0.01f, 0f, 0f));
            world.Add(13, AreaCenter + new Vector3(0f, 0f, 1f), isSummon: true);
            FakeCombatTarget dead = world.Add(14, AreaCenter);
            dead.LifeState = EParticipantLifeState.Dead;
            world.Add(15, AreaCenter + new Vector3(0f, 0f, -1f));
            world.Query.Remove(15);
            world.StartCast(10, 0.0);

            world.TickRange(0.0, ActivateOf(0.0) + 0.05);

            Assert.AreEqual(1, world.Hits(10));
            Assert.AreEqual(1, world.Hits(11), "경계 포함, 대상 BodyRadius는 쓰지 않는다");
            Assert.AreEqual(0, world.Hits(12));
            Assert.AreEqual(1, world.Hits(13), "소환물도 대상이다");
            Assert.AreEqual(0, world.Hits(14));
            Assert.AreEqual(0, world.Hits(15));
        }

        [Test]
        public void Rounds_DeathBetweenRounds_StopsHitting_AndEmptyRoundsStillReported()
        {
            World world = new World();
            FakeCombatTarget victim = world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);
            double activate = ActivateOf(0.0);

            world.TickRange(0.0, activate + 2.5);
            victim.LifeState = EParticipantLifeState.Dead;
            world.TickRange(activate + 2.5, activate + 7.0);

            Assert.AreEqual(3, world.Hits(10));
            Assert.AreEqual(6, world.Listener.Ticks.Count);
            Assert.AreEqual(0, world.Listener.Ticks[5].HitCount);
        }

        // 무적 등으로 막혀도 각 회차는 새로 요청한다, 결과는 기존 피해 판정기 규칙에 맡긴다
        [Test]
        public void Rounds_InvulnerableResult_DoesNotStopLaterRounds()
        {
            World world = new World();
            world.Resolver.Result = DamageResult.Ignored(EDamageOutcome.Invulnerable);
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);

            world.TickRange(0.0, ActivateOf(0.0) + 7.0);

            Assert.AreEqual(6, world.Hits(10));
        }

        [Test]
        public void Damage_UsesDefinitionRawDamage_AndIsUnblockable()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);

            world.TickRange(0.0, ActivateOf(0.0) + 0.05);

            DamageRequest request = world.Resolver.Requests[0];
            Assert.AreEqual(DAMAGE_REF * TEST_HREF, request.RawDamage, 0.001f);
            Assert.IsTrue(request.IsUnblockable);
            Assert.AreEqual(world.Self.CombatId, request.AttackerId);
            Assert.AreEqual(P05_ID, request.SourceDefinitionId);
            Assert.AreEqual(AreaCenter, request.SourcePosition);
        }

        /****************************************
        *       Concurrent Cap And Slot Return
        ****************************************/

        // 예약 포함 개수이고 활성으로 바뀌어도 이중 집계하지 않는다
        [Test]
        public void Slots_ReservationCountedFromTelegraph_NotDoubleCountedAtActivation_ReturnedAtExpiry()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);

            Assert.AreEqual(1, world.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(1, world.Manager.ReservedCount);
            Assert.AreEqual(0, world.Manager.ActiveCount);

            world.TickAt(ActivateOf(0.0));
            Assert.AreEqual(1, world.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(0, world.Manager.ReservedCount);
            Assert.AreEqual(1, world.Manager.ActiveCount);

            world.TickRange(ActivateOf(0.0), ActivateOf(0.0) + 6.2);
            Assert.AreEqual(0, world.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(0, world.Manager.ActiveCount);
            Assert.AreEqual(0, world.Manager.ReservedCount);
        }

        [Test]
        public void Slots_FourthCastIsRejected_ExistingAreasKeptAndNothingInstalled()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            Cast first  = world.StartCast(10, 0.0);
            Cast second = world.StartCast(10, 0.1);
            Cast third  = world.StartCast(10, 0.2);
            Assert.AreEqual(3, world.Manager.ActiveAreaCount(P05_ID));

            Cast fourth = world.StartCast(10, 0.3);

            Assert.IsFalse(fourth.Pattern.IsReserved);
            Assert.AreEqual(3, world.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(3, world.Listener.TelegraphStarts, "예약 실패한 시전은 예고를 시작하지 않는다");

            world.TickRange(0.3, 3.0);
            Assert.AreEqual(3, world.Listener.Activated.Count);
            Assert.IsFalse(fourth.Pattern.IsActivated);
            Assert.AreEqual(3, world.Manager.ActiveCount);
        }

        [Test]
        public void Slots_CancelBeforeActivation_ReleasesReservationOnceAndReportsTelegraphCancel()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            Cast cast = world.StartCast(10, 0.0);
            world.TickRange(0.0, 0.5);

            cast.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            world.Manager.ReleaseReservation(cast.Context.CastInstanceId);
            world.Manager.Cancel(EBossInterruptReason.PhaseTransition, CombatIds.NONE);
            world.TickAt(3.0);

            Assert.AreEqual(0, world.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(0, world.Manager.ReservedCount);
            Assert.AreEqual(0, world.Manager.ActiveCount);
            Assert.AreEqual(1, world.Listener.TelegraphCancels.Count);
            Assert.AreEqual(0, world.Listener.Activated.Count);
            Assert.AreEqual(0, world.Resolver.Requests.Count);
        }

        [Test]
        public void Slots_AfterReleaseFreedSlotCanBeReserved_AndCountsNeverGoNegative()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            Cast a = world.StartCast(10, 0.0);
            world.StartCast(10, 0.1);
            world.StartCast(10, 0.2);
            Assert.IsFalse(world.Manager.TryReserve(777, P05_ID, MAX_AREAS));

            a.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            world.Manager.ReleaseReservation(a.Context.CastInstanceId);
            world.Manager.ReleaseReservation(a.Context.CastInstanceId);

            Assert.AreEqual(2, world.Manager.ActiveAreaCount(P05_ID));
            Assert.IsTrue(world.Manager.TryReserve(777, P05_ID, MAX_AREAS));
            Assert.IsFalse(world.Manager.TryReserve(777, P05_ID, MAX_AREAS), "같은 번호의 이중 예약은 거절한다");
            Assert.AreEqual(3, world.Manager.ActiveAreaCount(P05_ID));
        }

        [Test]
        public void Manager_ActivateWithoutReservationOrTwice_IsRejected()
        {
            World world = new World();
            BossAreaSpec spec = new BossAreaSpec(5, 1, P05_ID, AreaCenter, RADIUS, DURATION, INTERVAL, 1f, EDamageFlags.Unblockable, 1.0, world.Gate);

            Assert.IsFalse(world.Manager.TryActivate(in spec));
            Assert.IsTrue(world.Manager.TryReserve(5, P05_ID, MAX_AREAS));
            Assert.IsTrue(world.Manager.TryActivate(in spec));
            Assert.IsFalse(world.Manager.TryActivate(in spec));
            Assert.AreEqual(1, world.Manager.ActiveAreaCount(P05_ID));
        }

        [Test]
        public void Manager_InvalidSpecFreesTheReservation()
        {
            World world = new World();
            BossAreaSpec bad = new BossAreaSpec(5, 1, P05_ID, AreaCenter, 0f, DURATION, INTERVAL, 1f, EDamageFlags.Unblockable, 1.0, world.Gate);
            Assert.IsTrue(world.Manager.TryReserve(5, P05_ID, MAX_AREAS));

            Assert.IsFalse(world.Manager.TryActivate(in bad));

            Assert.AreEqual(0, world.Manager.ActiveAreaCount(P05_ID));
        }

        /****************************************
        *       Pattern End Keeps Area
        ****************************************/

        [Test]
        public void Finish_AfterRecovery_AndNextCast_KeepActiveArea()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            Cast first = world.StartCast(10, 0.0);
            double activate = ActivateOf(0.0);

            world.TickRange(0.0, activate + RECOVERY + 0.2);
            Assert.IsTrue(first.Pattern.IsFinished);
            Assert.AreEqual(1, world.Manager.ActiveCount);

            world.StartCast(10, activate + 1.0);
            world.TickRange(activate + RECOVERY + 0.2, activate + 3.0);

            Assert.AreEqual(2, world.Manager.ActiveCount);
            Assert.AreEqual(0, world.Listener.Ended.Count);
            Assert.GreaterOrEqual(world.Hits(10), 3);
        }

        [Test]
        public void ManagerCancel_DirectControlReason_NeverRemovesActiveArea()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            Cast cast = world.StartCast(10, 0.0);
            world.TickRange(0.0, ActivateOf(0.0) + 0.5);

            world.Manager.Cancel(EBossInterruptReason.DirectControl, cast.Context.CastInstanceId);
            world.Manager.Cancel(EBossInterruptReason.DirectControl, CombatIds.NONE);

            Assert.AreEqual(1, world.Manager.ActiveCount);
            Assert.AreEqual(0, world.Listener.Ended.Count);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        public void ManagerCancel_StopReasons_RemoveReservationsAndActiveAreasAtOnce(EBossInterruptReason reason)
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);
            world.StartCast(10, 0.1);
            double activate = ActivateOf(0.0);
            world.TickRange(0.0, activate + 0.05);
            Assert.AreEqual(1, world.Manager.ActiveCount);
            Assert.AreEqual(1, world.Manager.ReservedCount);
            int requests = world.Resolver.Requests.Count;

            world.Manager.Cancel(reason, CombatIds.NONE);
            world.TickRange(activate + 0.05, activate + 8.0);

            Assert.AreEqual(0, world.Manager.ActiveCount);
            Assert.AreEqual(0, world.Manager.ReservedCount);
            Assert.AreEqual(0, world.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(requests, world.Resolver.Requests.Count, "정리 뒤 추가 피해가 없다");
            Assert.AreEqual(EBossAreaEnd.Cancelled, world.Listener.LastEnd);
        }

        /****************************************
        *           Coarse Tick Policy
        ****************************************/

        // 종료 시각을 건너뛴 한 번의 큰 틱, 종료 전에 도래한 회차를 모두 처리한 뒤 종료하고 종료 뒤 회차는 없다
        [Test]
        public void LargeTick_JumpOverEnd_ProcessesAllSixDueRoundsThenExpiresWithoutExtraHit()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);

            world.TickAt(100.0);

            Assert.AreEqual(1, world.Listener.Activated.Count);
            Assert.AreEqual(6, world.Hits(10));
            Assert.AreEqual(EBossAreaEnd.Expired, world.Listener.LastEnd);
            Assert.AreEqual(ActivateOf(0.0) + DURATION, world.Listener.LastEndAt, EPSILON / 1000.0);
            Assert.AreEqual(0, world.Manager.ActiveCount);
            for (int i = 0; i < 6; ++i)
            {
                Assert.AreEqual(i, world.Listener.Ticks[i].Index);
            }
        }

        // 놓친 회차는 건너뛰지 않고 틱 시각의 현재 위치로 처리한다. 그 시각의 위치를 복원한다고 주장하지 않는다
        [Test]
        public void LargeTick_MissedRounds_AreProcessedWithCurrentPositions_NotSkipped()
        {
            World world = new World();
            FakeCombatTarget late = world.Add(10, AreaCenter + new Vector3(20f, 0f, 0f));
            world.Add(11, AreaCenter);
            world.StartCast(11, 0.0);
            double activate = ActivateOf(0.0);
            world.TickRange(0.0, activate - 0.01);
            late.Position = AreaCenter;

            world.TickAt(activate + 2.5);

            Assert.AreEqual(3, world.Hits(10), "회차 0, 1, 2를 현재 위치로 처리한다");
            Assert.AreEqual(3, world.Hits(11));
            Assert.AreEqual(3, world.Listener.Ticks.Count);
            HashSet<int> actionIds = new HashSet<int>();
            for (int i = 0; i < world.Resolver.Requests.Count; ++i)
            {
                actionIds.Add(world.Resolver.Requests[i].ActionInstanceId);
            }

            Assert.AreEqual(3, actionIds.Count);
        }

        [Test]
        public void LargeTick_AllStagesInOneTick_ActivatesOnceAndMatchesFineTicks()
        {
            World fine = new World();
            World coarse = new World();
            foreach (World world in new[] { fine, coarse })
            {
                world.Add(10, AreaCenter);
                world.StartCast(10, 0.0);
            }

            fine.TickRange(0.0, 8.0, 0.005f);
            coarse.TickAt(50.0);

            Assert.AreEqual(1, coarse.Listener.Activated.Count);
            Assert.AreEqual(fine.Hits(10), coarse.Hits(10));
            Assert.AreEqual(6, coarse.Hits(10));
            Assert.AreEqual(0, coarse.Manager.ActiveCount);
        }

        // 취소가 항상 우선한다, 도래했지만 처리하지 못한 회차는 버리고 추가 피해를 내지 않는다
        [Test]
        public void CancelPriority_DueButUnprocessedRoundsAreDropped()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);
            double activate = ActivateOf(0.0);
            world.TickRange(0.0, activate + 0.5);
            Assert.AreEqual(1, world.Hits(10));

            world.Manager.Cancel(EBossInterruptReason.Death, CombatIds.NONE);
            world.TickAt(activate + 4.0);

            Assert.AreEqual(1, world.Hits(10));
            Assert.AreEqual(EBossAreaEnd.Cancelled, world.Listener.LastEnd);
        }

        [Test]
        public void ClosedGate_NoDamageButAreaStillExpiresOnTime()
        {
            World world = new World();
            world.Add(10, AreaCenter);
            world.StartCast(10, 0.0);
            world.TickRange(0.0, ActivateOf(0.0) + 0.05);
            int before = world.Resolver.Requests.Count;

            world.Gate.CloseForGood();
            world.TickRange(ActivateOf(0.0) + 0.05, ActivateOf(0.0) + 7.0);

            Assert.AreEqual(before, world.Resolver.Requests.Count);
            Assert.AreEqual(0, world.Manager.ActiveCount);
        }

        /****************************************
        *      Composite With Projectiles
        ****************************************/

        private sealed class ProjectileProbeListener : IBossProjectileListener
        {
            public int Ended;
            public EBossProjectileEnd Kind;

            public void OnProjectileLaunched(int castInstanceId, Vector3 origin, Vector3 direction, float speed, float width,
                float pathLength, double launchServerTime)
            {
            }

            public void OnProjectileEnded(int castInstanceId, EBossProjectileEnd end, Vector3 position, int hitTargetId)
            {
                ++Ended;
                Kind = end;
            }
        }

        // 투사체와 장판을 한 묶음으로 넘겨도 정리와 Tick과 개수 조회가 둘 다 빠지지 않는다
        [Test]
        public void Composite_CancelTickAndCountReachBothManagers_ProjectileRegressionKept()
        {
            World world = new World();
            ProjectileProbeListener probe = new ProjectileProbeListener();
            BossProjectileManager projectiles = new BossProjectileManager(world.Query, null, probe);
            BossEffectComposite composite = new BossEffectComposite(projectiles, world.Manager);
            world.Add(10, AreaCenter);
            world.Add(20, new Vector3(0f, 0f, 6f));
            world.StartCast(10, 0.0);

            BossProjectileSpec spec = new BossProjectileSpec(900, 901, 1, "wolbaek_p04", Vector3.zero, Vector3.forward, 10f, 35f, 1f,
                10f, EDamageFlags.None, 1.0, world.Gate);
            Assert.IsTrue(projectiles.TryLaunch(in spec));
            Assert.AreEqual(1, composite.ActiveAreaCount(P05_ID), "장판 수는 합산하고 투사체는 0이다");

            world.Resolver.Time = 2.0;
            composite.Tick(2.0);
            Assert.AreEqual(1, world.Hits(20), "묶음의 Tick이 투사체도 진행한다");
            Assert.AreEqual(0, projectiles.ActiveCount);

            BossProjectileSpec second = new BossProjectileSpec(902, 903, 1, "wolbaek_p04", Vector3.zero, Vector3.right, 10f, 35f, 1f,
                10f, EDamageFlags.None, 5.0, world.Gate);
            Assert.IsTrue(projectiles.TryLaunch(in second));
            composite.Tick(5.1);
            composite.Cancel(EBossInterruptReason.Death, CombatIds.NONE);

            Assert.AreEqual(0, projectiles.ActiveCount);
            Assert.AreEqual(0, world.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(0, composite.ActiveAreaCount(P05_ID));
            Assert.AreEqual(EBossProjectileEnd.Cancelled, probe.Kind);
        }

        [Test]
        public void Composite_RejectsNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new BossEffectComposite(null));
            Assert.Throws<ArgumentException>(() => new BossEffectComposite(new IBossEffectLifetime[] { null }));
        }

        /****************************************
        *          Through Boss Brain
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget  Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Vector3.zero, BodyRadius = 2f };
            public FakeCombatTarget  Player   { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = AreaCenter };
            public AreaQuery         Query    { get; } = new AreaQuery();
            public AreaResolver      Resolver { get; } = new AreaResolver();
            public AreaListener      Listener { get; } = new AreaListener();
            public FakeBossVitals    Vitals   { get; } = new FakeBossVitals();
            public FakeControlSource Control  { get; } = new FakeControlSource();
            public PatternDefinition Definition { get; } = MakeP05();
            public BossAreaManager   Manager  { get; }
            public BossProjectileManager Projectiles { get; }
            public BossBrain         Brain    { get; }

            public BrainRig()
            {
                Query.Targets.Add(Player);
                Manager     = new BossAreaManager(Query, Listener);
                Projectiles = new BossProjectileManager(Query);

                FakeBossCatalog catalog = new FakeBossCatalog { Boss = BossTestFixtures.MakeBoss() };
                catalog.Patterns.Add(Definition);

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = catalog,
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Resolver,
                    Patterns = new WolbaekPatternFactory(TEST_HREF, null, null, null, null, null, Projectiles, null, Manager, Listener),
                    Effects  = new BossEffectComposite(Projectiles, Manager),
                    Control  = Control,
                    Random   = new FakeBossRandom()
                });
            }

            public void TickAt(double time)
            {
                Resolver.Time = time;
                Brain.Tick(time);
            }

            public void TickRange(double from, double to, float step = 0.05f)
            {
                int count = (int)Math.Round((to - from) / step);
                for (int i = 1; i <= count; ++i)
                {
                    TickAt(from + (i * (double)step));
                }
            }
        }

        // 장판 수가 상한이면 선택에서 P05가 후보에서 빠지고, 빈자리가 생기면 다시 선택된다
        [Test]
        public void Brain_SelectionExcludesP05WhenFull_AndReselectsWhenSlotFreed()
        {
            BrainRig rig = new BrainRig();
            Assert.IsTrue(rig.Brain.Begin(0.0));
            rig.TickAt(0.0);

            // P05만 있는 보스, 1.5초(예고 1초 + 회복 0.5초)마다 한 번씩 새로 설치한다
            rig.TickRange(0.0, 4.4);
            Assert.AreEqual(3, rig.Listener.Activated.Count);
            Assert.AreEqual(3, rig.Manager.ActiveCount);

            rig.TickRange(4.4, 6.9);
            Assert.AreEqual(3, rig.Listener.Activated.Count, "가득 찬 동안은 새 장판을 설치하지 않는다");
            Assert.AreEqual(3, rig.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(0, rig.Listener.Ended.Count);

            rig.TickRange(6.9, 9.0);
            Assert.GreaterOrEqual(rig.Listener.Ended.Count, 1);
            Assert.GreaterOrEqual(rig.Listener.TelegraphStarts, 4, "첫 장판이 끝나 슬롯이 돌아오면 다시 선택된다");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Brain_DeathPhaseTransitionCombatEnd_RemoveReservationAndActiveAreasAndBlockDamage(int kind)
        {
            BrainRig rig = new BrainRig();
            Assert.IsTrue(rig.Brain.Begin(0.0));
            rig.TickAt(0.0);
            rig.TickRange(0.0, 3.2); // 장판 둘 활성, 세 번째 예고 진행 중
            Assert.AreEqual(2, rig.Manager.ActiveCount);
            Assert.AreEqual(1, rig.Manager.ReservedCount);
            int requests = rig.Resolver.Requests.Count;

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

            rig.TickAt(3.25);
            rig.TickRange(3.25, 9.0);

            Assert.AreEqual(0, rig.Manager.ActiveCount);
            Assert.AreEqual(0, rig.Manager.ReservedCount);
            Assert.AreEqual(0, rig.Manager.ActiveAreaCount(P05_ID));
            Assert.AreEqual(requests, rig.Resolver.Requests.Count, "정리 뒤 추가 피해가 없다");
            Assert.AreEqual(EBossAreaEnd.Cancelled, rig.Listener.LastEnd);
            Assert.AreEqual(1, rig.Listener.TelegraphCancels.Count, "예고 중이던 시전은 예약을 돌려주고 알린다");
        }

        [Test]
        public void Brain_AppliedDamageStays_WhenCombatEndsAfterRounds()
        {
            BrainRig rig = new BrainRig();
            Assert.IsTrue(rig.Brain.Begin(0.0));
            rig.TickAt(0.0);
            rig.TickRange(0.0, 2.2);
            int hits = rig.Resolver.Requests.Count;
            Assert.GreaterOrEqual(hits, 2);

            rig.Brain.RequestCombatEnd();
            rig.TickAt(2.25);

            Assert.AreEqual(hits, rig.Resolver.Requests.Count);
        }

        /****************************************
        *            Factory And Data
        ****************************************/

        [Test]
        public void Factory_P05WithAreaHost_CreatesFreshInstanceEachCall_WithoutHostReturnsNull_AllSixCreatable()
        {
            World world = new World();
            WolbaekPatternFactory with = new WolbaekPatternFactory(TEST_HREF, null, null, null, null, new BossDashPatternTests_Movement(),
                new BossProjectileManager(world.Query), null, world.Manager);
            WolbaekPatternFactory without = new WolbaekPatternFactory(TEST_HREF);

            IBossPattern first  = with.Create(MakeP05());
            IBossPattern second = with.Create(MakeP05());

            Assert.IsInstanceOf<BossAreaPattern>(first);
            Assert.AreNotSame(first, second);
            Assert.IsNull(without.Create(MakeP05()));
            string[] ids = { "wolbaek_p01", "wolbaek_p02", "wolbaek_p03", "wolbaek_p04", "wolbaek_p05", "wolbaek_p06" };
            for (int i = 0; i < ids.Length; ++i)
            {
                Assert.IsNotNull(with.Create(BossTestFixtures.MakePattern(ids[i])), ids[i]);
            }
        }

        [Test]
        public void Constructor_NullHostOrTargets_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BossAreaPattern(TEST_HREF, null));
            Assert.Throws<ArgumentNullException>(() => new BossAreaManager(null));
        }

        // 서버가 읽는 에셋 값이 기획 표의 P05와 같고 가중치는 그대로이며, 남은 IsTuned 오류는 보스 기본값뿐이다
        [Test]
        public void CatalogAsset_P05_HasPlannedValues_AndRemainingTuningErrorsAreBossBasicsOnly()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsTrue(catalog.TryGetPattern(P05_ID, out PatternDefinition p05));

            Assert.AreEqual(EEnemyAttackShape.TargetArea, p05.Shape.Shape);
            Assert.AreEqual(1f, p05.TelegraphSeconds);
            Assert.AreEqual(0f, p05.TrackingSeconds);
            Assert.AreEqual(0f, p05.ActiveSeconds);
            Assert.AreEqual(0.5f, p05.RecoverySeconds);
            Assert.AreEqual(3f, p05.Shape.Range);
            Assert.AreEqual(0.08f, p05.DamageRef);
            Assert.IsFalse(p05.Guardable);
            Assert.IsFalse(p05.HasControlWindow);
            Assert.AreEqual(6f, p05.AreaDurationSeconds);
            Assert.AreEqual(1f, p05.AreaTickInterval);
            Assert.AreEqual(3, p05.MaxConcurrentAreas);
            Assert.AreEqual(6, BossAreaManager.ComputeHitCount(p05.AreaDurationSeconds, p05.AreaTickInterval));
            Assert.AreEqual(15f, p05.BaseWeight);
            Assert.AreEqual(0.8f, p05.DistanceMultipliers[0]);
            Assert.AreEqual(1.4f, p05.NearbyMultipliers[2]);

            List<string> errors = new List<string>();
            catalog.ValidateTuning("boss_wolbaek", errors);
            string all = string.Join("\n", errors);
            Assert.IsFalse(catalog.IsTuned("boss_wolbaek"));
            Assert.AreEqual(3, errors.Count, all);
            StringAssert.DoesNotContain("wolbaek_p0", all);
            StringAssert.Contains("ArenaDiameter", all);
            StringAssert.Contains("HealthRef", all);
            StringAssert.Contains("PhaseTransitionHealthRate", all);
        }

        // 팩토리 시험용 이동 창구, 이동 자체는 쓰지 않는다
        private sealed class BossDashPatternTests_Movement : IBossMovement
        {
            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return maxDistance;
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                return to;
            }
        }
    }
}
