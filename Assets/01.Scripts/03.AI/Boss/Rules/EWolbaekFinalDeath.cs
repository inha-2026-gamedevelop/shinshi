namespace Shinshi.AI
{
    /// <summary> 최후의 영역이 서버에서 플레이어를 사망 처리하는 이유 </summary>
    public enum EWolbaekFinalDeath
    {
        // 외곽 바닥이 사라질 때 그 위에 서 있었다
        Fall,

        // 같은 순간 빨간 폭발 둘 이상에 실제로 맞았다
        DoubleBlast
    }
}
