// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 입찰자는 서버가 인증된 연결의 참가자 번호로 정한다. 메시지에 참가자를 싣지 않는다
    /// <summary> 클라이언트의 입찰 요청, 결과는 AuctionBidResultMessage로 온다 </summary>
    public readonly struct AuctionBidMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly long _amount;

        /****************************************
        *              Properties
        ****************************************/

        public long Amount => _amount;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionBidMessage(long amount)
        {
            _amount = amount;
        }
    }
}
