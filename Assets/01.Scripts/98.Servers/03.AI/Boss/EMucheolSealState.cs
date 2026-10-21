namespace Shinshi.AI
{
    /// <summary> 무철 방향 봉인 기믹의 진행 단계 </summary>
    public enum EMucheolSealState
    {
        // 체력이 발동 비율에 닿지 않았다
        Dormant,

        // 체력이 발동 비율에 닿았고 진행 중이던 패턴이 끝나 표식이 나오기를 기다린다, 이 동안의 타격은 타수에 넣지 않는다
        Pending,

        // 표식이 나와 있고 현재 방향의 타수를 센다
        Active,

        // 마지막 방향까지 채워 봉인이 깨졌다, 이후 타격부터 체력 피해가 다시 들어간다
        Broken,

        // 사망이나 전투 종료로 멈췄다
        Stopped
    }
}
