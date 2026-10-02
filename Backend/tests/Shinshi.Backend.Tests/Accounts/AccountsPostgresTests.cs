// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Data.Accounts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 실제 PostgreSQL에서 가입, 비밀번호 로그인 발급, 자격 변경의 폐기 범위와 아이디 경합을 검사한다 </summary>
    public sealed class AccountsPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PASSWORD = "Sentinel9Secret";
        private const string NEW_PASSWORD = "Another7Secret";
        private const string EMAIL = "alice@example.com";
        private const int CONCURRENT_SIGNUPS = 8;
        private const int GAME_SESSION_MINUTES = 15;

        private static readonly AccountPasswordHasher _hasher = new AccountPasswordHasher(
            AccountPasswordHasherTests.TEST_ITERATIONS);

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task CreateAccount_ThenPasswordLogin_IssuesValidTokensAndProfile()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);

            AccountSignupOutcome created = await repository.CreateAccountAsync("alice", _hasher.Hash(PASSWORD),
                "Alice@Example.com", "alice@example.com");

            Assert.True(created.IsSuccess);
            AccountCredentialRecord credential = (await repository.FindCredentialByLoginIdAsync("alice"))!;
            Assert.Equal(created.AccountId, credential.AccountId);
            Assert.True(_hasher.Verify(PASSWORD, credential.PasswordHash));
            AuthExchangeOutcome login = await repository.IssuePasswordLoginAsync(credential.AccountId,
                credential.PasswordHash);
            Assert.True(login.IsSuccess);
            AuthValidationOutcome<PlayerAuthIdentity> access = await repository.ValidateAccessAsync(
                login.Response!.AccessToken);
            Assert.True(access.IsSuccess);
            Assert.Equal(created.AccountId, access.Identity!.AccountId);
            Assert.True((await repository.RotateRefreshAsync(login.Response.RefreshToken)).IsSuccess);

            AccountProfileRecord profile = (await repository.GetProfileAsync(created.AccountId))!;
            Assert.Equal("alice", profile.LoginId);
            Assert.Equal("Alice@Example.com", profile.Email);
            Assert.False(profile.EmailVerified);
            Assert.Null(await repository.FindCredentialByLoginIdAsync("nobody"));
        }

        [PostgresFact]
        public async Task CreateAccount_DuplicateLoginId_RejectsWithoutLeavingAccountRow()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);
            Assert.True((await repository.CreateAccountAsync("alice", _hasher.Hash(PASSWORD), EMAIL, EMAIL)).IsSuccess);

            AccountSignupOutcome duplicate = await repository.CreateAccountAsync("alice", _hasher.Hash(PASSWORD),
                "other@example.com", "other@example.com");

            Assert.False(duplicate.IsSuccess);
            Assert.Equal(ContractCodes.RejectReason.LOGIN_ID_TAKEN, duplicate.ReasonCode);
            Assert.True(await repository.IsLoginIdTakenAsync("alice"));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM accounts;"));
        }

        [PostgresFact]
        public async Task CreateAccount_ConcurrentSameLoginId_OnlyOneSucceeds()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);
            string hash = _hasher.Hash(PASSWORD);

            AccountSignupOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_SIGNUPS)
                .Select(i => repository.CreateAccountAsync("racer", hash, "racer" + i + "@example.com",
                    "racer" + i + "@example.com")));

            Assert.Single(outcomes, outcome => outcome.IsSuccess);
            Assert.All(outcomes.Where(outcome => !outcome.IsSuccess),
                outcome => Assert.Equal(ContractCodes.RejectReason.LOGIN_ID_TAKEN, outcome.ReasonCode));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM accounts;"));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM account_credentials;"));
        }

        [PostgresFact]
        public async Task Email_UnverifiedDuplicatesAllowedButVerifiedEmailIsUnique()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);
            string hash = _hasher.Hash(PASSWORD);

            Assert.True((await repository.CreateAccountAsync("alice", hash, EMAIL, EMAIL)).IsSuccess);
            Assert.True((await repository.CreateAccountAsync("bob_2", hash, "ALICE@example.com", EMAIL)).IsSuccess);

            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("UPDATE account_credentials SET email_verified = true WHERE login_id = 'alice';");
            PostgresException duplicate = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "UPDATE account_credentials SET email_verified = true WHERE login_id = 'bob_2';"));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        }

        [PostgresFact]
        public async Task Schema_RejectsUppercaseLoginIdAndMalformedPasswordHash()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Guid accountId = Guid.NewGuid();
            await connection.ExecuteAsync("INSERT INTO accounts(account_id) VALUES (@AccountId);", new { AccountId = accountId });

            PostgresException upper = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                """
                INSERT INTO account_credentials(account_id, login_id, password_hash, email, email_key)
                VALUES (@AccountId, 'Alice', @Hash, 'a@b.cd', 'a@b.cd');
                """, new { AccountId = accountId, Hash = _hasher.Hash(PASSWORD) }));
            PostgresException plain = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                """
                INSERT INTO account_credentials(account_id, login_id, password_hash, email, email_key)
                VALUES (@AccountId, 'alice', @Hash, 'a@b.cd', 'a@b.cd');
                """, new { AccountId = accountId, Hash = PASSWORD }));

            Assert.Equal(PostgresErrorCodes.CheckViolation, upper.SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, plain.SqlState);
        }

        [PostgresFact]
        public async Task ChangePassword_RevokesEveryFamilyGameSessionAndInviteAndOldHashCannotIssue()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);
            AccountSignupOutcome created = await repository.CreateAccountAsync("alice", _hasher.Hash(PASSWORD), EMAIL, EMAIL);
            AccountCredentialRecord credential = (await repository.FindCredentialByAccountAsync(created.AccountId))!;
            AuthExchangeResponse first = (await repository.IssuePasswordLoginAsync(created.AccountId,
                credential.PasswordHash)).Response!;
            AuthExchangeResponse second = (await repository.IssuePasswordLoginAsync(created.AccountId,
                credential.PasswordHash)).Response!;
            PlayerAuthIdentity identity = (await repository.ValidateAccessAsync(first.AccessToken)).Identity!;
            IssuedAuthToken game = await repository.IssueGameSessionAsync(identity,
                TimeSpan.FromMinutes(GAME_SESSION_MINUTES));
            IssuedAuthToken invite = await repository.IssueInviteAsync(created.AccountId, revokeExistingSessions: false);
            string newHash = _hasher.Hash(NEW_PASSWORD);

            Assert.Equal(ECredentialUpdateStatus.Conflict, await repository.ChangePasswordAsync(created.AccountId,
                _hasher.Hash(PASSWORD), newHash));
            Assert.Equal(ECredentialUpdateStatus.Updated, await repository.ChangePasswordAsync(created.AccountId,
                credential.PasswordHash, newHash));

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await repository.ValidateAccessAsync(first.AccessToken)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await repository.ValidateAccessAsync(second.AccessToken)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await repository.ValidateGameSessionAsync(game.Token)).ReasonCode);
            Assert.False((await repository.RotateRefreshAsync(second.RefreshToken)).IsSuccess);
            Assert.False((await repository.ExchangeInviteAsync(invite.Token)).IsSuccess);
            Assert.Equal(ContractCodes.RejectReason.INVALID_CREDENTIALS,
                (await repository.IssuePasswordLoginAsync(created.AccountId, credential.PasswordHash)).ReasonCode);
            Assert.True((await repository.IssuePasswordLoginAsync(created.AccountId, newHash)).IsSuccess);
        }

        [PostgresFact]
        public async Task RecoverCredentials_FreshInviteFamily_SetsCredentialsAndRevokesEverything()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);
            Assert.True((await repository.CreateAccountAsync("taken", _hasher.Hash(PASSWORD), EMAIL, EMAIL)).IsSuccess);
            Guid accountId = await AddAccountAsync(database);
            IssuedAuthToken invite = await repository.IssueInviteAsync(accountId);
            AuthExchangeResponse inviteLogin = (await repository.ExchangeInviteAsync(invite.Token)).Response!;
            PlayerAuthIdentity identity = (await repository.ValidateAccessAsync(inviteLogin.AccessToken)).Identity!;
            IssuedAuthToken spareInvite = await repository.IssueInviteAsync(accountId, revokeExistingSessions: false);
            Assert.Null(await repository.FindCredentialByAccountAsync(accountId));
            Assert.Equal("invite", await FamilyOriginAsync(database, identity.FamilyId));
            string hash = _hasher.Hash(NEW_PASSWORD);

            Assert.Equal(ECredentialUpdateStatus.LoginIdTaken, await repository.RecoverCredentialsAsync(accountId,
                identity.FamilyId, "taken", hash, EMAIL, EMAIL));
            Assert.True((await repository.ValidateAccessAsync(inviteLogin.AccessToken)).IsSuccess);
            Assert.Equal(ECredentialUpdateStatus.Updated, await repository.RecoverCredentialsAsync(accountId,
                identity.FamilyId, "invited", hash, EMAIL, EMAIL));

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await repository.ValidateAccessAsync(inviteLogin.AccessToken)).ReasonCode);
            Assert.False((await repository.ExchangeInviteAsync(spareInvite.Token)).IsSuccess);
            Assert.Equal(ECredentialUpdateStatus.RecoveryNotAllowed, await repository.RecoverCredentialsAsync(accountId,
                identity.FamilyId, null, hash, null, null));
            AccountCredentialRecord credential = (await repository.FindCredentialByLoginIdAsync("invited"))!;
            Assert.Equal(accountId, credential.AccountId);
            AuthExchangeResponse passwordLogin = (await repository.IssuePasswordLoginAsync(accountId,
                credential.PasswordHash)).Response!;
            PlayerAuthIdentity passwordIdentity = (await repository.ValidateAccessAsync(passwordLogin.AccessToken)).Identity!;
            Assert.Equal("password", await FamilyOriginAsync(database, passwordIdentity.FamilyId));
        }

        [PostgresFact]
        public async Task RecoverCredentials_PasswordFamilyOrOldInviteFamily_IsNotAllowed()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);
            AccountSignupOutcome created = await repository.CreateAccountAsync("alice", _hasher.Hash(PASSWORD), EMAIL, EMAIL);
            AccountCredentialRecord credential = (await repository.FindCredentialByAccountAsync(created.AccountId))!;
            AuthExchangeResponse passwordLogin = (await repository.IssuePasswordLoginAsync(created.AccountId,
                credential.PasswordHash)).Response!;
            PlayerAuthIdentity passwordIdentity = (await repository.ValidateAccessAsync(passwordLogin.AccessToken)).Identity!;
            IssuedAuthToken invite = await repository.IssueInviteAsync(created.AccountId, revokeExistingSessions: false);
            AuthExchangeResponse inviteLogin = (await repository.ExchangeInviteAsync(invite.Token)).Response!;
            PlayerAuthIdentity inviteIdentity = (await repository.ValidateAccessAsync(inviteLogin.AccessToken)).Identity!;
            string hash = _hasher.Hash(NEW_PASSWORD);

            Assert.Equal(ECredentialUpdateStatus.RecoveryNotAllowed, await repository.RecoverCredentialsAsync(
                created.AccountId, passwordIdentity.FamilyId, null, hash, null, null));

            // 갱신 회전은 같은 묶음을 이어 쓰므로 초대 시작 수단과 시작 시각이 그대로 이어진다
            AuthExchangeResponse rotated = (await repository.RotateRefreshAsync(inviteLogin.RefreshToken)).Response!;
            Assert.Equal(inviteIdentity.FamilyId,
                (await repository.ValidateAccessAsync(rotated.AccessToken)).Identity!.FamilyId);
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    "UPDATE auth_token_families SET created_at = clock_timestamp() - @Age WHERE family_id = @FamilyId;",
                    new { Age = TimeSpan.FromMinutes(PostgresAuthRepository.RECOVERY_WINDOW_MINUTES + 1),
                        inviteIdentity.FamilyId });
            }
            Assert.Equal(ECredentialUpdateStatus.RecoveryNotAllowed, await repository.RecoverCredentialsAsync(
                created.AccountId, inviteIdentity.FamilyId, null, hash, null, null));
            Assert.True(_hasher.Verify(PASSWORD,
                (await repository.FindCredentialByAccountAsync(created.AccountId))!.PasswordHash));
            Assert.True((await repository.ValidateAccessAsync(passwordLogin.AccessToken)).IsSuccess);
        }

        [PostgresFact]
        public async Task RecoverCredentials_ExistingAccountFreshInvite_ResetsPasswordAndEmail()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);
            AccountSignupOutcome created = await repository.CreateAccountAsync("alice", _hasher.Hash(PASSWORD), EMAIL, EMAIL);
            IssuedAuthToken invite = await repository.IssueInviteAsync(created.AccountId);
            PlayerAuthIdentity identity = (await repository.ValidateAccessAsync(
                (await repository.ExchangeInviteAsync(invite.Token)).Response!.AccessToken)).Identity!;
            string hash = _hasher.Hash(NEW_PASSWORD);

            Assert.Equal(ECredentialUpdateStatus.Updated, await repository.RecoverCredentialsAsync(created.AccountId,
                identity.FamilyId, null, hash, "New@Example.com", "new@example.com"));

            AccountProfileRecord profile = (await repository.GetProfileAsync(created.AccountId))!;
            Assert.Equal("alice", profile.LoginId);
            Assert.Equal("New@Example.com", profile.Email);
            Assert.Equal(hash, (await repository.FindCredentialByLoginIdAsync("alice"))!.PasswordHash);
        }

        [PostgresFact]
        public async Task IssuePasswordLogin_UpgradedHash_ReplacesStoredHashInSameTransaction()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresAuthRepository repository = CreateRepository(database);
            string older = _hasher.Hash(PASSWORD);
            AccountSignupOutcome created = await repository.CreateAccountAsync("alice", older, EMAIL, EMAIL);
            AccountPasswordHasher current = new AccountPasswordHasher(AccountPasswordHasherTests.TEST_ITERATIONS * 2);
            Assert.True(current.NeedsRehash(older));
            string upgraded = current.Hash(PASSWORD);

            Assert.True((await repository.IssuePasswordLoginAsync(created.AccountId, older, upgraded)).IsSuccess);

            string stored = (await repository.FindCredentialByAccountAsync(created.AccountId))!.PasswordHash;
            Assert.Equal(upgraded, stored);
            Assert.False(current.NeedsRehash(stored));
            Assert.False((await repository.IssuePasswordLoginAsync(created.AccountId, older)).IsSuccess);
        }

        [PostgresFact]
        public async Task Schema_FamilyOriginIsRequiredAndChecked()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = await AddAccountAsync(database);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            PostgresException missing = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO auth_token_families(family_id, account_id) VALUES (@Id, @AccountId);",
                new { Id = Guid.NewGuid(), AccountId = accountId }));
            PostgresException unknown = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO auth_token_families(family_id, account_id, origin) VALUES (@Id, @AccountId, 'admin');",
                new { Id = Guid.NewGuid(), AccountId = accountId }));

            Assert.Equal(PostgresErrorCodes.NotNullViolation, missing.SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, unknown.SqlState);
        }

        private static async Task<Guid> AddAccountAsync(PostgresTestDatabase database)
        {
            Guid accountId = Guid.NewGuid();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts(account_id) VALUES (@AccountId);",
                new { AccountId = accountId });
            return accountId;
        }

        private static async Task<string> FamilyOriginAsync(PostgresTestDatabase database, Guid familyId)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<string>(
                "SELECT origin FROM auth_token_families WHERE family_id = @FamilyId;", new { FamilyId = familyId });
        }

        private static PostgresAuthRepository CreateRepository(PostgresTestDatabase database)
        {
            return new PostgresAuthRepository(database.DataSource, AuthTokenHasherTests.CreateOptions());
        }
    }
}
