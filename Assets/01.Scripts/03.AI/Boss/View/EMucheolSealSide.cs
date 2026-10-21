namespace Shinshi.AI
{
    // 서버의 봉인 방향 구역(EMucheolSealDirection)에 대응하는 클라이언트 값이다. 클라이언트 뷰는 서버 어셈블리를 참조하지 않아 별도로 둔다.
    // 봉인이 표식을 내는 방향만 있고 정면은 표식이 없다. 순서는 봉인이 진행되는 순서와 같다
    /// <summary> 무철 방향 봉인 표식의 방향 </summary>
    public enum EMucheolSealSide
    {
        Right,

        Left,

        Rear
    }
}
