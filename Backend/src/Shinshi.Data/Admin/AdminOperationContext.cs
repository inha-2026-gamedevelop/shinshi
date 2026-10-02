// System
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Data.Admin
{
    /// <summary> 어드민 쓰기 효과 콜백이 받는 실행 문맥. Execution의 연결과 트랜잭션만 쓰고, 확정하는 작업은 감사 행을 하나 이상 남겨야 한다 </summary>
    public sealed class AdminOperationContext
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IAdminAuditWriter _auditWriter;

        /****************************************
        *              Properties
        ****************************************/

        public OperationExecutionContext Execution { get; }
        public AdminIdentity Admin { get; }
        public Guid OperationId { get; }
        public string OperationKind { get; }
        public string Reason { get; }
        public byte[]? RequestIpHash { get; }

        // 이 콜백에서 쓴 감사 행 수. 실행기가 Committed 결정인데 0이면 전체를 롤백한다
        public int AuditCount { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperationContext(OperationExecutionContext execution, AdminIdentity admin, Guid operationId,
            string operationKind, string reason, byte[]? requestIpHash, IAdminAuditWriter auditWriter)
        {
            ArgumentNullException.ThrowIfNull(execution);
            ArgumentNullException.ThrowIfNull(admin);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            ArgumentNullException.ThrowIfNull(auditWriter);
            if (operationId == Guid.Empty)
            {
                throw new ArgumentException("Operation ID is required.", nameof(operationId));
            }

            Execution     = execution;
            Admin         = admin;
            OperationId   = operationId;
            OperationKind = operationKind;
            Reason        = reason;
            RequestIpHash = requestIpHash;
            _auditWriter  = auditWriter;
        }

        /// <summary> action은 작업 종류, 사유는 요청 reason으로 감사 행을 쓴다. 상태는 JSON 객체로 직렬화되는 값만 받는다 </summary>
        public Task AuditAsync(string targetKind, string targetId, object? beforeState, object? afterState,
            CancellationToken cancellationToken = default)
        {
            return AuditAsync(OperationKind, targetKind, targetId, beforeState, afterState, null, false, cancellationToken);
        }

        /// <summary> 한 작업이 여러 대상을 바꾸거나 승인 정보를 남길 때 쓴다 </summary>
        public async Task AuditAsync(string action, string targetKind, string targetId, object? beforeState,
            object? afterState, Guid? approvalId, bool selfApproved, CancellationToken cancellationToken = default)
        {
            AdminAuditEntry entry = AdminAuditEntry.ForAdmin(Admin, action, targetKind, targetId, Reason,
                AdminAuditJson.Serialize(beforeState), AdminAuditJson.Serialize(afterState), OperationId, approvalId,
                selfApproved, RequestIpHash);
            await _auditWriter.AppendAsync(Execution, entry, cancellationToken);
            ++AuditCount;
        }
    }
}
