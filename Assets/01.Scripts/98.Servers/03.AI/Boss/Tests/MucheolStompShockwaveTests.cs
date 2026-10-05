// System
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
    // 무철 P04(발구르기 충격파) 실행 검증, 시작 수치는 플레이 테스트 전 임시값이라 운영 카탈로그가 아니라 이 테스트의 데이터를 쓴다
    // 경계(예고 끝, 반경)는 부동소수 오차가 판정을 뒤집지 않게 경계에서 떨어진 시각과 거리로 검증하고, 반경 경계 포함은 대상 조회 계약 한 곳에서만 본다
    public class MucheolStompShockwaveTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float TEST_HREF  = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float TOLERANCE  = 0.0001f;
        private const float TIME_MARGIN = 0.1f; // 초, 경계 전후로 두는 여유
        private const int   PROBE_ID   = 11;

        // 임시 시작 수치
        private const float RADIUS     = 5f;
        private const float WEIGHT     = 10f;
        private const float TELEGRAPH  = 1.0f;
        private const float RECOVERY   = 0.8f;
        private const float DAMAGE_REF = 0.26f;

        private static readonly Vector3 Origin = new Vector3(3f, 0f, 2f);

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP04()
        {
            return new PatternDefinition(MucheolPatternFactory.P04_ID, MucheolPatternFactory.BOSS_ID, 1, WEIGHT,
                new[] { 1f, 0f, 0f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, 0f, 0f, 0f, RECOVERY, EnemyCatalogSO.AttackShapeSpec.Circle(RADIUS), DAMAGE_REF, true);
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

        private sealed class StompQuery : ICombatTargetQuery
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

            // 계약대로 수평 거리가 반경 이하이면 경계를 포함한다
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

        private sealed class StompListener : IBossCircleTelegraphListener
        {
            public int                        StartCount;
            public Vector3                    StartCenter;
            public float                      StartRadius;
            public float                      StartSeconds;
            public int                        HitEventCount;
            public int                        LastHitCount = -1;
            public List<EBossInterruptReason> Cancels = new List<EBossInterruptReason>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds)
            {
                ++StartCount;
                StartCenter  = center;
                StartRadius  = radius;
                StartSeconds = telegraphSeconds;
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                ++HitEventCount;
                LastHitCount = hitCount;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Cancels.Add(reason);
            }
        }

        // 팩토리가 만든 패턴을 공격 FSM에 올린 최소 구성, 보스는 Origin에 서고 추적 대상은 그 앞 3m에 선다
        private sealed class Rig
        {
            public FakeCombatTarget      Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public FakeCombatTarget      Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = Origin + new Vector3(0f, 0f, 3f) };
            public StompQuery            Query    { get; } = new StompQuery();
            public FakeDamageResolver    Resolver { get; } = new FakeDamageResolver();
            public StompListener         Listener { get; } = new StompListener();
            public BossAttackFsm         Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate        Gate     { get; }
            public BossCircleBlastPattern Pattern { get; }
            public PatternDefinition     Definition { get; }
            public BossPatternContext    Context  { get; }

            public Rig()
            {
                Query.Players.Add(Tracked);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = MakeP04();
                Pattern    = (BossCircleBlastPattern)new MucheolPatternFactory(TEST_HREF, null, null, null, Listener).Create(Definition);
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
        *               Factory
        ****************************************/

        [Test]
        public void Create_P04_ReturnsCirclePatternWithOrWithoutMovement()
        {
            PatternDefinition definition = MakeP04();
            var withoutMovement = new MucheolPatternFactory(TEST_HREF);
            var withMovement    = new MucheolPatternFactory(TEST_HREF, null, new NoMovement(), null, null);

            Assert.IsTrue(withoutMovement.CanCreate(definition));
            Assert.IsInstanceOf<BossCircleBlastPattern>(withoutMovement.Create(definition));
            Assert.IsTrue(withMovement.CanCreate(definition));
            Assert.IsInstanceOf<BossCircleBlastPattern>(withMovement.Create(definition));
        }

        // P04 추가 뒤에도 P01~P03의 생성 조건과 종류는 그대로다
        [Test]
        public void Create_OtherPatterns_KeepTheirTypesAndConditions()
        {
            var factory = new MucheolPatternFactory(TEST_HREF);
            var withMovement = new MucheolPatternFactory(TEST_HREF, null, new NoMovement(), null, null);
            PatternDefinition p01 = new PatternDefinition(MucheolPatternFactory.P01_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);
            PatternDefinition p02 = new PatternDefinition(MucheolPatternFactory.P02_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);
            PatternDefinition p03 = new PatternDefinition(MucheolPatternFactory.P03_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Dash, true);

            Assert.IsInstanceOf<BossFanBlastPattern>(factory.Create(p01));
            Assert.IsInstanceOf<BossFanBlastPattern>(factory.Create(p02));
            Assert.IsFalse(factory.CanCreate(p03));
            Assert.IsNull(factory.Create(p03));
            Assert.IsInstanceOf<BossDashPattern>(withMovement.Create(p03));
        }

        private sealed class NoMovement : IBossMovement
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

        /****************************************
        *            Shockwave Behaviour
        ****************************************/

        [Test]
        public void Begin_ReportsBossCenterRadiusAndTelegraphSeconds()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(Origin, rig.Listener.StartCenter);
            Assert.AreEqual(RADIUS, rig.Listener.StartRadius, TOLERANCE);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds, TOLERANCE);
            Assert.AreEqual(Origin, rig.Pattern.Center);
        }

        [Test]
        public void Tick_BeforeTelegraphEnds_AppliesNoDamage()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(TELEGRAPH * 0.5f);
            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Fsm.State);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
        }

        [Test]
        public void Tick_AfterTelegraphEnd_HitsTargetOnceWithExpectedRequest()
        {
            Rig rig = new Rig();
            rig.Start(0.0);

            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);
            rig.Fsm.Tick(TELEGRAPH + TIME_MARGIN);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            DamageRequest request = rig.Resolver.Requests[0];
            Assert.AreEqual(rig.Self.CombatId, request.AttackerId);
            Assert.AreEqual(rig.Tracked.CombatId, request.HitTargetId);
            Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
            Assert.AreEqual(MucheolPatternFactory.P04_ID, request.SourceDefinitionId);
            Assert.AreEqual(DAMAGE_REF * TEST_HREF, request.RawDamage, 0.001f);
            Assert.AreEqual(EDamageFlags.None, request.Flags);
            Assert.AreEqual(Origin, request.SourcePosition);
            Assert.IsTrue(request.HasValidIds);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
            Assert.AreEqual(1, rig.Listener.LastHitCount);
        }

        // 방향이 없는 원형이라 보스 뒤쪽도 반경 안이면 맞는다
        [Test]
        public void Tick_TargetsInsideAndOutsideRadius_HitsOnlyInside()
        {
            Rig rig = new Rig();
            FakeCombatTarget justInside  = rig.AddPlayer(PROBE_ID, Origin + new Vector3(RADIUS - 0.1f, 0f, 0f));
            FakeCombatTarget justOutside = rig.AddPlayer(PROBE_ID + 1, Origin + new Vector3(RADIUS + 0.1f, 0f, 0f));
            FakeCombatTarget behind      = rig.AddPlayer(PROBE_ID + 2, Origin + new Vector3(0f, 0f, -3f));
            FakeCombatTarget diagonalOut = rig.AddPlayer(PROBE_ID + 3, Origin + new Vector3(4f, 0f, 4f));
            rig.Start(0.0);

            rig.Fsm.Tick(TELEGRAPH + TIME_MARGIN);

            Assert.AreEqual(1, CountHits(rig.Resolver, justInside.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, justOutside.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, behind.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, diagonalOut.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
        }

        // 정확히 반경 위의 대상은 조회 계약(경계 포함)에 따라 맞는다. 패턴이 따로 경계를 판정하지 않는다
        [Test]
        public void Tick_TargetExactlyOnRadius_FollowsQueryBoundaryContract()
        {
            Rig rig = new Rig();
            FakeCombatTarget onEdge = rig.AddPlayer(PROBE_ID, Origin + new Vector3(RADIUS, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(TELEGRAPH + TIME_MARGIN);

            Assert.AreEqual(1, CountHits(rig.Resolver, onEdge.CombatId));
        }

        [Test]
        public void Tick_AfterStrike_DoesNotHitSameTargetAgain()
        {
            Rig rig = new Rig();
            FakeCombatTarget bystander = rig.AddPlayer(PROBE_ID, Origin + new Vector3(2f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(TELEGRAPH + TIME_MARGIN);
            rig.Fsm.Tick(TELEGRAPH + (TIME_MARGIN * 2f));
            rig.Fsm.Tick(TELEGRAPH + (RECOVERY * 0.5f));
            rig.Fsm.Tick(TELEGRAPH + RECOVERY + TIME_MARGIN);
            rig.Fsm.Tick(TELEGRAPH + RECOVERY + 1f);

            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, bystander.CombatId));
            Assert.AreEqual(1, rig.Listener.HitEventCount);
            Assert.IsTrue(rig.Pattern.IsFinished);
        }

        // 중심은 예고 시작 위치에 고정된다. 예고 중 보스가 밀려나도 원래 자리 둘레가 맞고 새 자리 둘레는 맞지 않는다
        [Test]
        public void Tick_BossMovesDuringTelegraph_StillStrikesAroundStartPosition()
        {
            Rig rig = new Rig();
            Vector3 movedTo = Origin + new Vector3(0f, 0f, 20f);
            FakeCombatTarget nearOld = rig.AddPlayer(PROBE_ID, Origin + new Vector3(1f, 0f, 0f));
            FakeCombatTarget nearNew = rig.AddPlayer(PROBE_ID + 1, movedTo + new Vector3(1f, 0f, 0f));
            rig.Start(0.0);

            rig.Fsm.Tick(TELEGRAPH * 0.5f);
            rig.Self.Position = movedTo;
            rig.Fsm.Tick(TELEGRAPH + TIME_MARGIN);

            Assert.AreEqual(Origin, rig.Pattern.Center);
            Assert.AreEqual(1, CountHits(rig.Resolver, nearOld.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, nearNew.CombatId));
            Assert.AreEqual(Origin, rig.Resolver.Requests[0].SourcePosition);
        }

        [Test]
        public void Cancel_DuringTelegraph_AppliesNoDamageEvenAfterTelegraphTime()
        {
            Rig rig = new Rig();
            rig.Start(0.0);
            rig.Fsm.Tick(TELEGRAPH * 0.5f);

            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.Fsm.Tick(TELEGRAPH + TIME_MARGIN);
            rig.Fsm.Tick(TELEGRAPH + RECOVERY + TIME_MARGIN);

            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.Death, rig.Listener.Cancels[0]);
        }
    }
}
