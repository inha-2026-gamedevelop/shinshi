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
using Shinshi.Backend.Tests.Market;
using Shinshi.Contracts;
using Shinshi.Data.Characters;
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;
using Shinshi.Data.Trade;

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
        private const string HERB          = MarketTradeRules.HERB_ID;
        private const int    LEASE_MINUTES = 5;
        private const string APPEARANCE   = "{}";
        private const string REQUEST      = "{}";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Create_AccountFirstOnceAndTradableStarterPerCharacter()
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
            Assert.Equal(10, GrowthTestEnvironment.Quantity(growth, HERB));
            Assert.Equal(100L, await CurrencyAsync(environment, account));
            Assert.Equal(100L, await environment.ScalarAsync(
                "SELECT action_value FROM account_wallets WHERE account_id = @Account;", new { Account = account }));

            CharacterCreateOutcome second = await characters.CreateAsync(account, Guid.NewGuid(), "Grant2", "grant2",
                ContractCodes.PlayerClass.SORCERER, APPEARANCE, REQUEST);
            CharacterGrowthSnapshot secondGrowth = Assert.IsType<CharacterGrowthSnapshot>(
                await environment.Reader.ReadAsync(second.Character!.CharacterId));
            Assert.Equal(500, secondGrowth.Essence);
            Assert.Equal(6, secondGrowth.Equipment.Length);
            Assert.Equal(9, GrowthTestEnvironment.Quantity(secondGrowth, POTION_SMALL));

            // 거래 가능한 약초와 엽전은 새 캐릭터마다 계정 보관함과 지갑에 들어간다
            Assert.Equal(20, GrowthTestEnvironment.Quantity(secondGrowth, HERB));
            Assert.Equal(200L, await CurrencyAsync(environment, account));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM initial_grants WHERE account_id = @Account AND family = 'AccountFirst';",
                new { Account = account }));
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM initial_grants WHERE account_id = @Account AND family = 'CharacterFirst';",
                new { Account = account }));
            Assert.Equal(5L, await environment.ScalarAsync(
                "SELECT count(DISTINCT asset_kind || asset_id) FROM economy_ledger WHERE account_id = @Account AND reason = @Reason;",
                new { Account = account, Reason = ContractCodes.EconomyReason.INITIAL_GRANT }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Create_ConcurrentCharactersOnNewAccount_GrantAccountShareOnceAndStarterPerCharacter()
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

            // 서로 다른 동시 캐릭터 셋은 각각 한 번씩 받고, 재전송은 더하지 않는다
            Assert.Equal(30L, await environment.ScalarAsync(
                "SELECT quantity FROM account_items WHERE account_id = @Account AND definition_id = @Herb;",
                new { Account = account, Herb = HERB }));
            Assert.Equal(300L, await CurrencyAsync(environment, account));
            Assert.Equal(3L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE account_id = @Account AND reason = @Reason AND asset_kind = 'Currency';",
                new { Account = account, Reason = ContractCodes.EconomyReason.INITIAL_GRANT }));
            Assert.Equal(3L, await environment.ScalarAsync(
                "SELECT count(*) FROM initial_grants WHERE account_id = @Account AND family = 'CharacterFirst';",
                new { Account = account }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Create_AfterDeleteAndRecreate_GrantsTradableStarterForNewCharacter()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            PostgresCharacterRepository characters = CreateRepository(environment, EGrowthContentProfile.Standard);
            Guid account = await CreateAccountAsync(environment);

            CharacterCreateOutcome first = await characters.CreateAsync(account, Guid.NewGuid(), "Delete1", "delete1",
                ContractCodes.PlayerClass.WARRIOR, APPEARANCE, REQUEST);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Result.Status);
            OperationResult deleted = await Shinshi.Backend.Tests.Accounts.CharacterDeleteTestSupport.DeleteAfterGraceAsync(
                environment.DataSource, characters, account, first.Character!.CharacterId, first.Character.Revision);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, deleted.Status);

            CharacterCreateOutcome again = await characters.CreateAsync(account, Guid.NewGuid(), "Delete2", "delete2",
                ContractCodes.PlayerClass.WARRIOR, APPEARANCE, REQUEST);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, again.Result.Status);

            // 승인된 정책: 삭제 뒤 만든 새 캐릭터도 정기, 수련 장비와 함께 약초 10, 엽전 100을 받는다
            Assert.Equal(500, Assert.IsType<CharacterGrowthSnapshot>(
                await environment.Reader.ReadAsync(again.Character!.CharacterId)).Essence);
            Assert.Equal(20L, await environment.ScalarAsync(
                "SELECT quantity FROM account_items WHERE account_id = @Account AND definition_id = @Herb;",
                new { Account = account, Herb = HERB }));
            Assert.Equal(200L, await CurrencyAsync(environment, account));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM initial_grants WHERE account_id = @Account AND family = 'AccountFirst';",
                new { Account = account }));
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM initial_grants WHERE account_id = @Account AND family = 'CharacterFirst';",
                new { Account = account }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Create_StarterHerbAndCurrency_PassTradeFilterAndCommit()
        {
            await using MarketTestEnvironment market = await MarketTestEnvironment.CreateAsync();
            GrowthTestEnvironment environment = market.Growth;
            PostgresCharacterRepository characters = CreateRepository(environment, EGrowthContentProfile.Standard);
            Guid account = await CreateAccountAsync(environment);
            CharacterCreateOutcome created = await characters.CreateAsync(account, Guid.NewGuid(), "Trader", "trader",
                ContractCodes.PlayerClass.ARCHER, APPEARANCE, REQUEST);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, created.Result.Status);
            CharacterCreateOutcome second = await characters.CreateAsync(account, Guid.NewGuid(), "Trader2", "trader2",
                ContractCodes.PlayerClass.WARRIOR, APPEARANCE, REQUEST);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, second.Result.Status);
            GrowthPlayer starter = new GrowthPlayer(account, second.Character!.CharacterId);
            Data.AccountLeaseOutcome lease = await environment.Leases.TryAcquireAsync(account, Guid.NewGuid(),
                environment.TownId, ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(LEASE_MINUTES));
            Assert.Equal(Data.EAccountLeaseStatus.Acquired, lease.Status);
            GrowthPlayer buyer = await market.CreatePlayerAsync(currency: 40);

            // 지급 물약은 거래 필터에서 막히고 지급 약초는 통과한다
            TradeOutcome potion = await market.Trades.PrepareAsync(market.TownId, market.PrepareInput(
                MarketTestEnvironment.Offer(starter, 0, MarketTestEnvironment.Item(POTION_SMALL, 1)),
                MarketTestEnvironment.Offer(buyer, 0)), false);
            Assert.Equal(ContractCodes.RejectReason.TRADE_NOT_TRADABLE, potion.Result!.ReasonCode);

            TradeOutcome prepared = await market.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(starter, 200, MarketTestEnvironment.Item(HERB, 20)),
                MarketTestEnvironment.Offer(buyer, 40));
            TradeRowSnapshot row = await market.ExecutionAsync(prepared.Execution!.ExecutionId);
            TradeOutcome committed = await market.Trades.CommitAsync(market.TownId,
                Guid.Parse(prepared.Execution.ExecutionId), row.CommitOperationId, false);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, committed.Result!.Status);
            Assert.Equal(0, await market.ItemQuantityAsync(starter, HERB));
            Assert.Equal(20, await market.ItemQuantityAsync(buyer, HERB));
            Assert.Equal(200 - 200 + 40, await market.CurrencyAsync(starter));
            Assert.Equal(40 + 200 - 40, await market.CurrencyAsync(buyer));
            await market.AssertReconciledAsync();
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

        private static Task<long> CurrencyAsync(GrowthTestEnvironment environment, Guid account)
        {
            return environment.ScalarAsync(
                "SELECT COALESCE((SELECT currency FROM account_wallets WHERE account_id = @Account), 0);",
                new { Account = account });
        }

        private static async Task<Guid> CreateAccountAsync(GrowthTestEnvironment environment)
        {
            Guid account = Guid.NewGuid();
            await environment.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@Account);", new { Account = account });
            return account;
        }
    }
}
