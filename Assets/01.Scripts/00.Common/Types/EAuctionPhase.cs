namespace Shinshi.Common
{
    /// <summary> 클라이언트에 알리는 파티 경매 품목 단계, 네트워크로 보내므로 값을 바꾸지 않는다 </summary>
    public enum EAuctionPhase
    {
        None      = 0,

        // 품목을 여는 중이다(API 개시 확인 전)
        Opening   = 1,

        // 입찰을 받는다
        Open      = 2,

        // 마감 시각이 지나 낙찰과 분배를 확정하는 중이다
        Closing   = 3,

        Sold      = 4,
        Unsold    = 5,

        // 품목을 열지 못했거나 마감을 확정하지 못했다. 예약금은 API 복구가 돌려준다
        Cancelled = 6
    }
}
