// System
using System;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Admin
{
    /// <summary> admin_audit_log 한 행. 비밀번호, TOTP 비밀, 토큰, 초대 원문을 상태 JSON이나 사유에 넣지 않는다 </summary>
    public sealed class AdminAuditEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ADMIN_ACTOR  = "Admin";
        public const string SYSTEM_ACTOR = "System";

        // DB CHECK와 같은 한도
        public const int MAX_ACTION_LENGTH      = 100;
        public const int MAX_TARGET_KIND_LENGTH = 64;
        public const int MAX_TARGET_ID_LENGTH   = 200;
        public const int MAX_REASON_LENGTH      = 500;
        public const int REQUEST_IP_HASH_BYTES  = 32;

        /****************************************
        *              Properties
        ****************************************/

        public Guid AuditId { get; }
        public Guid? AdminId { get; }
        public string ActorKind { get; }
        public EAdminRole? AdminRole { get; }
        public string Action { get; }
        public string TargetKind { get; }
        public string TargetId { get; }
        public string? BeforeState { get; }
        public string? AfterState { get; }
        public string Reason { get; }
        public Guid? OperationId { get; }
        public Guid? ApprovalId { get; }
        public bool SelfApproved { get; }
        public byte[]? RequestIpHash { get; }

        /****************************************
        *                Methods
        ****************************************/

        private AdminAuditEntry(Guid? adminId, string actorKind, EAdminRole? adminRole, string action, string targetKind,
            string targetId, string? beforeState, string? afterState, string reason, Guid? operationId, Guid? approvalId,
            bool selfApproved, byte[]? requestIpHash)
        {
            RequireText(action, MAX_ACTION_LENGTH, nameof(action));
            RequireText(targetKind, MAX_TARGET_KIND_LENGTH, nameof(targetKind));
            RequireText(targetId, MAX_TARGET_ID_LENGTH, nameof(targetId));
            RequireText(reason, MAX_REASON_LENGTH, nameof(reason));
            if (beforeState != null)
            {
                LedgerJson.RequireObject(beforeState, nameof(beforeState));
            }
            if (afterState != null)
            {
                LedgerJson.RequireObject(afterState, nameof(afterState));
            }
            if ((operationId == Guid.Empty) || (approvalId == Guid.Empty) || (adminId == Guid.Empty))
            {
                throw new ArgumentException("Audit IDs cannot be empty.");
            }
            if ((requestIpHash != null) && (requestIpHash.Length != REQUEST_IP_HASH_BYTES))
            {
                throw new ArgumentException("Request IP hash must contain 32 bytes.", nameof(requestIpHash));
            }

            AuditId       = Guid.NewGuid();
            AdminId       = adminId;
            ActorKind     = actorKind;
            AdminRole     = adminRole;
            Action        = action;
            TargetKind    = targetKind;
            TargetId      = targetId;
            BeforeState   = beforeState;
            AfterState    = afterState;
            Reason        = reason;
            OperationId   = operationId;
            ApprovalId    = approvalId;
            SelfApproved  = selfApproved;
            RequestIpHash = requestIpHash;
        }

        /// <summary> 운영자 행동. operationId가 있으면 같은 트랜잭션의 Pending 작업이어야 기록된다 </summary>
        public static AdminAuditEntry ForAdmin(AdminIdentity admin, string action, string targetKind, string targetId,
            string reason, string? beforeState = null, string? afterState = null, Guid? operationId = null,
            Guid? approvalId = null, bool selfApproved = false, byte[]? requestIpHash = null)
        {
            ArgumentNullException.ThrowIfNull(admin);
            return ForAdminActor(admin.AdminId, admin.Role, action, targetKind, targetId, reason, beforeState,
                afterState, operationId, approvalId, selfApproved, requestIpHash);
        }

        /// <summary> 로그인 잠금처럼 세션 없이 운영자 본인에게 생긴 사건 </summary>
        public static AdminAuditEntry ForAdminActor(Guid adminId, EAdminRole role, string action, string targetKind,
            string targetId, string reason, string? beforeState = null, string? afterState = null,
            Guid? operationId = null, Guid? approvalId = null, bool selfApproved = false, byte[]? requestIpHash = null)
        {
            if (!Enum.IsDefined(role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            return new AdminAuditEntry(adminId, ADMIN_ACTOR, role, action, targetKind, targetId, beforeState,
                afterState, reason, operationId, approvalId, selfApproved, requestIpHash);
        }

        /// <summary> 최초 Owner 부트스트랩처럼 운영자 없이 실행한 시스템 행동 </summary>
        public static AdminAuditEntry ForSystem(string action, string targetKind, string targetId, string reason,
            string? afterState = null)
        {
            return new AdminAuditEntry(null, SYSTEM_ACTOR, null, action, targetKind, targetId, null, afterState, reason,
                null, null, false, null);
        }

        private static void RequireText(string value, int maxLength, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || (value.Length > maxLength))
            {
                throw new ArgumentException("Audit text is empty or too long.", parameterName);
            }
        }
    }
}
