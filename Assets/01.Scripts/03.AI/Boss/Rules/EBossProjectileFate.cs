namespace Shinshi.AI
{
    /// <summary> 투사체 표현 한 건의 상태, Flying 말고는 끝이라 다시 살아나지 않는다 </summary>
    public enum EBossProjectileFate : byte
    {
        /// <summary> 날고 있다 </summary>
        Flying = 0,

        /// <summary> 서버가 첫 대상에 한 번 판정하고 사라졌다고 알렸다 </summary>
        Hit = 1,

        /// <summary> 벽 앞에서 피해 없이 사라졌다 </summary>
        Wall = 2,

        /// <summary> 최대 사거리에서 사라졌다 </summary>
        Range = 3,

        /// <summary> 보스 사망, 페이즈 전환, 전투 종료로 제거됐거나 복원이 끝난 것으로 판단해 지웠다 </summary>
        Cancelled = 4
    }
}
