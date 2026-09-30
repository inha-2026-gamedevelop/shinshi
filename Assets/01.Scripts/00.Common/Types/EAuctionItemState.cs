namespace Shinshi.Common
{
    /// <summary> 파티 경매 품목 상태 </summary>
    public enum EAuctionItemState
    {
        Pending         = 0,
        Open            = 1,
        Closing         = 2,
        Settling        = 3,
        Sold            = 4,
        Unsold          = 5,
        RecoveryPending = 6,
        Cancelled       = 7
    }
}
