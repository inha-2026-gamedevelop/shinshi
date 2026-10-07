// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Growth;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;
using Shinshi.Data.Market;
using Shinshi.Data.Trade;

namespace Shinshi.Backend.Tests.Market
{
    /// <summary> 성장 시험 환경 위에 경매장, 거래 저장소를 올린 격리 스키마. 시작 잔액은 원장을 거쳐 넣어 대사가 0건이어야 한다 </summary>
    internal sealed class MarketTestEnvironment : IAsyncDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string HERB     = MarketTradeRules.HERB_ID;
        public const string TALISMAN = "T01-1A";
        public const string POTION   = "item_potion_small";
        public const string WEAPON   = "equip_weapon_common";

        private const string SEED_KIND = "test.market.seed";

        /****************************************
        *              Properties
        ****************************************/

        public GrowthTestEnvironment    Growth  { get; }
        public PostgresMarketRepository Market  { get; }
        public PostgresMarketQueries    Queries { get; }
        public PostgresTradeRepository  Trades  { get; }

        public Guid                     TownId  => Growth.TownId;
        public NpgsqlDataSource         DataSource => Growth.DataSource;

        /****************************************
        *                Methods
        ****************************************/

        private MarketTestEnvironment(GrowthTestEnvironment growth)
        {
            Growth  = growth;
            Market  = new PostgresMarketRepository(growth.DataSource, growth.Executor, growth.Ledger, growth.History,
                growth.Content);
            Queries = new PostgresMarketQueries(growth.DataSource);
            Trades  = new PostgresTradeRepository(growth.DataSource, growth.Executor, growth.Ledger, growth.History,
                growth.Content);
        }

        public static async Task<MarketTestEnvironment> CreateAsync()
        {
            return new MarketTestEnvironment(await GrowthTestEnvironment.CreateAsync());
        }

        public ValueTask DisposeAsync()
        {
            return Growth.DisposeAsync();
        }

        /// <summary> 계정, 캐릭터, 마을 접속 임대를 만들고 엽전과 아이템을 원장과 함께 넣는다 </summary>
        public async Task<GrowthPlayer> CreatePlayerAsync(long currency = 0, IReadOnlyDictionary<string, long>? items = null,
            long essence = 0, long shards = 0)
        {
            GrowthPlayer player = await Growth.CreatePlayerAsync(essence: essence, shards: shards, items: items);
            if (currency > 0)
            {
                await SeedCurrencyAsync(player, currency);
            }

            return player;
        }

        public async Task SeedCurrencyAsync(GrowthPlayer player, long amount)
        {
            OperationRequest request = new OperationRequest(player.AccountId, Guid.NewGuid(), SEED_KIND, "{}");
            OperationResult result = await Growth.Executor.ExecuteAsync(request, async (context, token) =>
            {
                GrowthEffectContext seed = new GrowthEffectContext(context, Growth.Content, Growth.Ledger, Growth.History,
                    player.AccountId, "System", request.OperationId, player.AccountId, player.CharacterId, 0,
                    ContractCodes.EconomyReason.ADMIN_GRANT);
                await seed.CreditAsync(EconomyAsset.Currency(player.AccountId), amount, token);
                return OperationDecision.Committed(0, Guid.NewGuid().ToString("D"));
            });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
        }

        public async Task SeedItemAsync(GrowthPlayer player, string definitionId, long quantity)
        {
            await Growth.SeedAsync(player, 0, 0, new Dictionary<string, long> { [definitionId] = quantity });
        }

        public Task<MarketOperationOutcome> ListAsync(GrowthPlayer player, string definitionId, int quantity, long unitPrice,
            Guid? operationId = null)
        {
            return Market.ListAsync(TownId, player.CharacterId, new MarketListCommand(operationId ?? Guid.NewGuid(),
                MarketCodes.ASSET_ACCOUNT_ITEM, definitionId, null, quantity, unitPrice), false);
        }

        public Task<MarketOperationOutcome> ListEquipmentAsync(GrowthPlayer player, Guid itemId, long unitPrice,
            Guid? operationId = null)
        {
            return Market.ListAsync(TownId, player.CharacterId, new MarketListCommand(operationId ?? Guid.NewGuid(),
                MarketCodes.ASSET_EQUIPMENT, WEAPON, itemId, 1, unitPrice), false);
        }

        public Task<MarketOperationOutcome> BuyAsync(GrowthPlayer buyer, string listingId, int quantity, long unitPrice,
            Guid? operationId = null)
        {
            return Market.BuyAsync(TownId, buyer.CharacterId, operationId ?? Guid.NewGuid(), Guid.Parse(listingId), quantity,
                unitPrice, false);
        }

        public Task<MarketOperationOutcome> CancelAsync(GrowthPlayer seller, string listingId, Guid? operationId = null)
        {
            return Market.CancelAsync(TownId, seller.CharacterId, operationId ?? Guid.NewGuid(), Guid.Parse(listingId));
        }

        /// <summary> 확정 결과를 확인하고 등록 ID를 돌려준다 </summary>
        public static string Listed(MarketOperationOutcome outcome)
        {
            AssertCommitted(outcome);
            Assert.False(string.IsNullOrEmpty(outcome.ListingId));
            return outcome.ListingId;
        }

        public static void AssertCommitted(MarketOperationOutcome outcome)
        {
            Assert.NotNull(outcome.Result);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, outcome.Result!.Status);
            Assert.Equal(ContractCodes.RejectReason.NONE, outcome.Result.ReasonCode);
        }

        public static void AssertRejected(MarketOperationOutcome outcome, int reason)
        {
            Assert.NotNull(outcome.Result);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, outcome.Result!.Status);
            Assert.Equal(reason, outcome.Result.ReasonCode);
        }

        public async Task<long> CurrencyAsync(GrowthPlayer player)
        {
            return await Growth.ScalarAsync(
                "SELECT COALESCE((SELECT currency FROM account_wallets WHERE account_id = @AccountId), 0);",
                new { player.AccountId });
        }

        public async Task<long> ItemQuantityAsync(GrowthPlayer player, string definitionId)
        {
            return await Growth.ScalarAsync(
                "SELECT COALESCE((SELECT quantity FROM account_items WHERE account_id = @AccountId AND definition_id = @Id), 0);",
                new { player.AccountId, Id = definitionId });
        }

        public async Task<ListingSnapshot> ListingAsync(string listingId)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<ListingSnapshot>(
                """
                SELECT state AS "State", remaining_qty AS "RemainingQty", listed_qty AS "ListedQty",
                    gross_total AS "GrossTotal", fee_total AS "FeeTotal", net_total AS "NetTotal",
                    (seen_at IS NOT NULL) AS "Seen", revision AS "Revision"
                FROM market_listings WHERE listing_id = @ListingId;
                """, new { ListingId = Guid.Parse(listingId) });
        }

        /// <summary> 모든 자산의 원장 합계와 잔액이 같고, 활성 등록의 보관 수량이 음수 없이 닫혀 있다 </summary>
        public async Task AssertReconciledAsync()
        {
            await Growth.AssertReconciledAsync();
            Assert.Equal(0L, await Growth.ScalarAsync(
                "SELECT count(*) FROM market_listings WHERE state = 'Active' AND remaining_qty < 1;"));
        }

        /// <summary> 등록 한 건을 만료 시각이 지난 상태로 만든다(created_at도 앞당겨 만료 > 생성 제약을 지킨다) </summary>
        public Task ExpireNowAsync(string listingId)
        {
            return Growth.ExecuteAsync(
                """
                UPDATE market_listings SET created_at = clock_timestamp() - interval '2 days',
                    expires_at = clock_timestamp() - interval '1 day'
                WHERE listing_id = @ListingId;
                """, new { ListingId = Guid.Parse(listingId) });
        }

        public static TradeSideOffer Offer(GrowthPlayer player, long currency, params TradeItemLine[] items)
        {
            return new TradeSideOffer(player.AccountId.ToString("D"), player.CharacterId.ToString("D"), items, currency);
        }

        public static TradeItemLine Item(string definitionId, int quantity)
        {
            return new TradeItemLine(MarketCodes.ASSET_ACCOUNT_ITEM, definitionId, null, quantity);
        }

        public static TradeItemLine Equipment(Guid itemId)
        {
            return new TradeItemLine(MarketCodes.ASSET_EQUIPMENT, WEAPON, itemId.ToString("D"), 1);
        }

        /// <summary> prepare 요청. 실행 ID와 commit 작업 ID는 서로 다른 새 Guid다 </summary>
        public TradePrepareInput PrepareInput(TradeSideOffer sideA, TradeSideOffer sideB, Guid? executionId = null,
            Guid? commitOperationId = null, Guid? townId = null)
        {
            Guid execution = executionId ?? Guid.NewGuid();
            TradePrepareRequest request = new TradePrepareRequest(execution.ToString("D"), execution.ToString("D"),
                Guid.NewGuid().ToString("D"), (townId ?? TownId).ToString("D"), sideA, sideB,
                (commitOperationId ?? Guid.NewGuid()).ToString("D"));
            Assert.True(TradePrepareInput.TryParse(request, out TradePrepareInput? input));
            return input!;
        }

        public async Task<TradeOutcome> PrepareCommittedAsync(TradeSideOffer sideA, TradeSideOffer sideB)
        {
            TradeOutcome prepared = await Trades.PrepareAsync(TownId, PrepareInput(sideA, sideB), false);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, prepared.Result!.Status);
            Assert.Equal(TradeCodes.ExecutionState.PREPARED, prepared.Execution!.State);
            return prepared;
        }

        public async Task<TradeRowSnapshot> ExecutionAsync(string executionId)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<TradeRowSnapshot>(
                """
                SELECT state AS "State", reason_code AS "ReasonCode", commit_operation_id AS "CommitOperationId",
                    revision AS "Revision"
                FROM player_trade_executions WHERE execution_id = @ExecutionId;
                """, new { ExecutionId = Guid.Parse(executionId) });
        }

        public Task AgePreparedAsync(string executionId, int seconds)
        {
            return Growth.ExecuteAsync(
                "UPDATE player_trade_executions SET prepared_at = prepared_at - make_interval(secs => @Seconds) WHERE execution_id = @ExecutionId;",
                new { Seconds = (double)seconds, ExecutionId = Guid.Parse(executionId) });
        }
    }

    internal sealed class ListingSnapshot
    {
        public string State        { get; set; } = string.Empty;
        public int    RemainingQty { get; set; }
        public int    ListedQty    { get; set; }
        public long   GrossTotal   { get; set; }
        public long   FeeTotal     { get; set; }
        public long   NetTotal     { get; set; }
        public bool   Seen         { get; set; }
        public long   Revision     { get; set; }
    }

    internal sealed class TradeRowSnapshot
    {
        public string State             { get; set; } = string.Empty;
        public int    ReasonCode        { get; set; }
        public Guid   CommitOperationId { get; set; }
        public long   Revision          { get; set; }
    }
}
