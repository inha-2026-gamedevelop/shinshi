namespace Shinshi.AI
{
    /// <summary> 투사체가 사라진 이유 </summary>
    public enum EBossProjectileEnd
    {
        /// <summary> 경로상 첫 플레이어나 소환물에 한 번 판정하고 사라졌다, 방어와 반격과 무적으로 막혀도 같다 </summary>
        Hit = 0,

        /// <summary> 벽이 먼저라 피해 없이 사라졌다 </summary>
        Wall = 1,

        /// <summary> 최대 사거리까지 가서 사라졌다 </summary>
        Range = 2,

        /// <summary> 보스 사망, 페이즈 전환, 전투 종료로 즉시 제거됐다 </summary>
        Cancelled = 3
    }
}
