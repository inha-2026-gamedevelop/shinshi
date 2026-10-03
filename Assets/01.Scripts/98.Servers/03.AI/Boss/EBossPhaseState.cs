namespace Shinshi.AI
{
    /// <summary> 페이즈 FSM의 상태, 새 패턴 선택은 Active일 때만 허용된다 </summary>
    public enum EBossPhaseState
    {
        Active            = 0,
        // 전환 조건에 닿았고 진행 중인 공격과 지속물을 정리하는 중이다
        TransitionPending = 1,
        // 정리를 마쳤고 페이즈 번호는 이미 올라갔다. 인계 완료(CompleteHandoff)가 오기 전에는 새 선택을 하지 않는다
        AwaitingHandoff   = 2,
        // 인계는 끝났지만 이 페이즈에 실행 가능한 패턴이 없다(데이터 미지정이거나 공격 미구현). 새 선택도 오류 로그도 없이 기다리며,
        // 실행 가능한 패턴이 생기면 다음 확인(EmptyWeightRetrySeconds 주기)에서 Active로 돌아간다
        AwaitingReadiness = 3
    }
}
