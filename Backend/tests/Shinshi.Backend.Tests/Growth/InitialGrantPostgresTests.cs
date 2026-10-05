// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Logging;
using Xunit;

// Project
using Shinshi.Backend.Ledger;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts;
using Shinshi.Data.Characters;
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 캐릭터 생성의 최초 지원과 일일 대사를 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class InitialGrantPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string POTION_SMALL = "item_potion_small";
        private const string POTION_LARGE = "item_potion_large";
        private const string APPEARANCE   = "{}";
        private const string REQUEST      = "{}";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Create_AccountFirstOnceAndCharacterGrantPerCharacter()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            PostgresCharacterRepository characters = CreateRepository(environment, EGrowthContentProfile.Trial);
            Guid account = await CreateAccountAsync(environment);

            CharacterCreateOutcome first = await characters.CreateAsync(account, Guid.NewGuid(), "Grant1", "grant1",
                ContractCodes.PlayerClass.WARRIOR, APPEARANCE, REQUEST);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Result.Status);
            CharacterGrowthSnapshot growth = Assert.IsType<CharacterGrowthSnapshot>(
                await environment.Reader.ReadAsync(first.Character!.CharacterId));
            Assert.Equal(500, growth.Essence);
            Assert.Equal(6, growth.Equipment.Length);
            Assert.All(growth.Equipment, item => Assert.Equal((17, 1, true, true, GrowthCodes.EquipmentOrigin.TRIAL_GRANT),
                (item.Enhancement, item.Grade, item.Equipped, item.Bound, item.Origin)));
            Assert.Equal(Enumerable.Range(0, GrowthContent.SLOT_COUNT), growth.Equipment.Select(item => item.Slot));
            Assert.Equal(9, GrowthTestEnvironment.Quantity(growth, POTION_SMALL));
            Assert.Equal(9, GrowthTestEnvironment.Quantity(growth, POTION_LARGE));
            Assert.Equal(100L, await environment.ScalarAsync(
                "SELECT action_value FROM account_wallets WHERE account_id = @Account;", new { Account = account }));

            CharacterCreateOutcome second = await characters.CreateAsync(account, Guid.NewGuid(), "Grant2", "grant2",
                ContractCodes.PlayerClass.SORCERER, APPEARANCE, REQUEST);
            CharacterGrowthSnapshot secondGrowth = Assert.IsType<CharacterGrowthSnapshot>(
                await environment.Reader.ReadAsync(second.Character!.CharacterId));
            Assert.Equal(500, secondGrowth.Essence);
            Assert.Equal(6, secondGrowth.Equipment.Length);
            Assert.Equal(9, GrowthTestEnvironment.Quantity(secondGrowth, POTION_SMALL));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM initial_grants WHERE account_id = @Account AND family = 'AccountFirst';",
                new { Account = account }));
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM initial_grants WHERE account_id = @Account AND family = 'CharacterFirst';",
                new { Account = account }));
            Assert.Equal(3L, await environment.ScalarAsync(
                "SELECT count(DISTINCT asset_kind || asset_id) FROM economy_ledger WHERE account_id = @Account AND reason = @Reason;",
                new { Account = account, Reason = ContractCodes.EconomyReason.INITIAL_GRANT }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Create_ConcurrentCharactersOnNewAccount_GrantAccountShareOnce()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            PostgresCharacterRepository characters = CreateRepository(environment, EGrowthContentProfile.Standard);
            Guid account = await CreateAccountAsync(environment);

            CharacterCreateOutcome[] created = await Task.WhenAll(Enumerable.Range(0, 3).Select(index =>
                characters.CreateAsync(account, Guid.NewGuid(), "Racer" + index, "racer" + index,
                    ContractCodes.PlayerClass.SUMMONER, APPEARANCE, REQUEST)));

            Assert.All(created, outcome => Assert.Equal(ContractCodes.OperationStatus.COMMITTED, outcome.Result.Status));
            Assert.Equal(9L, await environment.ScalarAsync(
                "SELECT quantity FROM account_items WHERE account_id = @Account AND definition_id = @Potion;",
                new { Account = account, Potion = POTION_SMALL }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_items WHERE account_id = @Account AND definition_id = @Potion;",
                new { Account = account, Potion = POTION_LARGE }));
            Assert.Equal(1500L, await environment.ScalarAsync(
                "SELECT sum(essence)::bigint FROM characters WHERE account_id = @Account;", new { Account = account }));
            Assert.Equal(18L, await environment.ScalarAsync(
                "SELECT count(*) FROM equipment_instances WHERE account_id = @Account AND is_training AND origin = 'InitialGrant';",
                new { Account = account }));

            // 같은 생성 operationId 재전송은 지급을 다시 하지 않는다
            CharacterCreateOutcome replay = await characters.CreateAsync(account,
                Guid.Parse(created[0].Result.OperationId), "Racer0", "racer0", ContractCodes.PlayerClass.SUMMONER,
                APPEARANCE, REQUEST);
            Assert.Equal(created[0].Result.ReceiptId, replay.Result.ReceiptId);
            Assert.Equal(1500L, await environment.ScalarAsync(
                "SELECT sum(essence)::bigint FROM characters WHERE account_id = @Account;", new { Account = account }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Reconciliation_ClaimsOncePerDayAndLogsMismatchIdsOnly()
        {
            await using GrowthTestEnvironment clean = await GrowthTestEnvironment.CreateAsync();
            await clean.CreatePlayerAsync(essence: 300, shards: 5);
            RecordingLoggerProvider cleanLogs = new RecordingLoggerProvider();
            PostgresReconciliationRunRepository cleanRuns = new PostgresReconciliationRunRepository(clean.DataSource);
            EconomyReconciliationService cleanService = new EconomyReconciliationService(clean.Reconciler, cleanRuns,
                cleanLogs.CreateLogger<EconomyReconciliationService>(), TimeProvider.System);

            Assert.Equal(0L, await cleanService.RunOnceAsync(default));
            Assert.Null(await cleanService.RunOnceAsync(default));
            Assert.Equal(0L, await clean.ScalarAsync(
                "SELECT mismatch_count FROM economy_reconciliation_runs WHERE finished_at IS NOT NULL;"));
            Assert.Equal(0, cleanLogs.Count(LogLevel.Error, "mismatch"));

            await using GrowthTestEnvironment broken = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await broken.CreatePlayerAsync(essence: 300);
            await broken.ExecuteAsync("UPDATE characters SET essence = essence + 7 WHERE character_id = @CharacterId;",
                new { player.CharacterId });
            RecordingLoggerProvider brokenLogs = new RecordingLoggerProvider();
            EconomyReconciliationService brokenService = new EconomyReconciliationService(broken.Reconciler,
                new PostgresReconciliationRunRepository(broken.DataSource),
                brokenLogs.CreateLogger<EconomyReconciliationService>(), TimeProvider.System);

            Assert.Equal(1L, await brokenService.RunOnceAsync(default));
            Assert.Equal(1, brokenLogs.Count(LogLevel.Error, player.AccountId.ToString()));
            Assert.Equal(0, brokenLogs.Count(LogLevel.Error, "307"));
        }

        [PostgresFact]
        public async Task Progress_RecordOnceAndReadDerivedUnlocks()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            PostgresAccountProgressRepository progress = new PostgresAccountProgressRepository(environment.DataSource,
                environment.Content, EGrowthContentProfile.Trial);
            Guid account = await CreateAccountAsync(environment);

            AccountProgressResponse empty = await progress.ReadAsync(account);
            Assert.False(empty.IntroCompleted);
            Assert.False(empty.Regions.Single(view => view.TargetId == "region_forest").Unlocked);

            bool[] changes = new bool[5];
            Data.OperationRequest request = new Data.OperationRequest(account, Guid.NewGuid(), "test.progress", "{}");
            OperationResult result = await environment.Executor.ExecuteAsync(request, async (context, token) =>
            {
                changes[0] = await progress.RecordAsync(context, account, EAccountProgressKind.IntroCompleted, string.Empty, 0,
                    request.OperationId, token);
                changes[1] = await progress.RecordAsync(context, account, EAccountProgressKind.RegionCleared, "region_forest",
                    0, request.OperationId, token);
                changes[2] = await progress.RecordAsync(context, account, EAccountProgressKind.RegionCleared, "region_forest",
                    0, request.OperationId, token);
                changes[3] = await progress.RecordAsync(context, account, EAccountProgressKind.FoxTraces, string.Empty, 5,
                    request.OperationId, token);
                changes[4] = await progress.RecordAsync(context, account, EAccountProgressKind.FoxTraces, string.Empty, 1,
                    request.OperationId, token);
                return Data.OperationDecision.Committed(0, Guid.NewGuid().ToString("D"));
            });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
            Assert.Equal(new[] { true, true, false, true, false }, changes);
            AccountProgressResponse recorded = await progress.ReadAsync(account);
            Assert.True(recorded.IntroCompleted);
            Assert.Equal(environment.Content.Progression.FoxTraceCap, recorded.FoxTraces);
            Assert.Equal(3, recorded.Revision);
            ProgressTargetView forest = recorded.Regions.Single(view => view.TargetId == "region_forest");
            Assert.True(forest.Unlocked && forest.Cleared);
            Assert.Equal(environment.Content.ContentVersion, recorded.ContentVersion);
        }

        private static PostgresCharacterRepository CreateRepository(GrowthTestEnvironment environment,
            EGrowthContentProfile profile)
        {
            return new PostgresCharacterRepository(environment.DataSource, environment.Executor,
                new InitialGrantService(environment.Content, profile, environment.Ledger, environment.History));
        }

        private static async Task<Guid> CreateAccountAsync(GrowthTestEnvironment environment)
        {
            Guid account = Guid.NewGuid();
            await environment.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@Account);", new { Account = account });
            return account;
        }
    }
}
