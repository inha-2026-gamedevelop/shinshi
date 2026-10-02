namespace Shinshi.AI
{
    /// <summary> 장판 표현 한 개의 상태, Active 말고는 끝이라 다시 살아나지 않는다 </summary>
    public enum EBossAreaFate : byte
    {
        /// <summary> 서버가 활성을 알렸고 지속 시간 안이다 </summary>
        Active = 0,

        /// <summary> 지속 시간이 끝나 사라졌다, 끝날 때 추가 타격은 없다 </summary>
        Expired = 1,

        /// <summary> 보스 사망, 페이즈 전환, 전투 종료로 즉시 제거됐다 </summary>
        Cancelled = 2
    }
}
