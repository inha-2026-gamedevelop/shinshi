// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    // 접속 중인 자격자의 입찰만 던전 서버가 전달한다. operationId는 클라이언트가 만든 값을 그대로 싣고,
    // API는 참가자의 계정을 행위자로 써서 같은 계정, 같은 ID의 재시도에 최초 결과를 돌려준다
    /// <summary> 경매 품목 하나에 대한 입찰, 승인되면 입찰금을 계정 엽전에서 예약하고 이전 최고가 예약을 푼다 </summary>
    [DataContract]
    public sealed class AuctionBidRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "participantId")]    private string _participantId;

        // 정수 엽전. 첫 입찰은 최저가 이상, 이후 직전 최고가 + 최소 증액 이상이다
        [DataMember(Name = "amount")]           private long   _amount;

        // 입찰자가 본 입찰 순번. 그 사이 다른 입찰이 승인됐으면 StaleRevision으로 거절한다
        [DataMember(Name = "expectedBidSeq")]   private long   _expectedBidSeq;

        // 던전 서버가 이 입찰을 접수한 시각(유닉스 ms). 마감 연장 판정은 던전이 하고 API는 기록과 절대 상한 검사에만 쓴다
        [DataMember(Name = "acceptedAtUnixMs")] private long   _acceptedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public string ParticipantId    => _participantId;
        public long   Amount           => _amount;
        public long   ExpectedBidSeq   => _expectedBidSeq;
        public long   AcceptedAtUnixMs => _acceptedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionBidRequest(string operationId, string participantId, long amount, long expectedBidSeq,
            long acceptedAtUnixMs)
        {
            _operationId      = operationId;
            _participantId    = participantId;
            _amount           = amount;
            _expectedBidSeq   = expectedBidSeq;
            _acceptedAtUnixMs = acceptedAtUnixMs;
        }

        private AuctionBidRequest() { }
    }
}
