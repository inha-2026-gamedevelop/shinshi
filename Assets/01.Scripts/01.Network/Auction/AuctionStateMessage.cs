// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 받는 사람마다 자격 여부가 달라 참가자별로 보낸다. 마감 시각은 서버 NetworkTime 기준이라 클라이언트는 NetworkTime.time과 비교한다
    /// <summary> 파티 경매 품목의 현재 상태, 단계나 최고가가 바뀔 때 서버가 출정 참가자에게 보낸다 </summary>
    public readonly struct AuctionStateMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EAuctionPhase _phase;
        private readonly EAuctionMode  _mode;
        private readonly string        _itemDefinitionId;
        private readonly long          _highBid;
        private readonly int           _highParticipantId;
        private readonly long          _minimumBid;
        private readonly double        _endsAt;
        private readonly bool          _isEligible;
        private readonly long          _payoutEach;

        /****************************************
        *              Properties
        ****************************************/

        public EAuctionPhase Phase            => _phase;
        public EAuctionMode  Mode             => _mode;
        public string        ItemDefinitionId => _itemDefinitionId;

        // 최고 입찰이 없으면 0이다
        public long          HighBid           => _highBid;
        public int           HighParticipantId => _highParticipantId;

        // 다음에 받는 가장 낮은 금액, 단독 모드는 최저가 하나다
        public long          MinimumBid        => _minimumBid;

        // 서버 NetworkTime 기준 마감 시각, 막판 입찰로 늘어날 수 있다
        public double        EndsAt            => _endsAt;

        // 품목을 열 때 접속한 원래 참가자라 입찰할 수 있다. 중간 복귀자는 거짓이다
        public bool          IsEligible        => _isEligible;

        // 낙찰 뒤 분배 대상 한 명이 받는 엽전, 그 밖에는 0
        public long          PayoutEach        => _payoutEach;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionStateMessage(EAuctionPhase phase, EAuctionMode mode, string itemDefinitionId, long highBid,
            int highParticipantId, long minimumBid, double endsAt, bool isEligible, long payoutEach)
        {
            _phase             = phase;
            _mode              = mode;
            _itemDefinitionId  = itemDefinitionId;
            _highBid           = highBid;
            _highParticipantId = highParticipantId;
            _minimumBid        = minimumBid;
            _endsAt            = endsAt;
            _isEligible        = isEligible;
            _payoutEach        = payoutEach;
        }
    }
}
