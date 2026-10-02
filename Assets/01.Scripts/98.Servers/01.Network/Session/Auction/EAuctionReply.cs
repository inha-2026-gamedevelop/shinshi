namespace Shinshi.Network
{
    /// <summary> 경매 API 응답 분류 </summary>
    public enum EAuctionReply
    {
        Committed = 0,
        Rejected  = 1,
        Retry     = 2
    }
}
