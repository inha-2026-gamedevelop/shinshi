// System
using System.Text.RegularExpressions;

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

namespace Shinshi.Tests
{
    public class BossServerGimmickBodyTests
    {
        // 실제 월백 카탈로그로 만든 서버 몸체에 가짜 기믹 묶음을 붙여 몸체 쪽 연결만 본다: 받는 피해 상한, 기믹 중 무적,
        // 사망 보류와 보류가 풀린 틱의 사망 맞춤, 최종 성공의 체력 확정, 결계 대체. 기믹 자체의 진행은 보지 않는다.
        // 묶음을 붙이지 않은 몸체는 치명타에 바로 죽고 결계 전개가 도는 기존 동작 그대로인지도 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string CATALOG_PATH      = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  REFERENCE_ATTACK  = 644.32f;
        private const float  REFERENCE_HEALTH  = 3541f;
        private const float  HEALTH_SCALE      = 1f;
        private const float  PLAYER_MAX_HEALTH = 1000000f;
        private const float  PLAYER_DISTANCE   = 3f;
        private const double TICK_SECONDS      = 1.0 / Constants.Network.SERVER_TICK_RATE;
        private const int    HOLD_TICKS        = Constants.Network.SERVER_TICK_RATE * 2; // 보류가 여러 틱 유지되는지 보는 길이
        private const int    BARRIER_SECONDS   = 20;                                     // 결계 전개 시작, 완료를 기다리는 상한
        private const int    BARRIER_TICKS     = Constants.Network.SERVER_TICK_RATE * BARRIER_SECONDS;
        private const float  BELOW_HANDOFF     = 0.02f;  // 전환 비율보다 이만큼 아래로 체력을 맞춘다
        private const float  SMALL_DAMAGE      = 10f;
        private const float  CAP_FLOOR_RATE    = 0.5f;   // 가짜 상한이 체력을 멈추는 비율
        private const float  HUGE_DAMAGE       = 1000000000000f; // 방어 뒤에도 보스 최대 체력을 넘는 피해
        private const float  DEFENSE_CONSTANT  = 1000f;
        private const float  REDUCTION_CAP     = 0.6f;
        private const float  FLOAT_EPSILON     = 0.01f;
        private const int    RANDOM_SEED       = 20261014;
        private const int    REVIVE_ID         = 7;

        private static readonly Vector3 REVIVE_POINT = new Vector3(1f, 0f, 2f);

        private CombatTargetRegistry _registry;
        private EnemyTestRigPlayer   _player;
        private BossServer           _boss;
        private BossEffectLedger     _ledger;
        private float                _transitionRate;
        private double               _now;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ApplyDamage_LethalWithoutGimmicks_DiesImmediately()
        {
            Build(null);

            Assert.IsFalse(_boss.HasGimmicks);
            _boss.ApplyDamage(_boss.Health, true, _now);

            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState, "기믹이 없으면 치명타에 바로 죽는다");
            Assert.AreEqual(0f, _boss.Health);

            Step();

            Assert.IsTrue(_boss.IsBossDeathRecorded);
            Assert.IsTrue(_boss.HasUnreportedDefeat);
            Assert.AreEqual(EBossFlowState.Defeated, _boss.FlowState);
        }

        [Test]
        public void GimmickQueries_WithoutGimmicks_AnswerAsUnmanaged()
        {
            Build(null);

            Assert.IsTrue(_boss.IsReviveWindowOpen);
            Assert.AreEqual(EBossRevivePoint.NotManaged, _boss.TryGetGimmickRevivePoint(REVIVE_ID, out Vector3 _));
            Assert.IsFalse(_boss.TryGetGimmickStop(out EBossGimmickStop kind, out ESessionEndReason _, out string detail));
            Assert.AreEqual(EBossGimmickStop.None, kind);
            Assert.IsEmpty(detail);
            Assert.IsFalse(_boss.TryConsumeFinalSuccess(out BossFinalSuccess _));
            Assert.AreEqual(_boss.MaxHealth, _boss.Health, "성공이 없으면 체력을 건드리지 않는다");
            Assert.AreEqual(SMALL_DAMAGE, _boss.CapIncomingDamage(_boss.Health, SMALL_DAMAGE), "기믹이 없으면 상한이 없다");
        }

        [Test]
        public void ApplyDamage_LethalWhileDeathHeld_StaysAliveAtZeroThenDiesOnReleaseTick()
        {
            var fakes = new Fakes();
            fakes.Hold.IsDeathHeld = true;
            Build(fakes.CreateSet(false));

            _boss.ApplyDamage(_boss.Health, true, _now);

            Assert.AreEqual(EParticipantLifeState.Alive, _boss.LifeState, "보류 중에는 치명타에도 죽지 않는다");
            Assert.AreEqual(0f, _boss.Health);
            Assert.IsTrue(_boss.IsHealthZero);
            Assert.IsTrue(_boss.IsInvulnerable, "체력 0인 채 살아 있는 동안은 무적이다");

            for (int i = 0; i < HOLD_TICKS; ++i)
            {
                Step();
                Assert.AreEqual(EParticipantLifeState.Alive, _boss.LifeState);
                Assert.AreEqual(EBossFlowState.Combat, _boss.FlowState);
                Assert.IsFalse(_boss.IsBossDeathRecorded);
                Assert.IsFalse(_boss.HasUnreportedDefeat);
            }

            fakes.Hold.IsDeathHeld = false;
            Step();

            Assert.IsTrue(_boss.IsBossDeathRecorded, "보류가 풀린 틱에 두뇌가 사망을 확정한다");
            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState, "같은 틱에 몸체도 Dead로 맞춘다");
            Assert.IsTrue(_boss.HasUnreportedDefeat, "처치 보고가 열린다");
            Assert.AreEqual(EBossFlowState.Defeated, _boss.FlowState);
        }

        [Test]
        public void ApplyDamage_LethalWithGimmicksButNoHold_DiesImmediately()
        {
            var fakes = new Fakes();
            Build(fakes.CreateSet(false));

            _boss.ApplyDamage(_boss.Health, true, _now);

            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState, "보류하지 않는 기믹은 사망을 미루지 않는다");
        }

        [Test]
        public void IsInvulnerable_FollowsDamageCapBlock()
        {
            var fakes = new Fakes();
            Build(fakes.CreateSet(false));

            Assert.IsFalse(_boss.IsInvulnerable);

            fakes.Cap.IsDamageBlocked = true;
            Assert.IsTrue(_boss.IsInvulnerable, "기믹이 막는 동안은 무적이다");

            fakes.Cap.IsDamageBlocked = false;
            Assert.IsFalse(_boss.IsInvulnerable);
        }

        [Test]
        public void CapIncomingDamage_DelegatesToGimmickCapWithMaxHealth()
        {
            var fakes = new Fakes();
            fakes.Cap.FloorRate = CAP_FLOOR_RATE;
            Build(fakes.CreateSet(false));

            float allowed = _boss.MaxHealth * (1f - CAP_FLOOR_RATE);
            float capped  = _boss.CapIncomingDamage(_boss.Health, HUGE_DAMAGE);

            Assert.AreEqual(allowed, capped, FLOAT_EPSILON);
            Assert.AreEqual(_boss.MaxHealth, fakes.Cap.LastMaxHealth, "최대 체력은 몸체가 채워 넘긴다");
            Assert.AreEqual(SMALL_DAMAGE, _boss.CapIncomingDamage(_boss.Health, SMALL_DAMAGE), FLOAT_EPSILON);
        }

        [Test]
        public void Resolver_PlayerOverkill_StopsAtGimmickBoundaryThenHeldAtZero()
        {
            var fakes = new Fakes();
            fakes.Cap.FloorRate    = CAP_FLOOR_RATE;
            fakes.Hold.IsDeathHeld = true;
            Build(fakes.CreateSet(false));

            var hits     = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            var resolver = new DamageResolver(_registry, hits, DEFENSE_CONSTANT, REDUCTION_CAP);
            resolver.BeginTick(_now);

            DamageResult first = resolver.Apply(Attack(resolver.IssueInstanceId()));

            Assert.AreEqual(EDamageOutcome.Applied, first.Outcome);
            Assert.IsFalse(first.IsLethal, "상한에 걸린 초과 피해는 치명타가 아니다");
            Assert.AreEqual(_boss.MaxHealth * (1f - CAP_FLOOR_RATE), first.AppliedAmount, FLOAT_EPSILON);
            Assert.AreEqual(_boss.MaxHealth * CAP_FLOOR_RATE, _boss.Health, FLOAT_EPSILON, "체력이 경계에서 멈춘다");

            fakes.Cap.FloorRate = 0f;
            DamageResult second = resolver.Apply(Attack(resolver.IssueInstanceId()));

            Assert.IsTrue(second.IsLethal);
            Assert.AreEqual(EParticipantLifeState.Alive, _boss.LifeState, "보류 중이라 판정기의 치명타에도 살아 있다");
            Assert.AreEqual(0f, _boss.Health);

            DamageResult third = resolver.Apply(Attack(resolver.IssueInstanceId()));

            Assert.IsFalse(third.IsApplied, "보류 중인 몸체에는 치명타가 다시 적용되지 않는다");
            Assert.AreEqual(EParticipantLifeState.Alive, _boss.LifeState);
        }

        [Test]
        public void TryConsumeFinalSuccess_SetsHealthToZeroOnceAndDeathFollowsNextTick()
        {
            var fakes = new Fakes();
            fakes.Hold.IsDeathHeld = true;
            Build(fakes.CreateSet(false));

            // 비율 오차로 양수 체력이 남은 상황을 만든다
            _boss.ApplyDamage(_boss.Health - SMALL_DAMAGE, false, _now);
            Step();
            Assert.Greater(_boss.Health, 0f);
            Assert.IsFalse(_boss.TryConsumeFinalSuccess(out BossFinalSuccess _), "성공 전에는 내주지 않는다");
            Assert.Greater(_boss.Health, 0f);

            fakes.Hold.IsDeathHeld = false;
            fakes.Success.Ready(new BossFinalSuccess(_boss.CombatId, REVIVE_ID, _now));

            Assert.IsTrue(_boss.TryConsumeFinalSuccess(out BossFinalSuccess success));
            Assert.AreEqual(_boss.CombatId, success.BossCombatId);
            Assert.AreEqual(0f, _boss.Health, "성공을 내준 순간 체력을 0으로 확정한다");
            Assert.IsTrue(_boss.IsInvulnerable);
            Assert.IsFalse(_boss.TryConsumeFinalSuccess(out BossFinalSuccess _), "한 번만 내준다");

            Step();

            Assert.IsTrue(_boss.IsBossDeathRecorded);
            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState);
            Assert.IsTrue(_boss.HasUnreportedDefeat);
        }

        [Test]
        public void EndCombat_AfterFinalSuccess_RecordsDeathAndCombatEndTogether()
        {
            var fakes = new Fakes();
            Build(fakes.CreateSet(false));
            _boss.ApplyDamage(_boss.Health - SMALL_DAMAGE, false, _now);
            fakes.Success.Ready(new BossFinalSuccess(_boss.CombatId, REVIVE_ID, _now));
            Assert.IsTrue(_boss.TryConsumeFinalSuccess(out BossFinalSuccess _));

            _boss.EndCombat(_now);

            Assert.IsTrue(_boss.IsBossDeathRecorded, "성공한 틱의 종료 틱에서 사망이 함께 기록된다");
            Assert.IsTrue(_boss.IsCombatEndRecorded);
            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState);
        }

        [Test]
        public void AttachGimmicks_RegistersGimmicksAndExposesGates()
        {
            var fakes = new Fakes();
            Build(fakes.CreateSet(false));

            Assert.IsTrue(_boss.HasGimmicks);
            Step();
            Assert.Greater(fakes.Gimmick.Ticks, 0, "두뇌가 붙인 기믹을 틱마다 갱신한다");

            Assert.IsTrue(_boss.IsReviveWindowOpen);
            fakes.Revive.IsReviveWindowOpen = false;
            Assert.IsFalse(_boss.IsReviveWindowOpen);

            fakes.Revive.Answer = EBossRevivePoint.Available;
            Assert.AreEqual(EBossRevivePoint.Available, _boss.TryGetGimmickRevivePoint(REVIVE_ID, out Vector3 point));
            Assert.AreEqual(REVIVE_POINT, point);
            Assert.AreEqual(REVIVE_ID, fakes.Revive.LastCombatId);

            fakes.Revive.Answer = EBossRevivePoint.Unavailable;
            Assert.AreEqual(EBossRevivePoint.Unavailable, _boss.TryGetGimmickRevivePoint(REVIVE_ID, out Vector3 _));

            Assert.IsFalse(_boss.TryGetGimmickStop(out EBossGimmickStop none, out ESessionEndReason _, out string _));
            Assert.AreEqual(EBossGimmickStop.None, none);

            fakes.Stop.Kind   = EBossGimmickStop.Failed;
            fakes.Stop.Reason = ESessionEndReason.Timeout;
            Assert.IsTrue(_boss.TryGetGimmickStop(out EBossGimmickStop kind, out ESessionEndReason reason, out string detail));
            Assert.AreEqual(EBossGimmickStop.Failed, kind);
            Assert.AreEqual(ESessionEndReason.Timeout, reason);
            Assert.AreEqual(StopSource.DETAIL, detail);
        }

        [Test]
        public void AttachGimmicks_SecondCallNullOrAfterBegin_IsRefused()
        {
            BossServer unopened = CreateBoss(out CombatTargetRegistry _);
            Assert.IsFalse(unopened.AttachGimmicks(null));
            Assert.IsTrue(unopened.AttachGimmicks(new Fakes().CreateSet(false)));
            Assert.IsFalse(unopened.AttachGimmicks(new Fakes().CreateSet(false)), "한 번만 받는다");

            Build(null);
            var late = new Fakes();
            Assert.IsFalse(_boss.AttachGimmicks(late.CreateSet(true)), "전투를 연 뒤에는 받지 않는다");
            Assert.IsFalse(_boss.HasGimmicks);
            Assert.IsTrue(_boss.HasBarrier, "거절된 묶음은 결계를 끄지 않는다");
            Step();
            Assert.AreEqual(0, late.Gimmick.Ticks);
        }

        [Test]
        public void AttachGimmicks_SetWithEmptySlot_IsRefusedBeforeTouchingTheBrain()
        {
            BossServer boss  = CreateBoss(out CombatTargetRegistry _);
            var        fakes = new Fakes();
            var        set   = new BossGimmickSet(new IBossGimmick[] { fakes.Gimmick, null }, fakes.Cap,
                new IBossDeathHold[] { fakes.Hold }, fakes.Success, fakes.Stop, fakes.Revive, true);

            LogAssert.Expect(LogType.Error, new Regex("기믹이 비어 있어"));
            bool attached = boss.AttachGimmicks(set);

            Assert.IsFalse(attached);
            Assert.IsFalse(boss.HasGimmicks);
            Assert.IsTrue(boss.HasBarrier);
            Assert.IsTrue(boss.Begin(0.0));
            boss.Tick(TICK_SECONDS);
            Assert.AreEqual(0, fakes.Gimmick.Ticks, "빈 칸이 있는 묶음은 하나도 두뇌에 더하지 않는다");
        }

        [Test]
        public void Tick_TransitionWithBarrierReplaced_SendsNoBarrierAndHandsOffNextTick()
        {
            var fakes = new Fakes();
            Build(fakes.CreateSet(true));

            Assert.IsFalse(_boss.HasBarrier, "기믹이 결계를 대신하면 결계 없는 보스다");

            ForceTransition();

            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "전환한 틱에는 인계 대기다");
            Assert.IsFalse(_ledger.HasBarrier);

            Step();

            Assert.AreNotEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "다음 틱에 인계가 끝난다");

            for (int i = 0; i < BARRIER_TICKS; ++i)
            {
                Step();
                Assert.IsFalse(_ledger.HasBarrier, "결계 사건이 나가지 않는다");
                Assert.AreEqual(EBossBarrierState.Idle, _boss.BarrierState);
            }

            Assert.IsFalse(_boss.TryGetBarrierSnapshot(out BossBarrierStart _));
        }

        [Test]
        public void Tick_TransitionWithBarrierKept_RunsBarrierAndHandsOffAfterCompletion()
        {
            var fakes = new Fakes();
            Build(fakes.CreateSet(false));

            Assert.IsTrue(_boss.HasBarrier, "결계를 대신하지 않는 묶음은 결계 전개를 그대로 둔다");

            ForceTransition();
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState);

            for (int i = 0; (i < BARRIER_TICKS) && (!_ledger.HasBarrier); ++i)
            {
                Step();
            }

            Assert.IsTrue(_ledger.HasBarrier, "전환 뒤 결계 예고가 시작됐다");
            Assert.IsTrue(_boss.TryGetBarrierSnapshot(out BossBarrierStart _));

            for (int i = 0; (i < BARRIER_TICKS) && (_boss.BarrierState != EBossBarrierState.Completed); ++i)
            {
                Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "결계 전개가 끝나기 전에는 인계하지 않는다");
                Step();
            }

            Assert.AreEqual(EBossBarrierState.Completed, _boss.BarrierState);
            Assert.AreNotEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "완료한 틱에 인계를 마친다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        // set이 null이면 묶음을 붙이지 않은 기존 몸체다
        private void Build(BossGimmickSet set)
        {
            _boss = CreateBoss(out _registry);
            int playerId = _registry.IssueCombatId();
            _player = new EnemyTestRigPlayer(playerId, new Vector3(0f, 0f, PLAYER_DISTANCE), PLAYER_MAX_HEALTH);
            Assert.IsTrue(_registry.Register(_player));

            _ledger = new BossEffectLedger();
            _boss.SetEventListener(_ledger);

            if (set != null)
            {
                Assert.IsTrue(_boss.AttachGimmicks(set), "전투를 열기 전에는 묶음을 받는다");
            }

            _now = 0.0;
            Assert.IsTrue(_boss.Begin(_now));
            Assert.IsTrue(_registry.Register(_boss));
        }

        private BossServer CreateBoss(out CombatTargetRegistry registry)
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "월백 카탈로그 에셋이 없다");

            var catalog = new BossPatternCatalogSource(asset);
            Assume.That(catalog.IsTuned(Constants.Combat.DEV_BOSS_ID), Is.True, "월백 수치가 지정되지 않았다");
            Assert.IsTrue(catalog.TryGetBoss(Constants.Combat.DEV_BOSS_ID, out BossPatternCatalogSO.BossDefinition definition));
            _transitionRate = definition.PhaseTransitionHealthRate;

            registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            return new BossServer(Constants.Combat.DEV_BOSS_ID, registry.IssueCombatId(), Vector3.zero, Vector3.forward,
                Vector3.zero, REFERENCE_ATTACK, REFERENCE_HEALTH, HEALTH_SCALE, catalog, registry, new AlwaysAppliedResolver(),
                null, new SeededBossRandom(RANDOM_SEED));
        }

        // 체력을 전환 비율 바로 아래로 맞추고 한 틱 돌려 2페이즈 전환을 일으킨다
        private void ForceTransition()
        {
            float target = _boss.MaxHealth * (_transitionRate - BELOW_HANDOFF);
            _boss.ApplyDamage(_boss.Health - target, false, _now);
            Step();
        }

        private void Step()
        {
            _now += TICK_SECONDS;
            _boss.Tick(_now);
        }

        private DamageRequest Attack(int actionId)
        {
            return new DamageRequest(_player.CombatId, _boss.CombatId, actionId, actionId, HUGE_DAMAGE, EDamageFlags.None,
                _player.Position, Constants.Combat.BASIC_ATTACK_SOURCE_ID);
        }

        private sealed class AlwaysAppliedResolver : IDamageResolver
        {
            private int _nextId = 1;

            public DamageResult Apply(in DamageRequest request)
            {
                return DamageResult.Applied(request.RawDamage, false, false);
            }

            public int IssueInstanceId()
            {
                int id = _nextId;
                ++_nextId;
                return id;
            }
        }

        // 묶음 하나에 들어가는 가짜 창구 모음
        private sealed class Fakes
        {
            public IdleGimmick  Gimmick { get; } = new IdleGimmick();
            public DamageCap    Cap     { get; } = new DamageCap();
            public DeathHold    Hold    { get; } = new DeathHold();
            public FinalSuccess Success { get; } = new FinalSuccess();
            public StopSource   Stop    { get; } = new StopSource();
            public ReviveGate   Revive  { get; } = new ReviveGate();

            public BossGimmickSet CreateSet(bool replacesBarrier)
            {
                return new BossGimmickSet(new IBossGimmick[] { Gimmick }, Cap, new IBossDeathHold[] { Hold }, Success, Stop,
                    Revive, replacesBarrier);
            }
        }

        // 전환과 선택을 막지 않고 불린 횟수만 세는 기믹
        private sealed class IdleGimmick : IBossGimmick
        {
            public int  Ticks                 { get; private set; }
            public bool DefersPhaseTransition => false;
            public bool BlocksSelection       => false;

            public void Tick(double serverTime, int phase)
            {
                ++Ticks;
            }

            public bool TrySelect(BossBlackboard blackboard, out BossPatternCatalogSO.PatternDefinition pattern)
            {
                pattern = null;
                return false;
            }

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
            }
        }

        // FloorRate 아래로는 내려가지 못하게 줄인다. 0이면 줄이지 않는다
        private sealed class DamageCap : IBossDamageCap
        {
            public bool  IsDamageBlocked { get; set; }
            public float FloorRate       { get; set; }
            public float LastMaxHealth   { get; private set; }

            public float CapDamage(float currentHealth, float maxHealth, float damage)
            {
                LastMaxHealth = maxHealth;
                return Mathf.Min(damage, Mathf.Max(0f, currentHealth - (FloorRate * maxHealth)));
            }
        }

        private sealed class DeathHold : IBossDeathHold
        {
            public bool IsDeathHeld { get; set; }
        }

        private sealed class FinalSuccess : IBossFinalSuccess
        {
            private BossFinalSuccess _success;

            public bool HasFinalSuccess { get; private set; }

            public void Ready(BossFinalSuccess success)
            {
                _success        = success;
                HasFinalSuccess = true;
            }

            public bool TryConsumeFinalSuccess(out BossFinalSuccess success)
            {
                if (!HasFinalSuccess)
                {
                    success = default;
                    return false;
                }

                HasFinalSuccess = false;
                success         = _success;
                return true;
            }
        }

        private sealed class StopSource : IBossGimmickStopSource
        {
            public const string DETAIL = "fake stop";

            public EBossGimmickStop  Kind   { get; set; } = EBossGimmickStop.None;
            public ESessionEndReason Reason { get; set; }

            public bool TryGetStop(out EBossGimmickStop kind, out ESessionEndReason failureReason, out string detail)
            {
                kind          = Kind;
                failureReason = Reason;
                detail        = (Kind == EBossGimmickStop.None) ? string.Empty : DETAIL;
                return Kind != EBossGimmickStop.None;
            }
        }

        private sealed class ReviveGate : IBossGimmickReviveGate
        {
            public bool             IsReviveWindowOpen { get; set; } = true;
            public EBossRevivePoint Answer             { get; set; } = EBossRevivePoint.NotManaged;
            public int              LastCombatId       { get; private set; }

            public EBossRevivePoint TryGetRevivePoint(int combatId, out Vector3 point)
            {
                LastCombatId = combatId;
                point        = (Answer == EBossRevivePoint.Available) ? REVIVE_POINT : default;
                return Answer;
            }
        }
    }
}
