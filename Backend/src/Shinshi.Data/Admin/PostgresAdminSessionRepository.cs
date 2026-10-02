// System
using System;
using System.Data;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 로그인, 어드민 세션 검증과 폐기. 연속 실패 잠금과 TOTP 재사용 방지는 운영자 행 잠금 아래에서 판정한다 </summary>
    public sealed class PostgresAdminSessionRepository : IAdminSessionValidator
    {
        /****************************************
        *                Fields
        ****************************************/

        // 연속 실패 5회면 15분 잠근다. 잠그면서 실패 횟수를 0으로 되돌려 잠금이 풀린 뒤 다시 5회를 센다
        public const int MAX_FAILED_ATTEMPTS = 5;
        public const int LOCKOUT_MINUTES     = 15;

        private const string LOGIN_REASON   = "login";
        private const string LOGOUT_REASON  = "logout";
        private const string LOCKOUT_REASON = "too many failed logins";

        private readonly NpgsqlDataSource _dataSource;
        private readonly AdminTokenHasher _tokenHasher;
        private readonly AdminPasswordHasher _passwordHasher;
        private readonly AdminSecretProtector _secretProtector;
        private readonly IAdminAuditWriter _auditWriter;
        private readonly AdminSecuritySettings _settings;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminSessionRepository(NpgsqlDataSource dataSource, AdminTokenHasher tokenHasher,
            AdminPasswordHasher passwordHasher, AdminSecretProtector secretProtector, IAdminAuditWriter auditWriter,
            AdminSecuritySettings settings, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(tokenHasher);
            ArgumentNullException.ThrowIfNull(passwordHasher);
            ArgumentNullException.ThrowIfNull(secretProtector);
            ArgumentNullException.ThrowIfNull(auditWriter);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _dataSource      = dataSource;
            _tokenHasher     = tokenHasher;
            _passwordHasher  = passwordHasher;
            _secretProtector = secretProtector;
            _auditWriter     = auditWriter;
            _settings        = settings;
            _timeProvider    = timeProvider;
        }

        /// <summary> 비밀번호와 TOTP가 모두 맞고 잠금, 비활성, 코드 재사용이 아닐 때만 세션을 만든다. 실패 원인은 돌려주지 않는다 </summary>
        public async Task<AdminLoginOutcome> LoginAsync(string? loginName, string? password, string? totpCode,
            byte[]? requestIpHash, CancellationToken cancellationToken = default)
        {
            if (!AdminLoginNames.TryNormalize(loginName, out string normalized))
            {
                _passwordHasher.VerifyDummy(password);
                return AdminLoginOutcome.Failed;
            }

            // 느린 해시와 TOTP 계산은 행 잠금 밖에서 한다. 잠금 안에서는 그 사이 바뀐 해시와 마지막 스텝만 다시 확인한다
            CredentialRow? credential;
            await using (NpgsqlConnection reader = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                credential = await reader.QuerySingleOrDefaultAsync<CredentialRow>(new CommandDefinition(
                    """
                    SELECT admin_id AS "AdminId", password_hash AS "PasswordHash", password_salt AS "PasswordSalt",
                           password_iterations AS "PasswordIterations", totp_secret_encrypted AS "TotpSecretEncrypted"
                    FROM admin_users WHERE login_name = @LoginName;
                    """, new { LoginName = normalized }, cancellationToken: cancellationToken));
            }
            if (credential == null)
            {
                _passwordHasher.VerifyDummy(password);
                return AdminLoginOutcome.Failed;
            }

            bool passwordValid = AdminPasswordHasher.Verify(password, credential.PasswordSalt, credential.PasswordHash,
                credential.PasswordIterations);
            long? step = null;
            if (passwordValid)
            {
                byte[] secret = _secretProtector.Unprotect(credential.TotpSecretEncrypted, credential.AdminId);
                try
                {
                    step = AdminTotp.Match(secret, totpCode, _timeProvider.GetUtcNow());
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secret);
                }
            }

            return await CompleteLoginAsync(credential, passwordValid, step, requestIpHash, cancellationToken);
        }

        public async Task<AdminIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default)
        {
            if (!_tokenHasher.IsAuthentic(token))
            {
                return null;
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            SessionRow? row = await connection.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition(
                """
                SELECT s.session_id AS "SessionId", s.admin_id AS "AdminId", u.role AS "Role"
                FROM admin_sessions s
                JOIN admin_users u ON u.admin_id = s.admin_id
                WHERE s.token_hash = @TokenHash AND s.revoked_at IS NULL AND s.expires_at > clock_timestamp()
                    AND u.disabled_at IS NULL;
                """, new { TokenHash = _tokenHasher.HashAccessToken(token) }, cancellationToken: cancellationToken));
            return row == null ? null : new AdminIdentity(row.AdminId, (EAdminRole)row.Role, row.SessionId);
        }

        /// <summary> 현재 세션만 폐기한다. 이미 폐기된 세션은 감사 없이 그대로 성공이다 </summary>
        public async Task<bool> LogoutAsync(AdminIdentity admin, byte[]? requestIpHash,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(admin);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);

            // 운영자 비활성화와 같은 운영자 행 -> 세션 행 순서로 잠근다. 세션 행을 먼저 바꾸면 감사 FK 검사가 운영자 행을
            // 나중에 잠그고, 운영자 행 FOR UPDATE 뒤 세션을 갱신하는 비활성화와 서로를 기다려 교착한다.
            // 비활성화의 FOR UPDATE와 충돌하기만 하면 되므로 가장 약한 FOR KEY SHARE를 쓴다
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT 1 FROM admin_users WHERE admin_id = @AdminId FOR KEY SHARE;",
                new { admin.AdminId }, transaction, cancellationToken: cancellationToken));
            int revoked = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE admin_sessions SET revoked_at = clock_timestamp()
                WHERE session_id = @SessionId AND admin_id = @AdminId AND revoked_at IS NULL;
                """, new { admin.SessionId, admin.AdminId }, transaction, cancellationToken: cancellationToken));
            if (revoked == 0)
            {
                return false;
            }

            await AppendAsync(connection, transaction, AdminAuditEntry.ForAdmin(admin, AdminAuditActions.LOGOUT,
                AdminAuditActions.TARGET_ADMIN_USER, admin.AdminId.ToString("D"), LOGOUT_REASON,
                requestIpHash: requestIpHash), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        private async Task<AdminLoginOutcome> CompleteLoginAsync(CredentialRow credential, bool passwordValid, long? step,
            byte[]? requestIpHash, CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            LockedRow? row = await connection.QuerySingleOrDefaultAsync<LockedRow>(new CommandDefinition(
                """
                SELECT role AS "Role", password_hash AS "PasswordHash", disabled_at IS NOT NULL AS "Disabled",
                       COALESCE(locked_until > clock_timestamp(), false) AS "Locked",
                       failed_attempts AS "FailedAttempts", totp_last_step AS "TotpLastStep"
                FROM admin_users WHERE admin_id = @AdminId FOR UPDATE;
                """, new { credential.AdminId }, transaction, cancellationToken: cancellationToken));

            // 비활성과 잠금 중인 계정은 실패를 더 세지 않는다. 잠금이 공격으로 계속 연장되지 않게 한다
            if ((row == null) || row.Disabled || row.Locked)
            {
                return AdminLoginOutcome.Failed;
            }

            EAdminRole role = (EAdminRole)row.Role;
            bool unchanged = CryptographicOperations.FixedTimeEquals(row.PasswordHash, credential.PasswordHash);
            if (!passwordValid || !unchanged || (step == null) || (step.Value <= row.TotpLastStep))
            {
                await RecordFailureAsync(connection, transaction, credential.AdminId, role, row.FailedAttempts,
                    requestIpHash, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return AdminLoginOutcome.Failed;
            }

            Guid sessionId = Guid.NewGuid();
            string token = _tokenHasher.CreateToken();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE admin_users SET failed_attempts = 0, locked_until = NULL, totp_last_step = @Step
                WHERE admin_id = @AdminId;
                """, new { credential.AdminId, Step = step.Value }, transaction, cancellationToken: cancellationToken));
            DateTime expiresAt = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                """
                INSERT INTO admin_sessions (session_id, admin_id, token_hash, expires_at)
                VALUES (@SessionId, @AdminId, @TokenHash, clock_timestamp() + @Lifetime)
                RETURNING expires_at;
                """, new
                {
                    SessionId = sessionId,
                    credential.AdminId,
                    TokenHash = _tokenHasher.HashAccessToken(token),
                    Lifetime = _settings.SessionLifetime
                }, transaction, cancellationToken: cancellationToken));
            AdminIdentity identity = new AdminIdentity(credential.AdminId, role, sessionId);
            await AppendAsync(connection, transaction, AdminAuditEntry.ForAdmin(identity, AdminAuditActions.LOGIN,
                AdminAuditActions.TARGET_ADMIN_USER, credential.AdminId.ToString("D"), LOGIN_REASON,
                requestIpHash: requestIpHash), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminLoginOutcome.Succeeded(token, PostgresAdminUserRepository.ToOffset(expiresAt), identity);
        }

        private async Task RecordFailureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid adminId,
            EAdminRole role, int failedAttempts, byte[]? requestIpHash, CancellationToken cancellationToken)
        {
            bool lockNow = failedAttempts + 1 >= MAX_FAILED_ATTEMPTS;
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE admin_users
                SET failed_attempts = CASE WHEN @LockNow THEN 0 ELSE failed_attempts + 1 END,
                    locked_until = CASE WHEN @LockNow THEN clock_timestamp() + @Lockout ELSE locked_until END
                WHERE admin_id = @AdminId;
                """, new { AdminId = adminId, LockNow = lockNow, Lockout = TimeSpan.FromMinutes(LOCKOUT_MINUTES) },
                transaction, cancellationToken: cancellationToken));
            if (!lockNow)
            {
                return;
            }

            // 잠금은 운영자 세션 없이 생기는 사건이라 행위자를 잠긴 운영자 자신으로 남긴다
            await AppendAsync(connection, transaction, AdminAuditEntry.ForAdminActor(adminId, role,
                AdminAuditActions.LOCKOUT, AdminAuditActions.TARGET_ADMIN_USER, adminId.ToString("D"), LOCKOUT_REASON,
                afterState: AdminAuditJson.Serialize(new { LockoutMinutes = LOCKOUT_MINUTES }),
                requestIpHash: requestIpHash), cancellationToken);
        }

        private async Task AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AdminAuditEntry entry,
            CancellationToken cancellationToken)
        {
            DateTime now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", transaction: transaction, cancellationToken: cancellationToken));
            await _auditWriter.AppendAsync(new OperationExecutionContext(connection, transaction,
                PostgresAdminUserRepository.ToOffset(now)), entry, cancellationToken);
        }

        private sealed class CredentialRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AdminId { get; set; }
            public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
            public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
            public int PasswordIterations { get; set; }
            public byte[] TotpSecretEncrypted { get; set; } = Array.Empty<byte>();
        }

        private sealed class LockedRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public int Role { get; set; }
            public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
            public bool Disabled { get; set; }
            public bool Locked { get; set; }
            public int FailedAttempts { get; set; }
            public long TotpLastStep { get; set; }
        }

        private sealed class SessionRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid SessionId { get; set; }
            public Guid AdminId { get; set; }
            public int Role { get; set; }
        }
    }
}
