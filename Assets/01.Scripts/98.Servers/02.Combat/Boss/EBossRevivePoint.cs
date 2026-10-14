namespace Shinshi.Combat
{
    /// <summary> 보스 기믹이 부활 지점에 대해 내는 답 </summary>
    public enum EBossRevivePoint
    {
        NotManaged  = 0, // 기믹이 관여하지 않는다, 기존 지점 선택을 쓴다
        Available   = 1, // 기믹이 정한 지점이 있다
        Unavailable = 2  // 기믹이 관여하지만 지금 설 수 있는 지점이 없다, 부활을 미룬다
    }
}
