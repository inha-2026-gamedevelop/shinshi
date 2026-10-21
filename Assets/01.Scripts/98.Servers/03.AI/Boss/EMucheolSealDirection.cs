namespace Shinshi.AI
{
    // 보스 몸체 기준 방향 구역이다. 봉인이 활성화하는 방향은 Right, Left, Rear뿐이고 Front는 판정 결과로만 나온다
    /// <summary> 무철 방향 봉인의 방향 구역 </summary>
    public enum EMucheolSealDirection
    {
        // 방향을 정하지 못했거나 활성 방향이 없다
        None,

        // 정면 구역, 봉인 방향이 아니라 타수에 들어가지 않는다
        Front,

        Right,

        Left,

        Rear
    }
}
