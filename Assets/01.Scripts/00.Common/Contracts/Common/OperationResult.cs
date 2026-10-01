// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 상태를 바꾸는 요청의 공통 응답, 같은 operationId로 다시 보내면 처음 확정한 결과를 그대로 받는다 </summary>
    [DataContract]
    public sealed class OperationResult
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]       private string _operationId;

        // ContractCodes.OperationStatus, PENDING은 성공이 아니므로 확정 연출을 하지 않는다
        [DataMember(Name = "status")]            private int    _status;

        // ContractCodes.RejectReason, REJECTED일 때만 의미가 있다
        [DataMember(Name = "reasonCode")]        private int    _reasonCode;

        [DataMember(Name = "committedRevision")] private long   _committedRevision;
        [DataMember(Name = "serverTimeUnixMs")]  private long   _serverTimeUnixMs;
        [DataMember(Name = "receiptId")]         private string _receiptId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId       => _operationId;
        public int    Status            => _status;
        public int    ReasonCode        => _reasonCode;
        public long   CommittedRevision => _committedRevision;
        public long   ServerTimeUnixMs  => _serverTimeUnixMs;
        public string ReceiptId         => _receiptId;

        /****************************************
        *                Methods
        ****************************************/

        public OperationResult(string operationId, int status, int reasonCode, long committedRevision,
            long serverTimeUnixMs, string receiptId)
        {
            _operationId       = operationId;
            _status            = status;
            _reasonCode        = reasonCode;
            _committedRevision = committedRevision;
            _serverTimeUnixMs  = serverTimeUnixMs;
            _receiptId         = receiptId;
        }

        private OperationResult() { }
    }
}
