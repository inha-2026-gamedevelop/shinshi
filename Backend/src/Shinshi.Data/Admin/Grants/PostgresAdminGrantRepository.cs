// System
using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Inbox;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Admin.Grants
{
    /// <summary> 운영자 지급 작성, 실행(우편함에 넣기), 미수령 취소, 조회. 쓰기는 AdminOperationExecutor의 효과 콜백에서만 부르고
    /// 같은 콜백에서 감사 행을 남긴다. 지급 행을 먼저 잠그므로 같은 지급의 실행과 취소는 서로 기다린다 </summary>
    public sealed class PostgresAdminGrantRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 지급 변경은 revision 계약이 없어 0으로 확정한다
        private const long NO_REVISION = 0;

        private const string LOCK_GRANT_SQL =
            "SELECT state FROM admin_grants WHERE grant_id = @GrantId FOR UPDATE;";

        private const string LOCK_EXECUTE_SQL =
            """
            SELECT state AS "State", source_kind AS "SourceKind", cardinality(target_account_ids) AS "TargetCount"
            FROM admin_grants WHERE grant_id = @GrantId FOR UPDATE;
            """;

        // 같은 복구 대상에 취소되지 않은 보정 지급이 이미 있다(0008의 부분 유일 인덱스)
        private const string RECOVERY_REF_INDEX = "ux_admin_grants_recovery_ref";

        // 우편 상태 집계. @GrantId와 @Now(DB 시각)가 필요하다
        private const string COUNT_INBOX_SQL =
            """
            SELECT count(*)::int AS "Delivered",
                count(*) FILTER (WHERE claimed_at IS NOT NULL)::int AS "Claimed",
                count(*) FILTER (WHERE cancelled_at IS NOT NULL)::int AS "Cancelled",
                count(*) FILTER (WHERE claimed_at IS NULL AND cancelled_at IS NULL AND expires_at <= @Now)::int AS "Expired",
                count(*) FILTER (WHERE claimed_at IS NULL AND cancelled_at IS NULL AND expires_at > @Now)::int AS "Open"
            FROM reward_inbox WHERE grant_id = @GrantId;
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminGrantRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 대상 계정이 모두 있을 때만 Draft로 만든다. 영수증 ID는 지급 ID다. 같은 sourceRef에 취소되지 않은 보정 지급이
        /// 이미 있으면(동시 작성 포함) INVALID_STATE다. 앞 지급을 취소하면 다시 작성할 수 있다 </summary>
        public async Task<OperationDecision> CreateAsync(AdminOperationContext context, AdminGrantDraft draft,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(draft);
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context.Execution);
            Guid[] targets = draft.TargetAccountIds.ToArray();
            int existing = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*)::int FROM accounts WHERE account_id = ANY(@Ids);", new { Ids = targets }, transaction,
                cancellationToken: cancellationToken));
            if (existing != targets.Length)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND);
            }

            // 중복 확인을 먼저 읽지 않고 유일 인덱스에 맡긴다. 읽고 넣으면 두 운영자가 동시에 작성할 때 둘 다 통과한다.
            // 위반은 트랜잭션을 중단시키지만 실행기가 거절 결정을 효과 savepoint로 되돌려 결과만 저장한다
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO admin_grants (grant_id, created_by, created_operation_id, reason, rewards, target_account_ids,
                        claim_days, source_kind, source_ref)
                    VALUES (@GrantId, @CreatedBy, @OperationId, @Reason, CAST(@Rewards AS jsonb), @Targets, @ClaimDays,
                        @SourceKind, @SourceRef);
                    """, new
                    {
                        draft.GrantId,
                        CreatedBy = context.Admin.AdminId,
                        context.OperationId,
                        context.Reason,
                        Rewards = RewardLines.Serialize(draft.Rewards),
                        Targets = targets,
                        draft.ClaimDays,
                        draft.SourceKind,
                        draft.SourceRef
                    }, transaction, cancellationToken: cancellationToken));
            }
            catch (PostgresException exception) when ((exception.SqlState == PostgresErrorCodes.UniqueViolation)
                && (exception.ConstraintName == RECOVERY_REF_INDEX))
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE);
            }

            string grantId = draft.GrantId.ToString("D");
            await context.AuditAsync(AdminGrantAuditActions.TARGET_GRANT, grantId, null, new
            {
                State = AdminGrantCodes.STATE_DRAFT,
                draft.SourceKind,
                draft.SourceRef,
                draft.ClaimDays,
                TargetCount = targets.Length,
                draft.Rewards
            }, cancellationToken);
            return OperationDecision.Committed(NO_REVISION, grantId);
        }

        /// <summary> Draft만 실행한다. 대상마다 우편 한 행을 넣고((grant_id, account_id) 유일이라 다시 넣지 않는다) Executed로 바꾼다.
        /// 일괄, 보정 지급을 Admin 아래 역할이 실행하면 잠근 행 기준으로 FORBIDDEN_ROLE이다.
        /// P0는 2인 승인이 없어 실행자 단독 승인으로 감사에 표시한다 </summary>
        public async Task<OperationDecision> ExecuteAsync(AdminOperationContext context, Guid grantId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context.Execution);
            ExecuteLockRow? locked = await connection.QuerySingleOrDefaultAsync<ExecuteLockRow>(new CommandDefinition(
                LOCK_EXECUTE_SQL, new { GrantId = grantId }, transaction, cancellationToken: cancellationToken));
            if (locked == null)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND);
            }
            if ((!context.Admin.HasRole(EAdminRole.Admin))
                && (AdminGrantPolicy.RequiresAdminToExecute(locked.TargetCount, locked.SourceKind)))
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.FORBIDDEN_ROLE);
            }
            if (locked.State != AdminGrantCodes.STATE_DRAFT)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE);
            }

            // 대상은 최대 1000명이고 한 문장의 집합 INSERT라 문장 제한 시간(5초) 안에 끝난다. 나눠 커밋하면
            // 일부만 들어간 Executed 상태가 생기므로 한 트랜잭션으로 둔다. 모든 행이 같은 DB 시각으로 기한을 받는다
            int delivered = await connection.ExecuteAsync(new CommandDefinition(
                """
                WITH now AS (SELECT clock_timestamp() AS at)
                INSERT INTO reward_inbox (inbox_id, account_id, source_kind, grant_id, source_ref, rewards, created_at,
                    expires_at)
                SELECT gen_random_uuid(), target.account_id, grant_row.source_kind, grant_row.grant_id,
                    COALESCE(grant_row.source_ref, grant_row.grant_id::text), grant_row.rewards, now.at,
                    now.at + make_interval(days => grant_row.claim_days)
                FROM admin_grants AS grant_row
                CROSS JOIN LATERAL unnest(grant_row.target_account_ids) AS target(account_id)
                CROSS JOIN now
                WHERE grant_row.grant_id = @GrantId
                ORDER BY target.account_id
                ON CONFLICT (grant_id, account_id) DO NOTHING;
                """, new { GrantId = grantId }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE admin_grants SET state = @Executed, executed_at = clock_timestamp(), executed_by = @AdminId
                WHERE grant_id = @GrantId;
                """, new { GrantId = grantId, Executed = AdminGrantCodes.STATE_EXECUTED, context.Admin.AdminId },
                transaction, cancellationToken: cancellationToken));

            string target = grantId.ToString("D");
            await context.AuditAsync(context.OperationKind, AdminGrantAuditActions.TARGET_GRANT, target,
                new { State = AdminGrantCodes.STATE_DRAFT },
                new { State = AdminGrantCodes.STATE_EXECUTED, Delivered = delivered }, null, true, cancellationToken);
            return OperationDecision.Committed(NO_REVISION, target);
        }

        /// <summary> Draft는 그대로 Cancelled로, Executed는 아직 기한 안의 미수령 우편만 취소한다. 이미 수령한 우편은 건드리지 않고
        /// 수령, 만료 수를 감사 결과에 남긴다. 수령과 동시에 오면 우편 행 잠금 순서대로 한쪽만 이긴다 </summary>
        public async Task<OperationDecision> CancelAsync(AdminOperationContext context, Guid grantId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context.Execution);
            string? state = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(LOCK_GRANT_SQL,
                new { GrantId = grantId }, transaction, cancellationToken: cancellationToken));
            if (state == null)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND);
            }
            if (state == AdminGrantCodes.STATE_CANCELLED)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE);
            }

            DateTime now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", transaction: transaction, cancellationToken: cancellationToken));
            int cancelledNow = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE reward_inbox SET cancelled_at = @Now
                WHERE grant_id = @GrantId AND claimed_at IS NULL AND cancelled_at IS NULL AND expires_at > @Now;
                """, new { GrantId = grantId, Now = now }, transaction, cancellationToken: cancellationToken));
            InboxCounts counts = await connection.QuerySingleAsync<InboxCounts>(new CommandDefinition(COUNT_INBOX_SQL,
                new { GrantId = grantId, Now = now }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE admin_grants SET state = @Cancelled, cancelled_at = @Now, cancelled_by = @AdminId
                WHERE grant_id = @GrantId;
                """, new { GrantId = grantId, Cancelled = AdminGrantCodes.STATE_CANCELLED, Now = now, context.Admin.AdminId },
                transaction, cancellationToken: cancellationToken));

            string target = grantId.ToString("D");
            await context.AuditAsync(AdminGrantAuditActions.TARGET_GRANT, target, new { State = state }, new
            {
                State = AdminGrantCodes.STATE_CANCELLED,
                CancelledNow = cancelledNow,
                counts.Delivered,
                counts.Claimed,
                counts.Expired
            }, cancellationToken);
            return OperationDecision.Committed(NO_REVISION, target);
        }

        public async Task<AdminGrantRecord?> ReadAsync(Guid grantId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            DateTime now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", cancellationToken: cancellationToken));
            GrantRow? row = await connection.QuerySingleOrDefaultAsync<GrantRow>(new CommandDefinition(
                """
                SELECT grant_id AS "GrantId", state AS "State", source_kind AS "SourceKind", source_ref AS "SourceRef",
                    reason AS "Reason", created_by AS "CreatedBy", created_at AS "CreatedAt", executed_at AS "ExecutedAt",
                    cancelled_at AS "CancelledAt", claim_days AS "ClaimDays", rewards::text AS "Rewards",
                    cardinality(target_account_ids) AS "TargetCount"
                FROM admin_grants WHERE grant_id = @GrantId;
                """, new { GrantId = grantId }, cancellationToken: cancellationToken));
            if (row == null)
            {
                return null;
            }

            InboxCounts counts = await connection.QuerySingleAsync<InboxCounts>(new CommandDefinition(COUNT_INBOX_SQL,
                new { GrantId = grantId, Now = now }, cancellationToken: cancellationToken));
            return new AdminGrantRecord
            {
                GrantId        = row.GrantId,
                State          = row.State,
                SourceKind     = row.SourceKind,
                SourceRef      = row.SourceRef,
                Reason         = row.Reason,
                CreatedBy      = row.CreatedBy,
                CreatedAt      = ToUtc(row.CreatedAt),
                ExecutedAt     = row.ExecutedAt == null ? null : ToUtc(row.ExecutedAt.Value),
                CancelledAt    = row.CancelledAt == null ? null : ToUtc(row.CancelledAt.Value),
                ClaimDays      = row.ClaimDays,
                Rewards        = RewardLines.Deserialize(row.Rewards),
                TargetCount    = row.TargetCount,
                DeliveredCount = counts.Delivered,
                ClaimedCount   = counts.Claimed,
                OpenCount      = counts.Open,
                ExpiredCount   = counts.Expired,
                CancelledCount = counts.Cancelled
            };
        }

        private static DateTimeOffset ToUtc(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private sealed class GrantRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid      GrantId     { get; set; }
            public string    State       { get; set; } = string.Empty;
            public string    SourceKind  { get; set; } = string.Empty;
            public string?   SourceRef   { get; set; }
            public string    Reason      { get; set; } = string.Empty;
            public Guid      CreatedBy   { get; set; }
            public DateTime  CreatedAt   { get; set; }
            public DateTime? ExecutedAt  { get; set; }
            public DateTime? CancelledAt { get; set; }
            public int       ClaimDays   { get; set; }
            public string    Rewards     { get; set; } = string.Empty;
            public int       TargetCount { get; set; }
        }

        private sealed class ExecuteLockRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string State       { get; set; } = string.Empty;
            public string SourceKind  { get; set; } = string.Empty;
            public int    TargetCount { get; set; }
        }

        private sealed class InboxCounts
        {
            /****************************************
            *              Properties
            ****************************************/

            public int Delivered { get; set; }
            public int Claimed   { get; set; }
            public int Cancelled { get; set; }
            public int Expired   { get; set; }
            public int Open      { get; set; }
        }
    }
}
