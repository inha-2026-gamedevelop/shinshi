// System
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    public sealed class AuthMigrationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string RESOURCE_PREFIX = "Shinshi.Data.Migrations.";
        private const string INITIAL = "0001_initial.up.sql";
        private const string SECOND = "0002_dungeon_session_constraints.up.sql";
        private const string UP = "0003_auth_ledger.up.sql";
        private const string DOWN = "0003_auth_ledger.down.sql";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task UpThenDownThenUp_LegacyTokensAndUsedInvite_PreservesBaseRowsAndBackfillsFamilies()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(applyMigrations: false);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(ReadSql(INITIAL));
            await connection.ExecuteAsync(ReadSql(SECOND));
            Guid account = Guid.NewGuid();
            Guid refresh = Guid.NewGuid();
            await connection.ExecuteAsync("""
                INSERT INTO accounts (account_id) VALUES (@Account);
                INSERT INTO auth_invites (invite_id, account_id, code_hash, expires_at, used_at)
                VALUES (@Invite,@Account,@Hash,clock_timestamp() + interval '1 day',clock_timestamp());
                INSERT INTO auth_refresh_tokens (token_id,account_id,token_hash,expires_at,revoked_at)
                VALUES (@Refresh,@Account,@Hash,clock_timestamp() + interval '1 day',clock_timestamp());
                """, new { Account = account, Invite = Guid.NewGuid(), Refresh = refresh, Hash = new byte[32] });
            await connection.ExecuteAsync(ReadSql(UP));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT used_count FROM auth_invites;"));
            Assert.Equal(refresh, await connection.QuerySingleAsync<Guid>("SELECT family_id FROM auth_refresh_tokens;"));
            Assert.True(await connection.QuerySingleAsync<bool>("SELECT revoked_at IS NOT NULL FROM auth_token_families;"));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT max_uses FROM auth_invites;"));

            await connection.ExecuteAsync(ReadSql(DOWN));
            Assert.Equal(account, await connection.QuerySingleAsync<Guid>("SELECT account_id FROM accounts;"));
            Assert.Equal(refresh, await connection.QuerySingleAsync<Guid>("SELECT token_id FROM auth_refresh_tokens;"));
            Assert.True(await connection.QuerySingleAsync<bool>("SELECT used_at IS NOT NULL FROM auth_invites;"));
            await connection.ExecuteAsync(ReadSql(UP));
            Assert.Equal(refresh, await connection.QuerySingleAsync<Guid>("SELECT family_id FROM auth_refresh_tokens;"));
        }

        private static string ReadSql(string name)
        {
            Assembly assembly = typeof(SqlMigrationRunner).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(RESOURCE_PREFIX + name)
                ?? throw new InvalidOperationException("Required migration resource is absent.");
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
