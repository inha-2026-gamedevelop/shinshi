// System
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Admin
{
    /// <summary> admin_audit_log에 INSERT만 한다. operation이 있는 행은 같은 트랜잭션의 Pending 작업일 때만 들어간다 </summary>
    public sealed class PostgresAdminAuditWriter : IAdminAuditWriter
    {
        /****************************************
        *                Fields
        ****************************************/

        // INSERT ... SELECT의 매개변수는 형식 추론이 안 되므로 null이 될 수 있는 값은 모두 형을 지정한다
        private const string INSERT_SQL =
            """
            INSERT INTO admin_audit_log
                (audit_id, admin_id, actor_kind, admin_role, action, target_kind, target_id, before_state, after_state,
                 reason, operation_id, approval_id, self_approved, request_ip_hash)
            SELECT @AuditId, CAST(@AdminId AS uuid), @ActorKind, CAST(@AdminRole AS integer), @Action, @TargetKind,
                   @TargetId, CAST(@BeforeState AS jsonb), CAST(@AfterState AS jsonb), @Reason,
                   CAST(@OperationId AS uuid), CAST(@ApprovalId AS uuid), @SelfApproved, CAST(@RequestIpHash AS bytea)
            WHERE CAST(@OperationId AS uuid) IS NULL
                OR EXISTS (SELECT 1 FROM operations
                           WHERE actor_id = CAST(@AdminId AS uuid) AND operation_id = CAST(@OperationId AS uuid)
                               AND status = 'Pending');
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminAuditWriter(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        public async Task AppendAsync(OperationExecutionContext context, AdminAuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context, out NpgsqlTransaction transaction);
            await InsertAsync(connection, transaction, entry, cancellationToken);
        }

        public async Task AppendStandaloneAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.OperationId != null)
            {
                throw new ArgumentException("Operation audit entries must be written in the operation transaction.",
                    nameof(entry));
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            await InsertAsync(connection, transaction, entry, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private static async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            AdminAuditEntry entry, CancellationToken cancellationToken)
        {
            int inserted = await connection.ExecuteAsync(new CommandDefinition(INSERT_SQL, new
            {
                entry.AuditId,
                entry.AdminId,
                entry.ActorKind,
                AdminRole = (int?)entry.AdminRole,
                entry.Action,
                entry.TargetKind,
                entry.TargetId,
                entry.BeforeState,
                entry.AfterState,
                entry.Reason,
                entry.OperationId,
                entry.ApprovalId,
                entry.SelfApproved,
                entry.RequestIpHash
            }, transaction, cancellationToken: cancellationToken));
            if (inserted != 1)
            {
                throw new InvalidOperationException("Audit entry requires a pending operation of the same admin.");
            }
        }
    }
}
