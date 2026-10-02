// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> SessionEnded 저장 뒤 같은 세션을 한 번만 정산한다 </summary>
    [DataContract]
    public sealed class SessionSettlementRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // SessionEnded 이벤트의 settlementOperationId와 같아야 한다
        [DataMember(Name = "operationId")] private string _operationId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;

        /****************************************
        *                Methods
        ****************************************/

        public SessionSettlementRequest(string operationId)
        {
            _operationId = operationId;
        }

        private SessionSettlementRequest() { }
    }
}
