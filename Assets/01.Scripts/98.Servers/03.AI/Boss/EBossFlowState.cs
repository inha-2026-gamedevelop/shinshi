namespace Shinshi.AI
{
    /// <summary> 보스 상위 FSM의 상태 </summary>
    public enum EBossFlowState
    {
        Inactive = 0,
        Combat   = 1,
        // 사망 확정으로 끝났다
        Defeated = 2,
        // 전투 종료 요청으로 끝났다, 승패 판정은 세션이 한다
        Ended    = 3
    }
}
