// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 거래가 바뀔 때마다 두 참가자에게 각자 기준(나, 상대)으로 보낸다. 거래가 끝나면 Done, Failed, Cancelled를 한 번 보낸다.
    // Reason은 상태가 이렇게 된 이유다(취소 사유, 확정 거절로 편집에 돌아온 사유), 평소에는 None이다.
    // 카운트다운은 서버 종료 시각까지 남은 초로 보낸다. 클라이언트는 받은 시각에 더해 종료 시각을 정하고 표시만 한다
    /// <summary> 1:1 거래 한 건의 현재 상태, 참가자에게만 간다 </summary>
    public readonly struct TradeStateMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid             _tradeId;
        private readonly ETradeState      _state;
        private readonly ERejectReason    _reason;
        private readonly string           _partnerName;
        private readonly TradeOfferLine[] _myLines;
        private readonly TradeOfferLine[] _partnerLines;
        private readonly long             _myCurrency;
        private readonly long             _partnerCurrency;
        private readonly bool             _myLocked;
        private readonly bool             _partnerLocked;
        private readonly bool             _myAccepted;
        private readonly bool             _partnerAccepted;
        private readonly int              _offerRevision;
        private readonly int              _confirmGeneration;
        private readonly float            _countdownRemaining;

        /****************************************
        *              Properties
        ****************************************/

        public Guid             TradeId            => _tradeId;
        public ETradeState      State              => _state;
        public ERejectReason    Reason             => _reason;
        public string           PartnerName        => _partnerName;
        public TradeOfferLine[] MyLines            => _myLines ?? Array.Empty<TradeOfferLine>();
        public TradeOfferLine[] PartnerLines       => _partnerLines ?? Array.Empty<TradeOfferLine>();
        public long             MyCurrency         => _myCurrency;
        public long             PartnerCurrency    => _partnerCurrency;
        public bool             MyLocked           => _myLocked;
        public bool             PartnerLocked      => _partnerLocked;
        public bool             MyAccepted         => _myAccepted;
        public bool             PartnerAccepted    => _partnerAccepted;
        public int              OfferRevision      => _offerRevision;
        public int              ConfirmGeneration  => _confirmGeneration;

        // Countdown 상태에서 수락이 열릴 때까지 남은 초, 그 밖에는 0
        public float            CountdownRemaining => _countdownRemaining;

        public bool IsFinished => (_state == ETradeState.Done) || (_state == ETradeState.Failed) || (_state == ETradeState.Cancelled);

        /****************************************
        *                Methods
        ****************************************/

        public TradeStateMessage(Guid tradeId, ETradeState state, ERejectReason reason, string partnerName, TradeOfferLine[] myLines,
            TradeOfferLine[] partnerLines, long myCurrency, long partnerCurrency, bool myLocked, bool partnerLocked, bool myAccepted,
            bool partnerAccepted, int offerRevision, int confirmGeneration, float countdownRemaining)
        {
            _tradeId            = tradeId;
            _state              = state;
            _reason             = reason;
            _partnerName        = partnerName;
            _myLines            = myLines;
            _partnerLines       = partnerLines;
            _myCurrency         = myCurrency;
            _partnerCurrency    = partnerCurrency;
            _myLocked           = myLocked;
            _partnerLocked      = partnerLocked;
            _myAccepted         = myAccepted;
            _partnerAccepted    = partnerAccepted;
            _offerRevision      = offerRevision;
            _confirmGeneration  = confirmGeneration;
            _countdownRemaining = countdownRemaining;
        }
    }
}
