namespace Shinshi.Common
{
    /// <summary> 궁수 기본 공격에 실린 속성, Q 얼음, W 바람, E 불로 바꾼다 </summary>
    public enum EArcherElement : byte
    {
        // 속성 기술을 아직 쓰지 않은 상태, 스택이 쌓이지 않는다
        None = 0,
        Ice  = 1,
        Wind = 2,
        Fire = 3
    }
}
