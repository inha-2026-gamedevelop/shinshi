namespace Shinshi.Network
{
    /// <summary> 1:1 거래 요청 종류, 값은 직렬화되므로 바꾸지 않는다 </summary>
    public enum ETradeAction
    {
        None     = 0,
        Request  = 1,
        Respond  = 2,
        SetOffer = 3,
        Lock     = 4,
        Unlock   = 5,
        Accept   = 6,
        Cancel   = 7
    }

    // Locked는 한쪽만 확정한 상태, Countdown은 양쪽 확정 뒤 대기, Accepting은 대기가 끝나 수락을 받는 상태다.
    // Committing부터는 취소와 제안 변경이 막힌다. 값은 직렬화되므로 바꾸지 않는다
    /// <summary> 1:1 거래 진행 단계 </summary>
    public enum ETradeState
    {
        None       = 0,
        Requested  = 1,
        Editing    = 2,
        Locked     = 3,
        Countdown  = 4,
        Accepting  = 5,
        Committing = 6,
        Done       = 7,
        Failed     = 8,
        Cancelled  = 9
    }
}
