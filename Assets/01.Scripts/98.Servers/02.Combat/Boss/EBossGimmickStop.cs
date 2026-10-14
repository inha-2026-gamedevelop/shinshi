namespace Shinshi.Combat
{
    /// <summary> 보스 기믹이 세션에 알리는 멈춤의 종류 </summary>
    public enum EBossGimmickStop
    {
        None   = 0,
        Halted = 1, // 창구 장애로 더 진행할 수 없다, 성공도 실패도 아니다
        Failed = 2  // 전멸이 아닌 보스전 실패
    }
}
