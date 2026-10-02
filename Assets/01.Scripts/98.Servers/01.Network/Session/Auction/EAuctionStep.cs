namespace Shinshi.Network
{
    /// <summary> 던전 서버의 파티 경매 진행 단계 </summary>
    public enum EAuctionStep
    {
        Idle      = 0,
        Opening   = 1,
        Open      = 2,
        Closing   = 3,
        Finishing = 4,
        Finished  = 5
    }
}
