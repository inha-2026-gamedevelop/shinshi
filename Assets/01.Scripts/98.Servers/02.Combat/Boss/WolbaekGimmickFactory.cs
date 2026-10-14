// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Combat
{
    // 월백의 체력 기믹 넷을 만들어 한 묶음으로 낸다. 등록 순서는 부적진(75%), 색 결계(50%), 여우불 봉인(25%), 최후의 영역(하드 0%)이고
    // 뒤 기믹은 바로 앞 기믹을 앞선 기믹으로 받아 반격 중 도달한 경계를 이어받는다. 이지는 최후의 영역을 만들지 않는다.
    // 하나라도 만들지 못하면 묶음을 내지 않는다: 일부만 붙이면 인계가 끊겨 교착된다(봉인의 하드 인계 대기는 최후의 영역이 없으면 끝나지 않는다).
    //
    // 설정(Wolbaek*DevCatalog)과 지점(Wolbaek*DevSites, 전장 중심 기준)은 맵과 운영 데이터가 정해지기 전의 임시값이다.
    // 기믹 사건의 수신처(listener)는 아직 없어 비운다. 색 결계가 50% 구간을 대신하므로 결계 전개는 쓰지 않는다(ReplacesBarrier)
    /// <summary> 월백 체력 기믹 묶음 생성 </summary>
    public static class WolbaekGimmickFactory
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string NAME_TALISMAN = "부적진";
        private const string NAME_WARD     = "색 결계";
        private const string NAME_SEAL     = "여우불 봉인";
        private const string NAME_FINAL    = "최후의 영역";

        /****************************************
        *                Fields
        ****************************************/

        // 정지(Halted)는 전장, 지점, 바닥 창구의 장애라 출정을 서비스 취소로 닫고, 전멸 아닌 실패는 보스전 실패로 닫는다.
        // 전멸(Wipe) 실패는 세션의 전멸 판정이 같은 틱에 닫으므로 여기서는 알리지 않는다.
        // 한 번 잡힌 결과는 그대로 둔다: 전투 종료가 기믹을 Stopped로 옮겨도 답이 바뀌지 않고, 설명 문자열을 틱마다 만들지 않는다
        private sealed class StopSource : IBossGimmickStopSource
        {
            private readonly WolbaekTalismanFormation _talisman;
            private readonly WolbaekColorWard         _ward;
            private readonly WolbaekFoxfireSeal       _seal;
            private readonly WolbaekFinalArea         _final; // 이지는 null

            private EBossGimmickStop  _kind   = EBossGimmickStop.None;
            private ESessionEndReason _reason = ESessionEndReason.ServiceCancelled;
            private string            _detail = string.Empty;

            public StopSource(WolbaekTalismanFormation talisman, WolbaekColorWard ward, WolbaekFoxfireSeal seal,
                WolbaekFinalArea final)
            {
                _talisman = talisman;
                _ward     = ward;
                _seal     = seal;
                _final    = final;
            }

            public bool TryGetStop(out EBossGimmickStop kind, out ESessionEndReason failureReason, out string detail)
            {
                if (_kind == EBossGimmickStop.None)
                {
                    Evaluate();
                }

                kind          = _kind;
                failureReason = _reason;
                detail        = _detail;
                return _kind != EBossGimmickStop.None;
            }

            // 정지가 실패보다 먼저다. 문자열은 잡힌 틱에 한 번만 만든다
            private void Evaluate()
            {
                if (_talisman.State == EWolbaekTalismanState.Halted)
                {
                    Latch(EBossGimmickStop.Halted, ESessionEndReason.ServiceCancelled, $"{NAME_TALISMAN} 정지 {_talisman.HaltReason}");
                }
                else if (_ward.State == EWolbaekWardState.Halted)
                {
                    Latch(EBossGimmickStop.Halted, ESessionEndReason.ServiceCancelled, $"{NAME_WARD} 정지 {_ward.HaltReason}");
                }
                else if (_seal.State == EWolbaekSealState.Halted)
                {
                    Latch(EBossGimmickStop.Halted, ESessionEndReason.ServiceCancelled, $"{NAME_SEAL} 정지 {_seal.HaltReason}");
                }
                else if ((_final != null) && (_final.State == EWolbaekFinalState.Halted))
                {
                    Latch(EBossGimmickStop.Halted, ESessionEndReason.ServiceCancelled, $"{NAME_FINAL} 정지 {_final.HaltReason}");
                }
                else if ((_seal.State == EWolbaekSealState.Failed) && (_seal.FailReason == EWolbaekSealFail.TimeExpired))
                {
                    Latch(EBossGimmickStop.Failed, ESessionEndReason.Timeout, $"{NAME_SEAL} 제한 시간 만료");
                }
                else if ((_talisman.State == EWolbaekTalismanState.Failed)
                    && (_talisman.FailReason == EWolbaekFormationFail.Unrecoverable))
                {
                    // 전멸은 아니지만 더 진행할 수 없다. 새 종료 사유는 정산과 백엔드 계약을 바꾸므로 기존 값을 쓴다
                    Latch(EBossGimmickStop.Failed, ESessionEndReason.AllDead, $"{NAME_TALISMAN} 자리를 채울 수 없음");
                }
            }

            private void Latch(EBossGimmickStop kind, ESessionEndReason reason, string detail)
            {
                _kind   = kind;
                _reason = reason;
                _detail = detail;
            }
        }

        // 색 결계의 타격 중에는 부활을 다음 탐색까지 미루고, 최후의 영역이 진행 중이면 부활 지점을 남은 중앙 바닥으로 한정한다.
        // 그때 지점이 없으면 없다고 답해 부르는 쪽이 횟수를 쓰지 않고 미루게 한다. 기존 지점 선택으로 돌아가면 사라진 외곽에 되살아난다
        private sealed class ReviveGate : IBossGimmickReviveGate
        {
            private readonly WolbaekColorWard _ward;
            private readonly WolbaekFinalArea _final; // 이지는 null

            public bool IsReviveWindowOpen => _ward.IsReviveWindowOpen;

            public ReviveGate(WolbaekColorWard ward, WolbaekFinalArea final)
            {
                _ward  = ward;
                _final = final;
            }

            public EBossRevivePoint TryGetRevivePoint(int combatId, out Vector3 point)
            {
                if ((_final == null) || (_final.State != EWolbaekFinalState.Active))
                {
                    point = default;
                    return EBossRevivePoint.NotManaged;
                }

                if (_final.TryGetSafeRevivePoint(combatId, out point))
                {
                    return EBossRevivePoint.Available;
                }

                point = default;
                return EBossRevivePoint.Unavailable;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기믹 묶음을 만든다, 하나라도 만들지 못하면 거짓이고 reason에 이유를 담는다. 거짓이면 기믹 없이 전투를 연다 </summary>
        public static bool TryCreate(WolbaekGimmickInputs inputs, out BossGimmickSet set, out string reason)
        {
            set = null;
            if (!TryCheckInputs(inputs, out reason))
            {
                return false;
            }

            // 설정과 기믹 생성자는 잘못된 값을 예외로 거절한다. 보스 생성을 막지 않도록 종류를 가리지 않고 받아 이유로 돌려준다
            try
            {
                set = Create(inputs);
            }
            catch (Exception exception)
            {
                set    = null;
                reason = $"기믹 생성 실패: {exception.GetType().Name} {exception.Message}";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool TryCheckInputs(WolbaekGimmickInputs inputs, out string reason)
        {
            reason = string.Empty;
            if (inputs == null)
            {
                reason = "입력 묶음이 비어 있다";
            }
            else if (inputs.Boss == null)
            {
                reason = "보스 몸체가 비어 있다";
            }
            else if ((inputs.ScaledResolver == null) || (inputs.UnscaledResolver == null))
            {
                reason = "피해 판정기가 비어 있다";
            }
            else if (inputs.Registry == null)
            {
                reason = "전투 대상 등록부가 비어 있다";
            }
            else if (inputs.Random == null)
            {
                reason = "난수가 비어 있다";
            }
            else if ((inputs.Players == null) || (inputs.Roster == null))
            {
                reason = "참가자 목록이나 출정 명단이 비어 있다";
            }
            else if (inputs.RemainingRevives == null)
            {
                reason = "남은 부활 횟수 조회가 비어 있다";
            }
            else if ((inputs.Difficulty != EBossDifficulty.Easy) && (inputs.Difficulty != EBossDifficulty.Hard))
            {
                reason = "알 수 없는 난이도다";
            }

            return reason.Length == 0;
        }

        private static BossGimmickSet Create(WolbaekGimmickInputs inputs)
        {
            BossServer boss   = inputs.Boss;
            bool       isHard = inputs.Difficulty == EBossDifficulty.Hard;
            Vector3    center = boss.ArenaCenter;

            // 사망 확정 창구에는 시각 인자가 없다. 기믹은 보스 틱 안에서만 죽이므로 몸체가 가진 이번 틱의 시각을 읽는다
            IBossEventRelayHost clock = boss;
            var source = new BossParticipantSource(inputs.Players, inputs.Roster, inputs.RemainingRevives, () => clock.TickTime);

            var talisman = new WolbaekTalismanFormation(
                WolbaekTalismanDevCatalog.CreateSpec(isHard, inputs.PhaseTransitionHealthRate), boss, inputs.ReferenceHealth, boss,
                source, boss, boss, inputs.ScaledResolver, inputs.Registry, inputs.Random, boss);

            var ward = new WolbaekColorWard(WolbaekColorWardDevCatalog.CreateSpec(), boss, boss, source, source, boss,
                new WolbaekWardDevSites(center), inputs.UnscaledResolver, inputs.Registry, inputs.Random, boss, talisman);

            var seal = new WolbaekFoxfireSeal(WolbaekFoxfireSealDevCatalog.CreateSpec(isHard), boss, boss, source, source, boss,
                new WolbaekSealDevSites(center), inputs.UnscaledResolver, inputs.Registry, inputs.Random, boss, ward);

            if (!isHard)
            {
                return new BossGimmickSet(new IBossGimmick[] { talisman, ward, seal },
                    new BossDamageCapComposite(talisman, ward, seal), new IBossDeathHold[] { seal }, null,
                    new StopSource(talisman, ward, seal, null), new ReviveGate(ward, null), true);
            }

            WolbaekFinalAreaSpec finalSpec = WolbaekFinalAreaDevCatalog.CreateSpec();
            var terrain = new BossTerrainProbe(CombatLineOfSight.ResolveLayerMask(Constants.Combat.OBSTACLE_LAYER_NAME));
            var floor   = new BossArenaFloor(source, source, center, finalSpec.CentralRadius, null, terrain);

            var final = new WolbaekFinalArea(finalSpec, boss, boss, source, source, boss, new WolbaekFinalDevSites(center), floor,
                source, source, new BossYellowRegistry(inputs.Registry), source, inputs.UnscaledResolver, inputs.Registry, seal);

            return new BossGimmickSet(new IBossGimmick[] { talisman, ward, seal, final },
                new BossDamageCapComposite(talisman, ward, seal, final), new IBossDeathHold[] { seal, final }, final,
                new StopSource(talisman, ward, seal, final), new ReviveGate(ward, final), true);
        }
    }
}
