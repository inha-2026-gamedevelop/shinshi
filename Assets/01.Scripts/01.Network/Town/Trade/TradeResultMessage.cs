// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 요청이 받아들여졌는지만 알린다. 거래가 어떻게 바뀌었는지는 TradeStateMessage가 알린다.
    // Request의 결과에는 새로 만든 TradeId가 실린다
    /// <summary> 거래 요청 한 건의 결과, 요청한 클라이언트에게만 온다 </summary>
    public readonly struct TradeResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid          _requestId;
        private readonly Guid          _tradeId;
        private readonly ETradeAction  _action;
        private readonly ERejectReason _reason;

        /****************************************
        *              Properties
        ****************************************/

        public Guid          RequestId  => _requestId;
        public Guid          TradeId    => _tradeId;
        public ETradeAction  Action     => _action;
        public ERejectReason Reason     => _reason;
        public bool          IsAccepted => _reason == ERejectReason.None;

        /****************************************
        *                Methods
        ****************************************/

        public TradeResultMessage(Guid requestId, Guid tradeId, ETradeAction action, ERejectReason reason)
        {
            _requestId = requestId;
            _tradeId   = tradeId;
            _action    = action;
            _reason    = reason;
        }
    }
}
