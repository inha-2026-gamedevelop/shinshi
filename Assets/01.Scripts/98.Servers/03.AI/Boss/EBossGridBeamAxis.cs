namespace Shinshi.AI
{
    // 월드 축 기준이다. 보스나 대상의 방향을 따라 돌지 않는다
    /// <summary> 격자 순차 빔의 줄 방향 </summary>
    public enum EBossGridBeamAxis
    {
        /// <summary> 월드 X 축과 나란한 줄, 월드 Z가 작은 쪽부터 큰 쪽으로 차례로 발사한다 </summary>
        Horizontal = 0,

        /// <summary> 월드 Z 축과 나란한 줄, 월드 X가 작은 쪽부터 큰 쪽으로 차례로 발사한다 </summary>
        Vertical   = 1
    }
}
