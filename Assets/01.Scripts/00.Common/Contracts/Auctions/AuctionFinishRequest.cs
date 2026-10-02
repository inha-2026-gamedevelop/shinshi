// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    // 열린 품목이나 정산 중인 품목이 남아 있으면 거절한다. 품목이 없었던 출정도 이 요청으로 닫는다
    /// <summary> 파티 경매 단계를 끝내고 출정을 닫는다, 참가자 정산 완료와 접속 임대 해제, 서버 반환을 한 번에 확정한다 </summary>
    [DataContract]
    public sealed class AuctionFinishRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")] private string _operationId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionFinishRequest(string operationId)
        {
            _operationId = operationId;
        }

        private AuctionFinishRequest() { }
    }
}
