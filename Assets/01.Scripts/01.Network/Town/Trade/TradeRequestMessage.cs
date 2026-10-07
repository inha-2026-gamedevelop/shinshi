// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // Request만 TradeId가 빈 값이다. 그 뒤 모든 요청은 TradeId를 싣고, 현재 거래와 다르면 마을 서버가 버린다.
    // Lock과 Accept는 클라이언트가 본 OfferRevision, ConfirmGeneration을 싣는다. 지금 값과 다르면 무시하고 최신 상태를 다시 보낸다.
    // 종류별 인자: Request targetName / Respond accept / SetOffer lines, currency / Lock, Accept offerRevision, confirmGeneration /
    // Unlock, Cancel 없음
    /// <summary> 클라이언트의 1:1 거래 요청, 결과는 TradeResultMessage와 TradeStateMessage로 온다 </summary>
    public readonly struct TradeRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid              _requestId;
        private readonly Guid              _tradeId;
        private readonly ETradeAction      _action;
        private readonly string            _targetName;
        private readonly bool              _accept;
        private readonly TradeOfferLine[]  _lines;
        private readonly long              _currency;
        private readonly int               _offerRevision;
        private readonly int               _confirmGeneration;

        /****************************************
        *              Properties
        ****************************************/

        public Guid             RequestId         => _requestId;
        public Guid             TradeId           => _tradeId;
        public ETradeAction     Action            => _action;
        public string           TargetName        => _targetName;
        public bool             Accept            => _accept;
        public TradeOfferLine[] Lines             => _lines ?? Array.Empty<TradeOfferLine>();
        public long             Currency          => _currency;
        public int              OfferRevision     => _offerRevision;
        public int              ConfirmGeneration => _confirmGeneration;

        /****************************************
        *                Methods
        ****************************************/

        public TradeRequestMessage(Guid requestId, Guid tradeId, ETradeAction action, string targetName, bool accept,
            TradeOfferLine[] lines, long currency, int offerRevision, int confirmGeneration)
        {
            _requestId         = requestId;
            _tradeId           = tradeId;
            _action            = action;
            _targetName        = targetName;
            _accept            = accept;
            _lines             = lines;
            _currency          = currency;
            _offerRevision     = offerRevision;
            _confirmGeneration = confirmGeneration;
        }
    }
}
