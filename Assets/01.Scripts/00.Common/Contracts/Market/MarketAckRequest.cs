// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 판매 결과 확인 표시. 경제 효과와 원장이 없다 </summary>
    [DataContract]
    public sealed class MarketAckRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // Guid "D" 형식, 재전송은 같은 값
        [DataMember(Name = "operationId")] private string   _operationId;
        // 비면 확인하지 않은 종료 등록 전체
        [DataMember(Name = "listingIds")]  private string[] _listingIds;

        /****************************************
        *              Properties
        ****************************************/

        public string   OperationId => _operationId;
        public string[] ListingIds  => _listingIds;

        /****************************************
        *                Methods
        ****************************************/

        public MarketAckRequest(string operationId, string[] listingIds)
        {
            _operationId = operationId;
            _listingIds  = listingIds;
        }

        private MarketAckRequest() { }
    }
}
