// System
using System;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> admin_audit_log 조회 행. SQL은 FROM admin_audit_log AS audit 별칭을 쓴다. 요청 IP HMAC 원본은 응답에 싣지 않는다 </summary>
    internal sealed class AdminAuditRow
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SELECT_COLUMNS =
            """
            audit.audit_id AS "AuditId", audit.occurred_at AS "OccurredAt", audit.admin_id AS "AdminId",
                audit.actor_kind AS "ActorKind", audit.admin_role AS "AdminRole", audit.action AS "Action",
                audit.target_kind AS "TargetKind", audit.target_id AS "TargetId",
                audit.before_state::text AS "BeforeState", audit.after_state::text AS "AfterState",
                audit.reason AS "Reason", audit.operation_id AS "OperationId", audit.approval_id AS "ApprovalId",
                audit.self_approved AS "SelfApproved", audit.request_ip_hash AS "RequestIpHash"
            """;

        /****************************************
        *              Properties
        ****************************************/

        public Guid     AuditId       { get; set; }
        public DateTime OccurredAt    { get; set; }
        public Guid?    AdminId       { get; set; }
        public string   ActorKind     { get; set; } = string.Empty;
        public int?     AdminRole     { get; set; }
        public string   Action        { get; set; } = string.Empty;
        public string   TargetKind    { get; set; } = string.Empty;
        public string   TargetId      { get; set; } = string.Empty;
        public string?  BeforeState   { get; set; }
        public string?  AfterState    { get; set; }
        public string   Reason        { get; set; } = string.Empty;
        public Guid?    OperationId   { get; set; }
        public Guid?    ApprovalId    { get; set; }
        public bool     SelfApproved  { get; set; }
        public byte[]?  RequestIpHash { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminAuditRecord ToContract()
        {
            return new AdminAuditRecord(AuditId.ToString("D"), AdminLogValues.ToUnixMs(OccurredAt),
                AdminLogValues.ToText(AdminId), ActorKind, AdminRole, Action, TargetKind, TargetId,
                AdminLogValues.ToJson(BeforeState), AdminLogValues.ToJson(AfterState), Reason,
                AdminLogValues.ToText(OperationId), AdminLogValues.ToText(ApprovalId), SelfApproved,
                AdminLogValues.ToHashPrefix(RequestIpHash));
        }
    }
}
