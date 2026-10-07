namespace Shinshi.Network
{
    /// <summary> 유저 경매장 요청 종류, 값은 직렬화되므로 바꾸지 않는다 </summary>
    public enum EMarketAction
    {
        None     = 0,
        Search   = 1,
        Listings = 2,
        Mine     = 3,
        History  = 4,
        List     = 5,
        Buy      = 6,
        Cancel   = 7,
        Ack      = 8
    }
}
