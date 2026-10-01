// System
using System;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 시각은 이진 분수(0.25 단위)로만 넘겨 부동소수 오차가 경계 판정에 끼지 않게 한다
    public class BossBrainTests
    {
        /****************************************
        *               Helpers
        ****************************************/

        private static BossHarness Make(params PatternDefinition[] patterns)
        {
            return new BossHarness(BossTestFixtures.MakeBoss(), null, patterns);
        }

        // 예고 1초, 마지막 0.4초에 직접 제어로 취소할 수 있는 패턴
        private static PatternDefinition MakeControllable(string id = "p1")
        {
            return BossTestFixtures.MakePattern(id, controlPhase: EBossActionState.Telegraph,
                controlWindow: 0.4f, controlCancels: true);
        }

        /****************************************
        *            Normal Cycle
        ****************************************/

        [Test]
        public void Tick_NormalCycle_RunsTelegraphExecuteRecoverAndReturnsToReady()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0 };
            Assert.IsTrue(h.Brain.Begin(0.0));

            h.Brain.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);
            Assert.AreEqual("p1", h.Brain.ActivePatternId);
            Assert.AreEqual(0, h.Damage.Requests.Count);

            h.Brain.Tick(0.75);
            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);

            h.Brain.Tick(1.0);
            Assert.AreEqual(EBossAttackState.Execute, h.Brain.AttackState);
            Assert.AreEqual(1, h.Damage.Requests.Count);

            h.Brain.Tick(1.5);
            Assert.AreEqual(EBossAttackState.Recover, h.Brain.AttackState);

            h.Brain.Tick(1.75);
            Assert.AreEqual(EBossAttackState.Recover, h.Brain.AttackState);

            h.Brain.Tick(2.0);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.IsTrue(h.Factory.Last.IsFinished);
            Assert.IsFalse(h.Factory.Last.IsCancelled);
            Assert.AreEqual(1, h.Damage.Requests.Count);
            Assert.AreEqual(0, h.Effects.Cancels.Count);
        }

        [Test]
        public void Tick_NormalCycle_HitRequestCarriesCastAndTargetIds()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0 };
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);
            h.Brain.Tick(1.0);

            DamageRequest request = h.Damage.Requests[0];
            Assert.AreEqual(BossHarness.SELF_ID, request.AttackerId);
            Assert.AreEqual(BossHarness.PLAYER_ID, request.HitTargetId);
            Assert.AreEqual(h.Factory.Last.CastInstanceId, request.CastInstanceId);
            Assert.AreNotEqual(CombatIds.NONE, request.CastInstanceId);
            Assert.AreEqual("p1", request.SourceDefinitionId);
            Assert.IsTrue(request.HasValidIds);
        }

        [Test]
        public void Tick_SecondSelection_GetsNewCastInstanceId()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);
            int first = h.Factory.Last.CastInstanceId;
            h.Brain.Tick(2.0);
            h.Brain.Tick(2.25);

            Assert.AreEqual(2, h.Factory.CreateCount);
            Assert.AreNotEqual(first, h.Factory.Last.CastInstanceId);
        }

        /****************************************
        *        No Reselect While Busy
        ****************************************/

        [Test]
        public void Tick_WhileTelegraphExecuteRecover_DoesNotEvaluateSelection()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);
            Assert.AreEqual(1, h.Random.IntCalls);
            Assert.AreEqual(1, h.Factory.CreateCount);

            // 예고, 실행, 회복이 모두 끝나기 전까지 BT도 패턴 생성도 다시 일어나지 않는다
            for (double t = 0.25; t < 2.0; t += 0.25)
            {
                h.Brain.Tick(t);
            }

            Assert.AreEqual(1, h.Random.IntCalls);
            Assert.AreEqual(1, h.Factory.CreateCount);

            // 끝나는 틱에도 새로 고르지 않고, 다음 틱부터 고른다
            h.Brain.Tick(2.0);
            Assert.AreEqual(1, h.Random.IntCalls);
            h.Brain.Tick(2.25);
            Assert.AreEqual(2, h.Random.IntCalls);
            Assert.AreEqual(2, h.Factory.CreateCount);
        }

        /****************************************
        *       Cancel Prevents Pending Hit
        ****************************************/

        [Test]
        public void Tick_PhaseTransitionDuringExecute_DropsScheduledHitButKeepsAppliedOne()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0, 0.25 };
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);
            h.Brain.Tick(1.0);
            Assert.AreEqual(1, h.Damage.Requests.Count);

            // 두 번째 타격이 예약된 시각 직전에 체력이 전환 기준에 닿는다
            h.Vitals.HealthRate = 0.4f;
            h.Brain.Tick(1.125);

            h.Brain.Tick(1.25);
            h.Brain.Tick(1.5);
            h.Brain.Tick(2.0);

            Assert.AreEqual(1, h.Damage.Requests.Count);
            Assert.IsTrue(h.Factory.Last.IsCancelled);
            Assert.AreEqual(EBossInterruptReason.PhaseTransition, h.Factory.Last.CancelReason);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
        }

        [Test]
        public void Tick_DeathDuringExecute_DropsScheduledHit()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0, 0.25 };
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);
            h.Brain.Tick(1.0);

            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(1.125);
            h.Brain.Tick(1.25);

            Assert.AreEqual(EBossFlowState.Defeated, h.Brain.FlowState);
            Assert.AreEqual(1, h.Damage.Requests.Count);
            Assert.AreEqual(EBossInterruptReason.Death, h.Factory.Last.CancelReason);
        }

        [Test]
        public void Tick_CombatEndDuringExecute_DropsScheduledHit()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0, 0.25 };
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);
            h.Brain.Tick(1.0);

            h.Brain.RequestCombatEnd();
            h.Brain.Tick(1.125);
            h.Brain.Tick(1.25);

            Assert.AreEqual(EBossFlowState.Ended, h.Brain.FlowState);
            Assert.AreEqual(1, h.Damage.Requests.Count);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, h.Factory.Last.CancelReason);
        }

        [Test]
        public void TickAfterCancel_PatternBase_BlocksPendingHitEvenIfCalledDirectly()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0, 0.25 };
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);
            h.Brain.Tick(1.0);
            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(1.125);

            // FSM이 부르지 않아도 기반 클래스가 취소 뒤 피해 요청을 막는다
            h.Factory.Last.Tick(EBossAttackState.Execute, 5.0);

            Assert.AreEqual(1, h.Damage.Requests.Count);
        }

        [Test]
        public void TryApplyHit_AfterCancel_NeverReachesResolver()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);
            Assert.IsTrue(h.Factory.Last.TryHitNow());
            Assert.AreEqual(1, h.Damage.Requests.Count);

            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(0.25);

            // Tick 경로가 아니라 예약 콜백이 직접 불러도 취소 뒤에는 resolver까지 가지 않는다
            Assert.IsFalse(h.Factory.Last.TryHitNow());
            Assert.AreEqual(1, h.Damage.Requests.Count);
        }

        [Test]
        public void Tick_Interrupt_AlwaysOrdersLingeringEffectsCancelWithReason()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Brain.RequestCombatEnd();
            h.Brain.Tick(0.25);

            Assert.AreEqual(1, h.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, h.Effects.Cancels[0].Key);
            Assert.AreEqual(CombatIds.NONE, h.Effects.Cancels[0].Value);
        }

        [Test]
        public void Tick_InterruptWhileReady_StillOrdersLingeringEffectsCancel()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);

            // 패턴이 끝나도 남는 장판이 있으므로 진행 중인 공격이 없어도 정리를 지시한다
            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(0.0);

            Assert.AreEqual(EBossFlowState.Defeated, h.Brain.FlowState);
            Assert.AreEqual(1, h.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.Death, h.Effects.Cancels[0].Key);
        }

        /****************************************
        *        Same Tick Priority
        ****************************************/

        [Test]
        public void Tick_DeathAndPhaseTransitionSameTick_DeathWins()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Vitals.HealthRate = 0f;
            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(0.25);

            Assert.AreEqual(EBossFlowState.Defeated, h.Brain.FlowState);
            Assert.AreEqual(EBossPhaseState.Active, h.Brain.PhaseState);
            Assert.AreEqual(1, h.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.Death, h.Effects.Cancels[0].Key);
        }

        [Test]
        public void Tick_CombatEndAndPhaseTransitionSameTick_CombatEndWins()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Vitals.HealthRate = 0.4f;
            h.Brain.RequestCombatEnd();
            h.Brain.Tick(0.25);

            Assert.AreEqual(EBossFlowState.Ended, h.Brain.FlowState);
            Assert.AreEqual(EBossPhaseState.Active, h.Brain.PhaseState);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, h.Effects.Cancels[0].Key);
        }

        [Test]
        public void Tick_PhaseTransitionAndAllowedControlSameTick_PhaseTransitionWins()
        {
            BossHarness h = Make(MakeControllable());
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            // 0.75초는 예고 마지막 0.4초 안이라 제어도 허용되는 상태다
            h.Vitals.HealthRate = 0.4f;
            h.Control.Pending = true;
            h.Brain.Tick(0.75);

            Assert.AreEqual(EBossInterruptReason.PhaseTransition, h.Factory.Last.CancelReason);
            Assert.AreEqual(1, h.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.PhaseTransition, h.Effects.Cancels[0].Key);
            Assert.IsTrue(h.Control.Pending, "전환이 먼저 처리되어 제어는 소비되지 않는다");
        }

        [Test]
        public void Tick_AllowedControlWhileAttackWouldProgress_ControlRunsBeforeAttackProgress()
        {
            BossHarness h = Make(MakeControllable());
            h.Factory.HitOffsets = new[] { 0.0 };
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);
            int ticksBefore = h.Factory.Last.TickCount;

            // 0.75초는 예고 마지막 0.4초 안이다. 구간 끝 경계 처리는 기획 미정이라 경계 시각은 쓰지 않는다
            h.Control.Pending = true;
            h.Brain.Tick(0.75);

            Assert.AreEqual(ticksBefore, h.Factory.Last.TickCount, "제어가 먼저 처리되어 이번 틱의 공격 진행은 일어나지 않는다");
            Assert.AreEqual(EBossInterruptReason.DirectControl, h.Factory.Last.CancelReason);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.AreEqual(0, h.Damage.Requests.Count);
            Assert.AreEqual(EBossPhaseState.Active, h.Brain.PhaseState);
            Assert.AreEqual(1, h.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.DirectControl, h.Effects.Cancels[0].Key);
            Assert.AreEqual(h.Factory.Last.CastInstanceId, h.Effects.Cancels[0].Value);
        }

        [Test]
        public void Tick_CombatEndAndAttackProgressSameTick_CombatEndWins()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Factory.HitOffsets = new[] { 0.0 };
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Brain.RequestCombatEnd();
            h.Brain.Tick(1.0);

            Assert.AreEqual(EBossFlowState.Ended, h.Brain.FlowState);
            Assert.AreEqual(0, h.Damage.Requests.Count);
        }

        [Test]
        public void Tick_BossDeathAndPartyWipeSameTick_RecordsBothAndWipeTakesPriority()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Player.LifeState = EParticipantLifeState.Dead;
            h.Vitals.IsHealthZero = true;
            h.Brain.RequestCombatEnd();
            h.Brain.Tick(0.25);

            // 기획의 초기 기준: 보스 사망과 전원 사망이 같은 시각이면 전멸을 우선한다
            Assert.AreEqual(EBossFlowState.Ended, h.Brain.FlowState);
            Assert.IsTrue(h.Brain.IsBossDeathRecorded);
            Assert.IsTrue(h.Brain.IsCombatEndRecorded);
            Assert.AreEqual(EParticipantLifeState.Dead, h.Player.LifeState, "플레이어 사망 상태는 바뀌지 않는다");

            // 정리는 한 번만 지시한다
            Assert.AreEqual(1, h.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.Death, h.Effects.Cancels[0].Key);
            h.Brain.Tick(0.5);
            Assert.AreEqual(1, h.Effects.Cancels.Count);
        }

        [Test]
        public void Tick_PartyWipeWithoutBossDeath_RecordsOnlyCombatEnd()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Brain.RequestCombatEnd();
            h.Brain.Tick(0.25);

            Assert.AreEqual(EBossFlowState.Ended, h.Brain.FlowState);
            Assert.IsFalse(h.Brain.IsBossDeathRecorded);
            Assert.IsTrue(h.Brain.IsCombatEndRecorded);
        }

        [Test]
        public void Tick_BossDeathWithoutPartyWipe_RecordsOnlyDeath()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(0.25);

            Assert.AreEqual(EBossFlowState.Defeated, h.Brain.FlowState);
            Assert.IsTrue(h.Brain.IsBossDeathRecorded);
            Assert.IsFalse(h.Brain.IsCombatEndRecorded);
        }

        /****************************************
        *        Damage Gate After Stop
        ****************************************/

        [Test]
        public void TryApply_AfterBossDeath_GateBlocksEvenWhoeverStillHoldsIt()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            // 지속 장판이나 투사체가 패턴 밖에서 게이트를 잡고 있는 상황이다
            BossDamageGate held = h.Factory.Last.CapturedGate;
            Assert.IsTrue(held.IsOpen);

            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(0.25);

            DamageRequest request = new DamageRequest(BossHarness.SELF_ID, BossHarness.PLAYER_ID, 1, 1, 1f,
                EDamageFlags.None, Vector3.zero, "p1");
            Assert.IsFalse(held.IsOpen);
            Assert.IsFalse(held.TryApply(in request, out DamageResult _));
            Assert.AreEqual(0, h.Damage.Requests.Count);
        }

        [Test]
        public void TryApply_AfterCombatEnd_GateBlocksToo()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);
            BossDamageGate held = h.Factory.Last.CapturedGate;

            h.Brain.RequestCombatEnd();
            h.Brain.Tick(0.25);

            DamageRequest request = new DamageRequest(BossHarness.SELF_ID, BossHarness.PLAYER_ID, 1, 1, 1f,
                EDamageFlags.None, Vector3.zero, "p1");
            Assert.IsFalse(held.TryApply(in request, out DamageResult _));
            Assert.AreEqual(0, h.Damage.Requests.Count);
        }

        [Test]
        public void TryApply_AfterPhaseTransition_GateStaysOpenForNextPhase()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);
            BossDamageGate held = h.Factory.Last.CapturedGate;

            h.Vitals.HealthRate = 0.4f;
            h.Brain.Tick(0.25);

            Assert.IsTrue(held.IsOpen, "전환은 전투의 끝이 아니라서 게이트를 닫지 않는다");
        }

        /****************************************
        *           Appearance Wait
        ****************************************/

        [Test]
        public void Tick_DuringAppearance_NeitherSelectsTargetsNorAttacks()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            Assert.IsTrue(h.Brain.BeginAppearance(0.0, 3f));
            Assert.AreEqual(EBossFlowState.Appearing, h.Brain.FlowState);

            for (double t = 0.0; t < 3.0; t += 0.25)
            {
                h.Brain.Tick(t);
            }

            Assert.AreEqual(EBossFlowState.Appearing, h.Brain.FlowState);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.AreEqual(0, h.Random.IntCalls, "등장 대기 중에는 타깃을 고르지 않는다");
            Assert.AreEqual(0, h.Factory.CreateCount);
            Assert.AreEqual(0, h.Damage.Requests.Count);
        }

        [Test]
        public void Tick_AppearanceEnds_StartsCombatAndSelectsFirstPatternSameTick()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.BeginAppearance(0.0, 3f);

            h.Brain.Tick(2.75);
            Assert.AreEqual(EBossFlowState.Appearing, h.Brain.FlowState);

            h.Brain.Tick(3.0);
            Assert.AreEqual(EBossFlowState.Combat, h.Brain.FlowState);
            Assert.AreEqual("p1", h.Brain.ActivePatternId);
            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);
        }

        [Test]
        public void Tick_AppearanceAndOpeningDelay_ApplyInSequenceNotOverlapped()
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(openingDelay: 1f), null, BossTestFixtures.MakePattern("p1"));
            h.Brain.BeginAppearance(0.0, 3f);

            // 등장 대기가 끝나는 3초가 전투 시작이고, 첫 선택은 거기서 OpeningDelaySeconds 뒤인 4초다
            h.Brain.Tick(3.0);
            Assert.AreEqual(EBossFlowState.Combat, h.Brain.FlowState);
            Assert.AreEqual(0, h.Factory.CreateCount);

            h.Brain.Tick(3.75);
            Assert.AreEqual(0, h.Factory.CreateCount);

            h.Brain.Tick(4.0);
            Assert.AreEqual(1, h.Factory.CreateCount);
        }

        [Test]
        public void Tick_LateTickAfterAppearance_CountsOpeningDelayFromScheduledEnd()
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(openingDelay: 1f), null, BossTestFixtures.MakePattern("p1"));
            h.Brain.BeginAppearance(0.0, 3f);

            // 틱이 늦어도 전투 시작은 등장 대기가 끝나는 3초로 잡는다
            h.Brain.Tick(3.5);
            Assert.AreEqual(EBossFlowState.Combat, h.Brain.FlowState);
            Assert.AreEqual(0, h.Factory.CreateCount);

            h.Brain.Tick(4.0);
            Assert.AreEqual(1, h.Factory.CreateCount);
        }

        [Test]
        public void BeginAppearance_ZeroOrNegativeSeconds_StartsCombatImmediately()
        {
            BossHarness zero = Make(BossTestFixtures.MakePattern("p1"));
            Assert.IsTrue(zero.Brain.BeginAppearance(0.0, 0f));
            Assert.AreEqual(EBossFlowState.Combat, zero.Brain.FlowState);

            BossHarness negative = Make(BossTestFixtures.MakePattern("p1"));
            Assert.IsTrue(negative.Brain.BeginAppearance(0.0, -2f));
            Assert.AreEqual(EBossFlowState.Combat, negative.Brain.FlowState);
        }

        [Test]
        public void Tick_CombatEndDuringAppearance_EndsWithoutEverStartingCombat()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.BeginAppearance(0.0, 3f);

            h.Brain.RequestCombatEnd();
            h.Brain.Tick(1.0);

            Assert.AreEqual(EBossFlowState.Ended, h.Brain.FlowState);
            Assert.IsTrue(h.Brain.IsCombatEndRecorded);
            Assert.AreEqual(0, h.Factory.CreateCount);
            Assert.AreEqual(0, h.Effects.Cancels.Count, "시작하지 않은 전투에는 정리할 지속물이 없다");
        }

        [Test]
        public void Tick_BossDeathDuringAppearance_ReportsDefeatedWithoutCombat()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.BeginAppearance(0.0, 3f);

            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(1.0);

            Assert.AreEqual(EBossFlowState.Defeated, h.Brain.FlowState);
            Assert.AreEqual(0, h.Factory.CreateCount);
        }

        [Test]
        public void Tick_NoApiTakesClientCutsceneSignal_StartIsDrivenByServerTimeOnly()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.BeginAppearance(0.0, 3f);

            // 서버 시각이 도달하기 전에는 어떤 호출로도 전투가 시작되지 않는다
            h.Brain.Tick(0.0);
            h.Brain.Tick(1.0);
            h.Brain.Tick(2.999);

            Assert.AreEqual(EBossFlowState.Appearing, h.Brain.FlowState);
        }

        [Test]
        public void BeginAppearance_AfterBegin_IsRejected()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.BeginAppearance(0.0, 3f);

            Assert.IsFalse(h.Brain.Begin(0.0));
            Assert.IsFalse(h.Brain.BeginAppearance(0.0, 1f));
        }

        /****************************************
        *     Next Selection Reads Latest State
        ****************************************/

        [Test]
        public void Tick_NextSelectionAfterRecover_ReadsLatestSituation()
        {
            PatternDefinition near = BossTestFixtures.MakePattern("near", distanceMultipliers: new[] { 1f, 0f, 0f });
            PatternDefinition far = BossTestFixtures.MakePattern("far", distanceMultipliers: new[] { 0f, 0f, 1f });
            BossHarness h = Make(near, far);
            h.Random.Roll = 0f;
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);
            Assert.AreEqual("near", h.Brain.ActivePatternId);

            h.Brain.Tick(2.0);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);

            // 회복이 끝난 뒤 타깃이 멀어지면 다음 선택은 그 상황으로 계산된다
            h.Player.Position = new Vector3(0f, 0f, 20f);
            h.Brain.Tick(2.25);

            Assert.AreEqual("far", h.Brain.ActivePatternId);
        }

        /****************************************
        *            Direct Control
        ****************************************/

        [Test]
        public void Tick_ControlOutsideWindow_IsDroppedAndDoesNotUseUpTheOneChance()
        {
            BossHarness h = Make(MakeControllable());
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Control.Pending = true;
            h.Brain.Tick(0.25);
            Assert.IsFalse(h.Control.Pending, "허용 구간 밖의 제어는 소비되어 버려진다");
            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);
            Assert.IsFalse(h.Factory.Last.IsCancelled);

            h.Control.Pending = true;
            h.Brain.Tick(0.75);
            Assert.AreEqual(EBossInterruptReason.DirectControl, h.Factory.Last.CancelReason);
        }

        [Test]
        public void Tick_AppliedControl_RaisesNotificationWithCancelFlagAndCappedSeconds()
        {
            BossHarness h = Make(MakeControllable());
            BossDirectControlApplied? received = null;
            h.Brain.DirectControlApplied += applied => received = applied;
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            // 보스 정의의 직접 제어 최대 시간은 1초라 5초 요청은 1초로 잘린다
            h.Control.Next = new BossDirectControl("test_bind", 5f);
            h.Control.Pending = true;
            h.Brain.Tick(0.75);

            Assert.IsTrue(received.HasValue);
            Assert.IsTrue(received.Value.CancelledPattern);
            Assert.AreEqual("p1", received.Value.PatternId);
            Assert.AreEqual("test_bind", received.Value.Control.SourceId);
            Assert.AreEqual(1f, received.Value.AppliedSeconds, 0.0001f);
        }

        [Test]
        public void Tick_AppliedControlOnNonCancelingPattern_StillNotifiesWithoutCancel()
        {
            // 속박 같은 제어가 보스를 어떻게 움직일지는 협의 전이라, 알림만 나가고 흐름은 그대로다
            PatternDefinition pattern = BossTestFixtures.MakePattern("p1",
                controlPhase: EBossActionState.Telegraph, controlWindow: 0.4f, controlCancels: false);
            BossHarness h = Make(pattern);
            BossDirectControlApplied? received = null;
            h.Brain.DirectControlApplied += applied => received = applied;
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Control.Pending = true;
            h.Brain.Tick(0.75);

            Assert.IsTrue(received.HasValue);
            Assert.IsFalse(received.Value.CancelledPattern);
            Assert.AreEqual(0.5f, received.Value.AppliedSeconds, 0.0001f);
            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);
            Assert.IsFalse(h.Factory.Last.IsCancelled);
        }

        [Test]
        public void Tick_ControlWithoutRequestedSeconds_AppliesZeroSeconds()
        {
            BossHarness h = Make(MakeControllable());
            BossDirectControlApplied? received = null;
            h.Brain.DirectControlApplied += applied => received = applied;
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Control.Next = new BossDirectControl(null, 0f);
            h.Control.Pending = true;
            h.Brain.Tick(0.75);

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(0f, received.Value.AppliedSeconds);
            Assert.AreEqual(string.Empty, received.Value.Control.SourceId);
        }

        [Test]
        public void Tick_DroppedControl_DoesNotNotify()
        {
            BossHarness h = Make(MakeControllable());
            int count = 0;
            h.Brain.DirectControlApplied += applied => ++count;
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            // 허용 구간 밖의 제어는 버려지고 알림도 나가지 않는다
            h.Control.Pending = true;
            h.Brain.Tick(0.25);

            Assert.AreEqual(0, count);
        }

        [Test]
        public void Tick_ControlWhileReady_IsDropped()
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(openingDelay: 1f), null, MakeControllable());
            h.Brain.Begin(0.0);

            h.Control.Pending = true;
            h.Brain.Tick(0.5);

            Assert.IsFalse(h.Control.Pending);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.AreEqual(0, h.Effects.Cancels.Count);
        }

        [Test]
        public void Tick_ControlOnPatternWithoutCancel_DoesNotCancelOrTouchEffects()
        {
            // 취소하지 않는 제어가 걸린 동안의 보스 동작은 기획 미정이라 표시만 하고 흐름은 그대로 둔다
            PatternDefinition pattern = BossTestFixtures.MakePattern("p1",
                controlPhase: EBossActionState.Telegraph, controlWindow: 0.4f, controlCancels: false);
            BossHarness h = Make(pattern);
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Control.Pending = true;
            h.Brain.Tick(0.75);

            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);
            Assert.IsFalse(h.Factory.Last.IsCancelled);
            Assert.AreEqual(0, h.Effects.Cancels.Count);
        }

        [Test]
        public void Tick_ControlDuringImmuneTime_IsIgnoredAndWorksAgainAfter()
        {
            BossHarness h = Make(MakeControllable());
            h.Brain.Begin(0.0);

            // 0.75초에 첫 제어가 성공해 2.75초까지 면역이다
            h.Brain.Tick(0.0);
            h.Control.Pending = true;
            h.Brain.Tick(0.75);
            Assert.AreEqual(EBossInterruptReason.DirectControl, h.Factory.Last.CancelReason);

            // 두 번째 패턴은 1.0초에 시작해 예고가 2.0초에 끝나고 제어 구간은 1.6초부터다, 면역 안의 제어는 무시된다
            h.Brain.Tick(1.0);
            Assert.AreEqual(2, h.Factory.CreateCount);
            h.Control.Pending = true;
            h.Brain.Tick(1.75);
            Assert.IsFalse(h.Factory.Last.IsCancelled);
            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);

            // 두 번째 패턴이 3.0초에 끝나고 세 번째가 3.25초에 시작한다, 면역이 풀린 뒤라 제어가 듣는다
            h.Brain.Tick(2.0);
            h.Brain.Tick(2.5);
            h.Brain.Tick(3.0);
            h.Brain.Tick(3.25);
            Assert.AreEqual(3, h.Factory.CreateCount);
            h.Control.Pending = true;
            h.Brain.Tick(4.0);
            Assert.AreEqual(EBossInterruptReason.DirectControl, h.Factory.Last.CancelReason);
        }

        /****************************************
        *          Phase Transition
        ****************************************/

        [Test]
        public void Tick_AfterPhaseTransition_NeverSelectsNewPatternWhileAwaitingHandoff()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Brain.Tick(0.0);

            h.Vitals.HealthRate = 0.5f;
            h.Brain.Tick(0.25);
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, h.Brain.PhaseState);

            for (double t = 0.5; t < 20.0; t += 0.25)
            {
                h.Brain.Tick(t);
            }

            Assert.AreEqual(1, h.Factory.CreateCount);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.AreEqual(EBossFlowState.Combat, h.Brain.FlowState);
        }

        [Test]
        public void Tick_PhaseRateUnset_NeverTransitions()
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(phaseRate: 0f), null, BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);

            h.Vitals.HealthRate = 0.01f;
            h.Brain.Tick(0.0);

            Assert.AreEqual(EBossPhaseState.Active, h.Brain.PhaseState);
        }

        /****************************************
        *               Death
        ****************************************/

        [Test]
        public void Tick_HealthZeroWithoutDeathConfirmation_KeepsFighting()
        {
            FakeDeathPolicy policy = new FakeDeathPolicy();
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(), policy, BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);

            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(0.0);
            Assert.AreEqual(EBossFlowState.Combat, h.Brain.FlowState);
            Assert.AreEqual(EBossAttackState.Telegraph, h.Brain.AttackState);

            policy.Confirm = true;
            h.Brain.Tick(0.25);
            Assert.AreEqual(EBossFlowState.Defeated, h.Brain.FlowState);
        }

        [Test]
        public void Tick_AfterDefeated_DoesNothing()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);
            h.Vitals.IsHealthZero = true;
            h.Brain.Tick(0.0);

            h.Brain.Tick(5.0);

            Assert.AreEqual(0, h.Factory.CreateCount);
            Assert.AreEqual(1, h.Effects.Cancels.Count);
        }

        /****************************************
        *             Selection
        ****************************************/

        [Test]
        public void Tick_WeightedRoll_PicksPatternByRatio()
        {
            BossHarness low = Make(BossTestFixtures.MakePattern("a", 10f), BossTestFixtures.MakePattern("b", 30f));
            low.Random.Roll = 0.2f;
            low.Brain.Begin(0.0);
            low.Brain.Tick(0.0);
            Assert.AreEqual("a", low.Brain.ActivePatternId);

            BossHarness high = Make(BossTestFixtures.MakePattern("a", 10f), BossTestFixtures.MakePattern("b", 30f));
            high.Random.Roll = 0.3f;
            high.Brain.Begin(0.0);
            high.Brain.Tick(0.0);
            Assert.AreEqual("b", high.Brain.ActivePatternId);
        }

        [Test]
        public void Tick_RecentUseMultiplier_LowersJustUsedPatternOnNextSelection()
        {
            float[] recent = { 0f, 0.3f, 0.7f, 1f };
            BossHarness h = Make(
                BossTestFixtures.MakePattern("a", 10f, recentMultipliers: recent),
                BossTestFixtures.MakePattern("b", 10f, recentMultipliers: recent));
            h.Random.Roll = 0f;
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);
            Assert.AreEqual("a", h.Brain.ActivePatternId);

            h.Brain.Tick(2.0);
            h.Brain.Tick(2.25);
            Assert.AreEqual("b", h.Brain.ActivePatternId, "직전에 쓴 a는 배율 0이라 roll이 0이어도 뽑히지 않는다");
        }

        [Test]
        public void Tick_AllWeightsZero_StartsNothingAndWaitsBeforeRetry()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1", 0f));
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.AreEqual(0, h.Factory.CreateCount);
            Assert.AreEqual(1, h.Random.IntCalls);

            // 재조회 대기 0.25초 동안은 평가하지 않는다
            h.Brain.Tick(0.125);
            Assert.AreEqual(1, h.Random.IntCalls);

            h.Brain.Tick(0.25);
            Assert.AreEqual(2, h.Random.IntCalls);
        }

        [Test]
        public void Tick_NoLivingPlayers_StartsNothing()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Query.Players.Clear();
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);

            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.AreEqual(0, h.Factory.CreateCount);
        }

        [Test]
        public void Tick_AreaPatternAtConcurrentLimit_IsExcludedUntilAnAreaExpires()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("area", 10f, maxAreas: 1));
            h.Effects.Areas["area"] = 1;
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);
            Assert.AreEqual(0, h.Factory.CreateCount);

            h.Effects.Areas["area"] = 0;
            h.Brain.Tick(0.25);
            Assert.AreEqual("area", h.Brain.ActivePatternId);
        }

        [Test]
        public void Tick_OpeningDelay_HoldsFirstSelectionOnly()
        {
            BossHarness h = new BossHarness(BossTestFixtures.MakeBoss(openingDelay: 1f), null, BossTestFixtures.MakePattern("p1"));
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.5);
            Assert.AreEqual(0, h.Factory.CreateCount);

            h.Brain.Tick(1.0);
            Assert.AreEqual(1, h.Factory.CreateCount);
        }

        [Test]
        public void Tick_SelectionRuleRegistered_IsEvaluatedBeforeWeightedSelection()
        {
            PatternDefinition forced = BossTestFixtures.MakePattern("forced");
            FakeSelectionRule rule = new FakeSelectionRule { Forced = forced };
            BossHarness h = Make(BossTestFixtures.MakePattern("a"));
            Assert.IsTrue(h.Brain.AddSelectionRule(rule));
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);

            Assert.AreEqual("forced", h.Brain.ActivePatternId);
            Assert.AreEqual(0, h.Random.FloatCalls, "규칙이 정하면 가중 선택은 평가하지 않는다");
        }

        [Test]
        public void Tick_SelectionRuleDeclines_FallsBackToWeightedSelection()
        {
            FakeSelectionRule rule = new FakeSelectionRule();
            BossHarness h = Make(BossTestFixtures.MakePattern("a"));
            h.Brain.AddSelectionRule(rule);
            h.Brain.Begin(0.0);

            h.Brain.Tick(0.0);

            Assert.AreEqual("a", h.Brain.ActivePatternId);
        }

        [Test]
        public void AddSelectionRule_AfterBegin_IsRejected()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("a"));
            h.Brain.Begin(0.0);

            Assert.IsFalse(h.Brain.AddSelectionRule(new FakeSelectionRule()));
        }

        /****************************************
        *               Begin
        ****************************************/

        [Test]
        public void Begin_NotTuned_DoesNotOpenCombat()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Catalog.Tuned = false;

            LogAssert.Expect(LogType.Warning, new Regex("수치가 지정되지 않아"));
            Assert.IsFalse(h.Brain.Begin(0.0));
            Assert.AreEqual(EBossFlowState.Inactive, h.Brain.FlowState);

            h.Brain.Tick(0.0);
            Assert.AreEqual(0, h.Factory.CreateCount);
        }

        [Test]
        public void Begin_UnknownBoss_DoesNotOpenCombat()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Catalog.Boss = null;

            LogAssert.Expect(LogType.Error, new Regex("보스 정의가 없다"));
            Assert.IsFalse(h.Brain.Begin(0.0));
            Assert.AreEqual(EBossFlowState.Inactive, h.Brain.FlowState);
        }

        [Test]
        public void Begin_Twice_SecondCallIsRejected()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));

            Assert.IsTrue(h.Brain.Begin(0.0));
            Assert.IsFalse(h.Brain.Begin(1.0));
        }

        [Test]
        public void Tick_FactoryHasNoImplementation_StartsNothingAndRetriesLater()
        {
            BossHarness h = Make(BossTestFixtures.MakePattern("p1"));
            h.Factory.ReturnNull = true;
            h.Brain.Begin(0.0);

            LogAssert.Expect(LogType.Error, new Regex("패턴 구현이 없다"));
            h.Brain.Tick(0.0);

            Assert.AreEqual(EBossAttackState.Ready, h.Brain.AttackState);
            Assert.AreEqual(0, h.Damage.Requests.Count);

            // 재조회 대기 안에서는 다시 평가하지 않아 같은 오류가 반복되지 않는다
            h.Brain.Tick(0.125);
            Assert.AreEqual(1, h.Random.IntCalls);
        }

        [Test]
        public void Constructor_MissingDependencies_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies()));
        }

        [Test]
        public void Constructor_EmptyBossId_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => new BossBrain(string.Empty, new BossBrainDependencies()));
        }
    }
}
