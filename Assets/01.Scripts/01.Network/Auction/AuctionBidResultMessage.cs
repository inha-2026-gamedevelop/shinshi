// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 입찰 한 건의 결과, 입찰한 클라이언트에게만 온다. None이면 최고가로 확정됐다 </summary>
    public readonly struct AuctionBidResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly long          _amount;
        private readonly ERejectReason _reason;

        /****************************************
        *              Properties
        ****************************************/

        public long          Amount     => _amount;
        public ERejectReason Reason     => _reason;
        public bool          IsAccepted => _reason == ERejectReason.None;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionBidResultMessage(long amount, ERejectReason reason)
        {
            _amount = amount;
            _reason = reason;
        }
    }
}
