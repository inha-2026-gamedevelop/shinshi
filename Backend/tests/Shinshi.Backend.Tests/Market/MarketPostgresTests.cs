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
using Shinshi.Data.Growth;
using Shinshi.Data.Inbox;
using Shinshi.Data.Market;
using Shinshi.Data.Trade;

namespace Shinshi.Backend.Tests.Market
{
    /// <summary> 유저 경매장 쓰기를 실제 PostgreSQL에서 검사한다: 등록(보관), 구매(수수료, 대금), 취소, 만료, 경합, 재전송 </summary>
    public sealed class MarketPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int  CONCURRENT_CALLS = 4;
        private const long UNIT_PRICE       = 100;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task List_AccountItem_EscrowsQuantityAndWritesLedger()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));

            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 4,
                UNIT_PRICE));

            Assert.Equal(6, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            ListingSnapshot listing = await environment.ListingAsync(listingId);
            Assert.Equal((MarketCodes.ListingState.ACTIVE, 4, 4), (listing.State, listing.RemainingQty, listing.ListedQty));
            Assert.Equal(-4L, await environment.Growth.ScalarAsync(
                "SELECT COALESCE(sum(delta), 0) FROM economy_ledger WHERE reason = @Reason AND account_id = @AccountId;",
                new { Reason = ContractCodes.EconomyReason.MARKET_ESCROW, seller.AccountId }));
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                """
                SELECT count(*) FROM market_listings WHERE listing_id = @ListingId AND category = @Category
                    AND expires_at > created_at + interval '23 hours' AND expires_at <= created_at + interval '25 hours';
                """, new { ListingId = Guid.Parse(listingId), Category = MarketCodes.Category.HERB }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task List_RejectedInputs_ChangeNothing()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: new Dictionary<string, long>
            {
                [MarketTestEnvironment.HERB] = 10, [MarketTestEnvironment.POTION] = 10, ["item_boss_material_common"] = 5
            });

            MarketTestEnvironment.AssertRejected(await environment.ListAsync(seller, MarketTestEnvironment.POTION, 1, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_NOT_TRADABLE);
            MarketTestEnvironment.AssertRejected(await environment.ListAsync(seller, "item_boss_material_common", 1, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_NOT_TRADABLE);
            MarketTestEnvironment.AssertRejected(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 1, 0),
                ContractCodes.RejectReason.MARKET_PRICE_OUT_OF_RANGE);
            MarketTestEnvironment.AssertRejected(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 1,
                MarketLimits.MAX_UNIT_PRICE + 1), ContractCodes.RejectReason.MARKET_PRICE_OUT_OF_RANGE);
            MarketTestEnvironment.AssertRejected(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 0, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_PRICE_OUT_OF_RANGE);
            MarketTestEnvironment.AssertRejected(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 11, UNIT_PRICE),
                ContractCodes.RejectReason.INSUFFICIENT_ITEM);

            Assert.Equal(0L, await environment.Growth.ScalarAsync("SELECT count(*) FROM market_listings;"));
            Assert.Equal(10, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            Assert.Equal(10, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.POTION));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task List_ConcurrentBeyondTen_OnlyTenActiveAndNoExtraEscrow()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(100));

            MarketOperationOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, MarketLimits.MAX_ACTIVE_LISTINGS + 4)
                .Select(_ => environment.ListAsync(seller, MarketTestEnvironment.HERB, 1, UNIT_PRICE)));

            Assert.Equal(MarketLimits.MAX_ACTIVE_LISTINGS, outcomes.Count(outcome =>
                outcome.Result!.Status == ContractCodes.OperationStatus.COMMITTED));
            Assert.All(outcomes.Where(outcome => outcome.Result!.Status == ContractCodes.OperationStatus.REJECTED),
                outcome => Assert.Equal(ContractCodes.RejectReason.MARKET_LISTING_LIMIT, outcome.Result!.ReasonCode));
            Assert.Equal(MarketLimits.MAX_ACTIVE_LISTINGS, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM market_listings WHERE state = 'Active';"));
            Assert.Equal(100 - MarketLimits.MAX_ACTIVE_LISTINGS, await environment.ItemQuantityAsync(seller,
                MarketTestEnvironment.HERB));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Buy_PartialThenRest_SettlesFeeProceedsAndTotals()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 10000);
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 10, 7));

            // 7 * 3 = 21, 수수료 올림 2, 판매자 19
            MarketOperationOutcome first = await environment.BuyAsync(buyer, listingId, 3, 7);
            MarketTestEnvironment.AssertCommitted(first);
            Assert.Equal((21L, 2L, 19L), (first.Gross, first.Fee, first.Net));
            Assert.Equal(listingId, first.ListingId);
            Assert.Equal(10000 - 21, await environment.CurrencyAsync(buyer));
            Assert.Equal(19L, await environment.CurrencyAsync(seller));
            Assert.Equal(3, await environment.ItemQuantityAsync(buyer, MarketTestEnvironment.HERB));
            ListingSnapshot partial = await environment.ListingAsync(listingId);
            Assert.Equal((MarketCodes.ListingState.ACTIVE, 7, 21L, 2L, 19L),
                (partial.State, partial.RemainingQty, partial.GrossTotal, partial.FeeTotal, partial.NetTotal));

            MarketOperationOutcome rest = await environment.BuyAsync(buyer, listingId, 7, 7);
            MarketTestEnvironment.AssertCommitted(rest);
            Assert.Equal((49L, 3L, 46L), (rest.Gross, rest.Fee, rest.Net));
            ListingSnapshot sold = await environment.ListingAsync(listingId);
            Assert.Equal((MarketCodes.ListingState.SOLD_OUT, 0, 70L, 5L, 65L),
                (sold.State, sold.RemainingQty, sold.GrossTotal, sold.FeeTotal, sold.NetTotal));
            Assert.Equal(10, await environment.ItemQuantityAsync(buyer, MarketTestEnvironment.HERB));
            Assert.Equal(2L, await environment.Growth.ScalarAsync("SELECT count(*) FROM market_trades;"));

            // 수수료는 소각된다: 구매자 차감 합(70) = 판매자 대금(65) + 소각(5)
            Assert.Equal(65L, await environment.CurrencyAsync(seller));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Buy_PriceOneQuantityOne_FeeConsumesWholePriceAndSkipsZeroLedger()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(1));
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 5);
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 1, 1));

            MarketOperationOutcome bought = await environment.BuyAsync(buyer, listingId, 1, 1);

            MarketTestEnvironment.AssertCommitted(bought);
            Assert.Equal((1L, 1L, 0L), (bought.Gross, bought.Fee, bought.Net));
            Assert.Equal(4L, await environment.CurrencyAsync(buyer));
            Assert.Equal(0L, await environment.CurrencyAsync(seller));
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason;",
                new { Reason = ContractCodes.EconomyReason.MARKET_PROCEEDS }));
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM market_trades WHERE gross = 1 AND fee = 1 AND net = 0;"));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Buy_ConcurrentLastQuantity_ExactlyOneSucceedsAndNothingOversold()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(5));
            List<GrowthPlayer> buyers = new List<GrowthPlayer>();
            for (int index = 0; index < CONCURRENT_CALLS; ++index)
            {
                buyers.Add(await environment.CreatePlayerAsync(currency: 1000));
            }
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 5, UNIT_PRICE));

            MarketOperationOutcome[] outcomes = await Task.WhenAll(buyers.Select(buyer =>
                environment.BuyAsync(buyer, listingId, 5, UNIT_PRICE)));

            Assert.Single(outcomes, outcome => outcome.Result!.Status == ContractCodes.OperationStatus.COMMITTED);
            Assert.All(outcomes.Where(outcome => outcome.Result!.Status == ContractCodes.OperationStatus.REJECTED),
                outcome => Assert.Equal(ContractCodes.RejectReason.MARKET_SOLD_OUT, outcome.Result!.ReasonCode));
            ListingSnapshot listing = await environment.ListingAsync(listingId);
            Assert.Equal((MarketCodes.ListingState.SOLD_OUT, 0), (listing.State, listing.RemainingQty));

            long herbs = 0;
            long spent = 0;
            foreach (GrowthPlayer buyer in buyers)
            {
                herbs += await environment.ItemQuantityAsync(buyer, MarketTestEnvironment.HERB);
                spent += 1000 - await environment.CurrencyAsync(buyer);
            }
            Assert.Equal(5L, herbs);
            Assert.Equal(500L, spent);
            Assert.Equal(1L, await environment.Growth.ScalarAsync("SELECT count(*) FROM market_trades;"));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Buy_RejectionsApplyNothing()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(currency: 1000, items: Herbs(10));
            GrowthPlayer poor = await environment.CreatePlayerAsync(currency: 50);
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 10, UNIT_PRICE));

            // 자기 등록
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(seller, listingId, 1, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_SELF_PURCHASE);
            // 잔액 부족: 구매자도 판매자도 바뀌지 않는다
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(poor, listingId, 1, UNIT_PRICE),
                ContractCodes.RejectReason.INSUFFICIENT_CURRENCY);
            // 가격이 확인한 값과 다르다, 수량 부족, 범위 밖
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(buyer, listingId, 1, UNIT_PRICE + 1),
                ContractCodes.RejectReason.MARKET_PRICE_CHANGED);
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(buyer, listingId, 11, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_QUANTITY_CHANGED);
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(buyer, listingId, 0, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_PRICE_OUT_OF_RANGE);
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(buyer, Guid.NewGuid().ToString("D"), 1, UNIT_PRICE),
                ContractCodes.RejectReason.INVALID_TARGET);

            Assert.Equal(50L, await environment.CurrencyAsync(poor));
            Assert.Equal(1000L, await environment.CurrencyAsync(buyer));
            Assert.Equal(1000L, await environment.CurrencyAsync(seller));
            Assert.Equal(0L, await environment.ItemQuantityAsync(buyer, MarketTestEnvironment.HERB));
            Assert.Equal(10, (await environment.ListingAsync(listingId)).RemainingQty);
            Assert.Equal(0L, await environment.Growth.ScalarAsync("SELECT count(*) FROM market_trades;"));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Buy_InventoryFull_RejectsWithoutChargeForItemsAndEquipment()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));
            GrowthPlayer fullHerbs = await environment.CreatePlayerAsync(currency: 1000,
                items: Herbs(RewardStorageLimits.HERB_STACK));
            GrowthPlayer fullSlots = await environment.CreatePlayerAsync(currency: 1000);
            string herbListing = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 10,
                UNIT_PRICE));
            Guid item = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0);
            string equipmentListing = MarketTestEnvironment.Listed(await environment.ListEquipmentAsync(seller, item, UNIT_PRICE));
            for (int index = 0; index < environment.Growth.Content.Equipment.StorageSlotsPerCharacter; ++index)
            {
                await environment.Growth.InsertEquipmentAsync(fullSlots, "equip_head_common", 0);
            }

            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(fullHerbs, herbListing, 1, UNIT_PRICE),
                ContractCodes.RejectReason.INVENTORY_FULL);
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(fullSlots, equipmentListing, 1, UNIT_PRICE),
                ContractCodes.RejectReason.INVENTORY_FULL);

            Assert.Equal(1000L, await environment.CurrencyAsync(fullHerbs));
            Assert.Equal(1000L, await environment.CurrencyAsync(fullSlots));
            Assert.Equal(0L, await environment.CurrencyAsync(seller));
            Assert.Equal(MarketCodes.ListingState.ACTIVE, (await environment.ListingAsync(equipmentListing)).State);
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM equipment_instances WHERE item_id = @Item AND escrow_kind = 'Market';", new { Item = item }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Operations_SameIdSameBody_ReturnFirstResultAndApplyOnce()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
            Guid listOperation = Guid.NewGuid();
            Guid buyOperation = Guid.NewGuid();

            MarketOperationOutcome[] listed = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS).Select(_ =>
                environment.ListAsync(seller, MarketTestEnvironment.HERB, 5, UNIT_PRICE, listOperation)));
            string listingId = MarketTestEnvironment.Listed(listed[0]);
            Assert.All(listed, outcome => Assert.Equal(listingId, outcome.ListingId));
            Assert.Equal(1L, await environment.Growth.ScalarAsync("SELECT count(*) FROM market_listings;"));
            Assert.Equal(5, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));

            MarketOperationOutcome[] bought = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS).Select(_ =>
                environment.BuyAsync(buyer, listingId, 2, UNIT_PRICE, buyOperation)));
            MarketTestEnvironment.AssertCommitted(bought[0]);
            Assert.All(bought, outcome =>
            {
                Assert.Equal(bought[0].Result!.ReceiptId, outcome.Result!.ReceiptId);
                Assert.Equal(bought[0].TradeId, outcome.TradeId);
                Assert.Equal(200L, outcome.Gross);
            });
            // 처음 확정된 뒤 다시 보낸 재전송도 같은 결과다(남은 수량이 달라졌어도 거절하지 않는다)
            MarketOperationOutcome replay = await environment.BuyAsync(buyer, listingId, 2, UNIT_PRICE, buyOperation);
            Assert.Equal(bought[0].TradeId, replay.TradeId);
            Assert.Equal(2, await environment.ItemQuantityAsync(buyer, MarketTestEnvironment.HERB));
            Assert.Equal(1L, await environment.Growth.ScalarAsync("SELECT count(*) FROM market_trades;"));
            Assert.Equal(1000 - 200, await environment.CurrencyAsync(buyer));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Operations_SameIdChangedBody_IdempotencyConflict()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
            Guid listOperation = Guid.NewGuid();
            Guid buyOperation = Guid.NewGuid();
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 5,
                UNIT_PRICE, listOperation));

            MarketTestEnvironment.AssertRejected(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 5, UNIT_PRICE + 1,
                listOperation), ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);
            MarketTestEnvironment.AssertCommitted(await environment.BuyAsync(buyer, listingId, 1, UNIT_PRICE, buyOperation));
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(buyer, listingId, 2, UNIT_PRICE, buyOperation),
                ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);

            Assert.Equal(1, await environment.ItemQuantityAsync(buyer, MarketTestEnvironment.HERB));
            Assert.Equal(4, (await environment.ListingAsync(listingId)).RemainingQty);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task BuyVersusCancel_Race_ExactlyOneWinsAndItemsConserved()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            for (int round = 0; round < 5; ++round)
            {
                GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(5));
                GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
                string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 5,
                    UNIT_PRICE));

                Task<MarketOperationOutcome> buy = environment.BuyAsync(buyer, listingId, 5, UNIT_PRICE);
                Task<MarketOperationOutcome> cancel = environment.CancelAsync(seller, listingId);
                MarketOperationOutcome[] outcomes = await Task.WhenAll(buy, cancel);

                bool bought = outcomes[0].Result!.Status == ContractCodes.OperationStatus.COMMITTED;
                bool cancelled = outcomes[1].Result!.Status == ContractCodes.OperationStatus.COMMITTED;
                Assert.True(bought ^ cancelled);
                ListingSnapshot listing = await environment.ListingAsync(listingId);
                if (bought)
                {
                    Assert.Equal(MarketCodes.ListingState.SOLD_OUT, listing.State);
                    Assert.Equal(ContractCodes.RejectReason.MARKET_SOLD_OUT, outcomes[1].Result!.ReasonCode);
                    Assert.Equal(5, await environment.ItemQuantityAsync(buyer, MarketTestEnvironment.HERB));
                    Assert.Equal(0, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
                }
                else
                {
                    Assert.Equal(MarketCodes.ListingState.CANCELLED, listing.State);
                    Assert.Equal(ContractCodes.RejectReason.MARKET_SOLD_OUT, outcomes[0].Result!.ReasonCode);
                    Assert.Equal(0, await environment.ItemQuantityAsync(buyer, MarketTestEnvironment.HERB));
                    Assert.Equal(5, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
                    Assert.Equal(1000L, await environment.CurrencyAsync(buyer));
                }
            }

            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Cancel_ReturnsUnsoldRemainder_NotOwnerAndClosedAreRejected()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 10,
                UNIT_PRICE));
            MarketTestEnvironment.AssertCommitted(await environment.BuyAsync(buyer, listingId, 4, UNIT_PRICE));

            MarketTestEnvironment.AssertRejected(await environment.CancelAsync(buyer, listingId),
                ContractCodes.RejectReason.MARKET_NOT_OWNER);
            MarketTestEnvironment.AssertCommitted(await environment.CancelAsync(seller, listingId));
            MarketTestEnvironment.AssertRejected(await environment.CancelAsync(seller, listingId),
                ContractCodes.RejectReason.MARKET_SOLD_OUT);
            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(buyer, listingId, 1, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_SOLD_OUT);

            ListingSnapshot listing = await environment.ListingAsync(listingId);
            Assert.Equal((MarketCodes.ListingState.CANCELLED, 6, true), (listing.State, listing.RemainingQty, listing.Seen));
            Assert.Equal(6, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Expiry_PastDueListingCannotBeBoughtAndWorkerReturnsOnce()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
            string expiring = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 6,
                UNIT_PRICE));
            string fresh = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 4,
                UNIT_PRICE));
            await environment.ExpireNowAsync(expiring);

            MarketTestEnvironment.AssertRejected(await environment.BuyAsync(buyer, expiring, 1, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_EXPIRED);
            Assert.Equal(1000L, await environment.CurrencyAsync(buyer));

            // 같은 유지 작업을 동시에 돌려도 반환은 한 번이다
            MarketExpiryResult[] runs = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS).Select(_ =>
                environment.Market.ExpireDueAsync(50)));
            Assert.Equal(1, runs.Sum(run => run.Expired));
            Assert.Equal(0, runs.Sum(run => run.Failed));
            ListingSnapshot listing = await environment.ListingAsync(expiring);
            Assert.Equal((MarketCodes.ListingState.EXPIRED, 6, false), (listing.State, listing.RemainingQty, listing.Seen));
            Assert.Equal(6, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            Assert.Equal(MarketCodes.ListingState.ACTIVE, (await environment.ListingAsync(fresh)).State);
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason;",
                new { Reason = ContractCodes.EconomyReason.MARKET_RETURN }));
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason AND actor_kind = 'System';",
                new { Reason = ContractCodes.EconomyReason.MARKET_RETURN }));

            MarketExpiryResult again = await environment.Market.ExpireDueAsync(50);
            Assert.Equal(0, again.Expired);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Expiry_PassesWhileBuyWaitsForRowLock_BuyIsRejectedByLockedClock()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(5));
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 5,
                UNIT_PRICE));

            // 다른 연결이 등록 행을 잡고 만료 시각을 0.6초 뒤로 당긴 채 0.9초 동안 놓지 않는다. 구매는 그 사이 잠금을 기다리다
            // 만료 뒤에 행을 얻는다. 트랜잭션 시작 시각(now())으로 판정하면 아직 유효하다고 잘못 판단한다
            await using NpgsqlConnection holder = await environment.DataSource.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await holder.BeginTransactionAsync();
            await holder.ExecuteAsync(
                """
                UPDATE market_listings SET created_at = clock_timestamp() - interval '1 day',
                    expires_at = clock_timestamp() + interval '600 milliseconds'
                WHERE listing_id = @ListingId;
                """, new { ListingId = Guid.Parse(listingId) }, transaction);
            Task<MarketOperationOutcome> buy = environment.BuyAsync(buyer, listingId, 1, UNIT_PRICE);
            await Task.Delay(900);
            await transaction.CommitAsync();

            MarketOperationOutcome outcome = await buy;
            MarketTestEnvironment.AssertRejected(outcome, ContractCodes.RejectReason.MARKET_EXPIRED);
            Assert.Equal(1000L, await environment.CurrencyAsync(buyer));
            Assert.Equal(5, (await environment.ListingAsync(listingId)).RemainingQty);
        }

        [PostgresFact]
        public async Task Ack_MarksClosedListingsSeenWithoutOperationRows()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
            string sold = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 2, UNIT_PRICE));
            string open = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 2, UNIT_PRICE));
            MarketTestEnvironment.AssertCommitted(await environment.BuyAsync(buyer, sold, 2, UNIT_PRICE));

            (EGrowthAccess access, MarketMineResponse? before) = await environment.Queries.MineAsync(environment.TownId,
                seller.CharacterId);
            Assert.Equal(EGrowthAccess.Allowed, access);
            Assert.Equal(1, before!.UnseenCount);
            Assert.Equal(2, before.Listings.Length);
            long operationRows = await environment.Growth.ScalarAsync("SELECT count(*) FROM operations;");

            OperationResult ack = await environment.Market.AckAsync(environment.TownId, seller.CharacterId, Guid.NewGuid(),
                Array.Empty<Guid>());
            OperationResult replay = await environment.Market.AckAsync(environment.TownId, seller.CharacterId, Guid.NewGuid(),
                new[] { Guid.Parse(sold) });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, ack.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replay.Status);
            Assert.True((await environment.ListingAsync(sold)).Seen);
            Assert.False((await environment.ListingAsync(open)).Seen);
            Assert.Equal(operationRows, await environment.Growth.ScalarAsync("SELECT count(*) FROM operations;"));
            (_, MarketMineResponse? after) = await environment.Queries.MineAsync(environment.TownId, seller.CharacterId);
            Assert.Equal(0, after!.UnseenCount);

            // 다른 계정의 캐릭터로는 남의 등록을 확인 표시할 수 없다
            await environment.Market.AckAsync(environment.TownId, buyer.CharacterId, Guid.NewGuid(), new[] { Guid.Parse(open) });
            Assert.False((await environment.ListingAsync(open)).Seen);
        }

        [PostgresFact]
        public async Task Escrow_ReturnBeyondStorageLimit_GoesToRecoveryInboxAndClaimsPartially()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(5));
            string listingId = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 5,
                UNIT_PRICE));
            // 보관 중에 같은 품목이 상한까지 쌓였다
            await environment.SeedItemAsync(seller, MarketTestEnvironment.HERB, RewardStorageLimits.HERB_STACK);

            MarketTestEnvironment.AssertCommitted(await environment.CancelAsync(seller, listingId));

            // 보유가 상한이라 반환 5개 전부가 30일 회수함 한 행으로 간다
            Assert.Equal(RewardStorageLimits.HERB_STACK, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            Guid inboxId = await InboxIdAsync(environment, seller, listingId);
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM reward_inbox WHERE source_kind = 'MarketReturn' AND acquired_at IS NOT NULL AND grant_id IS NULL AND expires_at = acquired_at + interval '30 days';"));
            PostgresInboxRepository inbox = new PostgresInboxRepository(environment.DataSource, environment.Growth.Executor,
                environment.Growth.Ledger);
            InboxRecord entry = Assert.Single((await inbox.ListAsync(seller.AccountId)).Entries);
            Assert.Equal((RecoveryInbox.SOURCE_MARKET_RETURN, 5L), (entry.SourceKind, Assert.Single(entry.Rewards).Quantity));
            Assert.False(entry.RequiresCharacter);

            // 공간이 없으면 거절, 3칸이 생기면 3개만 옮기고 우편은 남는다, 나머지 공간이 생기면 수령 완료다
            AssertRejectedResult(await inbox.ClaimAsync(seller.AccountId, Guid.NewGuid(), inboxId, null),
                ContractCodes.RejectReason.INVENTORY_FULL);
            MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 3, UNIT_PRICE));
            AssertCommittedResult(await inbox.ClaimAsync(seller.AccountId, Guid.NewGuid(), inboxId, null));
            Assert.Equal(RewardStorageLimits.HERB_STACK, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            InboxRecord partial = Assert.Single((await inbox.ListAsync(seller.AccountId)).Entries);
            Assert.Equal(2L, Assert.Single(partial.Rewards).Quantity);

            MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 2, UNIT_PRICE));
            AssertCommittedResult(await inbox.ClaimAsync(seller.AccountId, Guid.NewGuid(), inboxId, null));
            Assert.Empty((await inbox.ListAsync(seller.AccountId)).Entries);
            Assert.Equal(RewardStorageLimits.HERB_STACK, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            AssertRejectedResult(await inbox.ClaimAsync(seller.AccountId, Guid.NewGuid(), inboxId, null),
                ContractCodes.RejectReason.INBOX_ALREADY_CLAIMED);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Equipment_ListEscrowedBuyAndCancel_MoveOwnershipWithHistory()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync();
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 1000);
            Guid sold = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0);
            Guid kept = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0);

            string soldListing = MarketTestEnvironment.Listed(await environment.ListEquipmentAsync(seller, sold, 200));
            string keptListing = MarketTestEnvironment.Listed(await environment.ListEquipmentAsync(seller, kept, 300));

            // 보관 중인 장비는 판매자 보관 칸을 계속 차지하고 스냅샷에 보관 표시가 붙는다
            CharacterGrowthSnapshot sellerGrowth = await environment.Growth.ReadAsync(seller);
            Assert.Equal(2, sellerGrowth.StorageUsed);
            Assert.All(sellerGrowth.Equipment, view => Assert.True(view.InMarket));
            MarketTestEnvironment.AssertRejected(await environment.ListEquipmentAsync(seller, sold, 200),
                ContractCodes.RejectReason.MARKET_ESCROWED);

            MarketOperationOutcome bought = await environment.BuyAsync(buyer, soldListing, 1, 200);
            MarketTestEnvironment.AssertCommitted(bought);
            Assert.Equal((200L, 10L, 190L), (bought.Gross, bought.Fee, bought.Net));
            MarketTestEnvironment.AssertCommitted(await environment.CancelAsync(seller, keptListing));

            CharacterGrowthSnapshot buyerGrowth = await environment.Growth.ReadAsync(buyer);
            EquipmentView received = Assert.Single(buyerGrowth.Equipment);
            Assert.Equal((sold.ToString("D"), false, false), (received.ItemId, received.InMarket, received.Bound));
            sellerGrowth = await environment.Growth.ReadAsync(seller);
            EquipmentView returned = Assert.Single(sellerGrowth.Equipment);
            Assert.Equal((kept.ToString("D"), false), (returned.ItemId, returned.InMarket));
            Assert.Equal(190L, await environment.CurrencyAsync(seller));
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM equipment_instances WHERE item_id = @Item AND account_id = @AccountId AND character_id = @CharacterId AND escrow_kind IS NULL AND escrow_ref IS NULL;",
                new { Item = sold, buyer.AccountId, buyer.CharacterId }));
            Assert.Equal(new[] { GrowthAssets.HISTORY_MARKET_ESCROWED, GrowthAssets.HISTORY_MARKET_SOLD }, await EventKindsAsync(
                environment, sold));
            Assert.Equal(new[] { GrowthAssets.HISTORY_MARKET_ESCROWED, GrowthAssets.HISTORY_MARKET_RETURNED }, await EventKindsAsync(
                environment, kept));

            // 반환된 장비는 다시 등록할 수 있다
            MarketTestEnvironment.AssertCommitted(await environment.ListEquipmentAsync(seller, kept, 300));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Equipment_NotTradableOrNotOwned_RejectedAtListing()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync();
            GrowthPlayer other = await environment.CreatePlayerAsync();
            Guid enhanced = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 1);
            Guid worn = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0, equipped: true);
            Guid locked = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0, locked: true);
            Guid granted = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0,
                origin: GrowthCodes.EquipmentOrigin.INITIAL_GRANT);
            Guid foreign = await environment.Growth.InsertEquipmentAsync(other, MarketTestEnvironment.WEAPON, 0);

            foreach (Guid item in new[] { enhanced, worn, locked, granted })
            {
                MarketTestEnvironment.AssertRejected(await environment.ListEquipmentAsync(seller, item, UNIT_PRICE),
                    ContractCodes.RejectReason.MARKET_NOT_TRADABLE);
            }
            MarketTestEnvironment.AssertRejected(await environment.ListEquipmentAsync(seller, foreign, UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_NOT_OWNER);
            MarketTestEnvironment.AssertRejected(await environment.ListEquipmentAsync(seller, Guid.NewGuid(), UNIT_PRICE),
                ContractCodes.RejectReason.MARKET_NOT_OWNER);
            Assert.Equal(0L, await environment.Growth.ScalarAsync("SELECT count(*) FROM market_listings;"));
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM equipment_instances WHERE escrow_kind IS NOT NULL;"));
        }

        [PostgresFact]
        public async Task Equipment_Escrowed_GrowthWritesAreRejected()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(essence: 1000, shards: 10);
            Guid item = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0);
            Guid target = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0);
            MarketTestEnvironment.Listed(await environment.ListEquipmentAsync(seller, item, UNIT_PRICE));
            Guid town = environment.TownId;

            GrowthPostgresTests.AssertRejected(await environment.Growth.Equipment.EnhanceAsync(town, Guid.NewGuid(),
                seller.CharacterId, 0, item, 0, false), ContractCodes.RejectReason.MARKET_ESCROWED);
            GrowthPostgresTests.AssertRejected(await environment.Growth.Equipment.EquipAsync(town, Guid.NewGuid(),
                seller.CharacterId, 0, item, false), ContractCodes.RejectReason.MARKET_ESCROWED);
            GrowthPostgresTests.AssertRejected(await environment.Growth.Equipment.LockAsync(town, Guid.NewGuid(),
                seller.CharacterId, 0, item, true, false), ContractCodes.RejectReason.MARKET_ESCROWED);
            GrowthPostgresTests.AssertRejected(await environment.Growth.Equipment.DismantleAsync(town, Guid.NewGuid(),
                seller.CharacterId, 0, item, false), ContractCodes.RejectReason.MARKET_ESCROWED);
            GrowthPostgresTests.AssertRejected(await environment.Growth.Equipment.InheritAsync(town, Guid.NewGuid(),
                seller.CharacterId, 0, item, target, false), ContractCodes.RejectReason.MARKET_ESCROWED);
            GrowthPostgresTests.AssertRejected(await environment.Growth.Equipment.UnequipAsync(town, Guid.NewGuid(),
                seller.CharacterId, 0, item, false), ContractCodes.RejectReason.MARKET_ESCROWED);

            CharacterGrowthSnapshot growth = await environment.Growth.ReadAsync(seller);
            EquipmentView view = growth.Equipment.Single(equipment => equipment.ItemId == item.ToString("D"));
            Assert.True(view.InMarket);
            Assert.Equal((0, false, false), (view.Enhancement, view.Equipped, view.Locked));
            Assert.Equal((1000L, 10L), (growth.Essence, growth.Shards));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Character_DeleteWithActiveEquipmentListing_IsRejected()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync();
            Guid item = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0);
            string listingId = MarketTestEnvironment.Listed(await environment.ListEquipmentAsync(seller, item, UNIT_PRICE));
            // 접속 중이면 삭제가 접속 검사에서 먼저 거절되므로 임대를 푼 상태(재접속 전)로 만든다
            await environment.Growth.ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;",
                new { seller.AccountId });
            Shinshi.Data.Characters.PostgresCharacterRepository characters = new Shinshi.Data.Characters.PostgresCharacterRepository(
                environment.DataSource, environment.Growth.Executor);

            OperationResult blocked = await characters.DeleteAsync(seller.AccountId, Guid.NewGuid(), seller.CharacterId, 0);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, blocked.Status);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_HAS_MARKET_LISTING, blocked.ReasonCode);
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM characters WHERE character_id = @CharacterId AND deleted_at IS NOT NULL;",
                new { seller.CharacterId }));

            // 등록을 닫으면 삭제할 수 있다. 수량형 등록은 삭제와 무관하다
            await environment.Growth.ExecuteAsync(
                """
                UPDATE market_listings SET state = 'Cancelled', closed_at = clock_timestamp() WHERE listing_id = @ListingId;
                """, new { ListingId = Guid.Parse(listingId) });
            OperationResult deleted = await Shinshi.Backend.Tests.Accounts.CharacterDeleteTestSupport.DeleteAfterGraceAsync(
                environment.DataSource, characters, seller.AccountId, seller.CharacterId, 0);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, deleted.Status);
        }

        [PostgresFact]
        public async Task List_TalismanStoresClassFromContentAndSearchFiltersByIt()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: new Dictionary<string, long>
            {
                [MarketTestEnvironment.HERB] = 5, [MarketTestEnvironment.TALISMAN] = 3
            });
            Assert.True(environment.Growth.Content.TryGetEffect(MarketTestEnvironment.TALISMAN,
                out GrowthContent.TalismanEffect effect));
            string talisman = MarketTestEnvironment.Listed(await environment.ListAsync(seller,
                MarketTestEnvironment.TALISMAN, 1, 50));
            string herb = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 1, 5));

            Assert.Equal((long)effect.PlayerClass, await environment.Growth.ScalarAsync(
                "SELECT class_id FROM market_listings WHERE listing_id = @Id;", new { Id = Guid.Parse(talisman) }));
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM market_listings WHERE listing_id = @Id AND class_id IS NOT NULL;",
                new { Id = Guid.Parse(herb) }));

            MarketSearchResponse same = await environment.Queries.SearchAsync(new MarketSearchRequest(null,
                effect.PlayerClass, null, null, MarketCodes.Sort.UNIT_PRICE, MarketCodes.Order.ASCENDING, 0));
            Assert.Equal(MarketTestEnvironment.TALISMAN, Assert.Single(same.Groups).DefinitionId);
            int other = (effect.PlayerClass + 1) % 3;
            MarketSearchResponse different = await environment.Queries.SearchAsync(new MarketSearchRequest(null, other,
                null, null, MarketCodes.Sort.UNIT_PRICE, MarketCodes.Order.ASCENDING, 0));
            Assert.Empty(different.Groups);
        }

        [PostgresFact]
        public async Task Expiry_FailingListingDoesNotStarveOthers()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(10));
            Guid weapon = await environment.Growth.InsertEquipmentAsync(seller, MarketTestEnvironment.WEAPON, 0);
            string broken = MarketTestEnvironment.Listed(await environment.ListEquipmentAsync(seller, weapon, 100));
            string healthy = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 4,
                UNIT_PRICE));
            await environment.ExpireNowAsync(healthy);
            await environment.ExpireNowAsync(broken);
            // 보관 표시가 사라져 반환이 항상 실패하는 가장 오래된 후보
            await environment.Growth.ExecuteAsync(
                "UPDATE market_listings SET created_at = clock_timestamp() - interval '4 days', expires_at = clock_timestamp() - interval '3 days' WHERE listing_id = @Id;",
                new { Id = Guid.Parse(broken) });
            await environment.Growth.ExecuteAsync(
                "UPDATE equipment_instances SET escrow_kind = NULL, escrow_ref = NULL WHERE item_id = @ItemId;",
                new { ItemId = weapon });

            MarketExpiryResult first = await environment.Market.ExpireDueAsync(1);
            Assert.Equal((0, 1), (first.Expired, first.Failed));
            Assert.Equal(Guid.Parse(broken), Assert.Single(first.FailedListingIds));

            // 실패한 행은 후보에서 빠지므로 batch가 1건이어도 다음 호출이 정상 등록을 처리한다
            MarketExpiryResult second = await environment.Market.ExpireDueAsync(1);
            Assert.Equal((1, 0), (second.Expired, second.Failed));
            Assert.Equal(MarketCodes.ListingState.EXPIRED, (await environment.ListingAsync(healthy)).State);
            Assert.Equal(MarketCodes.ListingState.ACTIVE, (await environment.ListingAsync(broken)).State);
        }

        [PostgresFact]
        public async Task Queries_SearchListingsMineAndHistory_ReturnConsistentViews()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: new Dictionary<string, long>
            {
                [MarketTestEnvironment.HERB] = 20, [MarketTestEnvironment.TALISMAN] = 5
            });
            GrowthPlayer buyer = await environment.CreatePlayerAsync(currency: 100000);
            MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 5, 30));
            string cheap = MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.HERB, 3, 10));
            MarketTestEnvironment.Listed(await environment.ListAsync(seller, MarketTestEnvironment.TALISMAN, 2, 500));
            MarketTestEnvironment.AssertCommitted(await environment.BuyAsync(buyer, cheap, 1, 10));

            MarketSearchResponse all = await environment.Queries.SearchAsync(new MarketSearchRequest(null, null, null, null,
                MarketCodes.Sort.UNIT_PRICE, MarketCodes.Order.ASCENDING, 0));
            Assert.Equal(2, all.TotalCount);
            Assert.Equal(new[] { MarketTestEnvironment.HERB, MarketTestEnvironment.TALISMAN }, all.Groups.Select(group => group.DefinitionId));
            MarketGroupView herb = all.Groups[0];
            Assert.Equal((10L, 7L, 2, 10L), (herb.LowestUnitPrice, herb.TotalQty, herb.ListingCount, herb.RecentUnitPrice));

            MarketSearchResponse byCategory = await environment.Queries.SearchAsync(new MarketSearchRequest(
                MarketCodes.Category.TALISMAN, null, null, null, MarketCodes.Sort.QUANTITY, MarketCodes.Order.DESCENDING, 0));
            Assert.Equal(MarketTestEnvironment.TALISMAN, Assert.Single(byCategory.Groups).DefinitionId);
            MarketSearchResponse byIds = await environment.Queries.SearchAsync(new MarketSearchRequest(null, null, null,
                new[] { MarketTestEnvironment.HERB }, MarketCodes.Sort.NEWEST, MarketCodes.Order.DESCENDING, 0));
            Assert.Equal(MarketTestEnvironment.HERB, Assert.Single(byIds.Groups).DefinitionId);
            Assert.Empty((await environment.Queries.SearchAsync(new MarketSearchRequest(null, null, null, null,
                MarketCodes.Sort.UNIT_PRICE, MarketCodes.Order.ASCENDING, 5))).Groups);

            MarketListingsResponse listings = await environment.Queries.ListingsAsync(new MarketListingsQueryRequest(
                MarketTestEnvironment.HERB, 0));
            Assert.Equal(new long[] { 10, 30 }, listings.Listings.Select(listing => listing.UnitPrice));
            Assert.Equal(2, listings.TotalCount);
            Assert.Equal(10L, listings.AverageUnitPrice24h);
            Assert.All(listings.Listings, listing => Assert.False(listing.Mine));

            (EGrowthAccess mineAccess, MarketMineResponse? mine) = await environment.Queries.MineAsync(environment.TownId,
                seller.CharacterId);
            Assert.Equal(EGrowthAccess.Allowed, mineAccess);
            Assert.Equal(3, mine!.Listings.Length);
            Assert.All(mine.Listings, listing => Assert.True(listing.Mine));
            Assert.Equal(await environment.CurrencyAsync(seller), mine.AvailableYeopjeon);

            (EGrowthAccess historyAccess, MarketHistoryResponse? sellerHistory) = await environment.Queries.HistoryAsync(
                environment.TownId, seller.CharacterId, 0);
            Assert.Equal(EGrowthAccess.Allowed, historyAccess);
            MarketTradeView sale = Assert.Single(sellerHistory!.Trades);
            Assert.Equal((MarketCodes.TradeRole.SELLER, 10L, 1L, 9L), (sale.Role, sale.Gross, sale.Fee, sale.Net));
            (_, MarketHistoryResponse? buyerHistory) = await environment.Queries.HistoryAsync(environment.TownId,
                buyer.CharacterId, 0);
            Assert.Equal(MarketCodes.TradeRole.BUYER, Assert.Single(buyerHistory!.Trades).Role);
            Assert.False(buyerHistory.HasMore);

            // 접속 임대가 다른 마을이면 거절, 없는 캐릭터는 NotFound다
            Assert.Equal(EGrowthAccess.WrongLocation, (await environment.Queries.MineAsync(Guid.NewGuid(), seller.CharacterId)).Access);
            Assert.Equal(EGrowthAccess.NotFound, (await environment.Queries.MineAsync(environment.TownId, Guid.NewGuid())).Access);
        }

        [PostgresFact]
        public async Task Operations_WrongTownOrUnknownCharacter_RejectWithoutEffect()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer seller = await environment.CreatePlayerAsync(items: Herbs(5));

            MarketOperationOutcome elsewhere = await environment.Market.ListAsync(Guid.NewGuid(), seller.CharacterId,
                new MarketListCommand(Guid.NewGuid(), MarketCodes.ASSET_ACCOUNT_ITEM, MarketTestEnvironment.HERB, null, 1,
                    UNIT_PRICE), false);
            MarketOperationOutcome unknown = await environment.Market.ListAsync(environment.TownId, Guid.NewGuid(),
                new MarketListCommand(Guid.NewGuid(), MarketCodes.ASSET_ACCOUNT_ITEM, MarketTestEnvironment.HERB, null, 1,
                    UNIT_PRICE), false);

            MarketTestEnvironment.AssertRejected(elsewhere, ContractCodes.RejectReason.WRONG_LOCATION);
            MarketTestEnvironment.AssertRejected(unknown, ContractCodes.RejectReason.INVALID_TARGET);
            Assert.Equal(5, await environment.ItemQuantityAsync(seller, MarketTestEnvironment.HERB));
            Assert.Equal(0L, await environment.Growth.ScalarAsync("SELECT count(*) FROM market_listings;"));
        }

        [PostgresFact]
        public async Task Stress_MixedMarketAndTradeOperations_NoFailuresAndTotalsConserved()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            const int players = 4;
            const int listingsEach = 4;
            const long herbsEach = 40;
            const long currencyEach = 5000;
            List<GrowthPlayer> all = new List<GrowthPlayer>();
            List<(GrowthPlayer Seller, string ListingId)> listings = new List<(GrowthPlayer, string)>();
            for (int index = 0; index < players; ++index)
            {
                GrowthPlayer player = await environment.CreatePlayerAsync(currency: currencyEach, items: Herbs(herbsEach));
                all.Add(player);
                for (int listing = 0; listing < listingsEach; ++listing)
                {
                    listings.Add((player, MarketTestEnvironment.Listed(await environment.ListAsync(player,
                        MarketTestEnvironment.HERB, 5, 10 + index))));
                }
            }

            // 서로 사고 취소하고 만료 작업과 거래가 한꺼번에 도는 동안 교착이나 처리되지 않은 예외가 없어야 한다
            List<Task> work = new List<Task>();
            Random random = new Random(7);
            foreach ((GrowthPlayer seller, string listingId) in listings)
            {
                GrowthPlayer buyer = all[random.Next(players)];
                if (buyer != seller)
                {
                    work.Add(environment.BuyAsync(buyer, listingId, 1 + random.Next(5), 10 + all.IndexOf(seller)));
                }
                if ((random.Next(3) == 0))
                {
                    work.Add(environment.CancelAsync(seller, listingId));
                }
            }
            for (int index = 0; index < players; ++index)
            {
                GrowthPlayer a = all[index];
                GrowthPlayer b = all[(index + 1) % players];
                work.Add(Task.Run(async () =>
                {
                    TradeOutcome prepared = await environment.Trades.PrepareAsync(environment.TownId, environment.PrepareInput(
                        MarketTestEnvironment.Offer(a, 10, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1)),
                        MarketTestEnvironment.Offer(b, 5)), false);
                    if (prepared.Result!.Status == ContractCodes.OperationStatus.COMMITTED)
                    {
                        await environment.Trades.CommitAsync(environment.TownId, Guid.Parse(prepared.Execution!.ExecutionId),
                            (await environment.ExecutionAsync(prepared.Execution.ExecutionId)).CommitOperationId, false);
                    }
                }));
            }
            work.Add(environment.Market.ExpireDueAsync(50));
            work.Add(environment.Trades.AbandonExpiredAsync(50));
            await Task.WhenAll(work);

            long herbs = 0;
            long currency = 0;
            foreach (GrowthPlayer player in all)
            {
                herbs += await environment.ItemQuantityAsync(player, MarketTestEnvironment.HERB);
                currency += await environment.CurrencyAsync(player);
            }
            long escrowed = await environment.Growth.ScalarAsync(
                "SELECT COALESCE(sum(remaining_qty), 0) FROM market_listings WHERE state = 'Active';");
            long burned = await environment.Growth.ScalarAsync("SELECT COALESCE(sum(fee), 0) FROM market_trades;");
            Assert.Equal(players * herbsEach, herbs + escrowed);
            Assert.Equal(players * currencyEach, currency + burned);
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM player_trade_executions WHERE state = 'Prepared';"));
            await environment.AssertReconciledAsync();
        }

        private static async Task<Guid> InboxIdAsync(MarketTestEnvironment environment, GrowthPlayer player, string listingId)
        {
            await using NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<Guid>(
                "SELECT inbox_id FROM reward_inbox WHERE account_id = @AccountId AND source_kind = 'MarketReturn' AND source_ref = @Ref;",
                new { player.AccountId, Ref = listingId });
        }

        private static void AssertCommittedResult(OperationResult result)
        {
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
        }

        private static void AssertRejectedResult(OperationResult result, int reason)
        {
            Assert.Equal((ContractCodes.OperationStatus.REJECTED, reason), (result.Status, result.ReasonCode));
        }

        private static IReadOnlyDictionary<string, long> Herbs(long quantity)
        {
            return new Dictionary<string, long> { [MarketTestEnvironment.HERB] = quantity };
        }

        private static async Task<string[]> EventKindsAsync(MarketTestEnvironment environment, Guid itemId)
        {
            await using NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            return (await connection.QueryAsync<string>(
                "SELECT event_kind FROM item_history WHERE item_id = @ItemId ORDER BY seq;", new { ItemId = itemId })).ToArray();
        }
    }
}
