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
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Social
{
    /// <summary> 0019의 적용 결과(검증된 제약, 인덱스), 되돌리기 뒤 재적용, 마을 입장 교환의 임대 character_id를 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class SocialMigrationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MIGRATION_0019 = "0019_social";
        private const int    SERVER_MINUTES = 15;

        private static readonly string[] G_Tables =
        {
            "friendships", "friend_requests", "guilds", "guild_members", "guild_join_requests", "social_deliveries"
        };

        private static readonly string[] G_Indexes =
        {
            "ux_account_leases_character", "ix_account_leases_instance_character", "ix_friendships_friend",
            "ix_friend_requests_target", "ix_friend_requests_expiry", "ux_guilds_name_key_alive", "ix_guild_members_guild",
            "ux_guild_members_leader", "ix_guild_join_requests_character", "ix_guild_join_requests_guild",
            "ix_guild_join_requests_expiry", "ix_social_deliveries_target_due", "ix_social_deliveries_target",
            "ix_social_deliveries_expiry"
        };

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Migration0019_Applied_ConstraintsValidatedAndIndexesExist()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            Assert.Equal(0L, await CountUnvalidatedAsync(connection));
            Assert.Equal(G_Indexes.Length, (await connection.QueryAsync<string>(
                "SELECT indexname FROM pg_indexes WHERE schemaname = current_schema() AND indexname = ANY(@Names);",
                new { Names = G_Indexes })).Count());
            Assert.Equal(G_Tables.Length, await CountTablesAsync(connection));
        }

        [PostgresFact]
        public async Task Migration0019_DownThenUp_RemovesSocialStateAndRestores()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            (long version, string name) = MigrationTestResources.All.Single(entry => entry.Name == MIGRATION_0019);

            await MigrationTestResources.RollBackAboveAsync(connection, version - 1);

            Assert.Equal(0L, await CountTablesAsync(connection));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = 'account_leases' AND column_name = 'character_id';
                """));

            await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync(name));
            foreach ((long Version, string Name) later in MigrationTestResources.All.Where(entry => entry.Version > version))
            {
                await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync(later.Name));
            }

            Assert.Equal(G_Tables.Length, await CountTablesAsync(connection));
            Assert.Equal(0L, await CountUnvalidatedAsync(connection));
        }

        /// <summary> 마을 입장 교환이 임대에 캐릭터를 적고, 같은 계정의 다른 캐릭터로 다시 들어오면(ON CONFLICT DO UPDATE) 새 캐릭터로 바뀐다 </summary>
        [PostgresFact]
        public async Task TownJoin_WritesLeaseCharacter_AndOverwritesOnReentry()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AuthTokenOptions options = AuthTokenHasherTests.CreateOptions();
            PostgresAuthRepository auth = new PostgresAuthRepository(database.DataSource, options);
            Guid accountId = Guid.NewGuid();
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();
            Guid townId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO accounts (account_id) VALUES (@AccountId);
                    INSERT INTO characters (character_id, account_id, class_id) VALUES (@First, @AccountId, 5), (@Second, @AccountId, 5);
                    INSERT INTO account_wallets (account_id) VALUES (@AccountId);
                    INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state, boot_id)
                    VALUES (@TownId, 0, '127.0.0.1', 7811, 'social', 'social', 'Idle', @Boot);
                    """, new { AccountId = accountId, First = first, Second = second, TownId = townId, Boot = Guid.NewGuid() });
            }
            IssuedAuthToken serverLease = await auth.IssueServerLeaseAsync(Guid.NewGuid(), townId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(SERVER_MINUTES));
            IssuedAuthToken invite = await auth.IssueInviteAsync(accountId);
            AuthExchangeResponse login = (await auth.ExchangeInviteAsync(invite.Token)).Response!;
            PlayerAuthIdentity identity = (await auth.ValidateAccessAsync(login.AccessToken)).Identity!;

            Assert.Equal(first, await JoinAsync(database, auth, identity, first, townId, serverLease));
            // 같은 계정 재입장은 먼저 임대를 풀어야 한다(ALREADY_CONNECTED). 풀린(만료) 행을 다른 캐릭터가 덮어쓴다
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    "UPDATE account_leases SET expires_at = clock_timestamp() - interval '1 second' WHERE account_id = @AccountId;",
                    new { AccountId = accountId });
            }
            Assert.Equal(second, await JoinAsync(database, auth, identity, second, townId, serverLease));
        }

        private static async Task<Guid> JoinAsync(PostgresTestDatabase database, PostgresAuthRepository auth,
            PlayerAuthIdentity identity, Guid characterId, Guid townId, IssuedAuthToken serverLease)
        {
            IssuedAuthToken token = (await auth.IssueTownJoinTokenAsync(identity, characterId, townId)).Identity!;
            ServerAuthIdentity server = (await auth.ValidateServerLeaseAsync(serverLease.Token)).Identity!;
            AuthValidationOutcome<JoinAuthIdentity> joined = await auth.ConsumeJoinTokenAsync(token.Token, townId,
                ContractCodes.ServerRole.TOWN, server.LeaseId);
            Assert.True(joined.IsSuccess);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<Guid>(
                "SELECT character_id FROM account_leases WHERE account_id = @AccountId AND lease_id = @LeaseId;",
                new { AccountId = identity.AccountId, LeaseId = joined.Identity!.AccountLeaseId });
        }

        private static Task<long> CountUnvalidatedAsync(NpgsqlConnection connection)
        {
            return connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM pg_constraint AS constraint_row
                JOIN pg_class AS table_row ON table_row.oid = constraint_row.conrelid
                JOIN pg_namespace AS schema_row ON schema_row.oid = table_row.relnamespace
                WHERE schema_row.nspname = current_schema() AND NOT constraint_row.convalidated;
                """);
        }

        private static Task<long> CountTablesAsync(NpgsqlConnection connection)
        {
            return connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = ANY(@Names);",
                new { Names = G_Tables });
        }
    }
}
