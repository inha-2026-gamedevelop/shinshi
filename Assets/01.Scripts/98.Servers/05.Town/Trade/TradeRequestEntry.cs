// System
using System;

namespace Shinshi.Network
{
    /// <summary> 아직 수락되지 않은 거래 요청 한 건 </summary>
    public readonly struct TradeRequestEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid   _tradeId;
        private readonly int    _requesterId;
        private readonly int    _targetId;
        private readonly double _expiresAt;

        /****************************************
        *              Properties
        ****************************************/

        public Guid   TradeId     => _tradeId;
        public int    RequesterId => _requesterId;
        public int    TargetId    => _targetId;
        public double ExpiresAt   => _expiresAt;

        /****************************************
        *                Methods
        ****************************************/

        public TradeRequestEntry(Guid tradeId, int requesterId, int targetId, double expiresAt)
        {
            _tradeId     = tradeId;
            _requesterId = requesterId;
            _targetId    = targetId;
            _expiresAt   = expiresAt;
        }
    }
}
