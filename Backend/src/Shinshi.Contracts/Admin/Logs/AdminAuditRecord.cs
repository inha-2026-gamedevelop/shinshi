// System
using System.Runtime.Serialization;

// Third Party
using Newtonsoft.Json.Linq;

namespace Shinshi.Contracts.Admin
{
    /// <summary> admin_audit_log 한 행. System 행은 adminId, adminRole이 null이다. 요청 IP는 HMAC의 hex 앞 8자만 보여
    /// 같은 출처인지만 대조할 수 있게 한다 </summary>
    [DataContract]
    public sealed class AdminAuditRecord
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "auditId")]             private string _auditId;
        [DataMember(Name = "occurredAtUnixMs")]    private long   _occurredAtUnixMs;
        [DataMember(Name = "adminId")]             private string _adminId;
        [DataMember(Name = "actorKind")]           private string _actorKind;
        [DataMember(Name = "adminRole")]           private int?   _adminRole;
        [DataMember(Name = "action")]              private string _action;
        [DataMember(Name = "targetKind")]          private string _targetKind;
        [DataMember(Name = "targetId")]            private string _targetId;
        [DataMember(Name = "beforeState")]         private JToken _beforeState;
        [DataMember(Name = "afterState")]          private JToken _afterState;
        [DataMember(Name = "reason")]              private string _reason;
        [DataMember(Name = "operationId")]         private string _operationId;
        [DataMember(Name = "approvalId")]          private string _approvalId;
        [DataMember(Name = "selfApproved")]        private bool   _selfApproved;
        [DataMember(Name = "requestIpHashPrefix")] private string _requestIpHashPrefix;

        /****************************************
        *              Properties
        ****************************************/

        public string AuditId             => _auditId;
        public long   OccurredAtUnixMs    => _occurredAtUnixMs;
        public string AdminId             => _adminId;
        public string ActorKind           => _actorKind;
        public int?   AdminRole           => _adminRole;
        public string Action              => _action;
        public string TargetKind          => _targetKind;
        public string TargetId            => _targetId;
        public JToken BeforeState         => _beforeState;
        public JToken AfterState          => _afterState;
        public string Reason              => _reason;
        public string OperationId         => _operationId;
        public string ApprovalId          => _approvalId;
        public bool   SelfApproved        => _selfApproved;
        public string RequestIpHashPrefix => _requestIpHashPrefix;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAuditRecord(string auditId, long occurredAtUnixMs, string adminId, string actorKind, int? adminRole,
            string action, string targetKind, string targetId, JToken beforeState, JToken afterState, string reason,
            string operationId, string approvalId, bool selfApproved, string requestIpHashPrefix)
        {
            _auditId             = auditId;
            _occurredAtUnixMs    = occurredAtUnixMs;
            _adminId             = adminId;
            _actorKind           = actorKind;
            _adminRole           = adminRole;
            _action              = action;
            _targetKind          = targetKind;
            _targetId            = targetId;
            _beforeState         = beforeState;
            _afterState          = afterState;
            _reason              = reason;
            _operationId         = operationId;
            _approvalId          = approvalId;
            _selfApproved        = selfApproved;
            _requestIpHashPrefix = requestIpHashPrefix;
        }

        private AdminAuditRecord() { }
    }
}
