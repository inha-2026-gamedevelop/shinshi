namespace Shinshi.Combat
{
    /// <summary> 플레이어에게 제어를 걸어 본 결과, 적 두뇌는 피해 결과와 별개로 이 값을 읽는다 </summary>
    public enum EPlayerControlResult
    {
        // 종류나 시간이 쓸 수 없는 값이다
        Invalid   = 0,

        // 죽었거나 관전 중이라 걸지 않았다
        NotAlive  = 1,

        // 직전 제어가 끝난 뒤 면역 시간이라 걸지 않았다, 피해는 그대로 받는다
        Immune    = 2,

        // 이 종류가 새로 걸렸다
        Applied   = 3,

        // 같은 종류가 이미 걸려 있었고 새 시간이 더 길어 종료 시각만 늘었다
        Extended  = 4,

        // 같은 종류가 이미 걸려 있었고 남은 시간이 더 길거나 같아 그대로다
        Unchanged = 5
    }
}
