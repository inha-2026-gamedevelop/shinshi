// System
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 계정 저장소. 부트스트랩만 자체 트랜잭션을 쓰고, 생성과 비활성화는 어드민 작업 실행기의 효과 트랜잭션 안에서 감사와 함께 쓴다 </summary>
    public sealed class PostgresAdminUserRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 동시에 실행한 부트스트랩 두 개가 모두 빈 테이블을 보지 않게 직렬화한다. 마이그레이션 잠금 키와 다르다
        private const long BOOTSTRAP_LOCK_KEY = 0x5348494E41444D;

        private const string BOOTSTRAP_REASON = "bootstrap owner";

        private const string INSERT_SQL =
            """
            INSERT INTO admin_users
                (admin_id, login_name, password_hash, password_salt, password_iterations, totp_secret_encrypted, role,
                 created_by)
            VALUES (@AdminId, @LoginName, @PasswordHash, @PasswordSalt, @PasswordIterations, @TotpSecretEncrypted, @Role,
                    CAST(@CreatedBy AS uuid))
            ON CONFLICT (login_name) DO NOTHING;
            """;

        private const string SELECT_COLUMNS =
            """
            admin_id AS "AdminId", login_name AS "LoginName", role AS "Role", created_at AS "CreatedAt",
            disabled_at AS "DisabledAt", locked_until AS "LockedUntil"
            """;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IAdminAuditWriter _auditWriter;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminUserRepository(NpgsqlDataSource dataSource, IAdminAuditWriter auditWriter)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(auditWriter);
            _dataSource  = dataSource;
            _auditWriter = auditWriter;
        }

        /// <summary> admin_users가 비어 있을 때만 Owner를 만든다. 이미 운영자가 있으면 아무것도 쓰지 않고 false다 </summary>
        public async Task<bool> BootstrapOwnerAsync(AdminNewOperator owner, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if ((owner.Role != EAdminRole.Owner) || (owner.CreatedBy != null))
            {
                throw new ArgumentException("Bootstrap creates only a self-standing Owner.", nameof(owner));
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(@Key);",
                new { Key = BOOTSTRAP_LOCK_KEY }, transaction, cancellationToken: cancellationToken));
            bool exists = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM admin_users);", transaction: transaction,
                cancellationToken: cancellationToken));
            if (exists)
            {
                return false;
            }

            await InsertAsync(connection, transaction, owner, cancellationToken);
            DateTimeOffset now = await ReadClockAsync(connection, transaction, cancellationToken);
            AdminAuditEntry audit = AdminAuditEntry.ForSystem(AdminAuditActions.BOOTSTRAP_OWNER,
                AdminAuditActions.TARGET_ADMIN_USER, owner.AdminId.ToString("D"), BOOTSTRAP_REASON,
                AdminAuditJson.Serialize(new { owner.LoginName, Role = (int)owner.Role }));
            await _auditWriter.AppendAsync(new OperationExecutionContext(connection, transaction, now), audit,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        /// <summary> 같은 로그인 이름이 있으면 쓰지 않고 false다. 성공하면 같은 트랜잭션에 감사 행을 남긴다.
        /// 행위자가 아직 활성 Owner인지는 실행기가 같은 트랜잭션에서 행위자 행을 잠가 먼저 확인한다 </summary>
        public async Task<bool> CreateAsync(AdminOperationContext context, AdminNewOperator newOperator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(newOperator);
            if (newOperator.CreatedBy != context.Admin.AdminId)
            {
                throw new ArgumentException("The creating admin must be the operation actor.", nameof(newOperator));
            }

            NpgsqlConnection connection = OperationTransactionGuard.Require(context.Execution,
                out NpgsqlTransaction transaction);
            if (!await InsertAsync(connection, transaction, newOperator, cancellationToken))
            {
                return false;
            }

            await context.AuditAsync(AdminAuditActions.TARGET_ADMIN_USER, newOperator.AdminId.ToString("D"), null,
                new { newOperator.LoginName, Role = (int)newOperator.Role }, cancellationToken);
            return true;
        }

        public async Task<IReadOnlyList<AdminOperatorRecord>> ListAsync(CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            IEnumerable<OperatorRow> rows = await connection.QueryAsync<OperatorRow>(new CommandDefinition(
                "SELECT " + SELECT_COLUMNS + " FROM admin_users ORDER BY created_at, admin_id;",
                cancellationToken: cancellationToken));
            return rows.Select(row => row.ToRecord()).ToList();
        }

        /// <summary> 대상을 비활성화하고 열린 어드민 세션을 모두 폐기한다. 자기 자신, 이미 비활성이거나 역할이 낮아진 행위자,
        /// 마지막 활성 Owner는 거절해 동시 요청에서도 활성 Owner가 0명이 되지 않게 한다. 어드민 작업 실행기에 lockActor false로
        /// 넘겨 부른다(행위자 잠금과 재검증을 여기서 한다) </summary>
        public async Task<EAdminDisableOutcome> DisableAsync(AdminOperationContext context, Guid adminId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (adminId == context.Admin.AdminId)
            {
                return EAdminDisableOutcome.SelfTarget;
            }

            NpgsqlConnection connection = OperationTransactionGuard.Require(context.Execution,
                out NpgsqlTransaction transaction);

            // 행위자와 대상을 ID 순서로 함께 잠근다. Owner 둘이 서로를 동시에 비활성화하면 한쪽이 먼저 끝나고,
            // 나중 쪽은 잠금을 얻은 뒤 자신이 이미 비활성인 것을 보고 거절된다. 순서가 같아 교착하지 않는다
            List<OperatorRow> locked = (await connection.QueryAsync<OperatorRow>(new CommandDefinition(
                "SELECT " + SELECT_COLUMNS
                + " FROM admin_users WHERE admin_id = ANY(@AdminIds) ORDER BY admin_id FOR UPDATE;",
                new { AdminIds = new[] { context.Admin.AdminId, adminId } }, transaction,
                cancellationToken: cancellationToken))).ToList();
            OperatorRow? actor = locked.Find(candidate => candidate.AdminId == context.Admin.AdminId);
            OperatorRow? row = locked.Find(candidate => candidate.AdminId == adminId);
            if ((actor == null) || (actor.DisabledAt != null))
            {
                return EAdminDisableOutcome.ActorDisabled;
            }
            if (actor.Role < (int)context.Admin.Role)
            {
                // 실행기의 행위자 잠금을 쓰지 않는 경로라 인증 뒤 역할이 낮아졌는지 여기서 잠근 행으로 다시 본다
                return EAdminDisableOutcome.ActorRoleLowered;
            }
            if (row == null)
            {
                return EAdminDisableOutcome.NotFound;
            }
            if (row.DisabledAt != null)
            {
                return EAdminDisableOutcome.AlreadyDisabled;
            }
            if (row.Role == (int)EAdminRole.Owner)
            {
                // 행위자 확인만으로도 남는 Owner가 생기지만, 역할이 바뀐 행위자 같은 경우까지 마지막 Owner를 직접 지킨다
                long remainingOwners = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    """
                    SELECT count(*) FROM admin_users
                    WHERE role = @Owner AND disabled_at IS NULL AND admin_id <> @AdminId;
                    """, new { Owner = (int)EAdminRole.Owner, AdminId = adminId }, transaction,
                    cancellationToken: cancellationToken));
                if (remainingOwners == 0)
                {
                    return EAdminDisableOutcome.LastOwner;
                }
            }

            DateTime disabledAt = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                """
                UPDATE admin_users SET disabled_at = clock_timestamp() WHERE admin_id = @AdminId RETURNING disabled_at;
                """, new { AdminId = adminId }, transaction, cancellationToken: cancellationToken));
            int revoked = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE admin_sessions SET revoked_at = clock_timestamp()
                WHERE admin_id = @AdminId AND revoked_at IS NULL;
                """, new { AdminId = adminId }, transaction, cancellationToken: cancellationToken));
            await context.AuditAsync(AdminAuditActions.TARGET_ADMIN_USER, adminId.ToString("D"),
                new { row.LoginName, row.Role, DisabledAtUnixMs = (long?)null },
                new { row.LoginName, row.Role, DisabledAtUnixMs = ToUnixMs(disabledAt), RevokedSessions = revoked },
                cancellationToken);
            return EAdminDisableOutcome.Disabled;
        }

        private static async Task<bool> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            AdminNewOperator newOperator, CancellationToken cancellationToken)
        {
            int inserted = await connection.ExecuteAsync(new CommandDefinition(INSERT_SQL, new
            {
                newOperator.AdminId,
                newOperator.LoginName,
                PasswordHash = newOperator.Password.Hash,
                PasswordSalt = newOperator.Password.Salt,
                PasswordIterations = newOperator.Password.Iterations,
                newOperator.TotpSecretEncrypted,
                Role = (int)newOperator.Role,
                newOperator.CreatedBy
            }, transaction, cancellationToken: cancellationToken));
            return inserted == 1;
        }

        private static async Task<DateTimeOffset> ReadClockAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, CancellationToken cancellationToken)
        {
            DateTime now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", transaction: transaction, cancellationToken: cancellationToken));
            return ToOffset(now);
        }

        internal static DateTimeOffset ToOffset(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private static long ToUnixMs(DateTime value)
        {
            return ToOffset(value).ToUnixTimeMilliseconds();
        }

        private sealed class OperatorRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AdminId { get; set; }
            public string LoginName { get; set; } = string.Empty;
            public int Role { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? DisabledAt { get; set; }
            public DateTime? LockedUntil { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public AdminOperatorRecord ToRecord()
            {
                return new AdminOperatorRecord(AdminId, LoginName, (EAdminRole)Role, ToOffset(CreatedAt),
                    DisabledAt == null ? null : ToOffset(DisabledAt.Value),
                    LockedUntil == null ? null : ToOffset(LockedUntil.Value));
            }
        }
    }
}
