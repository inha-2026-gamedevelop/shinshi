// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 던전 서버가 정산을 마친 보스 성공 출정에서 경매 품목 하나를 연다, 자격 집합은 이 순간 접속한 원래 참가자로 동결된다 </summary>
    [DataContract]
    public sealed class AuctionOpenRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 응답을 잃으면 새 ID로 다시 보내지 않고 같은 ID로 재시도한다
        [DataMember(Name = "operationId")]            private string   _operationId;

        // 던전 서버가 만든 품목 ID, 입찰과 마감 경로에 그대로 쓴다
        [DataMember(Name = "auctionId")]              private string   _auctionId;

        // 희귀 장신구 재료의 GameDB 아이템 ID
        [DataMember(Name = "definitionId")]           private string   _definitionId;

        // 자격자가 한 명이면 API가 단독 최저가 구매로 연다
        [DataMember(Name = "eligibleParticipantIds")] private string[] _eligibleParticipantIds;

        /****************************************
        *              Properties
        ****************************************/

        public string   OperationId            => _operationId;
        public string   AuctionId              => _auctionId;
        public string   DefinitionId           => _definitionId;
        public string[] EligibleParticipantIds => _eligibleParticipantIds;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionOpenRequest(string operationId, string auctionId, string definitionId, string[] eligibleParticipantIds)
        {
            _operationId            = operationId;
            _auctionId              = auctionId;
            _definitionId           = definitionId;
            _eligibleParticipantIds = eligibleParticipantIds;
        }

        private AuctionOpenRequest() { }
    }
}
