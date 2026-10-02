// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Accounts;

namespace Shinshi.Data.Auth
{
    /// <summary> 아이디, 비밀번호 자격과 가입. 로그인 발급은 초대 교환과 같은 토큰 묶음 경로를 쓴다 </summary>
    public sealed partial class PostgresAuthRepository : IAccountRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 같은 아이디 가입이 겹치면 계정 행까지 되돌리는 savepoint
        private const string SIGNUP_SAVEPOINT = "account_signup";

        // auth_token_families.origin 값. 초대 교환은 PostgresAuthRepository.ExchangeInviteAsync가 'invite'로 적는다
        private const string ORIGIN_INVITE   = "invite";
        private const string ORIGIN_PASSWORD = "password";

        // 현재 비밀번호 없는 복구를 허용하는, 초대로 시작한 로그인 묶음의 나이 상한
        public const int RECOVERY_WINDOW_MINUTES = 30;

        /****************************************
        *                Methods
        ****************************************/

        public async Task<AccountCredentialRecord?> FindCredentialByLoginIdAsync(string loginId,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(loginId);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            CredentialRow? row = await connection.QuerySingleOrDefaultAsync<CredentialRow>(new CommandDefinition(
                """
                SELECT account_id AS "AccountId", login_id AS "LoginId", password_hash AS "PasswordHash"
                FROM account_credentials WHERE login_id = @LoginId;
                """, new { LoginId = loginId }, cancellationToken: cancellationToken));
            return row == null ? null : new AccountCredentialRecord(row.AccountId, row.LoginId, row.PasswordHash);
        }

        public Task<AuthExchangeOutcome> IssuePasswordLoginAsync(Guid accountId, string verifiedPasswordHash,
            string? upgradedPasswordHash = null, CancellationToken cancellationToken = default)
        {
            ValidateId(accountId, nameof(accountId));
            ArgumentException.ThrowIfNullOrEmpty(verifiedPasswordHash);
            if ((upgradedPasswordHash != null) && !AccountPasswordHasher.TryParse(upgradedPasswordHash, out _, out _, out _))
            {
                throw new ArgumentException("The upgraded password hash has an unexpected format.",
                    nameof(upgradedPasswordHash));
            }

            return InTransactionAsync(async (connection, transaction, token) =>
            {
                // 비밀번호 변경과 같은 계정 잠금 아래에서 해시를 다시 확인한다. 검증과 발급 사이에 바뀐 옛 비밀번호로는 발급하지 않는다
                await LockAccountAsync(connection, transaction, accountId, token);
                bool unchanged = await connection.QuerySingleAsync<bool>(Command(
                    """
                    SELECT EXISTS (SELECT 1 FROM account_credentials
                        WHERE account_id = @AccountId AND password_hash = @Hash);
                    """, new { AccountId = accountId, Hash = verifiedPasswordHash }, transaction, token));
                if (!unchanged)
                {
                    return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.INVALID_CREDENTIALS);
                }

                if (upgradedPasswordHash != null)
                {
                    await connection.ExecuteAsync(Command(
                        """
                        UPDATE account_credentials SET password_hash = @NewHash, updated_at = clock_timestamp()
                        WHERE account_id = @AccountId AND password_hash = @Hash;
                        """, new { AccountId = accountId, Hash = verifiedPasswordHash, NewHash = upgradedPasswordHash },
                        transaction, token));
                }

                // 비밀번호로 시작한 묶음은 현재 비밀번호 없는 복구 권한을 갖지 않는다
                Guid familyId = Guid.NewGuid();
                await connection.ExecuteAsync(Command(
                    "INSERT INTO auth_token_families(family_id,account_id,origin) VALUES (@FamilyId,@AccountId,@Origin);",
                    new { FamilyId = familyId, AccountId = accountId, Origin = ORIGIN_PASSWORD }, transaction, token));
                return await InsertTokenPairAsync(connection, transaction, accountId, familyId, null, token);
            }, cancellationToken);
        }

        public Task<AccountSignupOutcome> CreateAccountAsync(string loginId, string passwordHash, string email,
            string emailKey, CancellationToken cancellationToken = default)
        {
            ValidateCredentialInput(loginId, passwordHash, email, emailKey);
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                DateTime now = await ReadTimeAsync(connection, transaction, token);
                Guid accountId = Guid.NewGuid();
                await transaction.SaveAsync(SIGNUP_SAVEPOINT, token);
                await connection.ExecuteAsync(Command("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                    new { AccountId = accountId }, transaction, token));

                // 같은 아이디의 동시 가입은 먼저 커밋한 쪽을 기다린 뒤 아무것도 넣지 않는다. 예외 없이 결과로 판정한다
                if (!await InsertCredentialAsync(connection, transaction, accountId, loginId, passwordHash, email,
                    emailKey, token))
                {
                    await transaction.RollbackAsync(SIGNUP_SAVEPOINT, token);
                    return AccountSignupOutcome.Rejected(ContractCodes.RejectReason.LOGIN_ID_TAKEN, ToUtc(now));
                }

                return AccountSignupOutcome.Success(accountId, ToUtc(now));
            }, cancellationToken);
        }

        public async Task<bool> IsLoginIdTakenAsync(string loginId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(loginId);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM account_credentials WHERE login_id = @LoginId);",
                new { LoginId = loginId }, cancellationToken: cancellationToken));
        }

        public async Task<AccountProfileRecord?> GetProfileAsync(Guid accountId,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            ProfileRow? row = await connection.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition(
                """
                SELECT account.account_id AS "AccountId", account.created_at AS "CreatedAt",
                    credential.login_id AS "LoginId", credential.email AS "Email",
                    COALESCE(credential.email_verified, false) AS "EmailVerified"
                FROM accounts AS account
                LEFT JOIN account_credentials AS credential ON credential.account_id = account.account_id
                WHERE account.account_id = @AccountId;
                """, new { AccountId = accountId }, cancellationToken: cancellationToken));
            return row == null ? null : new AccountProfileRecord(row.AccountId, row.LoginId, row.Email,
                row.EmailVerified, ToUtc(row.CreatedAt));
        }

        public async Task<AccountCredentialRecord?> FindCredentialByAccountAsync(Guid accountId,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            CredentialRow? row = await connection.QuerySingleOrDefaultAsync<CredentialRow>(new CommandDefinition(
                """
                SELECT account_id AS "AccountId", login_id AS "LoginId", password_hash AS "PasswordHash"
                FROM account_credentials WHERE account_id = @AccountId;
                """, new { AccountId = accountId }, cancellationToken: cancellationToken));
            return row == null ? null : new AccountCredentialRecord(row.AccountId, row.LoginId, row.PasswordHash);
        }

        public Task<ECredentialUpdateStatus> ChangePasswordAsync(Guid accountId, string expectedPasswordHash,
            string newPasswordHash, CancellationToken cancellationToken = default)
        {
            ValidateId(accountId, nameof(accountId));
            ArgumentException.ThrowIfNullOrEmpty(expectedPasswordHash);
            ArgumentException.ThrowIfNullOrEmpty(newPasswordHash);
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                // 잠금 순서: 계정 -> 자격 행 -> 토큰 묶음. 로그인 발급, 토큰 회전과 같은 방향이다
                await LockAccountAsync(connection, transaction, accountId, token);
                int changed = await connection.ExecuteAsync(Command(
                    """
                    UPDATE account_credentials SET password_hash = @NewHash, updated_at = clock_timestamp()
                    WHERE account_id = @AccountId AND password_hash = @ExpectedHash;
                    """, new { AccountId = accountId, ExpectedHash = expectedPasswordHash, NewHash = newPasswordHash },
                    transaction, token));
                if (changed == 0)
                {
                    return ECredentialUpdateStatus.Conflict;
                }

                await RevokeAccountSessionsAsync(connection, transaction, accountId, token);
                return ECredentialUpdateStatus.Updated;
            }, cancellationToken);
        }

        public Task<ECredentialUpdateStatus> RecoverCredentialsAsync(Guid accountId, Guid familyId, string? loginId,
            string passwordHash, string? email, string? emailKey, CancellationToken cancellationToken = default)
        {
            ValidateId(accountId, nameof(accountId));
            ValidateId(familyId, nameof(familyId));
            if (!AccountPasswordHasher.TryParse(passwordHash, out _, out _, out _))
            {
                throw new ArgumentException("The password hash has an unexpected format.", nameof(passwordHash));
            }
            if ((email == null) != (emailKey == null))
            {
                throw new ArgumentException("The email and its key must be supplied together.", nameof(emailKey));
            }

            return InTransactionAsync(async (connection, transaction, token) =>
            {
                // 잠금 순서: 계정 -> 로그인 묶음 -> 자격 행. 묶음의 시작 수단과 나이는 DB 시각으로 판정한다
                await LockAccountAsync(connection, transaction, accountId, token);
                bool allowed = await connection.QuerySingleOrDefaultAsync<bool?>(Command(
                    """
                    SELECT origin = @Invite AND revoked_at IS NULL AND created_at > clock_timestamp() - @Window
                    FROM auth_token_families WHERE account_id = @AccountId AND family_id = @FamilyId FOR UPDATE;
                    """, new { AccountId = accountId, FamilyId = familyId, Invite = ORIGIN_INVITE,
                        Window = TimeSpan.FromMinutes(RECOVERY_WINDOW_MINUTES) }, transaction, token)) ?? false;
                if (!allowed)
                {
                    return ECredentialUpdateStatus.RecoveryNotAllowed;
                }

                bool exists = await connection.QuerySingleAsync<bool>(Command(
                    "SELECT EXISTS (SELECT 1 FROM account_credentials WHERE account_id = @AccountId);",
                    new { AccountId = accountId }, transaction, token));
                if (exists)
                {
                    if (loginId != null)
                    {
                        throw new ArgumentException("An existing login ID cannot be changed.", nameof(loginId));
                    }

                    // 이메일을 바꾸면 인증 여부를 되돌린다
                    await connection.ExecuteAsync(Command(
                        """
                        UPDATE account_credentials SET password_hash = @PasswordHash, updated_at = clock_timestamp(),
                            email = COALESCE(@Email, email), email_key = COALESCE(@EmailKey, email_key),
                            email_verified = CASE WHEN @EmailKey IS NULL THEN email_verified ELSE false END
                        WHERE account_id = @AccountId;
                        """, new { AccountId = accountId, PasswordHash = passwordHash, Email = email, EmailKey = emailKey },
                        transaction, token));
                }
                else
                {
                    ValidateCredentialInput(loginId!, passwordHash, email!, emailKey!);

                    // 다른 계정과의 아이디 경합은 유일 제약이 가른다
                    if (!await InsertCredentialAsync(connection, transaction, accountId, loginId!, passwordHash, email!,
                        emailKey!, token))
                    {
                        return ECredentialUpdateStatus.LoginIdTaken;
                    }
                }

                await RevokeAccountSessionsAsync(connection, transaction, accountId, token);
                return ECredentialUpdateStatus.Updated;
            }, cancellationToken);
        }

        private static async Task<bool> InsertCredentialAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid accountId, string loginId, string passwordHash, string email, string emailKey, CancellationToken token)
        {
            Guid? inserted = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                """
                INSERT INTO account_credentials (account_id, login_id, password_hash, email, email_key)
                VALUES (@AccountId, @LoginId, @PasswordHash, @Email, @EmailKey)
                ON CONFLICT (login_id) DO NOTHING
                RETURNING account_id;
                """, new { AccountId = accountId, LoginId = loginId, PasswordHash = passwordHash, Email = email,
                    EmailKey = emailKey }, transaction, token));
            return inserted.HasValue;
        }

        // 자격이 바뀌면 이전 비밀번호로 얻은 모든 로그인을 끊는다. 호출한 묶음도 폐기되므로 새 자격으로 다시 로그인한다.
        // 남은 초대(복구 코드)도 폐기해 바뀐 자격을 우회하지 못하게 한다. 계정 잠금 안에서 부른다
        private static Task RevokeAccountSessionsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid accountId, CancellationToken token)
        {
            return connection.ExecuteAsync(Command(
                """
                UPDATE auth_token_families SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE account_id = @AccountId;
                UPDATE auth_refresh_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE account_id = @AccountId;
                UPDATE auth_access_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE account_id = @AccountId;
                UPDATE auth_game_sessions SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE account_id = @AccountId;
                UPDATE auth_join_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE account_id = @AccountId;
                UPDATE auth_invites SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE account_id = @AccountId;
                """, new { AccountId = accountId }, transaction, token));
        }

        private static void ValidateCredentialInput(string loginId, string passwordHash, string email, string emailKey)
        {
            if (!AccountCredentialRules.TryNormalizeLoginId(loginId, out string? normalized)
                || !string.Equals(normalized, loginId, StringComparison.Ordinal))
            {
                throw new ArgumentException("The login ID must be normalized.", nameof(loginId));
            }
            if (!AccountPasswordHasher.TryParse(passwordHash, out _, out _, out _))
            {
                throw new ArgumentException("The password hash has an unexpected format.", nameof(passwordHash));
            }

            ArgumentException.ThrowIfNullOrEmpty(email);
            ArgumentException.ThrowIfNullOrEmpty(emailKey);
        }

        private static DateTimeOffset ToUtc(DateTime time)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class CredentialRow
        {
            public Guid AccountId { get; set; }
            public string LoginId { get; set; } = string.Empty;
            public string PasswordHash { get; set; } = string.Empty;
        }

        private sealed class ProfileRow
        {
            public Guid AccountId { get; set; }
            public DateTime CreatedAt { get; set; }
            public string? LoginId { get; set; }
            public string? Email { get; set; }
            public bool EmailVerified { get; set; }
        }
    }
}
