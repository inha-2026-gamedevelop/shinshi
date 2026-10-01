namespace Shinshi.AI
{
    /// <summary> 보스 상위 FSM의 상태 </summary>
    public enum EBossFlowState
    {
        Inactive  = 0,
        // 보스는 컷씬 전에 미리 스폰되어 있고, 시작 시각까지 타깃 선택, 공격, 피해 처리를 하지 않고 기다린다
        Appearing = 1,
        Combat    = 2,
        // 보스 사망이 확정됐고 전투 종료 요청은 없었다
        Defeated  = 3,
        // 전투 종료 요청(전멸 등)으로 끝났다. 같은 시각에 보스 사망이 있었어도 이 상태이며 IsBossDeathRecorded로 확인한다
        Ended     = 4
    }
}
