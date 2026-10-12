// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Growth;
using Shinshi.Contracts;
using Shinshi.Data.Characters;
using Shinshi.Data.Growth;

namespace Shinshi.Backend.Tests.Experience
{
    /// <summary> 기존 자산을 보존하는 경험치 이관과 생성 프로필별 초기값을 실제 DB로 검사한다 </summary>
    public sealed class ExperienceInitializationTests
    {
        [PostgresFact]
        public async Task Create_TrialAndStandard_SetMatchingInitialAndTotalExperience()
        {
            foreach (EGrowthContentProfile profile in new[] { EGrowthContentProfile.Trial, EGrowthContentProfile.Standard })
            {
                await using GrowthTestEnvironment env = await GrowthTestEnvironment.CreateAsync();
                Guid account = Guid.NewGuid();
                await env.ExecuteAsync("INSERT INTO accounts(account_id) VALUES (@Account);", new { Account = account });
                PostgresCharacterRepository repository = new PostgresCharacterRepository(env.DataSource, env.Executor,
                    new InitialGrantService(env.Content, profile, env.Ledger, env.History));
                Guid operation = Guid.NewGuid();
                CharacterCreateOutcome created = await repository.CreateAsync(account, operation, "XpHero", "xphero",
                    ContractCodes.PlayerClass.WARRIOR, "{}", "{}");
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, created.Result.Status);
                long expected = profile == EGrowthContentProfile.Trial ? 2850 : 0;
                Assert.Equal(expected, await env.ScalarAsync("SELECT total_experience FROM characters;"));
                Assert.Equal(expected, await env.ScalarAsync("SELECT initial_experience FROM characters;"));
                Assert.Equal(0, await env.ScalarAsync("SELECT count(*) FROM character_experience_events;"));
                Assert.Equal(0, await env.ScalarAsync("SELECT last_experience_event_seq FROM characters;"));
                CharacterCreateOutcome replay = await repository.CreateAsync(account, operation, "XpHero", "xphero",
                    ContractCodes.PlayerClass.WARRIOR, "{}", "{}");
                Assert.Equal(created.Result.ReceiptId, replay.Result.ReceiptId);
                Assert.Equal(expected, await env.ScalarAsync("SELECT total_experience FROM characters;"));
            }
        }

        [PostgresFact]
        public async Task Migration0017_ExistingCharactersGain2850_OtherRowsAreUnchangedAndNewDefaultIsZero()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(false);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            foreach ((long version, string name) in MigrationTestResources.All.Where(entry => entry.Version < 17))
            {
                await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync(name));
            }
            Guid account = Guid.NewGuid();
            Guid character = Guid.NewGuid();
            await connection.ExecuteAsync("""
                INSERT INTO accounts(account_id) VALUES (@Account);
                INSERT INTO characters(character_id, account_id, class_id, essence) VALUES (@Character, @Account, 0, 321);
                INSERT INTO account_wallets(account_id, tickets) VALUES (@Account, 7);
                INSERT INTO account_items(account_id, definition_id, binding_kind, quantity) VALUES (@Account, 'keep-item', 'Account', 13);
                """, new { Account = account, Character = character });
            string before = await connection.QuerySingleAsync<string>("""
                SELECT jsonb_build_object('character', (SELECT to_jsonb(c) FROM characters c),
                    'wallet', (SELECT to_jsonb(w) FROM account_wallets w),
                    'items', (SELECT jsonb_agg(to_jsonb(i)) FROM account_items i))::text;
                """);
            await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync("0017_character_experience"));
            string after = await connection.QuerySingleAsync<string>("""
                SELECT jsonb_build_object('character', (SELECT to_jsonb(c) - 'initial_experience' - 'total_experience'
                        - 'last_experience_session_id' - 'last_experience_event_seq' FROM characters c),
                    'wallet', (SELECT to_jsonb(w) FROM account_wallets w),
                    'items', (SELECT jsonb_agg(to_jsonb(i)) FROM account_items i))::text;
                """);
            Assert.Equal(before, after);
            Assert.Equal(2850, await connection.QuerySingleAsync<long>("SELECT total_experience FROM characters;"));
            Assert.Equal(2850, await connection.QuerySingleAsync<long>("SELECT initial_experience FROM characters;"));
            await connection.ExecuteAsync("INSERT INTO characters(character_id, account_id, class_id) VALUES (@Id, @Account, 1);",
                new { Id = Guid.NewGuid(), Account = account });
            Assert.Equal(0, await connection.QuerySingleAsync<long>("SELECT total_experience FROM characters WHERE class_id = 1;"));
        }
    }
}
