// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 취소. 대상 등록은 경로의 listingId다 </summary>
    [DataContract]
    public sealed class MarketCancelRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // Guid "D" 형식, 재전송은 같은 값
        [DataMember(Name = "operationId")] private string _operationId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;

        /****************************************
        *                Methods
        ****************************************/

        public MarketCancelRequest(string operationId)
        {
            _operationId = operationId;
        }

        private MarketCancelRequest() { }
    }
}
