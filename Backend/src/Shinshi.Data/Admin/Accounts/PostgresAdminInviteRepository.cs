// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 초대 발급과 폐기. 인증 저장소의 IssueInviteAsync와 같은 HMAC 규칙으로 저장하되 어드민 작업 트랜잭션 안에서
    /// 감사와 함께 쓴다(그 메서드는 자체 트랜잭션이라 감사와 원자적으로 묶을 수 없다) </summary>
    public sealed class PostgresAdminInviteRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INVITE_COLUMNS =
            """
            invite_id AS "InviteId", account_id AS "AccountId", expires_at AS "ExpiresAt", max_uses AS "MaxUses",
            used_count AS "UsedCount", used_at AS "UsedAt", revoked_at AS "RevokedAt"
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminInviteRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 계정을 만들거나 잠그고, 요청하면 기존 로그인과 이전 초대를 폐기한 뒤 초대를 넣는다. 지정 계정이 없으면 null이다 </summary>
        public async Task<AdminInviteRecord?> IssueAsync(AdminOperationContext context, AdminInviteDraft draft,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(draft);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context.Execution,
                out NpgsqlTransaction transaction);
            bool createdAccount = !draft.AccountId.HasValue;
            Guid accountId = draft.AccountId ?? Guid.NewGuid();
            if (createdAccount)
            {
                // 지갑과 캐릭터는 첫 지급, 캐릭터 생성 경로가 필요할 때 만든다
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO accounts (account_id) VALUES (@AccountId);", new { AccountId = accountId },
                    transaction, cancellationToken: cancellationToken));
            }
            else if (!await AdminAccountTokenRevoker.LockAccountAsync(connection, transaction, accountId,
                cancellationToken))
            {
                return null;
            }

            AdminRevokedTokens? revoked = draft.RevokeExistingSessions
                ? await AdminAccountTokenRevoker.RevokeAsync(connection, transaction, accountId, true, cancellationToken)
                : null;
            InviteRow row = await connection.QuerySingleAsync<InviteRow>(new CommandDefinition(
                """
                INSERT INTO auth_invites (invite_id, account_id, code_hash, expires_at, max_uses)
                VALUES (@InviteId, @AccountId, @CodeHash, clock_timestamp() + @Lifetime, @MaxUses)
                RETURNING
                """ + " " + INVITE_COLUMNS + ";",
                new
                {
                    draft.InviteId,
                    AccountId = accountId,
                    draft.CodeHash,
                    draft.Lifetime,
                    MaxUses = (long)draft.MaxUses
                }, transaction, cancellationToken: cancellationToken));
            AdminInviteRecord invite = row.ToRecord();
            await context.AuditAsync(AdminAccountAuditActions.TARGET_INVITE, invite.InviteId.ToString("D"), null, new
            {
                AccountId = accountId,
                CreatedAccount = createdAccount,
                invite.MaxUses,
                ExpiresAtUnixMs = invite.ExpiresAt.ToUnixTimeMilliseconds(),
                draft.RevokeExistingSessions,
                Revoked = revoked
            }, cancellationToken);
            return invite;
        }

        /// <summary> 초대 행의 계정을 먼저 잠그고(교환과 같은 account -> invite 순서) 아직 폐기하지 않은 초대만 폐기한다 </summary>
        public async Task<EAdminInviteRevokeStatus> RevokeAsync(AdminOperationContext context, Guid inviteId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context.Execution,
                out NpgsqlTransaction transaction);
            Guid? accountId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT account_id FROM auth_invites WHERE invite_id = @InviteId;", new { InviteId = inviteId },
                transaction, cancellationToken: cancellationToken));
            if ((!accountId.HasValue)
                || (!await AdminAccountTokenRevoker.LockAccountAsync(connection, transaction, accountId.Value,
                    cancellationToken)))
            {
                return EAdminInviteRevokeStatus.NotFound;
            }

            InviteRow? revoked = await connection.QuerySingleOrDefaultAsync<InviteRow>(new CommandDefinition(
                """
                UPDATE auth_invites SET revoked_at = clock_timestamp()
                WHERE invite_id = @InviteId AND revoked_at IS NULL
                RETURNING
                """ + " " + INVITE_COLUMNS + ";",
                new { InviteId = inviteId }, transaction, cancellationToken: cancellationToken));
            if (revoked == null)
            {
                return EAdminInviteRevokeStatus.AlreadyRevoked;
            }

            AdminInviteRecord invite = revoked.ToRecord();
            await context.AuditAsync(AdminAccountAuditActions.TARGET_INVITE, inviteId.ToString("D"),
                new { invite.AccountId, invite.UsedCount, RevokedAtUnixMs = (long?)null },
                new { invite.AccountId, invite.UsedCount, RevokedAtUnixMs = invite.RevokedAt?.ToUnixTimeMilliseconds() },
                cancellationToken);
            return EAdminInviteRevokeStatus.Revoked;
        }

        /// <summary> 발급 재전송 응답을 채울 때 쓴다. 없으면 null </summary>
        public async Task<AdminInviteRecord?> FindAsync(Guid inviteId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            InviteRow? row = await connection.QuerySingleOrDefaultAsync<InviteRow>(new CommandDefinition(
                "SELECT " + INVITE_COLUMNS + " FROM auth_invites WHERE invite_id = @InviteId;",
                new { InviteId = inviteId }, cancellationToken: cancellationToken));
            return row?.ToRecord();
        }

        /****************************************
        *               DB Rows
        ****************************************/

        internal sealed class InviteRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid InviteId { get; set; }
            public Guid AccountId { get; set; }
            public DateTime ExpiresAt { get; set; }
            public long MaxUses { get; set; }
            public long UsedCount { get; set; }
            public DateTime? UsedAt { get; set; }
            public DateTime? RevokedAt { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public AdminInviteRecord ToRecord()
            {
                return new AdminInviteRecord(InviteId, AccountId, ToOffset(ExpiresAt), MaxUses, UsedCount,
                    UsedAt.HasValue ? ToOffset(UsedAt.Value) : null,
                    RevokedAt.HasValue ? ToOffset(RevokedAt.Value) : null);
            }

            private static DateTimeOffset ToOffset(DateTime value)
            {
                return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
            }
        }
    }
}
