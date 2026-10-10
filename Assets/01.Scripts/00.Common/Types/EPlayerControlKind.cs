namespace Shinshi.Common
{
    /// <summary> 플레이어가 받는 제어 종류, 복제 상태가 값 하나로 싣는다 </summary>
    public enum EPlayerControlKind : byte
    {
        None  = 0,
        Root  = 1,
        Stun  = 2
    }
}
