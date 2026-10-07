// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 받은 거래 요청, 남은 초 안에 Respond로 tradeId를 보내면 거래가 열린다 </summary>
    public readonly struct TradeInviteMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid   _tradeId;
        private readonly string _fromName;
        private readonly float  _expiresIn;

        /****************************************
        *              Properties
        ****************************************/

        public Guid   TradeId   => _tradeId;
        public string FromName  => _fromName;
        public float  ExpiresIn => _expiresIn;

        /****************************************
        *                Methods
        ****************************************/

        public TradeInviteMessage(Guid tradeId, string fromName, float expiresIn)
        {
            _tradeId   = tradeId;
            _fromName  = fromName;
            _expiresIn = expiresIn;
        }
    }
}
