// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 경매 단계(state Auction) 출정 한 줄. overdue는 경매 단계 기한에 복구 유예까지 지난 것, instanceLost는 배정 boot의
    /// 생존 증거가 장애 취소 기준보다 오래 없고 다른 서버는 살아 있는 것(자동 복구가 경매를 닫는 조건)이다. 페이지의 livenessDeferred가
    /// true인 동안 instanceLost는 항상 false다. 진행 중 품목(Open, Closing)이 없으면 auctionId부터 hardEndsAtUnixMs까지 null 또는 0이다 </summary>
    [DataContract]
    public sealed class AdminAuctionSession
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "sessionId")]            private string _sessionId;
        [DataMember(Name = "instanceId")]           private string _instanceId;
        [DataMember(Name = "auctionEndsAtUnixMs")]  private long   _auctionEndsAtUnixMs;
        [DataMember(Name = "overdue")]              private bool   _overdue;
        [DataMember(Name = "instanceLost")]         private bool   _instanceLost;

        // 진행 중 품목. 출정 하나는 품목을 하나씩 진행한다
        [DataMember(Name = "auctionId")]            private string _auctionId;
        [DataMember(Name = "itemState")]            private string _itemState;
        [DataMember(Name = "definitionId")]         private string _definitionId;
        [DataMember(Name = "bidSeq")]               private long   _bidSeq;
        [DataMember(Name = "hardEndsAtUnixMs")]     private long   _hardEndsAtUnixMs;

        // 진행 중 품목의 최고 입찰 예약금 합계(엽전). 복구가 품목을 취소하면 이 금액의 예약이 풀린다
        [DataMember(Name = "reservedCurrency")]     private long   _reservedCurrency;

        /****************************************
        *              Properties
        ****************************************/

        public string SessionId           => _sessionId;
        public string InstanceId          => _instanceId;
        public long   AuctionEndsAtUnixMs => _auctionEndsAtUnixMs;
        public bool   Overdue             => _overdue;
        public bool   InstanceLost        => _instanceLost;
        public string AuctionId           => _auctionId;
        public string ItemState           => _itemState;
        public string DefinitionId        => _definitionId;
        public long   BidSeq              => _bidSeq;
        public long   HardEndsAtUnixMs    => _hardEndsAtUnixMs;
        public long   ReservedCurrency    => _reservedCurrency;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAuctionSession(string sessionId, string instanceId, long auctionEndsAtUnixMs, bool overdue,
            bool instanceLost, string auctionId, string itemState, string definitionId, long bidSeq,
            long hardEndsAtUnixMs, long reservedCurrency)
        {
            _sessionId           = sessionId;
            _instanceId          = instanceId;
            _auctionEndsAtUnixMs = auctionEndsAtUnixMs;
            _overdue             = overdue;
            _instanceLost        = instanceLost;
            _auctionId           = auctionId;
            _itemState           = itemState;
            _definitionId        = definitionId;
            _bidSeq              = bidSeq;
            _hardEndsAtUnixMs    = hardEndsAtUnixMs;
            _reservedCurrency    = reservedCurrency;
        }

        private AdminAuctionSession() { }
    }
}
