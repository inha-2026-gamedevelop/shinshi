// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 거래 확정. operationId는 prepare 요청의 commitOperationId와 같아야 한다 </summary>
    [DataContract]
    public sealed class TradeCommitRequest
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

        public TradeCommitRequest(string operationId)
        {
            _operationId = operationId;
        }

        private TradeCommitRequest() { }
    }
}
