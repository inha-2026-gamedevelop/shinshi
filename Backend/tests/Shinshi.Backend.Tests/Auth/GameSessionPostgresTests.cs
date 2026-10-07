// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> 실제 PostgreSQL에서 게임 하위 세션의 회전 경합, 폐기, 부모 로그아웃 연쇄 폐기를 검사한다 </summary>
    public sealed class GameSessionPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int GAME_SESSION_MINUTES = 15;
        private const int CONCURRENT_REFRESHES = 4;
        private static readonly TimeSpan _duration = TimeSpan.FromMinutes(GAME_SESSION_MINUTES);

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task RotateGameSessionAsync_RevokesOldAndKeepsSameParent()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken original = await IssueAsync(fixture);

            AuthValidationOutcome<IssuedAuthToken> rotated = await fixture.Repository.RotateGameSessionAsync(
                original.Token, _duration);

            Assert.True(rotated.IsSuccess);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateGameSessionAsync(original.Token)).ReasonCode);
            AuthValidationOutcome<PlayerAuthIdentity> next = await fixture.Repository.ValidateGameSessionAsync(
                rotated.Identity!.Token);
            Assert.True(next.IsSuccess);
            Assert.Equal(fixture.Identity.RefreshTokenId, next.Identity!.RefreshTokenId);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.RotateGameSessionAsync(original.Token, _duration)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateAccessAsync(rotated.Identity.Token)).ReasonCode);
        }

        [PostgresFact]
        public async Task RotateGameSessionAsync_ConcurrentSameToken_OnlyOneSucceeds()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken original = await IssueAsync(fixture);

            AuthValidationOutcome<IssuedAuthToken>[] outcomes = await Task.WhenAll(Enumerable
                .Range(0, CONCURRENT_REFRESHES)
                .Select(_ => fixture.Repository.RotateGameSessionAsync(original.Token, _duration)));

            Assert.Single(outcomes, outcome => outcome.IsSuccess);
            Assert.All(outcomes.Where(outcome => !outcome.IsSuccess),
                outcome => Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, outcome.ReasonCode));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM auth_game_sessions WHERE revoked_at IS NULL;"));
            Assert.Equal(2L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM auth_game_sessions;"));
        }

        [PostgresFact]
        public async Task RotateGameSessionAsync_ExpiredChild_RejectsWithoutIssuing()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken original = await IssueAsync(fixture);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                "UPDATE auth_game_sessions SET expires_at=clock_timestamp()-INTERVAL '1 second';");

            Assert.Equal(ContractCodes.RejectReason.TOKEN_EXPIRED,
                (await fixture.Repository.RotateGameSessionAsync(original.Token, _duration)).ReasonCode);
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM auth_game_sessions;"));
        }

        [PostgresFact]
        public async Task RevokeGameSessionAsync_IsIdempotentAndIgnoresOtherTokenKinds()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken child = await IssueAsync(fixture);

            Assert.True(await fixture.Repository.RevokeGameSessionAsync(child.Token));
            Assert.True(await fixture.Repository.RevokeGameSessionAsync(child.Token));
            Assert.False(await fixture.Repository.RevokeGameSessionAsync(fixture.Login.AccessToken));

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateGameSessionAsync(child.Token)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.RotateGameSessionAsync(child.Token, _duration)).ReasonCode);
            Assert.True((await fixture.Repository.ValidateAccessAsync(fixture.Login.AccessToken)).IsSuccess);
        }

        [PostgresFact]
        public async Task RevokeFamilyAsync_Logout_InvalidatesGameSessionsAndBlocksRotationAndIssue()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken child = await IssueAsync(fixture);

            Assert.True(await fixture.Repository.RevokeFamilyAsync(fixture.AccountId, fixture.Identity.FamilyId));

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateGameSessionAsync(child.Token)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.RotateGameSessionAsync(child.Token, _duration)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.IssueGameSessionForPlayerAsync(fixture.Identity, _duration)).ReasonCode);
        }

        [PostgresFact]
        public async Task RotateRefreshAsync_LauncherRotation_RevokesGameSessionChildren()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken child = await IssueAsync(fixture);

            Assert.True((await fixture.Repository.RotateRefreshAsync(fixture.Login.RefreshToken)).IsSuccess);

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateGameSessionAsync(child.Token)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.RotateGameSessionAsync(child.Token, _duration)).ReasonCode);
        }

        [PostgresFact]
        public async Task RevokeFamilyByRefreshTokenAsync_CurrentToken_RevokesFamilyAndChildrenAndIsIdempotent()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken child = await IssueAsync(fixture);

            Assert.True(await fixture.Repository.RevokeFamilyByRefreshTokenAsync(fixture.Login.RefreshToken));
            Assert.True(await fixture.Repository.RevokeFamilyByRefreshTokenAsync(fixture.Login.RefreshToken));

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.RotateRefreshAsync(fixture.Login.RefreshToken)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateAccessAsync(fixture.Login.AccessToken)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateGameSessionAsync(child.Token)).ReasonCode);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT (SELECT COUNT(*) FROM auth_access_tokens WHERE revoked_at IS NULL)
                    +(SELECT COUNT(*) FROM auth_refresh_tokens WHERE revoked_at IS NULL)
                    +(SELECT COUNT(*) FROM auth_game_sessions WHERE revoked_at IS NULL)
                    +(SELECT COUNT(*) FROM auth_token_families WHERE revoked_at IS NULL);
                """));
        }

        [PostgresFact]
        public async Task RevokeFamilyByRefreshTokenAsync_ExpiredToken_StillRevokesFamily()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken child = await IssueAsync(fixture);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                "UPDATE auth_refresh_tokens SET expires_at=clock_timestamp()-INTERVAL '1 second';");

            Assert.True(await fixture.Repository.RevokeFamilyByRefreshTokenAsync(fixture.Login.RefreshToken));

            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM auth_token_families WHERE revoked_at IS NOT NULL;"));
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateGameSessionAsync(child.Token)).ReasonCode);
        }

        [PostgresFact]
        public async Task RevokeFamilyByRefreshTokenAsync_RotatedOldToken_RevokesSuccessorFamily()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse successor = (await fixture.Repository.RotateRefreshAsync(
                fixture.Login.RefreshToken)).Response!;

            Assert.True(await fixture.Repository.RevokeFamilyByRefreshTokenAsync(fixture.Login.RefreshToken));

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.RotateRefreshAsync(successor.RefreshToken)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateAccessAsync(successor.AccessToken)).ReasonCode);
        }

        [PostgresFact]
        public async Task RevokeFamilyByRefreshTokenAsync_UnknownOrOtherKindToken_ChangesNothing()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthTokenHasher hasher = new AuthTokenHasher(AuthTokenHasherTests.CreateOptions());

            Assert.False(await fixture.Repository.RevokeFamilyByRefreshTokenAsync(hasher.CreateToken()));
            Assert.False(await fixture.Repository.RevokeFamilyByRefreshTokenAsync("not-a-token"));
            Assert.False(await fixture.Repository.RevokeFamilyByRefreshTokenAsync(fixture.Login.AccessToken));

            Assert.True((await fixture.Repository.ValidateAccessAsync(fixture.Login.AccessToken)).IsSuccess);
            Assert.True((await fixture.Repository.RotateRefreshAsync(fixture.Login.RefreshToken)).IsSuccess);
        }

        [PostgresFact]
        public async Task RevokeFamilyByRefreshTokenAsync_ConcurrentWithRotation_NeverLeavesLiveSuccessor()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);

            Task<bool> logout = fixture.Repository.RevokeFamilyByRefreshTokenAsync(fixture.Login.RefreshToken);
            Task<AuthExchangeOutcome> rotate = fixture.Repository.RotateRefreshAsync(fixture.Login.RefreshToken);
            await Task.WhenAll(logout, rotate);

            Assert.True(logout.Result);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM auth_refresh_tokens WHERE revoked_at IS NULL;"));
            if (rotate.Result.IsSuccess)
            {
                Assert.False((await fixture.Repository.ValidateAccessAsync(rotate.Result.Response!.AccessToken)).IsSuccess);
            }
        }

        private static async Task<IssuedAuthToken> IssueAsync(Fixture fixture)
        {
            AuthValidationOutcome<IssuedAuthToken> issued = await fixture.Repository.IssueGameSessionForPlayerAsync(
                fixture.Identity, _duration);
            Assert.True(issued.IsSuccess);
            return issued.Identity!;
        }

        private static async Task<Fixture> CreateFixtureAsync(PostgresTestDatabase database)
        {
            PostgresAuthRepository repository = new PostgresAuthRepository(database.DataSource,
                AuthTokenHasherTests.CreateOptions());
            Guid accountId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts(account_id) VALUES (@AccountId);",
                    new { AccountId = accountId });
            }

            IssuedAuthToken invite = await repository.IssueInviteAsync(accountId);
            AuthExchangeOutcome login = await repository.ExchangeInviteAsync(invite.Token);
            Assert.True(login.IsSuccess);
            PlayerAuthIdentity identity = (await repository.ValidateAccessAsync(login.Response!.AccessToken)).Identity!;
            return new Fixture(repository, accountId, login.Response, identity);
        }

        /****************************************
        *               Fixture
        ****************************************/

        private sealed class Fixture
        {
            public PostgresAuthRepository Repository { get; }
            public Guid AccountId { get; }
            public AuthExchangeResponse Login { get; }
            public PlayerAuthIdentity Identity { get; }

            public Fixture(PostgresAuthRepository repository, Guid accountId, AuthExchangeResponse login,
                PlayerAuthIdentity identity)
            {
                Repository = repository;
                AccountId  = accountId;
                Login      = login;
                Identity   = identity;
            }
        }
    }
}
