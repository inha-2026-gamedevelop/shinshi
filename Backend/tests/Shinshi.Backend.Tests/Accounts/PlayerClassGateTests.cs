// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Accounts;
using Shinshi.Backend.Characters;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Auth;
using Shinshi.Data.Characters;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 직업 ID 범위(0~5)와 새 직업 3, 4, 5의 서버 설정 문(Characters:EnabledClassIds), 0016 마이그레이션을 검사한다 </summary>
    public sealed class PlayerClassGateTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MIGRATION_0016 = "0016_player_class_range";
        private const int NEW_CLASS = 3;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(0, true)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        [InlineData(-1, false)]
        public void IsKnown_AcceptsZeroToFiveOnly(int classId, bool expected)
        {
            Assert.Equal(expected, PlayerClassPolicy.IsKnown(classId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("3,4,5")]
        public void Parse_DefaultOrEmpty_OpensOnlyNewClasses(string? text)
        {
            PlayerClassPolicy policy = PlayerClassPolicy.Parse(text);

            Assert.Equal(new[] { 3, 4, 5 }, policy.EnabledClassIds());
            Assert.False(policy.IsEnabled(0));
            Assert.False(policy.IsEnabled(1));
            Assert.False(policy.IsEnabled(2));
            Assert.False(policy.IsEnabled(6));
        }

        [Fact]
        public void Parse_ListWithNewClasses_OpensOnlyListedNewClasses()
        {
            PlayerClassPolicy policy = PlayerClassPolicy.Parse(" 3, 5 ");

            Assert.Equal(new[] { 3, 5 }, policy.EnabledClassIds());
            Assert.True(policy.IsEnabled(3));
            Assert.False(policy.IsEnabled(4));
            Assert.True(policy.IsEnabled(5));
        }

        [Theory]
        [InlineData("0,1,2")]
        [InlineData("2")]
        public void Parse_LegacyClassList_DoesNotReopenCreation(string text)
        {
            PlayerClassPolicy policy = PlayerClassPolicy.Parse(text);

            Assert.Empty(policy.EnabledClassIds());
        }

        [Fact]
        public void Parse_LegacyAndNewClassList_EnablesOnlyNewClasses()
        {
            PlayerClassPolicy policy = PlayerClassPolicy.Parse("0,1,2,3,4,5,3");

            Assert.Equal(new[] { 3, 4, 5 }, policy.EnabledClassIds());
        }

        [Fact]
        public void Constructor_EmptyList_DoesNotAutomaticallyEnableLegacyClasses()
        {
            PlayerClassPolicy policy = new PlayerClassPolicy(Array.Empty<int>());

            Assert.Empty(policy.EnabledClassIds());
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, false)]
        [InlineData(4, false)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        [InlineData(-1, false)]
        public void IsSelectable_LegacyAndEnabledNewClasses_PreservesStoredCharacterCompatibility(int classId,
            bool expected)
        {
            PlayerClassPolicy policy = PlayerClassPolicy.Parse("5");

            Assert.Equal(expected, policy.IsSelectable(classId));
        }

        [Theory]
        [InlineData("6")]
        [InlineData("-1")]
        [InlineData("a")]
        [InlineData("1;2")]
        public void Parse_InvalidList_FailsAtStartup(string text)
        {
            Assert.Throws<InvalidOperationException>(() => PlayerClassPolicy.Parse(text));
        }

        [Fact]
        public void Options_DefaultValue_OpensOnlyNewClasses()
        {
            PlayerClassPolicy policy = new CharacterOptions().ResolvePolicy();

            Assert.Equal(CharacterOptions.DEFAULT_ENABLED_CLASS_IDS, new CharacterOptions().EnabledClassIds);
            Assert.Equal(new[] { 3, 4, 5 }, policy.EnabledClassIds());
            Assert.False(policy.IsEnabled(ContractCodes.PlayerClass.SUMMONER));
        }

        [Theory]
        [InlineData("3,4,5", new[] { 3, 4, 5 })]
        [InlineData("3", new[] { 3 })]
        [InlineData("", new[] { 3, 4, 5 })]
        [InlineData("0,1,2,3,4,5", new[] { 3, 4, 5 })]
        public void AddShinshiAccounts_ConfiguredClassList_ReplacesDefaultAndCannotReopenLegacyClasses(string text,
            int[] expected)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Characters:EnabledClassIds"] = text }).Build();
            ServiceCollection services = new ServiceCollection();
            services.AddShinshiAccounts(configuration);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Equal(expected, provider.GetRequiredService<PlayerClassPolicy>().EnabledClassIds());
        }

        [PostgresFact]
        public async Task CharacterCreate_DefaultPolicy_PersistsOnlyNewClassesAndDoesNotStoreLegacyRejections()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = new PostgresCharacterRepository(database.DataSource,
                new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)));
            using AccountApiHost host = new AccountApiHost(characterRepository: repository);
            (Guid accountId, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts(account_id) VALUES (@AccountId);",
                new { AccountId = accountId });

            for (int classId = PlayerClassPolicy.MIN_CLASS_ID; classId <= PlayerClassPolicy.LEGACY_MAX_ID; ++classId)
            {
                using HttpResponseMessage rejected = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                    new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "Legacy" + classId, classId,
                        CharacterTestData.Appearance())));

                Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
                Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                    AccountApiHost.Read<ApiRejection>(await rejected.Content.ReadAsStringAsync()).ReasonCode);
            }

            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM characters;"));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM operations;"));

            foreach (int classId in PlayerClassPolicy.Default.EnabledClassIds())
            {
                using HttpResponseMessage created = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                    new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "NewClass" + classId, classId,
                        CharacterTestData.Appearance())));

                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                CharacterCreateResponse result = AccountApiHost.Read<CharacterCreateResponse>(
                    await created.Content.ReadAsStringAsync());
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Result.Status);
                Assert.Equal(classId, result.Character.ClassId);
            }

            Assert.Equal(new[] { 3, 4, 5 }, (await repository.ListAsync(accountId))
                .Select(character => character.ClassId).OrderBy(classId => classId).ToArray());
            Assert.Equal(3L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM operations;"));
        }

        /// <summary> 0016 적용 뒤 CHECK 이름이 바뀌어 있고, 새 직업 행이 있으면 down이 데이터를 지우지 않고 중단하며, 행을 치우면 되돌아가고 다시 올라간다 </summary>
        [PostgresFact]
        public async Task Migration0016_DownGuardsNewClassRows_ThenRestoresAndReapplies()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            (long version, string name) = MigrationTestResources.All.Single(entry => entry.Name == MIGRATION_0016);
            Guid accountId = Guid.NewGuid();
            Guid characterId = Guid.NewGuid();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts(account_id) VALUES (@AccountId);
                INSERT INTO characters(character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, @Class);
                """, new { AccountId = accountId, CharacterId = characterId, Class = NEW_CLASS });

            Assert.Equal(1L, await ConstraintCountAsync(connection, "ck_characters_class_id"));
            Assert.Equal(1L, await ConstraintCountAsync(connection, "ck_market_listings_class_id"));
            Assert.Equal(0L, await ConstraintCountAsync(connection, "characters_class_id_check"));

            await Assert.ThrowsAsync<PostgresException>(() => MigrationTestResources.RollBackAboveAsync(connection, version - 1));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM characters WHERE character_id = @CharacterId;", new { CharacterId = characterId }));

            await connection.ExecuteAsync("DELETE FROM characters WHERE character_id = @CharacterId;",
                new { CharacterId = characterId });
            await MigrationTestResources.RollBackAboveAsync(connection, version - 1);

            Assert.Equal(1L, await ConstraintCountAsync(connection, "characters_class_id_check"));
            Assert.Equal(1L, await ConstraintCountAsync(connection, "market_listings_class_id_check"));
            Assert.Equal(0L, await ConstraintCountAsync(connection, "ck_characters_class_id"));
            PostgresException restored = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO characters(character_id, account_id, class_id) VALUES (@Id, @AccountId, @Class);",
                new { Id = Guid.NewGuid(), AccountId = accountId, Class = NEW_CLASS }));
            Assert.Equal(PostgresErrorCodes.CheckViolation, restored.SqlState);

            await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync(name));
            foreach ((long _, string later) in MigrationTestResources.All.Where(entry => entry.Version > version))
            {
                await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync(later));
            }

            await connection.ExecuteAsync(
                "INSERT INTO characters(character_id, account_id, class_id) VALUES (@Id, @AccountId, @Class);",
                new { Id = Guid.NewGuid(), AccountId = accountId, Class = NEW_CLASS });
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM pg_constraint AS constraint_row
                JOIN pg_class AS table_row ON table_row.oid = constraint_row.conrelid
                JOIN pg_namespace AS schema_row ON schema_row.oid = table_row.relnamespace
                WHERE schema_row.nspname = current_schema() AND NOT constraint_row.convalidated;
                """));
        }

        /// <summary> 마을 입장권 발급은 닫힌 새 직업을 FEATURE_DISABLED로 거절하고, 열면 문을 통과하며, 기존 직업은 설정과 무관하다.
        /// 대상 마을이 없어도 문 검사가 먼저이므로 서버 인스턴스를 심지 않는다 </summary>
        [PostgresFact]
        public async Task IssueTownJoin_NewClassCharacter_RespectsGate()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AuthTokenOptions options = AuthTokenHasherTests.CreateOptions();
            PostgresAuthRepository closed = new PostgresAuthRepository(database.DataSource, options,
                PlayerClassPolicy.Parse("0,1,2"));
            PostgresAuthRepository open = new PostgresAuthRepository(database.DataSource, options);
            (Guid accountId, Guid newClassCharacter, Guid legacyCharacter) = await SeedAsync(database);
            PlayerAuthIdentity identity = await LoginAsync(closed, accountId);
            Guid missingTown = Guid.NewGuid();

            AuthValidationOutcome<IssuedAuthToken> closedNew = await closed.IssueTownJoinTokenAsync(identity,
                newClassCharacter, missingTown);
            AuthValidationOutcome<IssuedAuthToken> openNew = await open.IssueTownJoinTokenAsync(identity,
                newClassCharacter, missingTown);
            AuthValidationOutcome<IssuedAuthToken> closedLegacy = await closed.IssueTownJoinTokenAsync(identity,
                legacyCharacter, missingTown);

            Assert.False(closedNew.IsSuccess);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED, closedNew.ReasonCode);
            // 문을 통과하면 다음 검사(등록된 마을 없음)에서 멈춘다
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, openNew.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, closedLegacy.ReasonCode);
        }

        private static async Task<(Guid AccountId, Guid NewClassCharacter, Guid LegacyCharacter)> SeedAsync(
            PostgresTestDatabase database)
        {
            Guid accountId = Guid.NewGuid();
            Guid newClassCharacter = Guid.NewGuid();
            Guid legacyCharacter = Guid.NewGuid();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts(account_id) VALUES (@AccountId);
                INSERT INTO account_wallets(account_id) VALUES (@AccountId);
                INSERT INTO characters(character_id, account_id, class_id)
                    VALUES (@NewClassCharacter, @AccountId, @NewClass), (@LegacyCharacter, @AccountId, @LegacyClass);
                """, new { AccountId = accountId, NewClassCharacter = newClassCharacter, LegacyCharacter = legacyCharacter,
                    NewClass = NEW_CLASS, LegacyClass = ContractCodes.PlayerClass.WARRIOR });
            return (accountId, newClassCharacter, legacyCharacter);
        }

        private static async Task<PlayerAuthIdentity> LoginAsync(PostgresAuthRepository auth, Guid accountId)
        {
            IssuedAuthToken invite = await auth.IssueInviteAsync(accountId);
            AuthExchangeResponse login = (await auth.ExchangeInviteAsync(invite.Token)).Response!;
            return (await auth.ValidateAccessAsync(login.AccessToken)).Identity!;
        }

        private static Task<long> ConstraintCountAsync(NpgsqlConnection connection, string constraintName)
        {
            return connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM pg_constraint AS constraint_row
                JOIN pg_class AS table_row ON table_row.oid = constraint_row.conrelid
                JOIN pg_namespace AS schema_row ON schema_row.oid = table_row.relnamespace
                WHERE schema_row.nspname = current_schema() AND constraint_row.conname = @Name;
                """, new { Name = constraintName });
        }
    }
}
