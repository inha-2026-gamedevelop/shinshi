namespace Shinshi.AI
{
    /// <summary> 페이즈 FSM의 상태, 새 패턴 선택은 Active일 때만 허용된다 </summary>
    public enum EBossPhaseState
    {
        Active            = 0,
        // 전환 조건에 닿았고 진행 중인 공격과 지속물을 정리하는 중이다
        TransitionPending = 1,
        // 정리를 마쳤고 다음 페이즈 실행기를 기다린다, 입력이 오기 전에는 새 선택을 하지 않는다
        AwaitingHandoff   = 2
    }
}
