// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        // 과거 v1 10키 요청의 고정 골든 값. 이 셋 중 하나라도 바뀌면 이미 저장된 과거 operationId의 재시도가 깨진다
        private const string BASELINE_OPERATION_ID = "11111111-2222-3333-4444-555555555555";
        private const string BASELINE_NAME          = "기존캐릭터";
        private const int    BASELINE_CLASS_ID       = 0;
        private const string BASELINE_HASH           = "FF141452A6D2314A391BDFBE86E69CEC145D4FC9871E00342A519179099C8AB9";
        private const string RAW_REQUEST_JSON = "{\"operationId\":\"" + BASELINE_OPERATION_ID + "\"," +
            "\"name\":\"" + BASELINE_NAME + "\",\"classId\":0,\"appearance\":{\"version\":1,\"bodyType\":0," +
            "\"face\":[128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128]," +
            "\"body\":[128,128,128,128,128,128,128,128,128,128],\"skinTone\":128,\"eyeColor\":0,\"browColor\":0," +
            "\"lipColor\":0,\"hairId\":0,\"hairColor\":0,\"faceId\":1,\"outfitId\":3}}";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task List_AccountsWithDifferentCharacters_ReturnsOnlyOwnerAndEmptyForUnknownAccount()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid owner = await AddAccountAsync(database);
            Guid other = await AddAccountAsync(database);
            CharacterCreateOutcome mine = await CreateAsync(repository, owner, Guid.NewGuid(), "MyWarrior");
            await CreateAsync(repository, other, Guid.NewGuid(), "OtherWarrior");

            CharacterRecord character = Assert.Single(await repository.ListAsync(owner));
            Assert.Equal(owner, character.AccountId);
            Assert.Equal(mine.Character!.CharacterId, character.CharacterId);
            Assert.Empty(await repository.ListAsync(Guid.NewGuid()));
        }

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

        // DB가 필요 없는 순수 JSON 회귀 테스트. v1 10키 입력에 v2 전용 신규 키가 섞여 들어와도 구조 검증을 막지 않고,
        // 재직렬화에서는 항상 생략해 과거 operationId의 request_hash가 그대로 보존되어야 한다
        [Fact]
        public void V1AppearanceRequest_DeserializedWithUnknownNewKeys_ReserializesToOriginalTenKeysAndHash()
        {
            CharacterCreateRequest request = JsonConvert.DeserializeObject<CharacterCreateRequest>(RAW_REQUEST_JSON,
                ContractJson.CreateSettings())!;

            Assert.Equal(CharacterAppearance.VERSION_1, request.Appearance.Version);
            Assert.True(request.Appearance.IsWellFormed());

            string reserialized = JsonConvert.SerializeObject(request, ContractJson.CreateSettings());
            Assert.DoesNotContain("faceId", reserialized, StringComparison.Ordinal);
            Assert.DoesNotContain("outfitId", reserialized, StringComparison.Ordinal);
            Assert.Equal(BASELINE_HASH, OperationRequestHasher.ComputeHash(
                PostgresCharacterRepository.CREATE_OPERATION_KIND, reserialized));
        }

        // 과거에 커밋된 이 해시의 영수증을 실제 DB에 심어 두고 같은 계정, 같은 operationId, 같은 본문으로 다시 요청하면
        // 효과를 다시 실행하지 않고 저장된 결과만 그대로 돌려줘야 한다(IdempotentOperationExecutor.Replay)
        [PostgresFact]
        public async Task Create_ReplaysSeededCommittedOperationWithBaselineHash_ReturnsStoredCharacterWithoutNewSideEffects()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = await AddAccountAsync(database);
            Guid operationId = Guid.Parse(BASELINE_OPERATION_ID);
            Guid characterId = Guid.NewGuid();
            (string requestJson, string appearanceJson, string name, string nameKey) = BaselineRequestParts();
            await SeedCommittedOperationAsync(database, accountId, operationId, characterId, BASELINE_CLASS_ID, name,
                nameKey, appearanceJson);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            long charactersBefore = await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM characters;");
            long operationsBefore = await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM operations;");

            PostgresCharacterRepository repository = CreateRepository(database);
            CharacterCreateOutcome replay = await repository.CreateAsync(accountId, operationId, name, nameKey,
                BASELINE_CLASS_ID, appearanceJson, requestJson);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replay.Result.Status);
            Assert.Equal(characterId.ToString("D"), replay.Result.ReceiptId);
            Assert.NotNull(replay.Character);
            Assert.Equal(BASELINE_NAME, replay.Character!.Name);
            Assert.Equal(charactersBefore, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM characters;"));
            Assert.Equal(operationsBefore, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM operations;"));
        }

        // 다른 계정이 같은 본문(같은 해시)으로 과거에 이름 중복으로 거절된 영수증을 심어 두고 다시 요청하면
        // 효과를 다시 실행하지 않고 저장된 거절만 그대로 돌려줘야 한다
        [PostgresFact]
        public async Task Create_ReplaysSeededRejectedOperationWithBaselineHash_ReturnsStoredRejectionWithoutNewSideEffects()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = await AddAccountAsync(database);
            Guid operationId = Guid.Parse(BASELINE_OPERATION_ID);
            (string requestJson, string appearanceJson, string name, string nameKey) = BaselineRequestParts();
            await SeedRejectedOperationAsync(database, accountId, operationId, ContractCodes.RejectReason.CHARACTER_NAME_TAKEN);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            long charactersBefore = await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM characters;");
            long operationsBefore = await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM operations;");

            PostgresCharacterRepository repository = CreateRepository(database);
            CharacterCreateOutcome replay = await repository.CreateAsync(accountId, operationId, name, nameKey,
                BASELINE_CLASS_ID, appearanceJson, requestJson);

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, replay.Result.Status);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NAME_TAKEN, replay.Result.ReasonCode);
            Assert.Null(replay.Character);
            Assert.Equal(charactersBefore, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM characters;"));
            Assert.Equal(operationsBefore, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM operations;"));
        }

        // 위 순수 테스트와 같은 원문을 실제로 생성까지 밀어 넣는다. v1 요청에 v2 전용 신규 키가 섞여도
        // 저장된 외형은 과거 10키 그대로이고 operations.request_hash도 같은 골든 값이어야 한다
        [PostgresFact]
        public async Task Create_V1RequestWithIgnoredFaceAndOutfitKeys_CommitsWithBaselineHashAndTenKeyStoredAppearance()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            Guid operationId = Guid.Parse(BASELINE_OPERATION_ID);
            (string requestJson, string appearanceJson, string name, string nameKey) = BaselineRequestParts();

            CharacterCreateOutcome outcome = await repository.CreateAsync(accountId, operationId, name, nameKey,
                BASELINE_CLASS_ID, appearanceJson, requestJson);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, outcome.Result.Status);
            Assert.NotNull(outcome.Character);
            JObject storedAppearance = JObject.Parse(outcome.Character!.AppearanceJson!);
            Assert.Equal(10, storedAppearance.Properties().Count());
            Assert.DoesNotContain("faceId", storedAppearance.Properties().Select(property => property.Name));
            Assert.DoesNotContain("outfitId", storedAppearance.Properties().Select(property => property.Name));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(BASELINE_HASH, await connection.QuerySingleAsync<string>(
                "SELECT request_hash FROM operations WHERE actor_id = @Actor AND operation_id = @Operation;",
                new { Actor = accountId, Operation = operationId }));
        }

        // 직업과 무관하게 얼굴/헤어/의상을 독립 선택한다. hair(1~3) x outfit(1~3) 9조합 전체가 어떤 직업에서도 통과하고,
        // 저장된 외형은 v2 12키 그대로이며 이름은 생성 경로에서도 정규화(전각 -> 반각)된다
        [PostgresFact]
        public async Task Create_V2AllNineHairOutfitCombinationsIndependentOfClass_CommitsWithTwelveKeyStoredAppearanceAndNormalizedName()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            int[] classes = { ContractCodes.PlayerClass.WARRIOR, ContractCodes.PlayerClass.SORCERER,
                ContractCodes.PlayerClass.SUMMONER };

            for (int hairId = 1; hairId <= 3; ++hairId)
            {
                for (int outfitId = 1; outfitId <= 3; ++outfitId)
                {
                    int classId = classes[(hairId + outfitId) % classes.Length];
                    string rawName = ((hairId == 1) && (outfitId == 1)) ? "ｈｅｒｏ１" : "Hero" + hairId + outfitId;
                    Assert.True(CharacterNameRules.TryNormalize(rawName, out string expectedName, out string _));
                    CharacterAppearance appearance = CharacterTestData.AppearanceV2(CharacterAppearanceCatalog.FACE_CUTE,
                        hairId, outfitId);
                    Assert.True(CharacterAppearanceCatalog.IsAllowed(classId, appearance));
                    Guid accountId = await AddAccountAsync(database);

                    CharacterCreateOutcome outcome = await CreateAsync(repository, accountId, Guid.NewGuid(), rawName,
                        classId, appearance);

                    Assert.Equal(ContractCodes.OperationStatus.COMMITTED, outcome.Result.Status);
                    Assert.NotNull(outcome.Character);
                    Assert.Equal(expectedName, outcome.Character!.Name);
                    CharacterAppearance stored = JsonConvert.DeserializeObject<CharacterAppearance>(
                        outcome.Character.AppearanceJson!, ContractJson.CreateSettings())!;
                    Assert.True(appearance.ContentEquals(stored));
                    JObject storedJson = JObject.Parse(outcome.Character.AppearanceJson!);
                    Assert.Equal(12, storedJson.Properties().Count());
                }
            }
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

        private static Task<CharacterCreateOutcome> CreateAsync(PostgresCharacterRepository repository, Guid accountId,
            Guid operationId, string rawName, int classId, CharacterAppearance appearance)
        {
            Assert.True(CharacterNameRules.TryNormalize(rawName, out string name, out string nameKey));
            CharacterCreateRequest request = new CharacterCreateRequest(operationId.ToString("D"), rawName, classId,
                appearance);
            string requestJson = JsonConvert.SerializeObject(request, ContractJson.CreateSettings());
            string appearanceJson = JsonConvert.SerializeObject(appearance, ContractJson.CreateSettings());
            return repository.CreateAsync(accountId, operationId, name, nameKey, classId, appearanceJson, requestJson);
        }

        // 골든 원문을 역직렬화, 재직렬화해 과거 request_hash(BASELINE_HASH)와 같은 값을 내는
        // 요청/외형 JSON과 이름 쌍을 돌려준다. operationId는 항상 BASELINE_OPERATION_ID다
        private static (string RequestJson, string AppearanceJson, string Name, string NameKey) BaselineRequestParts()
        {
            CharacterCreateRequest request = JsonConvert.DeserializeObject<CharacterCreateRequest>(RAW_REQUEST_JSON,
                ContractJson.CreateSettings())!;
            Assert.True(CharacterNameRules.TryNormalize(request.Name, out string name, out string nameKey));
            string requestJson = JsonConvert.SerializeObject(request, ContractJson.CreateSettings());
            string appearanceJson = JsonConvert.SerializeObject(request.Appearance, ContractJson.CreateSettings());
            return (requestJson, appearanceJson, name, nameKey);
        }

        // 과거에 커밋된 작업과 그 캐릭터 행을 같은 모양으로 심는다. operations.result_json은 실제 OperationResult 직렬화와 같다
        private static async Task SeedCommittedOperationAsync(PostgresTestDatabase database, Guid accountId,
            Guid operationId, Guid characterId, int classId, string name, string nameKey, string appearanceJson)
        {
            OperationResult result = new OperationResult(operationId.ToString("D"), ContractCodes.OperationStatus.COMMITTED,
                ContractCodes.RejectReason.NONE, 0, 0, characterId.ToString("D"));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO characters(character_id, account_id, class_id, name, name_key, appearance)
                    VALUES (@CharacterId, @AccountId, @ClassId, @Name, @NameKey, CAST(@Appearance AS jsonb));
                INSERT INTO operations(actor_id, operation_id, operation_kind, request_hash, status, result_json,
                        committed_revision, receipt_id, committed_at)
                    VALUES (@AccountId, @OperationId, @Kind, @Hash, 'Committed', CAST(@ResultJson AS jsonb), 0,
                        @ReceiptId, clock_timestamp());
                """, new { CharacterId = characterId, AccountId = accountId, ClassId = classId, Name = name,
                    NameKey = nameKey, Appearance = appearanceJson, OperationId = operationId,
                    Kind = PostgresCharacterRepository.CREATE_OPERATION_KIND, Hash = BASELINE_HASH,
                    ResultJson = JsonConvert.SerializeObject(result, ContractJson.CreateSettings()),
                    ReceiptId = characterId.ToString("D") });
        }

        // 과거에 거절된 작업만 심는다. 거절은 캐릭터 행이 없고 receipt_id도 비어 있다
        private static async Task SeedRejectedOperationAsync(PostgresTestDatabase database, Guid accountId,
            Guid operationId, int reasonCode)
        {
            OperationResult result = new OperationResult(operationId.ToString("D"), ContractCodes.OperationStatus.REJECTED,
                reasonCode, 0, 0, string.Empty);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO operations(actor_id, operation_id, operation_kind, request_hash, status, result_json,
                        committed_revision, receipt_id, committed_at)
                    VALUES (@AccountId, @OperationId, @Kind, @Hash, 'Rejected', CAST(@ResultJson AS jsonb), 0, NULL,
                        clock_timestamp());
                """, new { AccountId = accountId, OperationId = operationId,
                    Kind = PostgresCharacterRepository.CREATE_OPERATION_KIND, Hash = BASELINE_HASH,
                    ResultJson = JsonConvert.SerializeObject(result, ContractJson.CreateSettings()) });
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
