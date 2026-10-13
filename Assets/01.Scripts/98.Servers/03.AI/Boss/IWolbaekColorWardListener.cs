namespace Shinshi.AI
{
    // 색 결계 기믹의 상태 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다
    // 서버 프로세스의 C# 호출은 원격 클라이언트에 닿지 않으므로 실제 연결에서는 ClientRpc와 늦은 접속 복원이 따로 필요하다.
    // 성공, 실패, 정지, 정리 알림은 모두 표시 정리를 뜻한다
    /// <summary> 색 결계 기믹의 시작, 중앙 집결, 지점, 광역 공격, 연결 거리 피해, 합류 유예, 부활 창구, 성공과 실패와 정리 알림 </summary>
    public interface IWolbaekColorWardListener
    {
        /// <summary> 기믹에 진입했다, 개인 색 배정과 중앙 결계가 담겨 있다 </summary>
        void OnWardStarted(in WolbaekWardStart start);

        /// <summary> 필수 참가자 전원이 중앙 결계에 모여 연속 집결을 시작했다 </summary>
        void OnGatherHoldStarted(int castInstanceId, double serverTime);

        /// <summary> 연속 집결이 0으로 돌아갔다, combatId는 처음 이탈한 참가자다 </summary>
        void OnGatherHoldReset(int castInstanceId, double serverTime, EWolbaekHoldReset reason, int combatId);

        /// <summary> 집결을 채워 중앙 결계가 두 색으로 갈라진다, 이어서 첫 지점이 나타난다 </summary>
        void OnGatherCompleted(int castInstanceId, double serverTime);

        /// <summary> 새 지점의 두 색 결계가 나타나 탐색 시간이 시작됐다, 이전 지점의 결계는 사라진다 </summary>
        void OnStageStarted(int castInstanceId, in WolbaekWardStage stage);

        /// <summary> 광역 공격 예고를 시작했다 </summary>
        void OnStrikeTelegraphed(int castInstanceId, in WolbaekWardStrike strike);

        /// <summary> 광역 공격 판정이 났다, hitCount는 실제로 피해가 적용된 대상 수이고 protectedCount는 올바른 색 결계가 막은 대상 수다 </summary>
        void OnStrikeResolved(int castInstanceId, int stageIndex, int strikeIndex, double strikeServerTime, int hitCount,
            int protectedCount);

        /// <summary> 같은 색 두 명의 거리 피해가 한 번 났다, rate는 최대 체력 대비 방어 전 피해 비율이다 </summary>
        void OnLinkDamaged(int castInstanceId, double serverTime, int combatIdA, int combatIdB, float distance, float rate);

        /// <summary> 복귀자의 합류 유예가 시작됐다 </summary>
        void OnGraceStarted(int castInstanceId, int combatId, double graceEndServerTime);

        /// <summary> 복귀자의 합류 유예가 끝났다 </summary>
        void OnGraceEnded(int castInstanceId, double serverTime, int combatId, EWolbaekGraceEnd reason);

        /// <summary> 보류했던 부활과 재접속 복귀를 처리할 수 있는 시점이다, 새 지점의 탐색 시간이 시작될 때 알린다 </summary>
        void OnReviveWindowOpened(int castInstanceId, double serverTime);

        /// <summary> 세 번째 지점의 마지막 광역 공격을 통과해 성공했다, 표시와 공격은 정리되고 월백 무적은 풀린다 </summary>
        void OnWardSucceeded(int castInstanceId, double serverTime);

        /// <summary> 필수 참가자가 모두 쓰러져 실패했다 </summary>
        void OnWardFailed(int castInstanceId, double serverTime, EWolbaekWardFail reason);

        /// <summary> 진행하지 못하고 멈췄다, 성공도 실패도 아니다. 표시를 정리하고 원인을 알린다 </summary>
        void OnWardHalted(int castInstanceId, double serverTime, EWolbaekWardHaltReason reason);

        /// <summary> 사망이나 전투 종료로 표시를 정리한다 </summary>
        void OnWardCleared(int castInstanceId, EBossInterruptReason reason);
    }
}
