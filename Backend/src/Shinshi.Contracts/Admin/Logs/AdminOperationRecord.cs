// System
using System.Runtime.Serialization;

// Third Party
using Newtonsoft.Json.Linq;

namespace Shinshi.Contracts.Admin
{
    /// <summary> operations 한 행. status는 Pending, Committed, Rejected이고 result는 저장된 OperationResult JSON 그대로다.
    /// 요청 본문은 저장하지 않으므로 해시만 보인다 </summary>
    [DataContract]
    public sealed class AdminOperationRecord
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "actorId")]           private string _actorId;
        [DataMember(Name = "operationId")]       private string _operationId;
        [DataMember(Name = "operationKind")]     private string _operationKind;
        [DataMember(Name = "status")]            private string _status;
        [DataMember(Name = "requestHash")]       private string _requestHash;
        [DataMember(Name = "result")]            private JToken _result;
        [DataMember(Name = "committedRevision")] private long?  _committedRevision;
        [DataMember(Name = "receiptId")]         private string _receiptId;
        [DataMember(Name = "createdAtUnixMs")]   private long   _createdAtUnixMs;
        [DataMember(Name = "committedAtUnixMs")] private long?  _committedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string ActorId           => _actorId;
        public string OperationId       => _operationId;
        public string OperationKind     => _operationKind;
        public string Status            => _status;
        public string RequestHash       => _requestHash;
        public JToken Result            => _result;
        public long?  CommittedRevision => _committedRevision;
        public string ReceiptId         => _receiptId;
        public long   CreatedAtUnixMs   => _createdAtUnixMs;
        public long?  CommittedAtUnixMs => _committedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperationRecord(string actorId, string operationId, string operationKind, string status,
            string requestHash, JToken result, long? committedRevision, string receiptId, long createdAtUnixMs,
            long? committedAtUnixMs)
        {
            _actorId           = actorId;
            _operationId       = operationId;
            _operationKind     = operationKind;
            _status            = status;
            _requestHash       = requestHash;
            _result            = result;
            _committedRevision = committedRevision;
            _receiptId         = receiptId;
            _createdAtUnixMs   = createdAtUnixMs;
            _committedAtUnixMs = committedAtUnixMs;
        }

        private AdminOperationRecord() { }
    }
}
