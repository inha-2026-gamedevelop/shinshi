// System
using System;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> operations 조회 행. SQL은 FROM operations AS operation 별칭을 쓴다 </summary>
    internal sealed class AdminOperationRow
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SELECT_COLUMNS =
            """
            operation.actor_id AS "ActorId", operation.operation_id AS "OperationId",
                operation.operation_kind AS "OperationKind", operation.status AS "Status",
                operation.request_hash AS "RequestHash", operation.result_json::text AS "ResultJson",
                operation.committed_revision AS "CommittedRevision", operation.receipt_id AS "ReceiptId",
                operation.created_at AS "CreatedAt", operation.committed_at AS "CommittedAt"
            """;

        /****************************************
        *              Properties
        ****************************************/

        public Guid      ActorId           { get; set; }
        public Guid      OperationId       { get; set; }
        public string    OperationKind     { get; set; } = string.Empty;
        public string    Status            { get; set; } = string.Empty;
        public string    RequestHash       { get; set; } = string.Empty;
        public string?   ResultJson        { get; set; }
        public long?     CommittedRevision { get; set; }
        public string?   ReceiptId         { get; set; }
        public DateTime  CreatedAt         { get; set; }
        public DateTime? CommittedAt       { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperationRecord ToContract()
        {
            return new AdminOperationRecord(ActorId.ToString("D"), OperationId.ToString("D"), OperationKind, Status,
                RequestHash, AdminLogValues.ToJson(ResultJson), CommittedRevision, ReceiptId,
                AdminLogValues.ToUnixMs(CreatedAt), AdminLogValues.ToUnixMs(CommittedAt));
        }
    }
}
