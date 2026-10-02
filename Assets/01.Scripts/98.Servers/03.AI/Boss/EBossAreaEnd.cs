namespace Shinshi.AI
{
    /// <summary> 장판이 사라진 이유 </summary>
    public enum EBossAreaEnd
    {
        /// <summary> 지속 시간이 끝나 사라졌다, 끝날 때 추가 타격은 없다 </summary>
        Expired = 0,

        /// <summary> 보스 사망, 페이즈 전환, 전투 종료로 즉시 제거됐다 </summary>
        Cancelled = 1
    }
}
