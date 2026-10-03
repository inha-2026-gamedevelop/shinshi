namespace Shinshi.AI
{
    /// <summary> 결계 전개 시퀀스의 진행 상태 </summary>
    public enum EBossBarrierState
    {
        /// <summary> 아직 시작하지 않았다 </summary>
        Idle      = 0,

        /// <summary> 보스가 전개 위치로 이동 중이다 </summary>
        Moving    = 1,

        /// <summary> 방출과 위험 구역 예고 중이다, 피해는 없다 </summary>
        Telegraph = 2,

        /// <summary> 위험 구역이 활성됐고 전개 시퀀스가 끝나기를 기다린다 </summary>
        Deploying = 3,

        /// <summary> 시퀀스가 끝났다, 위험 구역은 정리될 때까지 유지된다 </summary>
        Completed = 4,

        /// <summary> 정리됐다, 같은 전투에서 다시 시작하지 않는다. 리셋만 처음으로 돌린다 </summary>
        Cleared   = 5
    }
}
