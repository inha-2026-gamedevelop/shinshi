// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 월백 2페이즈 P06 원형 폭발을 에셋의 실제 정의와 실제 팩토리로 확인한다. 실행은 1페이즈 원형 공격(BossCircleBlastPattern)을 그대로 쓴다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 시각은 이진 분수(0.25 단위)로만 넘긴다
    public class BossPhaseTwoCircleBlastTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string P06_ID       = BossPatternCatalogSO.WOLBAEK_P2_P06_ID;
        private const float  HREF         = 1000f;
        private const float  TOLERANCE    = 0.001f;
        private const float  TELEGRAPH    = 1.2f;
        private const float  RECOVERY     = 0.8f;
        private const float  RADIUS       = 5f;
        private const float  DAMAGE_REF   = 0.28f;

        // 단계 경계는 정의의 float 값을 double로 넓혀 더한다. 1.2와 2.0 같은 십진수와 비교하면 한 틱 어긋난다
        private static readonly double TELEGRAPH_END = 1.2f;
        private static readonly double COMPLETE_END  = (double)1.2f + (double)0.8f;

        private static readonly Vector3 Origin = Vector3.zero;

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class RadiusQuery : ICombatTargetQuery
        {
            public List<ICombatTarget> Targets { get; } = new List<ICombatTarget>();

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
                    ICombatTarget target = Targets[i];
                    if ((target.LifeState != EParticipantLifeState.Alive) || (target.Faction != faction)
                        || (target.IsSummon && !includeSummons))
                    {
                        continue;
                    }

                    Vector3 offset = target.Position - center;
                    offset.y = 0f;
                    if (offset.magnitude <= radius)
                    {
                        results.Add(target);
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

        private sealed class RecordingListener : IBossCircleTelegraphListener
        {
            public int     StartedCount;
            public int     HitCalls;
            public int     CancelCalls;
            public int     LastHitCount;
            public Vector3 Center;
            public float   Radius;
            public float   Seconds;

            public void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds)
            {
                ++StartedCount;
                Center  = center;
                Radius  = radius;
                Seconds = telegraphSeconds;
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                ++HitCalls;
                LastHitCount = hitCount;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                ++CancelCalls;
            }
        }

        // 공격 FSM과 실제 팩토리가 만든 패턴을 가짜 대상 조회와 가짜 피해 처리기에 이어 붙인 실행 단위다
        private sealed class Run
        {
            public FakeCombatTarget   Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public RadiusQuery        Query     = new RadiusQuery();
            public FakeDamageResolver Resolver  = new FakeDamageResolver();
            public BossDamageGate     Gate;
            public RecordingListener  Listener  = new RecordingListener();
            public BossAttackFsm      Fsm       = new BossAttackFsm();
            public PatternDefinition  Definition;

            public Run(int selectedTargetId = 10)
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = LoadDefinition();

                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, circleListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);

                BossPatternContext context = new BossPatternContext(Definition, 100, selectedTargetId, Self, Query, Gate);
                Fsm.Start(Definition, pattern, in context, 0.0);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player,
                    Position = position, IsSummon = isSummon };
                Query.Targets.Add(target);
                return target;
            }
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

        private static PatternDefinition LoadDefinition()
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern(P06_ID, out PatternDefinition definition));
            return definition;
        }

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_P06_HasAgreedExecutionValuesAndKeepsSelectionData()
        {
            PatternDefinition p = LoadDefinition();

            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Pressure, p.Group);
            Assert.AreEqual(1.2f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0f, p.TrackingSeconds);
            Assert.AreEqual(0f, p.ActiveSeconds);
            Assert.AreEqual(0.8f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Circle, p.Shape.Shape);
            Assert.AreEqual(5f, p.Shape.Range, TOLERANCE);
            Assert.AreEqual(0.28f, p.DamageRef, TOLERANCE);
            Assert.IsTrue(p.Guardable);
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(0f, p.AreaDurationSeconds);
            Assert.AreEqual(8f, p.BaseWeight, TOLERANCE);
            Assert.AreEqual(9f, p.EnhancedBaseWeight, TOLERANCE);
            Assert.AreEqual(1.7f, p.DistanceMultipliers[0], TOLERANCE);
            Assert.AreEqual(0f, p.DistanceMultipliers[2], TOLERANCE, "원거리는 선택하지 않는다");
        }

        [Test]
        public void Asset_P06_FactoryCanCreateAndPatternIsReadyAgree()
        {
            PatternDefinition p = LoadDefinition();
            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF);

            Assert.IsTrue(factory.CanCreate(p));
            Assert.IsInstanceOf<BossCircleBlastPattern>(factory.Create(p));
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p));
        }

        [Test]
        public void Asset_PhaseOneCirclePattern_IsUnchanged()
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern("wolbaek_p03", out PatternDefinition p));

            Assert.AreEqual(1, p.Phase);
            Assert.AreEqual(1.2f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0.5f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(10f, p.BaseWeight, TOLERANCE);
            Assert.AreEqual(EPatternGroup.None, p.Group);
        }

        /****************************************
        *       Center, Radius, Targets, Damage
        ****************************************/

        [Test]
        public void Start_FixesCenterAtBossServerPositionAndNotifiesRadiusAndTime()
        {
            Run r = new Run();

            Assert.AreEqual(1, r.Listener.StartedCount);
            Assert.AreEqual(Origin, r.Listener.Center);
            Assert.AreEqual(RADIUS, r.Listener.Radius, TOLERANCE);
            Assert.AreEqual(TELEGRAPH, r.Listener.Seconds, TOLERANCE);
        }

        [Test]
        public void Tick_BossMovesDuringTelegraph_StillStrikesAtOriginalCenter()
        {
            Run r = new Run();
            FakeCombatTarget nearOriginal = r.AddPlayer(10, new Vector3(0f, 0f, 3f));
            FakeCombatTarget nearMoved = r.AddPlayer(11, new Vector3(20f, 0f, 0f));

            r.Self.Position = new Vector3(20f, 0f, 0f);
            r.Fsm.Tick(0.75);
            r.Fsm.Tick(TELEGRAPH_END);

            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(nearOriginal.CombatId, r.Resolver.Requests[0].HitTargetId);
            Assert.AreEqual(Origin, r.Resolver.Requests[0].SourcePosition);
            Assert.IsNotNull(nearMoved);
        }

        [Test]
        public void Tick_BeforeTelegraphEnds_DoesNotStrike()
        {
            Run r = new Run();
            r.AddPlayer(10, new Vector3(0f, 0f, 3f));

            r.Fsm.Tick(1.0);
            r.Fsm.Tick(1.19);

            Assert.AreEqual(EBossAttackState.Telegraph, r.Fsm.State);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.HitCalls);
        }

        [Test]
        public void Tick_AtTelegraphEnd_StrikesOncePerTargetWithGuardableDamage()
        {
            Run r = new Run();
            r.AddPlayer(10, new Vector3(0f, 0f, 3f));
            r.AddPlayer(11, new Vector3(2f, 0f, -2f));

            r.Fsm.Tick(TELEGRAPH_END);

            Assert.AreEqual(2, r.Resolver.Requests.Count);
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.AreEqual(DAMAGE_REF * HREF, request.RawDamage, TOLERANCE);
                Assert.IsFalse(request.IsUnblockable, "반격 가능");
                Assert.AreEqual(r.Self.CombatId, request.AttackerId);
                Assert.AreEqual(P06_ID, request.SourceDefinitionId);
            }

            Assert.AreEqual(r.Resolver.Requests[0].ActionInstanceId, r.Resolver.Requests[1].ActionInstanceId, "한 동작 번호를 공유한다");
            Assert.AreEqual(1, r.Listener.HitCalls);
            Assert.AreEqual(2, r.Listener.LastHitCount);
        }

        [Test]
        public void Tick_RadiusBoundary_IncludesEdgeAndExcludesBeyond()
        {
            Run r = new Run();
            r.AddPlayer(10, new Vector3(RADIUS, 0f, 0f));
            r.AddPlayer(11, new Vector3(RADIUS + 0.01f, 0f, 0f));

            r.Fsm.Tick(TELEGRAPH_END);

            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(10, r.Resolver.Requests[0].HitTargetId);
        }

        [Test]
        public void Tick_SummonsAreHitAndDeadPlayersAreNot()
        {
            Run r = new Run();
            r.AddPlayer(10, new Vector3(1f, 0f, 0f));
            r.AddPlayer(11, new Vector3(2f, 0f, 0f), isSummon: true);
            FakeCombatTarget dead = r.AddPlayer(12, new Vector3(3f, 0f, 0f));
            dead.LifeState = EParticipantLifeState.Dead;

            r.Fsm.Tick(TELEGRAPH_END);

            Assert.AreEqual(2, r.Resolver.Requests.Count);
            Assert.IsFalse(HasTarget(r.Resolver, 12));
            Assert.IsTrue(HasTarget(r.Resolver, 11));
        }

        [Test]
        public void Tick_SelectedTargetDiesAfterStart_CenterAndExecutionAreKept()
        {
            Run r = new Run(selectedTargetId: 10);
            FakeCombatTarget selected = r.AddPlayer(10, new Vector3(0f, 0f, 3f));
            r.AddPlayer(11, new Vector3(0f, 0f, -3f));

            r.Fsm.Tick(0.75);
            selected.LifeState = EParticipantLifeState.Dead;
            r.Fsm.Tick(TELEGRAPH_END);

            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(11, r.Resolver.Requests[0].HitTargetId);
            Assert.AreEqual(Origin, r.Resolver.Requests[0].SourcePosition);
        }

        private static bool HasTarget(FakeDamageResolver resolver, int targetId)
        {
            for (int i = 0; i < resolver.Requests.Count; ++i)
            {
                if (resolver.Requests[i].HitTargetId == targetId)
                {
                    return true;
                }
            }

            return false;
        }

        /****************************************
        *        Big Ticks, Recovery, Cancel
        ****************************************/

        [Test]
        public void Tick_HugeStep_StrikesExactlyOnceAndFinishes()
        {
            Run r = new Run();
            r.AddPlayer(10, new Vector3(0f, 0f, 3f));

            r.Fsm.Tick(100.0);
            r.Fsm.Tick(101.0);
            r.Fsm.Tick(200.0);

            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(1, r.Listener.HitCalls);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [Test]
        public void Tick_RecoveryIsEightTenthsOfASecond()
        {
            Run r = new Run();
            r.AddPlayer(10, new Vector3(0f, 0f, 3f));

            r.Fsm.Tick(TELEGRAPH_END);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State, "즉발이라 발동 단계를 바로 지난다");

            r.Fsm.Tick(1.75);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);

            r.Fsm.Tick(COMPLETE_END);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        public void Cancel_BeforeStrike_StopsHitAndFurtherDamage(EBossInterruptReason reason)
        {
            Run r = new Run();
            r.AddPlayer(10, new Vector3(0f, 0f, 3f));

            r.Fsm.Tick(0.75);
            r.Fsm.Cancel(reason);
            r.Fsm.Tick(TELEGRAPH_END);
            r.Fsm.Tick(100.0);

            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.HitCalls);
            Assert.AreEqual(1, r.Listener.CancelCalls);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [Test]
        public void Tick_GateClosedBeforeStrike_RequestsNothing()
        {
            Run r = new Run();
            r.AddPlayer(10, new Vector3(0f, 0f, 3f));

            r.Fsm.Tick(0.75);
            r.Gate.CloseForGood();
            r.Fsm.Tick(TELEGRAPH_END);

            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        /****************************************
        *      Weights, Group And 25 Percent
        ****************************************/

        private static float Weight(float hp, EPatternGroup previous, int distance, int nearby)
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossDefinition boss));
            return BossPatternSelector.ComputeSecondPhaseWeight(LoadDefinition(), boss, hp, previous, distance, 1, nearby, 3);
        }

        [Test]
        public void Weight_IsPressureGroupAndUsesFlowFromEachPreviousGroup()
        {
            Assert.AreEqual(EPatternGroup.Pressure, LoadDefinition().Group);

            // 근거리 1.7, 주변 1명 0.5, 방향 1.0, 기본값 8
            float neutral = 8f * 1.7f * 0.5f;
            Assert.AreEqual(neutral, Weight(0.6f, EPatternGroup.None, 0, 0), TOLERANCE);
            Assert.AreEqual(neutral * 1.25f, Weight(0.6f, EPatternGroup.Large, 0, 0), TOLERANCE);
            Assert.AreEqual(neutral * 0.6f, Weight(0.6f, EPatternGroup.Pressure, 0, 0), TOLERANCE);
            Assert.AreEqual(neutral * 1.1f, Weight(0.6f, EPatternGroup.Basic, 0, 0), TOLERANCE);
        }

        [Test]
        public void Weight_At25PercentBoundary_UsesEnhancedBaseAndFlow()
        {
            float enhanced = 9f * 1.7f * 0.5f;
            float normal   = 8f * 1.7f * 0.5f;

            Assert.AreEqual(enhanced * 1.35f, Weight(0.25f, EPatternGroup.Large, 0, 0), TOLERANCE);
            Assert.AreEqual(normal * 1.25f, Weight(0.2501f, EPatternGroup.Large, 0, 0), TOLERANCE);
        }

        [Test]
        public void Weight_FarDistance_IsZeroAndNearbyThreeRaisesIt()
        {
            Assert.AreEqual(0f, Weight(0.6f, EPatternGroup.None, 2, 2), TOLERANCE);
            Assert.AreEqual(8f * 1.7f * 2.4f, Weight(0.6f, EPatternGroup.None, 0, 2), TOLERANCE);
        }

        /****************************************
        *   Brain: Only P06 Ready, Reselection
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public FakeCombatTarget   Player   = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public RadiusQuery        Query    = new RadiusQuery();
            public FakeDamageResolver Damage   = new FakeDamageResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeEffectLifetime Effects  = new FakeEffectLifetime();
            public FakeBossRandom     Random   = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public BossBrain          Brain;

            // 마지막으로 새 공격이 시작된 틱의 서버 시각, 예고는 이 시각에 시작한다
            public double             AttackStart;

            public BrainRig()
            {
                Query.Targets.Add(Player);

                // 실제 카탈로그와 실제 팩토리, 1페이즈 패턴 구현이 필요 없도록 전환 전에 공격을 시작하지 않는다
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog()),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = new WolbaekPatternFactory(HREF, circleListener: Listener),
                    Effects  = Effects,
                    Control  = new FakeControlSource(),
                    Random   = Random
                });
            }

            public void TransitionAndHandoff()
            {
                Assert.IsTrue(Brain.Begin(0.0));
                Vitals.HealthRate = 0.5f;
                Brain.Tick(0.25);
                Assert.AreEqual(EBossPhaseState.AwaitingHandoff, Brain.PhaseState);
                Assert.IsTrue(Brain.CompleteHandoff());
            }

            // 새 공격이 시작될 때까지 틱을 진행한다, 시작하지 못하면 거짓이다
            public bool TickUntilAttack(double from, double to)
            {
                for (double t = from; t <= to; t += 0.25)
                {
                    Brain.Tick(t);
                    if (Brain.AttackState != EBossAttackState.Ready)
                    {
                        AttackStart = t;
                        return true;
                    }
                }

                return false;
            }
        }

        [Test]
        public void Brain_OnlyP06Ready_AfterHandoffIsActiveAndSelectsP06()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();

            Assert.AreEqual(EBossPhaseState.Active, r.Brain.PhaseState, "실행 가능한 후보가 있어 준비 대기로 가지 않는다");
            Assert.IsTrue(r.TickUntilAttack(0.5, 10.0));
            Assert.AreEqual(P06_ID, r.Brain.ActivePatternId);
            Assert.AreEqual(2, r.Brain.Phase);
            Assert.AreEqual(1, r.Listener.StartedCount);
        }

        [Test]
        public void Brain_AfterUse_RecentZeroKeepsRequeryingWithoutErrorsOrNewAttacks()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            Assert.IsTrue(r.TickUntilAttack(0.5, 10.0));

            // 예고 1.2초, 회복 0.8초가 지나 다시 선택할 수 있는 시각 이후를 길게 돈다
            for (double t = 3.0; t < 30.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, r.Listener.StartedCount, "직전 사용 배율 0이라 같은 패턴이 바로 다시 뽑히지 않는다");
            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState);
            Assert.AreEqual(EBossPhaseState.Active, r.Brain.PhaseState);
            Assert.AreEqual(EBossFlowState.Combat, r.Brain.FlowState);
        }

        [Test]
        public void Brain_FarTarget_P06WeightZeroSelectsNothingAndRaisesNoErrors()
        {
            BrainRig r = new BrainRig();
            r.Player.Position = new Vector3(0f, 0f, 20f);
            r.TransitionAndHandoff();

            Assert.IsFalse(r.TickUntilAttack(0.5, 20.0));

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(0, r.Listener.StartedCount);
            Assert.AreEqual(EBossPhaseState.Active, r.Brain.PhaseState);
        }

        [Test]
        public void Brain_HealthChangesDuringAttack_DoNotChangeDamageOrTiming()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            Assert.IsTrue(r.TickUntilAttack(0.5, 10.0));
            Assert.AreEqual(0, r.Damage.Requests.Count);

            // 25% 이하로 내려도 이미 시작한 공격의 수치와 시간은 그대로다
            r.Vitals.HealthRate = 0.2f;
            r.Brain.Tick(r.AttackStart + 1.0);
            Assert.AreEqual(0, r.Damage.Requests.Count);
            Assert.AreEqual(EBossAttackState.Telegraph, r.Brain.AttackState);

            r.Brain.Tick(r.AttackStart + TELEGRAPH_END);

            Assert.AreEqual(1, r.Damage.Requests.Count);
            Assert.AreEqual(DAMAGE_REF * HREF, r.Damage.Requests[0].RawDamage, TOLERANCE);
            Assert.AreEqual(P06_ID, r.Damage.Requests[0].SourceDefinitionId);
        }
    }
}
