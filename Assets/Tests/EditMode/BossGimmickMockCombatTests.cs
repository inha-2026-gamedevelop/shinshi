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
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class BossGimmickMockCombatTests
    {
        // 운영 서버와 같은 조립으로 월백 체력 기믹을 끝까지 돌려 본다: 실제 PlayerServer, 실제 DamageResolver와 전투 대상 등록부,
        // 실제 BossServer에 WolbaekGimmickFactory가 만든 묶음을 붙인다. 가짜는 벽 하나(정지 재현)뿐이다.
        // 플레이어 공격은 판정기에 피해 요청을 직접 넣고, 플레이어 위치는 틱마다 시험이 정해 기믹 조건을 채운다.
        // 보스 공격에 맞아 쓰러지면 기믹이 실패로 끝나 보려는 구간에 닿지 못하므로 틱마다 살아 있는 플레이어의 체력을 채운다.
        // 사망은 시험이 직접 일으킨 것과 기믹이 판정기 밖에서 확정한 것(추락)만 남는다

        /****************************************
        *                Fields
        ****************************************/

        private const string CATALOG_PATH     = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  REFERENCE_ATTACK = 644.32f;
        private const float  REFERENCE_HEALTH = 3541f;
        private const float  HEALTH_SCALE     = 1f;
        private const float  DAMAGE_SCALE     = 1f;
        private const int    RANDOM_SEED      = 20261014;
        private const int    GIMMICK_SEED     = 4177;
        private const int    REVIVES          = 3;
        private const int    TICK_RATE        = Constants.Network.SERVER_TICK_RATE;
        private const double TICK_SECONDS     = 1.0 / Constants.Network.SERVER_TICK_RATE;

        private const float HUGE_DAMAGE   = 1000000000000f; // 방어 뒤에도 보스 최대 체력을 넘는 피해
        private const float HEALTH_SLACK  = 0.5f;           // 체력 45만 규모에서 경계 비교에 두는 부동소수 여유
        private const float REVIVE_RATE   = 0.5f;
        private const float SECOND_RATE   = 0.5f;           // 부적진 반격 중 멈추는 다음 경계, 월백의 2페이즈 전환 비율과 같다
        private const int   SECOND_PHASE  = 2;
        private const int   TWO_PLAYERS   = 2;
        private const int   THREE_PLAYERS = 3;

        // 플레이어 자리, 전장 중심 둘레의 네 방위에 놓는다
        private const float GATHER_RADIUS  = 3f;    // m, 색 결계 집결 반경(5m) 안이고 봉인 핵과 폭발 반경보다 멀다
        private const float INSIDE_RADIUS  = 5f;    // m, 최후의 영역 중앙 바닥(8m) 안
        private const float OUTSIDE_RADIUS = 12f;   // m, 중앙 바닥 밖이고 전장(18m) 안
        private const float QUARTER_TURN   = Mathf.PI * 0.5f;
        private const float QUERY_RADIUS   = 30f;   // m, 전장 전체를 덮는 조회 반경

        // 각 구간을 기다리는 모의 시간 상한(초), 구간 길이에 여유를 더했다
        private const int TALISMAN_SECONDS = 20;
        private const int COUNTER_SECONDS  = 10;
        private const int GATHER_SECONDS   = 15;
        private const int WARD_SECONDS     = 40;
        private const int SEAL_SECONDS     = 45;
        private const int SEAL_FAIL_SECONDS = 70;
        private const int COLLAPSE_SECONDS = 6;
        private const int FALL_WAIT_SECONDS = 2;
        private const int BARRIER_SECONDS  = 20;

        private const double RETURN_AFTER_SECONDS = 0.3; // 바닥 제거 뒤 이만큼 지나 중앙으로 돌아온다, 낙하 시간(0.6초)보다 짧다
        private const double TIME_SLACK           = 0.0001;

        private const float PLAIN_DAMAGE_RATE = 0.3f; // 기믹 없는 보스에 한 번 넣는 방어 전 피해의 최대 체력 대비 비율
        private const int   PLAIN_HIT_LIMIT   = 20;

        private static readonly Vector3 BLOCKED_SPAWN = new Vector3(6f, 0f, 0f);

        // 워프 순서 시험의 착지 지점, 플레이어 자리(중심에서 3m)와 겹치지 않고 전장(18m) 안이다
        private static readonly Vector3 WARP_POINT = new Vector3(0f, 0f, -8f);

        private const int   WARP_CAST_ID           = 900001; // 시험이 직접 알리는 시전 번호, 두뇌가 발급하는 번호와 겹치지 않게 크게 둔다
        private const float WARP_RADIUS            = 3f;
        private const float WARP_TELEGRAPH_SECONDS = 1f;

        private static readonly string[] EASY_HISTORY =
        {
            "Talisman:Transition", "Talisman:Active", "Talisman:Counter", "Talisman:Completed",
            "Ward:Gather", "Ward:Search", "Ward:Strike", "Ward:Search", "Ward:Strike", "Ward:Search", "Ward:Strike", "Ward:Completed",
            "Seal:Active", "Seal:Counter", "Seal:Stopped"
        };

        private static readonly string[] HARD_HISTORY =
        {
            "Talisman:Transition", "Talisman:Active", "Talisman:Counter", "Talisman:Completed",
            "Ward:Gather", "Ward:Search", "Ward:Strike", "Ward:Search", "Ward:Strike", "Ward:Search", "Ward:Strike", "Ward:Completed",
            "Seal:Active", "Seal:Counter", "Seal:Completed", "Final:Active", "Final:Succeeded"
        };

        private PlayerServerTestRig      _rig;
        private SessionConfigSO          _config;
        private ParticipantRoster        _roster;
        private BossServer               _boss;
        private BossGimmickSet           _set;
        private BossEventRecorder        _events;
        private WolbaekTalismanFormation _talisman;
        private WolbaekColorWard         _ward;
        private WolbaekFoxfireSeal       _seal;
        private WolbaekFinalArea         _final;
        private List<string>             _history;
        private List<AppliedDamageNotice> _notices;
        private List<ICombatTarget>      _targets;
        private float                    _transitionRate;
        private int                      _revives;
        private double                   _now;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _rig     = new PlayerServerTestRig();
            _config  = ScriptableObject.CreateInstance<SessionConfigSO>();
            _roster  = new ParticipantRoster(_config);
            _history = new List<string>();
            _notices = new List<AppliedDamageNotice>();
            _targets = new List<ICombatTarget>();
            _revives = REVIVES;
            _now     = 0.0;

            // NUnit이 시험 사이에 같은 인스턴스를 다시 쓰므로 앞 시험의 묶음을 지운다
            _set      = null;
            _talisman = null;
            _ward     = null;
            _seal     = null;
            _final    = null;
            _rig.Resolver.DamageApplied += _notices.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Resolver.DamageApplied -= _notices.Add;
            _rig.Dispose();
            UnityEngine.Object.DestroyImmediate(_config);
        }

        /****************************************
        *            Cap And Triggers
        ****************************************/

        [Test]
        public void Apply_OverkillAtFullHealth_StopsAtTalismanBoundaryWithCappedResult()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, true);

            DamageResult hit = Strike(_boss.CombatId);

            Assert.AreEqual(EDamageOutcome.Applied, hit.Outcome);
            Assert.IsFalse(hit.IsLethal, "경계에서 멈춘 피해는 치명타가 아니다");
            Assert.AreEqual(_boss.MaxHealth * (1f - WolbaekTalismanDevCatalog.TRIGGER_HEALTH_RATE), hit.AppliedAmount, HEALTH_SLACK,
                "적용량은 경계까지 깎인 값이다");
            Assert.AreEqual(_boss.MaxHealth * WolbaekTalismanDevCatalog.TRIGGER_HEALTH_RATE, _boss.Health, HEALTH_SLACK);
            Assert.AreEqual(hit.AppliedAmount, _notices[_notices.Count - 1].Result.AppliedAmount, "피해 통지도 깎인 값을 싣는다");
            Assert.IsTrue(_boss.IsInvulnerable, "경계에 닿은 체력은 기믹이 시작하기 전에도 더 깎이지 않는다");

            DamageResult second = Strike(_boss.CombatId);

            Assert.AreEqual(EDamageOutcome.Invulnerable, second.Outcome);
            Assert.AreEqual(_boss.MaxHealth * WolbaekTalismanDevCatalog.TRIGGER_HEALTH_RATE, _boss.Health, HEALTH_SLACK);
            Assert.AreEqual(EWolbaekTalismanState.Dormant, _talisman.State, "발동은 다음 보스 틱이다");

            Step();

            Assert.AreEqual(EWolbaekTalismanState.Transition, _talisman.State);
        }

        [Test]
        public void Tick_EasyFight_TriggersTalismanWardSealInOrderAndDiesOnLethalInSealCounter()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, true);

            RunToWardCompleted();

            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "색 결계가 끝난 틱에 2페이즈 전환이 일어난다");
            Assert.IsFalse(_boss.HasBarrier, "색 결계가 붙은 보스는 결계 전개를 쓰지 않는다");

            Step();

            Assert.AreEqual(SECOND_PHASE, _boss.Phase);
            Assert.AreNotEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState, "결계 전개를 기다리지 않고 다음 틱에 인계한다");
            Assert.AreEqual(EBossBarrierState.Idle, _boss.BarrierState);

            RunToSealCounter();

            Assert.AreEqual(0, _events.Count(nameof(IBossEventListener.OnBarrierStarted)), "결계 전개 사건이 한 번도 나가지 않았다");
            Assert.AreEqual(EBossBarrierState.Idle, _boss.BarrierState);
            Assert.IsFalse(_boss.IsInvulnerable, "봉인 반격 중에는 피해를 받는다");

            float before = _boss.Health;
            DamageResult kill = Strike(_boss.CombatId);

            Assert.IsTrue(kill.IsLethal, "이지는 다음 경계가 없어 반격 중 치명타가 그대로 들어간다");
            Assert.AreEqual(before, kill.AppliedAmount, HEALTH_SLACK);
            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState, "이지는 보류 없이 그 자리에서 죽는다");
            Assert.AreEqual(0f, _boss.Health);

            Step();

            Assert.IsTrue(_boss.IsBossDeathRecorded);
            Assert.IsTrue(_boss.HasUnreportedDefeat);
            Assert.AreEqual(EBossFlowState.Defeated, _boss.FlowState);
            Assert.IsFalse(_boss.TryGetGimmickStop(out EBossGimmickStop kind, out _, out _), "정상 처치는 정지나 실패가 아니다");
            Assert.AreEqual(EBossGimmickStop.None, kind);
            CollectionAssert.AreEqual(EASY_HISTORY, _history);
            AssertEveryoneAlive();
        }

        /****************************************
        *           Hard Final Area
        ****************************************/

        [Test]
        public void Apply_HardLethalInSealCounter_HoldsDeathAtZeroAndHandsOffToFinalArea()
        {
            Build(EBossDifficulty.Hard, TWO_PLAYERS, true);
            RunToWardCompleted();
            Step();
            RunToSealCounter();

            float before = _boss.Health;
            DamageResult kill = Strike(_boss.CombatId);

            Assert.IsTrue(kill.IsLethal, "0% 경계까지 깎인 피해는 체력을 0으로 만든다");
            Assert.AreEqual(before, kill.AppliedAmount, HEALTH_SLACK);
            Assert.AreEqual(0f, _boss.Health);
            Assert.AreEqual(EParticipantLifeState.Alive, _boss.LifeState, "하드는 사망을 보류한다");
            Assert.IsTrue(_boss.IsInvulnerable);
            Assert.IsFalse(Strike(_boss.CombatId).IsApplied, "보류 중인 몸체는 다시 맞지 않는다");

            Assert.IsTrue(StepUntil(() => _final.State == EWolbaekFinalState.Active, null, COUNTER_SECONDS),
                "봉인 반격이 끝난 틱에 최후의 영역이 인계를 받아 시작한다");

            Assert.AreEqual(EWolbaekSealState.Completed, _seal.State);
            Assert.AreEqual(EParticipantLifeState.Alive, _boss.LifeState);
            Assert.AreEqual(0f, _boss.Health);
            Assert.IsTrue(_boss.IsInvulnerable);
            Assert.IsFalse(_boss.IsBossDeathRecorded);
            Assert.IsFalse(_boss.HasUnreportedDefeat);
            Assert.AreEqual(EBossFlowState.Combat, _boss.FlowState);
            Assert.IsFalse(_final.IsOuterFloorRemoved, "예고 시간 동안에는 바닥이 남아 있다");
        }

        [Test]
        public void Tick_HardOuterFloorRemoved_OnlyPlayerStayingOutsideFallsAndRevivedPlayerFallsAgain()
        {
            Build(EBossDifficulty.Hard, THREE_PLAYERS, true);
            PlayerServer stayer   = _rig.Players[0];
            PlayerServer returner = _rig.Players[1];
            PlayerServer insider  = _rig.Players[2];
            RunToFinalArea();

            Action standApart = () =>
            {
                Place(stayer, Spot(0, OUTSIDE_RADIUS));
                Place(returner, Spot(1, OUTSIDE_RADIUS));
                Place(insider, Spot(2, INSIDE_RADIUS));
            };
            Assert.IsTrue(StepUntil(() => _final.IsOuterFloorRemoved, standApart, COLLAPSE_SECONDS));

            double removedAt = _now;
            Assert.AreEqual(EParticipantLifeState.Alive, stayer.LifeState, "바닥 제거 순간에는 외곽에 서 있어도 죽지 않는다");
            Assert.AreEqual(EParticipantLifeState.Alive, returner.LifeState);
            int noticesBefore = CountNotices(stayer.CombatId);

            int maxTicks = TICK_RATE * FALL_WAIT_SECONDS;
            for (int i = 0; (i < maxTicks) && (stayer.LifeState == EParticipantLifeState.Alive); ++i)
            {
                Place(stayer, Spot(0, OUTSIDE_RADIUS));
                Place(returner, ((_now + TICK_SECONDS - removedAt) >= RETURN_AFTER_SECONDS) ? Spot(1, INSIDE_RADIUS) : Spot(1, OUTSIDE_RADIUS));
                Place(insider, Spot(2, INSIDE_RADIUS));
                Step();

                if ((_now - removedAt) < (BossArenaFloor.FALL_SECONDS - TIME_SLACK))
                {
                    Assert.AreEqual(EParticipantLifeState.Alive, stayer.LifeState, "낙하 시간이 차기 전에는 죽지 않는다");
                }
            }

            Assert.AreEqual(EParticipantLifeState.Dead, stayer.LifeState, "외곽에 낙하 시간 이상 머문 참가자는 추락 사망한다");
            Assert.AreEqual(_now, stayer.DeathTime, TIME_SLACK, "사망 시각은 낙하가 확정된 틱의 시각이다");
            Assert.GreaterOrEqual(stayer.DeathTime - removedAt, BossArenaFloor.FALL_SECONDS - TIME_SLACK);
            Assert.AreEqual(noticesBefore, CountNotices(stayer.CombatId), "추락은 판정기를 거치지 않아 피해 통지가 없다");

            StepSeconds(1, () =>
            {
                Place(returner, Spot(1, INSIDE_RADIUS));
                Place(insider, Spot(2, INSIDE_RADIUS));
            });

            Assert.AreEqual(EParticipantLifeState.Alive, returner.LifeState, "낙하 시간 안에 중앙으로 돌아온 참가자는 산다");
            Assert.AreEqual(EParticipantLifeState.Alive, insider.LifeState);
            Assert.AreEqual(EWolbaekFinalState.Active, _final.State, "생존자가 남아 있어 전멸 실패가 아니다");
            Assert.AreNotEqual(EBossRevivePoint.NotManaged, _boss.TryGetGimmickRevivePoint(stayer.CombatId, out _),
                "최후의 영역이 진행 중이면 부활 지점을 기믹이 맡는다");

            stayer.Revive(Spot(0, OUTSIDE_RADIUS), REVIVE_RATE, _now);
            double revivedAt = _now;
            Assert.AreEqual(EParticipantLifeState.Alive, stayer.LifeState);

            Assert.IsTrue(StepUntil(() => stayer.LifeState != EParticipantLifeState.Alive, () =>
            {
                Place(stayer, Spot(0, OUTSIDE_RADIUS));
                Place(returner, Spot(1, INSIDE_RADIUS));
                Place(insider, Spot(2, INSIDE_RADIUS));
            }, FALL_WAIT_SECONDS), "부활한 참가자는 새 생명이라 외곽에 머물면 다시 떨어진다");

            Assert.GreaterOrEqual(stayer.DeathTime - revivedAt, BossArenaFloor.FALL_SECONDS - TIME_SLACK);
            Assert.AreEqual(noticesBefore, CountNotices(stayer.CombatId));
            Assert.AreEqual(EWolbaekFinalState.Active, _final.State);
        }

        [Test]
        public void Apply_HardHeldBossBodyInFinalArea_StaysTargetableButDoesNotBlockYellowHits()
        {
            Build(EBossDifficulty.Hard, TWO_PLAYERS, true);
            RunToFinalArea();
            Assert.IsTrue(StepUntil(() => _final.IsOuterFloorRemoved, StandInside, COLLAPSE_SECONDS));

            PlayerServer attacker = _rig.Players[0];
            int found = _rig.Registry.GetLivingInRadius(attacker.Position, QUERY_RADIUS, ECombatFaction.Enemy, true, _targets);

            Assert.AreEqual(_final.YellowCount + 1, found, "범위 조회에 노란 여우불과 보류 중인 보스 몸체가 함께 나온다");
            Assert.IsTrue(Contains(_targets, _boss.CombatId), "체력 0으로 보류된 몸체도 살아 있는 대상으로 남는다");
            for (int i = 0; i < _final.YellowBodies.Count; ++i)
            {
                Assert.IsTrue(Contains(_targets, _final.YellowBodies[i].CombatId));
            }

            // 판정기는 체력 0인 대상을 무적 검사보다 먼저 거른다
            Assert.IsFalse(Strike(_boss.CombatId).IsApplied, "몸체는 피해를 받지 않는다");

            WolbaekYellowFoxfire yellow = _final.YellowBodies[0];
            DamageResult hit = Strike(yellow.CombatId);

            Assert.AreEqual(EDamageOutcome.Applied, hit.Outcome, "몸체가 남아 있어도 노란 여우불은 판정기로 맞는다");
            Assert.IsTrue(hit.IsLethal);
            Assert.AreEqual(WolbaekFinalAreaDevCatalog.YELLOW_HEALTH, hit.AppliedAmount, HEALTH_SLACK);
            Assert.IsTrue(yellow.IsDestroyed);
            Assert.AreEqual(_final.YellowCount - 1, _final.YellowRemaining);
            Assert.AreEqual(_final.YellowCount, _rig.Registry.GetLivingInRadius(attacker.Position, QUERY_RADIUS, ECombatFaction.Enemy,
                true, _targets), "부서진 노란 여우불만 조회에서 빠진다");
            Assert.AreEqual(EParticipantLifeState.Alive, _boss.LifeState);
            Assert.AreEqual(0f, _boss.Health);
        }

        [Test]
        public void Tick_HardAllYellowsDestroyed_FinalSuccessConsumedOnceAndEndCombatRecordsDeath()
        {
            Build(EBossDifficulty.Hard, TWO_PLAYERS, true);
            RunToFinalSuccess();

            Assert.AreEqual(EParticipantLifeState.Alive, _boss.LifeState, "성공한 틱에는 아직 사망이 확정되지 않았다");
            Assert.IsFalse(_boss.IsBossDeathRecorded);

            Assert.IsTrue(_boss.TryConsumeFinalSuccess(out BossFinalSuccess success));
            Assert.AreEqual(_boss.CombatId, success.BossCombatId);
            Assert.AreEqual(0f, _boss.Health);
            Assert.IsFalse(_boss.TryConsumeFinalSuccess(out _), "최종 성공은 한 번만 내준다");

            _boss.EndCombat(_now);
            Observe();

            Assert.IsTrue(_boss.IsBossDeathRecorded, "성공한 틱의 전투 종료에서 사망이 함께 기록된다");
            Assert.IsTrue(_boss.IsCombatEndRecorded);
            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState);
            Assert.IsTrue(_boss.HasUnreportedDefeat);
            Assert.IsFalse(_boss.TryGetGimmickStop(out _, out _, out _), "최종 성공은 정지나 실패가 아니다");
            CollectionAssert.AreEqual(HARD_HISTORY, _history);
            AssertVictoryProtection();
        }

        [Test]
        public void Tick_HardFinalSuccess_NextTickRecordsDeathAndOpensDefeatReport()
        {
            Build(EBossDifficulty.Hard, TWO_PLAYERS, true);
            RunToFinalSuccess();
            Assert.IsTrue(_boss.TryConsumeFinalSuccess(out _));

            Step();

            Assert.IsTrue(_boss.IsBossDeathRecorded);
            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState);
            Assert.IsTrue(_boss.HasUnreportedDefeat);
            Assert.AreEqual(EBossFlowState.Defeated, _boss.FlowState);
            AssertVictoryProtection();
        }

        /****************************************
        *            Stop And Failure
        ****************************************/

        [Test]
        public void TryGetGimmickStop_SealTimeLimitExpired_FailedWithTimeout()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, true);
            RunToWardCompleted();
            Step();
            EnterSeal();

            Assert.IsFalse(_boss.TryGetGimmickStop(out _, out _, out _), "진행 중에는 멈춘 것이 없다");
            Assert.IsTrue(StepUntil(() => _seal.State == EWolbaekSealState.Failed, StandAtGather, SEAL_FAIL_SECONDS),
                "핵을 부수지 않으면 제한 시간에 실패한다");

            Assert.AreEqual(EWolbaekSealFail.TimeExpired, _seal.FailReason);
            Assert.GreaterOrEqual(_now, _seal.LimitEndServerTime - TIME_SLACK);
            Assert.IsTrue(_boss.TryGetGimmickStop(out EBossGimmickStop kind, out ESessionEndReason reason, out string detail));
            Assert.AreEqual(EBossGimmickStop.Failed, kind);
            Assert.AreEqual(ESessionEndReason.Timeout, reason);
            Assert.IsNotEmpty(detail);
            Assert.IsTrue(_boss.IsInvulnerable, "실패한 기믹의 보스는 무적으로 남는다");
            AssertEveryoneAlive();
        }

        [Test]
        public void TryGetGimmickStop_BossCannotReachArenaCenter_Halted()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, true, BLOCKED_SPAWN, new SolidWall());
            Strike(_boss.CombatId);
            Step();
            Assert.AreEqual(EWolbaekTalismanState.Transition, _talisman.State);

            Assert.IsTrue(StepUntil(() => _talisman.State == EWolbaekTalismanState.Halted, SeatPlayers, TALISMAN_SECONDS),
                "전환 시간이 끝날 때 중앙에 닿지 못하면 정지한다");

            Assert.AreEqual(EWolbaekHaltReason.MovementBlocked, _talisman.HaltReason);
            Assert.AreEqual(BLOCKED_SPAWN, _boss.Position);
            Assert.IsTrue(_boss.TryGetGimmickStop(out EBossGimmickStop kind, out ESessionEndReason reason, out string detail));
            Assert.AreEqual(EBossGimmickStop.Halted, kind);
            Assert.AreEqual(ESessionEndReason.ServiceCancelled, reason);
            Assert.IsNotEmpty(detail);
            Assert.IsTrue(_boss.IsInvulnerable);
        }

        [Test]
        public void TryGetGimmickStop_EveryoneDeadDuringTalisman_ReturnsFalseForWipe()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, true);
            Strike(_boss.CombatId);
            Step();

            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                Assert.IsTrue(_rig.Players[i].KillByServer(_now));
            }

            Step();

            Assert.AreEqual(EWolbaekTalismanState.Failed, _talisman.State);
            Assert.AreEqual(EWolbaekFormationFail.Wipe, _talisman.FailReason);
            Assert.IsFalse(_boss.TryGetGimmickStop(out EBossGimmickStop kind, out _, out string detail),
                "전멸은 세션의 전멸 판정이 닫으므로 기믹이 알리지 않는다");
            Assert.AreEqual(EBossGimmickStop.None, kind);
            Assert.IsEmpty(detail);
        }

        [Test]
        public void TryGetGimmickStop_SeatHolderDeadWithoutRevives_FailedWithAllDead()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, true);
            Strike(_boss.CombatId);
            Step();

            _revives = 0;
            Assert.IsTrue(_rig.Players[0].KillByServer(_now));
            Step();

            Assert.AreEqual(EWolbaekTalismanState.Failed, _talisman.State);
            Assert.AreEqual(EWolbaekFormationFail.Unrecoverable, _talisman.FailReason);
            Assert.IsTrue(_boss.TryGetGimmickStop(out EBossGimmickStop kind, out ESessionEndReason reason, out _));
            Assert.AreEqual(EBossGimmickStop.Failed, kind);
            Assert.AreEqual(ESessionEndReason.AllDead, reason);
        }

        /****************************************
        *              Regression
        ****************************************/

        [Test]
        public void Tick_BossWithoutGimmicks_HasNoCapRunsBarrierAndDiesOnLethal()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, false);
            Assert.IsFalse(_boss.HasGimmicks);
            Assert.IsTrue(_boss.HasBarrier);

            // 부적진 경계(75%)와 전환 비율(50%)을 차례로 지나 내려간다
            for (int i = 0; (i < PLAIN_HIT_LIMIT) && (_boss.Health >= _boss.MaxHealth * _transitionRate); ++i)
            {
                float        before = _boss.Health;
                DamageResult hit    = Strike(_boss.CombatId, _boss.MaxHealth * PLAIN_DAMAGE_RATE);

                Assert.AreEqual(EDamageOutcome.Applied, hit.Outcome, "기믹이 없으면 어느 체력에서도 피해가 들어간다");
                Assert.IsFalse(hit.IsLethal);
                Assert.AreEqual(before - hit.AppliedAmount, _boss.Health, HEALTH_SLACK, "적용량이 깎이지 않고 그대로 체력에서 빠진다");
            }

            Assert.Less(_boss.Health, _boss.MaxHealth * _transitionRate, "경계에서 멈추지 않고 전환 비율 아래로 내려간다");
            Assert.Greater(_boss.Health, 0f);
            Assert.IsFalse(_boss.IsInvulnerable);

            Step();
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, _boss.PhaseState);

            Assert.IsTrue(StepUntil(() => _events.Count(nameof(IBossEventListener.OnBarrierStarted)) > 0, StandAtGather, BARRIER_SECONDS),
                "기믹이 없으면 전환 뒤 결계 전개가 돈다");
            Assert.AreNotEqual(EBossBarrierState.Idle, _boss.BarrierState);

            DamageResult kill = Strike(_boss.CombatId);

            Assert.IsTrue(kill.IsLethal);
            Assert.AreEqual(EParticipantLifeState.Dead, _boss.LifeState, "기믹이 없으면 치명타에 바로 죽는다");

            Step();

            Assert.IsTrue(_boss.IsBossDeathRecorded);
            Assert.IsTrue(_boss.HasUnreportedDefeat);
            Assert.IsFalse(_boss.TryConsumeFinalSuccess(out _));
        }

        /****************************************
        *        Wolbaek Gimmick Replication
        ****************************************/

        // 실제 기믹이 몸체의 월백 기믹 중계를 거쳐 복제 상태 계약(IBossWolbaekGimmickEventListener)에 닿는지 본다.
        // 수신처는 복제 상태(BossState)가 서버 기록을 이어 가는 것과 같은 연결로 기록도 만든다
        [Test]
        public void Tick_EasyFight_GimmickEventsReachTheReplicationContractAndWardSuccessPrecedesTheGeneration()
        {
            Assert.IsTrue(typeof(IBossWolbaekGimmickEventListener).IsAssignableFrom(typeof(BossState)),
                "복제 상태가 계약을 구현해야 몸체가 SetEventListener로 기믹 사건을 넘긴다");
            Build(EBossDifficulty.Easy, TWO_PLAYERS, true);
            BossWolbaekEventRecorder recorder = AttachWolbaekRecorder();

            RunToWardCompleted();

            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnTalismanStarted)));
            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnTalismanSucceeded)));
            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnTalismanCounterEnded)));
            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnWardStarted)));
            Assert.AreEqual(WolbaekColorWardDevCatalog.STAGE_COUNT,
                recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnStageStarted)));
            Assert.Greater(recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnStrikeResolved)), 0);
            Assert.AreEqual(1, recorder.WardSuccessCount);
            Assert.Less(recorder.IndexOf(nameof(IBossWolbaekGimmickEventListener.OnTalismanCounterEnded)),
                recorder.IndexOf(nameof(IBossWolbaekGimmickEventListener.OnWardStarted)), "75% 반격이 끝난 뒤 50%가 시작된다");

            // 성공 알림은 성공 틱 안에서 나가고 세대는 그 틱의 두뇌 틱이 끝난 뒤 오른다
            Assert.AreEqual(0, recorder.WardSuccessGeneration, "성공에는 전환 전 세대가 실린다");
            Assert.AreEqual(0, recorder.GenerationAtWardSuccessCallback, "성공이 온 순간 몸체 세대는 아직 오르지 않았다");
            Assert.AreEqual(1, _boss.PhaseGeneration, "같은 틱의 전환으로 세대가 오른다");
            Assert.AreEqual(_now, _boss.PhaseTransitionServerTime, TIME_SLACK);
            Assert.Greater(_boss.PhaseGeneration, recorder.WardSuccessGeneration, "클라이언트는 세대가 성공 값보다 커질 때 그로기를 끝낸다");
            Assert.IsTrue(recorder.Ledger.IsEmpty, "끝난 75%와 50%는 늦은 접속에 넘기지 않는다");
            Assert.IsFalse(double.IsNaN(_boss.AppearStartServerTime), "전투를 연 시각을 복제한다");
            Assert.IsTrue(double.IsNaN(_boss.DefeatServerTime));
        }

        [Test]
        public void Tick_EasySeal_TurnEventsCarryTheDestroyedCoreMaskAndCounterIsRestorable()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, true);
            BossWolbaekEventRecorder recorder = AttachWolbaekRecorder();
            RunToWardCompleted();
            Step();

            RunToSealCounter();

            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnSealStarted)));
            Assert.AreEqual(WolbaekFoxfireSealDevCatalog.REQUIRED_SUCCESSES,
                recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnSealTurnResolved)));
            Assert.Greater(recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnSealFoxfiresLocked)), 0);
            Assert.AreNotEqual(0, recorder.LastCoreMask, "여우불로 부순 핵이 마스크에 실린다");
            Assert.IsTrue(BossWolbaekGimmickLedger.IsCoreDestroyed(recorder.LastCoreMask, 0) == _seal.IsCoreDestroyed(0));
            Assert.IsTrue(recorder.Ledger.HasSeal);
            Assert.AreEqual(EWolbaekSealState.Counter, recorder.Ledger.Seal.Snapshot.State);

            Strike(_boss.CombatId);
            Step();

            Assert.IsFalse(double.IsNaN(_boss.DefeatServerTime), "처치 시각을 복제한다");
            Assert.LessOrEqual(_boss.DefeatServerTime, _now);
        }

        [Test]
        public void Tick_HardFinal_FinalEventsReachTheReplicationContract()
        {
            Build(EBossDifficulty.Hard, TWO_PLAYERS, true);
            BossWolbaekEventRecorder recorder = AttachWolbaekRecorder();

            RunToFinalSuccess();

            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnFinalStarted)));
            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnFinalOuterFloorRemoved)));
            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnFinalYellowsSpawned)));
            Assert.Greater(recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnFinalYellowDestroyed)), 0);
            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnFinalSucceeded)));
            Assert.IsFalse(recorder.Ledger.HasFinal, "성공한 최후의 영역은 늦은 접속에 넘기지 않는다");
        }

        // 워프는 착지 위치로 몸체를 옮긴 뒤 같은 호출에서 원형 예고를 알린다. 출발 위치가 그 예고보다 먼저 나가야 한다
        [Test]
        public void TryWarp_ThenCircleTelegraph_SendsTheDepartureBeforeTheTelegraphOnlyInThatTick()
        {
            Build(EBossDifficulty.Easy, TWO_PLAYERS, false);
            BossWolbaekEventRecorder recorder = AttachWolbaekRecorder();
            Assert.IsTrue(StepUntil(() => _boss.FlowState == EBossFlowState.Combat, null, TALISMAN_SECONDS));

            IBossWarp                    warp      = _boss;
            IBossCircleTelegraphListener telegraph = _boss;
            Vector3                      departure = _boss.Position;
            Assert.IsTrue(warp.TryWarp(WARP_POINT, out Vector3 landed));
            telegraph.OnTelegraphStarted(WARP_CAST_ID, landed, WARP_RADIUS, WARP_TELEGRAPH_SECONDS);

            int departed    = recorder.IndexOf(nameof(IBossWolbaekGimmickEventListener.OnWarpDeparted));
            int telegraphed = recorder.IndexOf(nameof(IBossEventListener.OnTelegraphStarted));
            Assert.GreaterOrEqual(departed, 0);
            Assert.Less(departed, telegraphed, "출발 위치가 원형 예고보다 먼저다");
            Assert.AreEqual(WARP_CAST_ID, recorder.LastDepartureCastId);
            Assert.AreEqual(departure, recorder.LastDeparture);
            Assert.AreEqual(WARP_POINT, _boss.Position);

            telegraph.OnTelegraphStarted(WARP_CAST_ID + 1, landed, WARP_RADIUS, WARP_TELEGRAPH_SECONDS);
            Assert.IsTrue(warp.TryWarp(departure, out _));
            Step();
            telegraph.OnTelegraphStarted(WARP_CAST_ID + 2, departure, WARP_RADIUS, WARP_TELEGRAPH_SECONDS);

            Assert.AreEqual(1, recorder.Count(nameof(IBossWolbaekGimmickEventListener.OnWarpDeparted)),
                "워프 없는 예고와 다음 틱의 예고에는 출발 위치를 싣지 않는다");
        }

        private BossWolbaekEventRecorder AttachWolbaekRecorder()
        {
            BossServer boss = _boss;
            var recorder = new BossWolbaekEventRecorder(() => boss.PhaseGeneration);
            _boss.SetEventListener(recorder);
            return recorder;
        }

        /****************************************
        *                Build
        ****************************************/

        private void Build(EBossDifficulty difficulty, int playerCount, bool withGimmicks)
        {
            Build(difficulty, playerCount, withGimmicks, Vector3.zero, null);
        }

        // 운영 서버의 보스 생성과 같은 순서다: 몸체, 묶음 붙이기, 전투 열기, 등록. 전장 중심은 원점이고 terrain을 비우면 전장 경계만 막는다
        private void Build(EBossDifficulty difficulty, int playerCount, bool withGimmicks, Vector3 spawn, IBossWallQuery terrain)
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "월백 카탈로그 에셋이 없다");

            var catalog = new BossPatternCatalogSource(asset, difficulty);
            Assume.That(catalog.TryGetBoss(Constants.Combat.DEV_BOSS_ID, out BossPatternCatalogSO.BossDefinition definition), Is.True,
                "월백 정의가 없다");
            Assume.That(catalog.IsTuned(Constants.Combat.DEV_BOSS_ID), Is.True, "월백 수치가 지정되지 않았다");
            _transitionRate = definition.PhaseTransitionHealthRate;
            Assume.That(_transitionRate, Is.EqualTo(SECOND_RATE), "월백의 2페이즈 전환 비율이 바뀌었다");

            for (int i = 0; i < playerCount; ++i)
            {
                int          participantId = i + 1;
                PlayerServer player        = _rig.Add(participantId, Spot(i, GATHER_RADIUS));
                Assert.IsTrue(_rig.Registry.Register(player));
                Assert.IsTrue(_roster.TryAdd(participantId, $"participant-{participantId}"));
            }

            _roster.Seal();

            var scaled = new BossDamageScaler(_rig.Resolver, DAMAGE_SCALE);
            _boss = new BossServer(Constants.Combat.DEV_BOSS_ID, _rig.Registry.IssueCombatId(), spawn, Vector3.forward, Vector3.zero,
                REFERENCE_ATTACK, REFERENCE_HEALTH, HEALTH_SCALE, catalog, _rig.Registry, scaled, terrain,
                new SeededBossRandom(RANDOM_SEED), null, new BossParticipantConnections(_rig.Players, _roster),
                new BossPlayerDisplaceLookup(_rig.Players));

            IBossEventRelayHost relay = _boss;
            _events = new BossEventRecorder(() => relay.ActivePatternId);
            _boss.SetEventListener(_events);

            if (withGimmicks)
            {
                var inputs = new WolbaekGimmickInputs(_boss, difficulty, REFERENCE_HEALTH, scaled, _rig.Resolver, _rig.Registry,
                    new SeededBossRandom(GIMMICK_SEED), _rig.Players, _roster, () => _revives, _transitionRate);
                Assert.IsTrue(WolbaekGimmickFactory.TryCreate(inputs, out _set, out string reason), reason);
                Assert.IsTrue(_boss.AttachGimmicks(_set));

                _talisman = (WolbaekTalismanFormation)_set.Gimmicks[0];
                _ward     = (WolbaekColorWard)_set.Gimmicks[1];
                _seal     = (WolbaekFoxfireSeal)_set.Gimmicks[2];
                _final    = (difficulty == EBossDifficulty.Hard) ? (WolbaekFinalArea)_set.Gimmicks[3] : null;
            }

            Assert.IsTrue(_boss.Begin(_now));
            Assert.IsTrue(_rig.Registry.Register(_boss));
        }

        /****************************************
        *                Ticks
        ****************************************/

        // 운영 틱의 순서 가운데 보스에 닿는 부분만 돈다: 판정기 틱 시작, 보스 틱. 플레이어 공격과 이동은 시험이 틱 사이에 넣는다
        private void Step()
        {
            _now += TICK_SECONDS;
            _rig.Resolver.BeginTick(_now);
            _events.Phase = _boss.Phase;
            if (_boss.IsActive)
            {
                _boss.Tick(_now);
            }

            Observe();
            AssertBlockFollowsGimmicks();
            HealLiving();
        }

        private bool StepUntil(Func<bool> isDone, Action arrange, int maxSeconds)
        {
            int maxTicks = TICK_RATE * maxSeconds;
            for (int i = 0; (i < maxTicks) && (!isDone()); ++i)
            {
                arrange?.Invoke();
                Step();
            }

            return isDone();
        }

        private void StepSeconds(int seconds, Action arrange)
        {
            int ticks = TICK_RATE * seconds;
            for (int i = 0; i < ticks; ++i)
            {
                arrange?.Invoke();
                Step();
            }
        }

        // 기믹이 바뀐 상태만 온 순서대로 적는다. 한 틱에 여러 기믹이 바뀌면 등록 순서로 적는다
        private void Observe()
        {
            if (_set == null)
            {
                return;
            }

            Record("Talisman", _talisman.State.ToString());
            Record("Ward", _ward.State.ToString());
            Record("Seal", _seal.State.ToString());
            if (_final != null)
            {
                Record("Final", _final.State.ToString());
            }
        }

        private void Record(string gimmick, string state)
        {
            if (state == "Dormant")
            {
                return;
            }

            string entry  = $"{gimmick}:{state}";
            string prefix = $"{gimmick}:";
            for (int i = _history.Count - 1; i >= 0; --i)
            {
                if (_history[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (_history[i] == entry)
                    {
                        return;
                    }

                    break;
                }
            }

            _history.Add(entry);
        }

        // 살아서 전투 중이고 체력이 남은 보스의 무적은 기믹이 막는 구간과 정확히 같다
        private void AssertBlockFollowsGimmicks()
        {
            if ((_set == null) || (_boss.LifeState != EParticipantLifeState.Alive) || (_boss.FlowState != EBossFlowState.Combat))
            {
                return;
            }

            if (_boss.IsHealthZero)
            {
                Assert.IsTrue(_boss.IsInvulnerable, "체력 0으로 보류된 몸체는 무적이다");
                return;
            }

            Assert.AreEqual(_set.DamageCap.IsDamageBlocked, _boss.IsInvulnerable, $"시각 {_now}에 보스 무적이 기믹의 막는 구간과 다르다");
        }

        private void HealLiving()
        {
            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                _rig.Players[i].Heal(_rig.Players[i].MaxHealth);
            }
        }

        /****************************************
        *                Stages
        ****************************************/

        // 75%에서 멈춘 뒤 부적진을 채우고, 반격 중 50%까지 깎아 색 결계를 이어서 채운다. 끝나면 색 결계가 막 끝난 틱이다
        private void RunToWardCompleted()
        {
            DamageResult first = Strike(_boss.CombatId);
            Assert.IsFalse(first.IsLethal);
            Assert.AreEqual(_boss.MaxHealth * WolbaekTalismanDevCatalog.TRIGGER_HEALTH_RATE, _boss.Health, HEALTH_SLACK);

            Step();
            Assert.AreEqual(EWolbaekTalismanState.Transition, _talisman.State, "75%에서 부적진이 발동한다");
            Assert.AreEqual(_rig.Players.Count, _talisman.RequiredCount);
            Assert.IsTrue(StepUntil(() => _talisman.State == EWolbaekTalismanState.Counter, SeatPlayers, TALISMAN_SECONDS),
                "전원이 자기 자리를 지키면 부적진이 성공한다");

            float        before = _boss.Health;
            DamageResult second = Strike(_boss.CombatId);
            Assert.IsFalse(second.IsLethal, "반격 중에도 다음 경계 아래로 내려가지 않는다");
            Assert.AreEqual(before - (_boss.MaxHealth * _transitionRate), second.AppliedAmount, HEALTH_SLACK);
            Assert.AreEqual(_boss.MaxHealth * _transitionRate, _boss.Health, HEALTH_SLACK);
            Assert.AreEqual(EDamageOutcome.Invulnerable, Strike(_boss.CombatId).Outcome);

            Assert.IsTrue(StepUntil(() => _talisman.State == EWolbaekTalismanState.Completed, SeatPlayers, COUNTER_SECONDS));
            Assert.AreEqual(EWolbaekWardState.Gather, _ward.State, "부적진 반격이 끝난 틱에 색 결계가 이어서 발동한다");
            Assert.AreEqual(1, _boss.Phase, "색 결계가 전환을 보류한다");

            Assert.IsTrue(StepUntil(() => _ward.State == EWolbaekWardState.Search, StandAtGather, GATHER_SECONDS),
                "전원이 중앙에 모이면 첫 지점이 시작된다");
            Assert.AreEqual(_boss.MaxHealth * _transitionRate, _boss.Health, HEALTH_SLACK);
            Assert.IsTrue(StepUntil(() => _ward.State == EWolbaekWardState.Completed, StandInWards, WARD_SECONDS),
                "세 지점의 광역 공격을 자기 색 결계에서 버티면 색 결계가 성공한다");
            Assert.AreEqual(WolbaekColorWardDevCatalog.STAGE_COUNT - 1, _ward.StageIndex);
        }

        // 2페이즈로 넘어온 보스를 25%까지 깎고 봉인을 시작시킨다
        private void EnterSeal()
        {
            float        before = _boss.Health;
            DamageResult hit    = Strike(_boss.CombatId);
            Assert.IsFalse(hit.IsLethal);
            Assert.AreEqual(before - (_boss.MaxHealth * WolbaekFoxfireSealDevCatalog.TRIGGER_HEALTH_RATE), hit.AppliedAmount, HEALTH_SLACK);
            Assert.AreEqual(_boss.MaxHealth * WolbaekFoxfireSealDevCatalog.TRIGGER_HEALTH_RATE, _boss.Health, HEALTH_SLACK);

            Step();
            Assert.AreEqual(EWolbaekSealState.Active, _seal.State, "25%에서 봉인이 발동한다");
        }

        private void RunToSealCounter()
        {
            EnterSeal();
            Assert.IsTrue(StepUntil(() => _seal.State == EWolbaekSealState.Counter, GuideFoxfires, SEAL_SECONDS),
                "여우불을 핵으로 유도해 세 차례 모두 부수면 봉인이 성공한다");
            Assert.AreEqual(WolbaekFoxfireSealDevCatalog.REQUIRED_SUCCESSES, _seal.SuccessCount);
        }

        // 하드에서 봉인 반격 중 0으로 만들어 최후의 영역이 인계를 받은 틱까지 간다
        private void RunToFinalArea()
        {
            RunToWardCompleted();
            Step();
            RunToSealCounter();

            Assert.IsTrue(Strike(_boss.CombatId).IsLethal);
            Assert.IsTrue(StepUntil(() => _final.State == EWolbaekFinalState.Active, null, COUNTER_SECONDS));
        }

        // 바닥이 제거된 뒤 노란 여우불을 판정기로 모두 부수고 성공이 확정된 틱까지 간다
        private void RunToFinalSuccess()
        {
            RunToFinalArea();
            Assert.IsTrue(StepUntil(() => _final.IsOuterFloorRemoved, StandInside, COLLAPSE_SECONDS));
            Assert.Greater(_final.YellowCount, 0);
            Assert.AreEqual(_final.YellowCount, _final.YellowBodies.Count);

            for (int i = 0; i < _final.YellowBodies.Count; ++i)
            {
                DamageResult hit = Strike(_final.YellowBodies[i].CombatId);
                Assert.IsTrue(hit.IsLethal, "노란 여우불은 일반 적처럼 치명타에 바로 부서진다");
            }

            Assert.AreEqual(EWolbaekFinalState.Active, _final.State, "파괴는 다음 보스 틱에 읽는다");
            StandInside();
            Step();
            Assert.AreEqual(EWolbaekFinalState.Succeeded, _final.State);
        }

        /****************************************
        *               Players
        ****************************************/

        private DamageResult Strike(int targetId)
        {
            return Strike(targetId, HUGE_DAMAGE);
        }

        // 첫 번째 참가자의 기본 공격으로 넣는다. 동작 번호를 매번 새로 받아 같은 대상을 다시 맞힐 수 있다
        private DamageResult Strike(int targetId, float rawDamage)
        {
            PlayerServer attacker = _rig.Players[0];
            int          actionId = _rig.Resolver.IssueInstanceId();
            return _rig.Resolver.Apply(new DamageRequest(attacker.CombatId, targetId, actionId, actionId, rawDamage,
                EDamageFlags.None, attacker.Position, Constants.Combat.BASIC_ATTACK_SOURCE_ID));
        }

        private static void Place(PlayerServer player, Vector3 position)
        {
            player.SetSkillPosition(position);
        }

        // 전장 중심 둘레의 index번째 방위(90도 간격)에서 radius만큼 떨어진 자리
        private static Vector3 Spot(int index, float radius)
        {
            float angle = index * QUARTER_TURN;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private void StandAtGather()
        {
            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                Place(_rig.Players[i], Spot(i, GATHER_RADIUS));
            }
        }

        private void StandInside()
        {
            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                Place(_rig.Players[i], Spot(i, INSIDE_RADIUS));
            }
        }

        // 부적진이 배정한 구조물 위에 선다. 보스 공격에 밀려도 다음 틱 전에 다시 올라선다
        private void SeatPlayers()
        {
            IReadOnlyList<WolbaekTalismanStructure> structures = _talisman.Structures;
            for (int i = 0; i < structures.Count; ++i)
            {
                PlayerServer player = FindPlayer(structures[i].AssignedCombatId);
                if (player != null)
                {
                    Place(player, structures[i].Center);
                }
            }
        }

        // 지점이 열리면 자기 색 결계의 중심에 선다
        private void StandInWards()
        {
            WolbaekWardStage stage = _ward.Stage;
            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                PlayerServer player = _rig.Players[i];
                if (_ward.TryGetColor(player.CombatId, out EWolbaekWardColor color))
                {
                    Place(player, stage.GetCenter(color));
                }
            }
        }

        // 추적 여우불은 차례 시작 때의 생존자 순서로 한 명씩 따라간다. 낙하 위치가 고정될 때까지 서로 다른 핵 위에 서 있다가
        // 고정된 뒤에는 폭발 반경 밖으로 물러난다
        private void GuideFoxfires()
        {
            bool isLuring = (_seal.Step == EWolbaekSealStep.Prepare) || (_seal.Step == EWolbaekSealStep.Track);
            int  living   = 0;
            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                PlayerServer player = _rig.Players[i];
                if (player.LifeState != EParticipantLifeState.Alive)
                {
                    continue;
                }

                bool hasCore = (isLuring) && (living < _seal.Cores.Count);
                Place(player, hasCore ? _seal.Cores[living].Center : Spot(i, GATHER_RADIUS));
                ++living;
            }
        }

        private PlayerServer FindPlayer(int combatId)
        {
            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                if (_rig.Players[i].CombatId == combatId)
                {
                    return _rig.Players[i];
                }
            }

            return null;
        }

        private int CountNotices(int targetCombatId)
        {
            int count = 0;
            for (int i = 0; i < _notices.Count; ++i)
            {
                if (_notices[i].TargetCombatId == targetCombatId)
                {
                    ++count;
                }
            }

            return count;
        }

        private static bool Contains(List<ICombatTarget> targets, int combatId)
        {
            for (int i = 0; i < targets.Count; ++i)
            {
                if (targets[i].CombatId == combatId)
                {
                    return true;
                }
            }

            return false;
        }

        private void AssertEveryoneAlive()
        {
            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                Assert.AreEqual(EParticipantLifeState.Alive, _rig.Players[i].LifeState);
            }
        }

        // 승리 보호가 걸린 참가자는 보스가 공격자인 피해를 받지 않는다
        private void AssertVictoryProtection()
        {
            for (int i = 0; i < _rig.Players.Count; ++i)
            {
                PlayerServer player   = _rig.Players[i];
                float        before   = player.Health;
                int          actionId = _rig.Resolver.IssueInstanceId();
                DamageResult result   = _rig.Resolver.Apply(new DamageRequest(_boss.CombatId, player.CombatId, actionId, actionId,
                    HUGE_DAMAGE, EDamageFlags.Unblockable, _boss.Position, Constants.Combat.BASIC_ATTACK_SOURCE_ID));

                Assert.IsTrue(player.IsInvulnerable, "최종 성공이 승리 보호를 건다");
                Assert.AreEqual(EDamageOutcome.Invulnerable, result.Outcome);
                Assert.AreEqual(before, player.Health);
                Assert.AreEqual(EParticipantLifeState.Alive, player.LifeState);
            }
        }

        /****************************************
        *                Fakes
        ****************************************/

        // 어느 방향으로도 한 발짝도 못 가게 막는 벽, 보스가 전장 중심으로 가지 못하는 상황을 만든다
        private sealed class SolidWall : IBossWallQuery
        {
            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return 0f;
            }
        }
    }
}
