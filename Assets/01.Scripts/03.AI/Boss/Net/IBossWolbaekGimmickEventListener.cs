// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 몸체의 월백 기믹 중계(BossWolbaekGimmickEventRelay)가 보내고, 복제 상태(BossState)가 Rpc와 복원 기록으로 옮기는 선택 계약이다.
    // 값은 서버 기믹 알림을 그대로 싣고 표현에 쓰지 않는 값(적중 수, 거리, 비율, 서버 전용 중단 사유)은 버린다.
    // 실패, 정지, 사망과 전투 종료의 정리는 모두 표시 정리라 기믹마다 Cleared 하나로 보낸다.
    // 시각은 모두 서버 시각이고 시전 번호는 서버가 기믹 진입 때 발급한 번호다. 순서가 바뀌어 와도 받는 쪽은 시전 번호로 거른다
    /// <summary> 월백 체력 기믹 4종(75% 부적진, 50% 색 결계, 25% 여우불 봉인, 하드 0% 최후의 영역)과 P07 워프 출발 사건 </summary>
    public interface IBossWolbaekGimmickEventListener
    {
        /****************************************
        *            75% Talisman
        ****************************************/

        /// <summary> 부적진에 진입했다, 구조물 배치와 배정과 전환 끝 시각이 담겨 있다 </summary>
        void OnTalismanStarted(in WolbaekTalismanStart start);

        /// <summary> 필수 참가자 전원이 자기 자리에서 연속 유지를 시작했다 </summary>
        void OnTalismanHoldStarted(int castId, double serverTime);

        /// <summary> 연속 유지가 0으로 돌아갔다, combatId는 처음 자리를 벗어난 참가자다 </summary>
        void OnTalismanHoldReset(int castId, double serverTime, EWolbaekHoldReset reason, int combatId);

        /// <summary> 보스 공격 예고가 시작됐다 </summary>
        void OnTalismanAttackTelegraphed(int castId, in WolbaekTalismanAttack attack);

        /// <summary> 보스 공격의 즉발 판정이 났다, 일회성 효과라 복원하지 않는다 </summary>
        void OnTalismanAttackStruck(int castId, int attackId, double strikeServerTime);

        /// <summary> 판정 전에 공격이 취소됐다 </summary>
        void OnTalismanAttackCancelled(int castId, int attackId);

        /// <summary> 자리 유지에 성공해 반격 시간이 시작됐다 </summary>
        void OnTalismanSucceeded(int castId, double serverTime, double counterEndServerTime);

        /// <summary> 반격 시간이 끝났다 </summary>
        void OnTalismanCounterEnded(int castId);

        /// <summary> 실패, 정지, 사망이나 전투 종료로 표시를 정리한다 </summary>
        void OnTalismanCleared(int castId);

        /****************************************
        *            50% Color Ward
        ****************************************/

        /// <summary> 색 결계에 진입했다, 개인 색 배정과 중앙 결계가 담겨 있다 </summary>
        void OnWardStarted(in WolbaekWardStart start);

        /// <summary> 필수 참가자 전원이 중앙 결계에서 연속 집결을 시작했다 </summary>
        void OnGatherHoldStarted(int castId, double serverTime);

        /// <summary> 연속 집결이 0으로 돌아갔다 </summary>
        void OnGatherHoldReset(int castId);

        /// <summary> 집결을 채워 중앙 결계가 두 색으로 갈라진다 </summary>
        void OnGatherCompleted(int castId);

        /// <summary> 새 지점의 두 색 결계가 나타나 탐색 시간이 시작됐다 </summary>
        void OnStageStarted(int castId, in WolbaekWardStage stage);

        /// <summary> 광역 공격 예고가 시작됐다 </summary>
        void OnStrikeTelegraphed(int castId, in WolbaekWardStrike strike);

        /// <summary> 광역 공격 판정이 났다, 일회성 효과라 복원하지 않고 다음 공격 번호만 남긴다 </summary>
        void OnStrikeResolved(int castId, int stageIndex, int strikeIndex, double strikeServerTime);

        /// <summary> 같은 색 두 명의 거리 피해가 한 번 났다, 일회성이다 </summary>
        void OnLinkDamaged(int castId, double serverTime);

        /// <summary> 복귀자의 합류 유예가 시작됐다 </summary>
        void OnGraceStarted(int castId, int combatId, double graceEndServerTime);

        /// <summary> 복귀자의 합류 유예가 끝났다 </summary>
        void OnGraceEnded(int castId, int combatId);

        /// <summary> 보류한 부활을 처리할 수 있는 시점이다, 일회성이다 </summary>
        void OnReviveWindowOpened(int castId);

        // 성공 Rpc는 즉시 가고 페이즈 상태는 SyncVar 주기로 가서 도착 순서가 정해져 있지 않다. 성공 순간의 페이즈 세대를 실어
        // 클라이언트가 BossState.PhaseGeneration이 이 값보다 커지는 순간 그로기를 끝내게 한다
        /// <summary> 색 결계가 성공했다, phaseGenerationAtSuccess는 성공 순간 서버의 페이즈 세대다 </summary>
        void OnWardSucceeded(int castId, ushort phaseGenerationAtSuccess);

        /// <summary> 실패, 정지, 사망이나 전투 종료로 표시를 정리한다 </summary>
        void OnWardCleared(int castId);

        /****************************************
        *            25% Foxfire Seal
        ****************************************/

        /// <summary> 여우불 봉인에 진입했다 </summary>
        void OnSealStarted(in WolbaekSealStart start);

        /// <summary> 새 차례가 시작돼 핵과 추적 여우불이 나타났다 </summary>
        void OnSealTurnStarted(int castId, in WolbaekSealTurn turn);

        /// <summary> 여우불 낙하 위치가 대상을 따라가기 시작했다 </summary>
        void OnSealTrackingStarted(int castId, int turnIndex, double serverTime);

        /// <summary> 대상이 쓰러지거나 접속이 끊겨 여우불이 마지막 서버 위치에 머문다 </summary>
        void OnSealFoxfireDetached(int castId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position);

        /// <summary> 하드의 넓은 휩쓸기 예고가 시작됐다 </summary>
        void OnSealSweepTelegraphed(int castId, in WolbaekSealSweep sweep);

        /// <summary> 휩쓸기 판정이 났다, 일회성 효과라 복원하지 않는다 </summary>
        void OnSealSweepResolved(int castId, int turnIndex, int sweepIndex, double serverTime);

        /// <summary> 여우불 낙하 위치가 고정됐다, centers는 차례의 여우불 순서와 같다 </summary>
        void OnSealFoxfiresLocked(int castId, int turnIndex, double serverTime, Vector3[] centers);

        /// <summary> 낙하와 핵 파괴가 판정됐다, destroyedCoreMask의 i번째 비트가 i번 핵의 파괴다 </summary>
        void OnSealTurnResolved(int castId, int turnIndex, double serverTime, int successCount, byte destroyedCoreMask);

        /// <summary> 필요한 성공 횟수를 채워 반격 시간이 시작됐다 </summary>
        void OnSealSucceeded(int castId, double serverTime, double counterEndServerTime);

        /// <summary> 반격 시간이 끝났다 </summary>
        void OnSealCounterEnded(int castId);

        /// <summary> 실패, 정지, 사망이나 전투 종료로 표시를 정리한다 </summary>
        void OnSealCleared(int castId);

        /****************************************
        *          Hard 0% Final Area
        ****************************************/

        /// <summary> 최후의 영역에 진입해 외곽 붕괴 예고가 시작됐다 </summary>
        void OnFinalStarted(in WolbaekFinalStart start);

        /// <summary> 외곽 바닥이 사라졌다 </summary>
        void OnFinalOuterFloorRemoved(int castId, double serverTime);

        /// <summary> 노란 여우불이 중앙 바닥에 생겼다 </summary>
        void OnFinalYellowsSpawned(int castId, WolbaekFinalYellow[] yellows);

        /// <summary> 노란 여우불 하나가 부서졌다 </summary>
        void OnFinalYellowDestroyed(int castId, int yellowIndex, double serverTime);

        /// <summary> 빨간 여우불 묶음이 나타나 추적을 시작했다 </summary>
        void OnFinalBundleStarted(int castId, in WolbaekFinalBundle bundle);

        /// <summary> 대상이 쓰러지거나 접속이 끊겨 빨간 여우불이 마지막 서버 위치에 머문다 </summary>
        void OnFinalRedDetached(int castId, int bundleIndex, int redIndex, double serverTime, Vector3 position);

        /// <summary> 빨간 여우불 낙하 위치가 고정됐다, centers는 묶음의 여우불 순서와 같다 </summary>
        void OnFinalRedsLocked(int castId, int bundleIndex, double serverTime, Vector3[] centers);

        /// <summary> 빨간 폭발이 판정됐다, 일회성 효과라 복원하지 않는다 </summary>
        void OnFinalBundleResolved(int castId, int bundleIndex, double serverTime);

        /// <summary> 마지막 노란 여우불이 부서져 최종 성공이 확정됐다 </summary>
        void OnFinalSucceeded(int castId, double serverTime);

        /// <summary> 실패, 정지, 사망이나 전투 종료로 표시를 정리한다 </summary>
        void OnFinalCleared(int castId);

        /****************************************
        *               P07 Warp
        ****************************************/

        // 서버는 시전 시작 틱에 착지 위치로 몸체를 옮기고 같은 틱에 착지 위치로 원형 예고를 보낸다. 이 사건은 그 예고보다 먼저 온다
        /// <summary> 워프 직전 몸체가 서 있던 출발 위치 </summary>
        void OnWarpDeparted(int castId, Vector3 departure, double serverTime);
    }
}
