// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Growth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> 실제 PostgreSQL에서 초대와 토큰 회전, 폐기 및 참가자 소비의 경합을 검사한다 </summary>
    public sealed class PostgresAuthRepositoryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TWO_INVITE_USES = 2;
        private const int CONCURRENT_CONSUMERS = 2;
        private const int LOCK_OBSERVATION_MILLISECONDS = 1500;
        private const int LOCK_POLL_MILLISECONDS = 10;
        private const int CONSUMER_TIMEOUT_SECONDS = 30;
        private const int CHILD_DURATION_MINUTES = 5;
        private const int SERVER_DURATION_MINUTES = 15;
        private const long ACCESS_DURATION_MILLISECONDS = 15 * 60 * 1000;
        private const long REFRESH_DURATION_MILLISECONDS = 24 * 60 * 60 * 1000;
        private const long JOIN_DURATION_MILLISECONDS = 60 * 1000;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task ExchangeInviteAsync_ConcurrentSingleUse_OnlyOneConsumesAndIssues()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken invite = await fixture.Repository.IssueInviteAsync(fixture.AccountId);

            AuthExchangeOutcome[] outcomes = await Task.WhenAll(
                fixture.Repository.ExchangeInviteAsync(invite.Token), fixture.Repository.ExchangeInviteAsync(invite.Token));

            Assert.Single(outcomes, outcome => outcome.IsSuccess);
            Assert.Single(outcomes, outcome => outcome.ReasonCode == ContractCodes.RejectReason.INVITE_CODE_USED);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT used_count FROM auth_invites WHERE invite_id=@Id;", new { Id = invite.TokenId }));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM auth_access_tokens;"));
            AuthExchangeResponse issued = outcomes.Single(outcome => outcome.IsSuccess).Response!;
            Assert.Equal(fixture.AccountId.ToString("D"), issued.AccountId);
            Assert.Equal(ACCESS_DURATION_MILLISECONDS, issued.AccessTokenExpiresAtUnixMs - issued.ServerTimeUnixMs);
            Assert.Equal(REFRESH_DURATION_MILLISECONDS, issued.RefreshTokenExpiresAtUnixMs - issued.ServerTimeUnixMs);
        }

        [PostgresFact]
        public async Task ExchangeInviteAsync_BoundedUses_ConsumesAtomicMaximum()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken invite = await fixture.Repository.IssueInviteAsync(fixture.AccountId, TWO_INVITE_USES);

            AuthExchangeOutcome[] outcomes = await Task.WhenAll(
                fixture.Repository.ExchangeInviteAsync(invite.Token), fixture.Repository.ExchangeInviteAsync(invite.Token),
                fixture.Repository.ExchangeInviteAsync(invite.Token));

            Assert.Equal(TWO_INVITE_USES, outcomes.LongCount(outcome => outcome.IsSuccess));
            Assert.Single(outcomes, outcome => outcome.ReasonCode == ContractCodes.RejectReason.INVITE_CODE_USED);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(TWO_INVITE_USES, await connection.QuerySingleAsync<long>(
                "SELECT used_count FROM auth_invites WHERE invite_id=@Id;", new { Id = invite.TokenId }));
        }

        [PostgresFact]
        public async Task ExchangeInviteAsync_InvalidExpiredUsedAndRevoked_ReturnsDistinctExistingReasons()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken invite = await fixture.Repository.IssueInviteAsync(fixture.AccountId);
            AuthTokenHasher hasher = new AuthTokenHasher(fixture.Options);

            Assert.Equal(ContractCodes.RejectReason.INVALID_INVITE_CODE,
                (await fixture.Repository.ExchangeInviteAsync(hasher.CreateToken())).ReasonCode);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("UPDATE auth_invites SET expires_at=clock_timestamp()-INTERVAL '1 second';");
            Assert.Equal(ContractCodes.RejectReason.INVITE_CODE_EXPIRED,
                (await fixture.Repository.ExchangeInviteAsync(invite.Token)).ReasonCode);
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM auth_token_families;"));

            IssuedAuthToken current = await fixture.Repository.IssueInviteAsync(fixture.AccountId);
            Assert.True((await fixture.Repository.ExchangeInviteAsync(current.Token)).IsSuccess);
            Assert.Equal(ContractCodes.RejectReason.INVITE_CODE_USED,
                (await fixture.Repository.ExchangeInviteAsync(current.Token)).ReasonCode);
            await fixture.Repository.IssueInviteAsync(fixture.AccountId);
            Assert.Equal(ContractCodes.RejectReason.INVALID_INVITE_CODE,
                (await fixture.Repository.ExchangeInviteAsync(current.Token)).ReasonCode);
        }

        [PostgresFact]
        public async Task ExchangeInviteAsync_StoredHash_ContainsNoTokenPlaintextAndSeparatesPurposes()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken invite = await fixture.Repository.IssueInviteAsync(fixture.AccountId);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            byte[] stored = await connection.QuerySingleAsync<byte[]>(
                "SELECT code_hash FROM auth_invites WHERE invite_id=@Id;", new { Id = invite.TokenId });

            Assert.Equal(new AuthTokenHasher(fixture.Options).Hash(AuthTokenHasher.INVITE_DOMAIN, invite.Token), stored);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateAccessAsync(invite.Token)).ReasonCode);
        }

        [PostgresFact]
        public async Task RotateRefreshAsync_PreviousTokenReuse_RevokesSuccessorAccessAndChildFamily()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse initial = await LoginAsync(fixture);
            AuthExchangeResponse rotated = (await fixture.Repository.RotateRefreshAsync(initial.RefreshToken)).Response!;
            PlayerAuthIdentity identity = (await fixture.Repository.ValidateAccessAsync(rotated.AccessToken)).Identity!;
            IssuedAuthToken child = await fixture.Repository.IssueGameSessionAsync(identity,
                TimeSpan.FromMinutes(CHILD_DURATION_MINUTES));
            Assert.True((await fixture.Repository.ValidateGameSessionAsync(child.Token)).IsSuccess);

            AuthExchangeOutcome replay = await fixture.Repository.RotateRefreshAsync(initial.RefreshToken);

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, replay.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateAccessAsync(initial.AccessToken)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateAccessAsync(rotated.AccessToken)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateGameSessionAsync(child.Token)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.RotateRefreshAsync(rotated.RefreshToken)).ReasonCode);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT (SELECT COUNT(*) FROM auth_access_tokens WHERE revoked_at IS NULL)
                    +(SELECT COUNT(*) FROM auth_refresh_tokens WHERE revoked_at IS NULL)
                    +(SELECT COUNT(*) FROM auth_game_sessions WHERE revoked_at IS NULL);
                """));
        }

        [PostgresFact]
        public async Task RotateRefreshAsync_ConcurrentSameToken_OneRotatesThenEntireFamilyIsRevoked()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse initial = await LoginAsync(fixture);

            AuthExchangeOutcome[] outcomes = await Task.WhenAll(
                fixture.Repository.RotateRefreshAsync(initial.RefreshToken),
                fixture.Repository.RotateRefreshAsync(initial.RefreshToken));

            Assert.Single(outcomes, outcome => outcome.IsSuccess);
            Assert.Single(outcomes, outcome => outcome.ReasonCode == ContractCodes.RejectReason.TOKEN_REVOKED);
            Assert.False((await fixture.Repository.ValidateAccessAsync(outcomes.Single(outcome => outcome.IsSuccess)
                .Response!.AccessToken)).IsSuccess);
        }

        [PostgresFact]
        public async Task RotateRefreshAsync_SuccessorRotationRacesOldReplay_CannotEscapeFamilyRevocation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse initial = await LoginAsync(fixture);
            AuthExchangeResponse successor = (await fixture.Repository.RotateRefreshAsync(initial.RefreshToken)).Response!;

            AuthExchangeOutcome[] outcomes = await Task.WhenAll(
                fixture.Repository.RotateRefreshAsync(successor.RefreshToken),
                fixture.Repository.RotateRefreshAsync(initial.RefreshToken));

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, outcomes[1].ReasonCode);
            if (outcomes[0].IsSuccess)
            {
                Assert.False((await fixture.Repository.ValidateAccessAsync(outcomes[0].Response!.AccessToken)).IsSuccess);
                Assert.False((await fixture.Repository.RotateRefreshAsync(outcomes[0].Response!.RefreshToken)).IsSuccess);
            }
            else
            {
                Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, outcomes[0].ReasonCode);
            }
        }

        [PostgresFact]
        public async Task RotateRefreshAsync_ExpiredToken_RejectsWithoutNewTokens()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse initial = await LoginAsync(fixture);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                "UPDATE auth_refresh_tokens SET expires_at=clock_timestamp()-INTERVAL '1 second';");

            Assert.Equal(ContractCodes.RejectReason.TOKEN_EXPIRED,
                (await fixture.Repository.RotateRefreshAsync(initial.RefreshToken)).ReasonCode);
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM auth_refresh_tokens;"));
        }

        [PostgresFact]
        public async Task ValidateAccessAsync_FamilyFlagWithoutChildUpdates_RejectsAllDescendants()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse initial = await LoginAsync(fixture);
            PlayerAuthIdentity identity = (await fixture.Repository.ValidateAccessAsync(initial.AccessToken)).Identity!;
            IssuedAuthToken child = await fixture.Repository.IssueGameSessionAsync(identity,
                TimeSpan.FromMinutes(CHILD_DURATION_MINUTES));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("UPDATE auth_token_families SET revoked_at=clock_timestamp();");

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateAccessAsync(initial.AccessToken)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateGameSessionAsync(child.Token)).ReasonCode);
        }

        [PostgresFact]
        public async Task IssueInviteAsync_AccountRecovery_RevokesExistingLoginAndGameChild()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse initial = await LoginAsync(fixture);
            PlayerAuthIdentity identity = (await fixture.Repository.ValidateAccessAsync(initial.AccessToken)).Identity!;
            IssuedAuthToken child = await fixture.Repository.IssueGameSessionAsync(identity,
                TimeSpan.FromMinutes(CHILD_DURATION_MINUTES));

            IssuedAuthToken replacement = await fixture.Repository.IssueInviteAsync(fixture.AccountId);

            Assert.False((await fixture.Repository.ValidateAccessAsync(initial.AccessToken)).IsSuccess);
            Assert.False((await fixture.Repository.ValidateGameSessionAsync(child.Token)).IsSuccess);
            Assert.True((await fixture.Repository.ExchangeInviteAsync(replacement.Token)).IsSuccess);
        }

        [PostgresFact]
        public async Task ConsumeJoinTokenAsync_ConcurrentConsumption_OnlyOneBoundIdentityWins()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse initial = await LoginAsync(fixture);
            PlayerAuthIdentity identity = (await fixture.Repository.ValidateAccessAsync(initial.AccessToken)).Identity!;
            IssuedAuthToken join = await fixture.Repository.IssueJoinTokenAsync(identity, fixture.CharacterId,
                fixture.ParticipantId, fixture.SessionId, fixture.InstanceId, fixture.ServerLease.TokenId);
            Assert.Equal(JOIN_DURATION_MILLISECONDS, join.ExpiresAtUnixMs - join.ServerTimeUnixMs);

            AuthValidationOutcome<JoinAuthIdentity>[] results = await Task.WhenAll(
                ConsumeAsync(fixture, join.Token), ConsumeAsync(fixture, join.Token));

            JoinAuthIdentity joined = results.Single(result => result.IsSuccess).Identity!;
            Assert.Equal(fixture.AccountId, joined.AccountId);
            Assert.Equal(fixture.ParticipantId, joined.ParticipantId);
            Assert.Equal(fixture.CharacterId, joined.CharacterId);
            Assert.Single(results, result => result.ReasonCode == ContractCodes.RejectReason.TOKEN_REVOKED);
        }

        [PostgresFact]
        public async Task ConsumeJoinTokenAsync_WrongBinding_DoesNotConsumeValidToken()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken join = await IssueJoinAsync(fixture);

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                (await fixture.Repository.ConsumeJoinTokenAsync(join.Token, fixture.InstanceId,
                    ContractCodes.ServerRole.TOWN, fixture.ServerLease.TokenId)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                (await fixture.Repository.ConsumeJoinTokenAsync(join.Token, Guid.NewGuid(),
                    ContractCodes.ServerRole.DUNGEON, fixture.ServerLease.TokenId)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                (await fixture.Repository.ConsumeJoinTokenAsync(join.Token, fixture.InstanceId,
                    ContractCodes.ServerRole.DUNGEON, Guid.NewGuid())).ReasonCode);
            Assert.True((await ConsumeAsync(fixture, join.Token)).IsSuccess);
        }
        [PostgresFact]
        public async Task ConsumeJoinTokenAsync_ExpiredLeaseAndToken_UsesDatabaseTime()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken join = await IssueJoinAsync(fixture);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("UPDATE auth_join_tokens SET expires_at=clock_timestamp()-INTERVAL '1 second';");

            Assert.Equal(ContractCodes.RejectReason.TOKEN_EXPIRED, (await ConsumeAsync(fixture, join.Token)).ReasonCode);
            await connection.ExecuteAsync("UPDATE auth_join_tokens SET expires_at=clock_timestamp()+INTERVAL '1 minute';");
            await connection.ExecuteAsync("UPDATE auth_server_leases SET expires_at=clock_timestamp()-INTERVAL '1 second';");
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, (await ConsumeAsync(fixture, join.Token)).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_EXPIRED,
                (await fixture.Repository.ValidateServerLeaseAsync(fixture.ServerLease.Token)).ReasonCode);
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM auth_join_tokens WHERE consumed_at IS NOT NULL;"));
        }

        [PostgresFact]
        public async Task ConsumeJoinTokenAsync_FamilyRevoked_RejectsWithoutConsumption()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken join = await IssueJoinAsync(fixture);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("UPDATE auth_token_families SET revoked_at=clock_timestamp();");

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, (await ConsumeAsync(fixture, join.Token)).ReasonCode);
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM auth_join_tokens WHERE consumed_at IS NOT NULL;"));
        }

        [PostgresFact]
        public async Task IssueServerLeaseAsync_ReplacementAndWrongRole_RespectsInstanceRoleAndRevocation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);

            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.IssueServerLeaseAsync(Guid.NewGuid(),
                fixture.InstanceId, ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(SERVER_DURATION_MINUTES)));
            IssuedAuthToken replacement = await fixture.Repository.IssueServerLeaseAsync(Guid.NewGuid(), fixture.InstanceId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(SERVER_DURATION_MINUTES));

            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.ValidateServerLeaseAsync(fixture.ServerLease.Token)).ReasonCode);
            ServerAuthIdentity server = (await fixture.Repository.ValidateServerLeaseAsync(replacement.Token)).Identity!;
            Assert.Equal(fixture.InstanceId, server.InstanceId);
            Assert.Equal(ContractCodes.ServerRole.DUNGEON, server.Role);
            Assert.Equal(replacement.TokenId, server.LeaseId);
        }

        [PostgresFact]
        public async Task IssueJoinTokenAsync_WrongCharacterOrStartedSession_RejectsTrustedCallerMistakes()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            AuthExchangeResponse initial = await LoginAsync(fixture);
            PlayerAuthIdentity identity = (await fixture.Repository.ValidateAccessAsync(initial.AccessToken)).Identity!;

            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.IssueJoinTokenAsync(identity, Guid.NewGuid(),
                fixture.ParticipantId, fixture.SessionId, fixture.InstanceId, fixture.ServerLease.TokenId));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("UPDATE game_sessions SET state='Running';");
            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.IssueJoinTokenAsync(identity, fixture.CharacterId,
                fixture.ParticipantId, fixture.SessionId, fixture.InstanceId, fixture.ServerLease.TokenId));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM auth_join_tokens;"));
        }

        [PostgresFact]
        public async Task ConsumeJoinTokenAsync_AccountLockForcesOverlap_ExactlyOneConsumerCommits()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken join = await IssueJoinAsync(fixture);
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(CONSUMER_TIMEOUT_SECONDS));
            await using NpgsqlConnection blocker = await database.DataSource.OpenConnectionAsync(timeout.Token);
            await using NpgsqlConnection observer = await database.DataSource.OpenConnectionAsync(timeout.Token);
            await using NpgsqlTransaction held = await blocker.BeginTransactionAsync(timeout.Token);
            int blockerPid = await blocker.QuerySingleAsync<int>("SELECT pg_backend_pid();", transaction: held);
            await blocker.QuerySingleAsync<Guid>(new CommandDefinition(
                "SELECT account_id FROM accounts WHERE account_id = @AccountId FOR UPDATE;",
                new { fixture.AccountId }, held, cancellationToken: timeout.Token));

            Task<AuthValidationOutcome<JoinAuthIdentity>>[] tasks = Enumerable.Range(0, CONCURRENT_CONSUMERS)
                .Select(_ => fixture.Repository.ConsumeJoinTokenAsync(join.Token, fixture.InstanceId, ContractCodes.ServerRole.DUNGEON, fixture.ServerLease.TokenId, timeout.Token)).ToArray();
            bool overlapped = false;
            try
            {
                DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(LOCK_OBSERVATION_MILLISECONDS);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    // tuple 잠금 대기자가 앞선 waiter에 연결돼도 같은 blocker의 대기 사슬만 센다
                    int waiting = await observer.QuerySingleAsync<int>(new CommandDefinition(
                        """
                        WITH RECURSIVE waiters(pid) AS (
                            SELECT pid FROM pg_stat_activity WHERE @BlockerPid = ANY(pg_blocking_pids(pid))
                            UNION
                            SELECT activity.pid FROM pg_stat_activity AS activity
                            JOIN waiters ON waiters.pid = ANY(pg_blocking_pids(activity.pid))
                        )
                        SELECT count(DISTINCT pid)::integer FROM waiters;
                        """, new { BlockerPid = blockerPid }, cancellationToken: timeout.Token));
                    if (waiting >= CONCURRENT_CONSUMERS)
                    {
                        overlapped = true;
                        break;
                    }
                    await Task.Delay(LOCK_POLL_MILLISECONDS, timeout.Token);
                }
            }
            finally
            {
                await held.RollbackAsync(CancellationToken.None);
            }

            AuthValidationOutcome<JoinAuthIdentity>[] results = await Task.WhenAll(tasks);
            Assert.True(overlapped, "Both consumers must be blocked on the account before releasing it.");
            Assert.Single(results, result => result.IsSuccess);
            Assert.Single(results, result => result.ReasonCode == ContractCodes.RejectReason.TOKEN_REVOKED);
            Assert.Equal(1L, await observer.QuerySingleAsync<long>(
                "SELECT count(*) FROM auth_join_tokens WHERE consumed_at IS NOT NULL;"));
        }
        [PostgresFact]
        public async Task ConsumeJoinTokenAsync_RenewedLease_AcceptsCurrentLeaseAndRejectsOriginal()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            IssuedAuthToken join = await IssueJoinAsync(fixture);
            IssuedAuthToken current = await fixture.Repository.IssueServerLeaseAsync(Guid.NewGuid(), fixture.InstanceId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(SERVER_DURATION_MINUTES));

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, (await ConsumeAsync(fixture, join.Token)).ReasonCode);
            Assert.True((await fixture.Repository.ConsumeJoinTokenAsync(join.Token, fixture.InstanceId,
                ContractCodes.ServerRole.DUNGEON, current.TokenId)).IsSuccess);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(fixture.ServerLease.TokenId, await connection.QuerySingleAsync<Guid>(
                "SELECT server_lease FROM game_sessions WHERE session_id = @SessionId;", new { fixture.SessionId }));
        }

        [PostgresFact]
        public async Task IssueDungeonJoinTokenAsync_NoActiveFamily_RejectsWithoutIssuingToken()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            ServerAuthIdentity town = (await fixture.Repository.ValidateServerLeaseAsync(fixture.TownLease.Token)).Identity!;
            AuthValidationOutcome<IssuedAuthToken> missing = await fixture.Repository.IssueDungeonJoinTokenAsync(town, Grant(fixture));
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, missing.ReasonCode);
            AuthExchangeResponse login = await LoginAsync(fixture);
            PlayerAuthIdentity player = (await fixture.Repository.ValidateAccessAsync(login.AccessToken)).Identity!;
            await fixture.Repository.RevokeFamilyAsync(player.AccountId, player.FamilyId);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.IssueDungeonJoinTokenAsync(town, Grant(fixture))).ReasonCode);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM auth_join_tokens;"));
        }

        [PostgresFact]
        public async Task IssueDungeonJoinTokenAsync_InvalidRosterOrServer_RejectsWithoutIssuingToken()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await CreateFixtureAsync(database);
            await LoginAsync(fixture);
            ServerAuthIdentity town = (await fixture.Repository.ValidateServerLeaseAsync(fixture.TownLease.Token)).Identity!;
            DungeonJoinGrant wrong = new DungeonJoinGrant(fixture.SessionId, Guid.NewGuid(), fixture.AccountId,
                fixture.CharacterId, fixture.InstanceId, "127.0.0.1", 7780, TimeSpan.FromMilliseconds(JOIN_DURATION_MILLISECONDS), DateTimeOffset.MaxValue);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                (await fixture.Repository.IssueDungeonJoinTokenAsync(town, wrong)).ReasonCode);
            ServerAuthIdentity dungeon = (await fixture.Repository.ValidateServerLeaseAsync(fixture.ServerLease.Token)).Identity!;
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.IssueDungeonJoinTokenAsync(dungeon, Grant(fixture))).ReasonCode);
            await fixture.Repository.IssueServerLeaseAsync(Guid.NewGuid(), fixture.TownInstanceId, ContractCodes.ServerRole.TOWN,
                TimeSpan.FromMinutes(SERVER_DURATION_MINUTES));
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                (await fixture.Repository.IssueDungeonJoinTokenAsync(town, Grant(fixture))).ReasonCode);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM auth_join_tokens;"));
        }

        private static DungeonJoinGrant Grant(Fixture fixture)
        {
            return new DungeonJoinGrant(fixture.SessionId, fixture.ParticipantId, fixture.AccountId, fixture.CharacterId,
                fixture.InstanceId, "127.0.0.1", 7780, TimeSpan.FromMilliseconds(JOIN_DURATION_MILLISECONDS), DateTimeOffset.MaxValue);
        }
        private static async Task<Fixture> CreateFixtureAsync(PostgresTestDatabase database)
        {
            GrowthContent content = GrowthContent.LoadEmbedded();
            AuthTokenOptions options = AuthTokenHasherTests.CreateOptions();
            Fixture fixture = new Fixture(new PostgresAuthRepository(database.DataSource, options), options);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts(account_id) VALUES (@AccountId);
                INSERT INTO characters(character_id,account_id,class_id) VALUES (@CharacterId,@AccountId,@Class);
                INSERT INTO server_instances(instance_id,role,address,port,build_id,content_version,state)
                    VALUES (@InstanceId,@Role,'127.0.0.1',7780,'auth-test',@ContentVersion,'Reserved');
                INSERT INTO server_instances(instance_id,role,address,port,build_id,content_version,state)
                    VALUES (@TownInstanceId,@Town,'127.0.0.1',7777,'auth-test',@ContentVersion,'Idle');
                """, new { fixture.AccountId, fixture.CharacterId, fixture.InstanceId, fixture.TownInstanceId, Town = ContractCodes.ServerRole.TOWN,
                    Class = ContractCodes.PlayerClass.WARRIOR, Role = ContractCodes.ServerRole.DUNGEON, content.ContentVersion });
            fixture.TownLease = await fixture.Repository.IssueServerLeaseAsync(Guid.NewGuid(), fixture.TownInstanceId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(SERVER_DURATION_MINUTES));
            fixture.ServerLease = await fixture.Repository.IssueServerLeaseAsync(Guid.NewGuid(), fixture.InstanceId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(SERVER_DURATION_MINUTES));
            await connection.ExecuteAsync(
                """
                INSERT INTO game_sessions(session_id,mode,map_id,state,starting_count,content_version,instance_id,server_lease,
                    expires_at,reserved_by_instance_id,experience_settings)
                    VALUES (@SessionId,'Normal','region_forest','Reserved',1,@ContentVersion,@InstanceId,@LeaseId,
                        clock_timestamp() + interval '150 seconds',@TownInstanceId,CAST(@ExperienceSettings AS jsonb));
                INSERT INTO session_participants(session_id,participant_id,account_id,character_id,state)
                    VALUES (@SessionId,@ParticipantId,@AccountId,@CharacterId,'Reserved');
                """, new { fixture.SessionId, fixture.InstanceId, fixture.TownInstanceId, fixture.ParticipantId, fixture.AccountId, fixture.CharacterId, LeaseId = fixture.ServerLease.TokenId, content.ContentVersion, ExperienceSettings = Newtonsoft.Json.JsonConvert.SerializeObject(content.CharacterLevel) });
            return fixture;
        }

        private static async Task<AuthExchangeResponse> LoginAsync(Fixture fixture)
        {
            IssuedAuthToken invite = await fixture.Repository.IssueInviteAsync(fixture.AccountId);
            AuthExchangeOutcome login = await fixture.Repository.ExchangeInviteAsync(invite.Token);
            Assert.True(login.IsSuccess);
            return login.Response!;
        }

        private static async Task<IssuedAuthToken> IssueJoinAsync(Fixture fixture)
        {
            AuthExchangeResponse login = await LoginAsync(fixture);
            PlayerAuthIdentity identity = (await fixture.Repository.ValidateAccessAsync(login.AccessToken)).Identity!;
            return await fixture.Repository.IssueJoinTokenAsync(identity, fixture.CharacterId, fixture.ParticipantId,
                fixture.SessionId, fixture.InstanceId, fixture.ServerLease.TokenId);
        }

        private static Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeAsync(Fixture fixture, string token)
        {
            return fixture.Repository.ConsumeJoinTokenAsync(token, fixture.InstanceId, ContractCodes.ServerRole.DUNGEON, fixture.ServerLease.TokenId);
        }

        private sealed class Fixture
        {
            /****************************************
            *              Properties
            ****************************************/

            public PostgresAuthRepository Repository { get; }
            public AuthTokenOptions Options { get; }
            public Guid AccountId { get; } = Guid.NewGuid();
            public Guid CharacterId { get; } = Guid.NewGuid();
            public Guid InstanceId { get; } = Guid.NewGuid();
            public Guid TownInstanceId { get; } = Guid.NewGuid();
            public IssuedAuthToken TownLease { get; set; } = null!;
            public Guid ParticipantId { get; } = Guid.NewGuid();
            public Guid SessionId { get; } = Guid.NewGuid();
            public IssuedAuthToken ServerLease { get; set; } = null!;

            /****************************************
            *                Methods
            ****************************************/

            public Fixture(PostgresAuthRepository repository, AuthTokenOptions options)
            {
                Repository = repository;
                Options    = options;
            }
        }
    }
}

