// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Characters;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 실제 PostgreSQL에서 캐릭터 생성의 이름 유일성, 멱등 재전송, 동시 같은 이름 경합을 검사한다 </summary>
    public sealed class CharactersPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CONCURRENT_ACCOUNTS = 6;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Create_CommitsAndSameOperationReplaysSameCharacter()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            Guid operationId = Guid.NewGuid();

            CharacterCreateOutcome first = await CreateAsync(repository, accountId, operationId, "Shinshi");
            CharacterCreateOutcome replay = await CreateAsync(repository, accountId, operationId, "Shinshi");

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Result.Status);
            Assert.NotNull(first.Character);
            Assert.Equal("Shinshi", first.Character!.Name);
            Assert.Equal(first.Result.ReceiptId, first.Character.CharacterId.ToString("D"));
            Assert.Equal(first.Result.ReceiptId, replay.Result.ReceiptId);
            Assert.Equal(first.Character.CharacterId, replay.Character!.CharacterId);
            CharacterAppearance appearance = JsonConvert.DeserializeObject<CharacterAppearance>(
                first.Character.AppearanceJson!, ContractJson.CreateSettings())!;
            Assert.True(appearance.IsWellFormed());
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM characters;"));
            Assert.Equal("shinshi", await connection.QuerySingleAsync<string>("SELECT name_key FROM characters;"));
        }

        [PostgresFact]
        public async Task Create_SameOperationDifferentBody_IsIdempotencyConflict()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            Guid operationId = Guid.NewGuid();
            await CreateAsync(repository, accountId, operationId, "Shinshi");

            CharacterCreateOutcome conflict = await CreateAsync(repository, accountId, operationId, "Other");

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, conflict.Result.Status);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.Result.ReasonCode);
            Assert.Null(conflict.Character);
        }

        [PostgresFact]
        public async Task Create_FullwidthAndCaseVariantsOfTakenName_AreRejectedAndRejectionIsStored()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid owner = await AddAccountAsync(database);
            Guid other = await AddAccountAsync(database);
            await CreateAsync(repository, owner, Guid.NewGuid(), "Shinshi");

            Guid fullwidthOperation = Guid.NewGuid();
            CharacterCreateOutcome fullwidth = await CreateAsync(repository, other, fullwidthOperation, "ＳＨＩＮＳＨＩ");
            CharacterCreateOutcome lower = await CreateAsync(repository, other, Guid.NewGuid(), "shinshi");
            CharacterCreateOutcome replay = await CreateAsync(repository, other, fullwidthOperation, "ＳＨＩＮＳＨＩ");

            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NAME_TAKEN, fullwidth.Result.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NAME_TAKEN, lower.Result.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NAME_TAKEN, replay.Result.ReasonCode);
            Assert.Null(fullwidth.Character);
            Assert.True(await repository.IsNameTakenAsync("shinshi"));
            Assert.False(await repository.IsNameTakenAsync("free"));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal("Rejected", await connection.QuerySingleAsync<string>(
                "SELECT status FROM operations WHERE actor_id = @Actor AND operation_id = @Operation;",
                new { Actor = other, Operation = fullwidthOperation }));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM characters;"));
        }

        [PostgresFact]
        public async Task Create_ConcurrentSameNameFromDifferentAccounts_OnlyOneWins()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid[] accounts = new Guid[CONCURRENT_ACCOUNTS];
            for (int i = 0; i < accounts.Length; ++i)
            {
                accounts[i] = await AddAccountAsync(database);
            }
            string[] variants = { "여우Fox", "여우FOX", "여우ｆｏｘ", "여우fox", "여우Ｆｏｘ", "여우fOx" };

            CharacterCreateOutcome[] outcomes = await Task.WhenAll(accounts.Select((accountId, index) =>
                CreateAsync(repository, accountId, Guid.NewGuid(), variants[index])));

            Assert.Single(outcomes, outcome => outcome.Result.Status == ContractCodes.OperationStatus.COMMITTED);
            Assert.All(outcomes.Where(outcome => outcome.Result.Status != ContractCodes.OperationStatus.COMMITTED),
                outcome => Assert.Equal(ContractCodes.RejectReason.CHARACTER_NAME_TAKEN, outcome.Result.ReasonCode));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM characters WHERE name_key = '여우fox';"));
        }

        [PostgresFact]
        public async Task Create_PastAccountLimit_IsRejectedWithNoCapacity()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            for (int i = 0; i < PostgresCharacterRepository.MAX_CHARACTERS_PER_ACCOUNT; ++i)
            {
                CharacterCreateOutcome created = await CreateAsync(repository, accountId, Guid.NewGuid(), "hero" + i);
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, created.Result.Status);
            }

            CharacterCreateOutcome extra = await CreateAsync(repository, accountId, Guid.NewGuid(), "heroextra");

            Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, extra.Result.ReasonCode);
        }

        [PostgresFact]
        public async Task Schema_LegacyRowsWithoutNameAreAllowedButNamedRowsMustMatchRules()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = await AddAccountAsync(database);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            await connection.ExecuteAsync(
                "INSERT INTO characters(character_id, account_id, class_id) VALUES (@A, @Account, 0), (@B, @Account, 1);",
                new { A = Guid.NewGuid(), B = Guid.NewGuid(), Account = accountId });
            PostgresException mismatch = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO characters(character_id, account_id, class_id, name, name_key) VALUES (@Id, @Account, 0, 'Fox', 'fox2');",
                new { Id = Guid.NewGuid(), Account = accountId }));
            PostgresException symbol = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO characters(character_id, account_id, class_id, name, name_key) VALUES (@Id, @Account, 0, 'f x', 'f x');",
                new { Id = Guid.NewGuid(), Account = accountId }));

            Assert.Equal(PostgresErrorCodes.CheckViolation, mismatch.SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, symbol.SqlState);
        }

        private static Task<CharacterCreateOutcome> CreateAsync(PostgresCharacterRepository repository, Guid accountId,
            Guid operationId, string rawName)
        {
            Assert.True(CharacterNameRules.TryNormalize(rawName, out string name, out string nameKey));
            CharacterCreateRequest request = new CharacterCreateRequest(operationId.ToString("D"), rawName,
                ContractCodes.PlayerClass.WARRIOR, CharacterTestData.Appearance());
            string requestJson = JsonConvert.SerializeObject(request, ContractJson.CreateSettings());
            string appearanceJson = JsonConvert.SerializeObject(request.Appearance, ContractJson.CreateSettings());
            return repository.CreateAsync(accountId, operationId, name, nameKey, request.ClassId, appearanceJson,
                requestJson);
        }

        private static PostgresCharacterRepository CreateRepository(PostgresTestDatabase database)
        {
            return new PostgresCharacterRepository(database.DataSource,
                new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)));
        }

        private static async Task<Guid> AddAccountAsync(PostgresTestDatabase database)
        {
            Guid accountId = Guid.NewGuid();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts(account_id) VALUES (@AccountId);",
                new { AccountId = accountId });
            return accountId;
        }
    }
}
