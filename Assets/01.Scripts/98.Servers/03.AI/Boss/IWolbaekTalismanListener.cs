namespace Shinshi.AI
{
    // 사방 부적진 기믹의 상태 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다
    // 서버 프로세스의 C# 호출은 원격 클라이언트에 닿지 않으므로 실제 연결에서는 ClientRpc와 늦은 접속 복원이 따로 필요하다.
    // 성공, 실패, 정리 알림은 모두 표시 정리를 뜻한다
    /// <summary> 부적진 기믹의 시작, 자리 유지, 공격, 성공과 실패와 정리 알림 </summary>
    public interface IWolbaekTalismanListener
    {
        /// <summary> 기믹에 진입했다, 구조물 배치와 배정과 단계 시각이 모두 담겨 있다 </summary>
        void OnFormationStarted(in WolbaekTalismanStart start);

        /// <summary> 필수 참가자 전원이 자기 자리에 모여 연속 유지를 시작했다 </summary>
        void OnHoldStarted(int castInstanceId, double serverTime);

        /// <summary> 연속 유지가 0으로 돌아갔다, combatId는 처음 자리를 벗어난 참가자다 </summary>
        void OnHoldReset(int castInstanceId, double serverTime, EWolbaekHoldReset reason, int combatId);

        /// <summary> 보스 공격의 예고를 시작했다 </summary>
        void OnAttackTelegraphed(int castInstanceId, in WolbaekTalismanAttack attack);

        /// <summary> 보스 공격의 즉발 판정이 났다, hitCount는 실제로 피해가 적용된 대상 수다 </summary>
        void OnAttackStruck(int castInstanceId, int attackId, double strikeServerTime, int hitCount);

        /// <summary> 판정 전에 공격이 취소됐다 </summary>
        void OnAttackCancelled(int castInstanceId, int attackId);

        /// <summary> 자리 유지에 성공했다, 구조물과 공격 표시는 정리하고 반격 시간이 끝나는 시각까지 보스는 피해를 받는다 </summary>
        void OnFormationSucceeded(int castInstanceId, double serverTime, double counterEndServerTime);

        /// <summary> 반격 시간이 끝났다 </summary>
        void OnCounterEnded(int castInstanceId, double serverTime);

        /// <summary> 자리를 채울 수 없어 실패했다 </summary>
        void OnFormationFailed(int castInstanceId, double serverTime, EWolbaekFormationFail reason);

        /// <summary> 진행하지 못하고 멈췄다, 성공도 실패도 아니다. 표시를 정리하고 원인을 알린다 </summary>
        void OnFormationHalted(int castInstanceId, double serverTime, EWolbaekHaltReason reason);

        /// <summary> 사망이나 전투 종료로 표시를 정리한다 </summary>
        void OnFormationCleared(int castInstanceId, EBossInterruptReason reason);
    }
}
