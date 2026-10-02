using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Npgsql;
using Xunit;

using Shinshi.Data;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests
{
    public sealed class EconomyLedgerReconcilerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS        = 3L;
        private const long ESSENCE        = 17L;
        private const long SHARDS         = 9L;
        private const long UNBOUND_ITEMS  = 5L;
        private const long BOUND_ITEMS    = 7L;
        private const long DEBIT_AMOUNT   = 33L;
        private const int ASSET_COUNT     = 6;
        private const int PAGE_SIZE       = 2;
        private const int LARGE_BATCH     = 100;
        private const string OVERFLOW_TOTAL = "18446744073709551614";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task FindMismatchesAsync_BalancedCreditsAndDebit_ReturnsNoMismatchForAllAssetKinds()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid characterId = Guid.NewGuid();
            await SeedBalancedAssetsAsync(database, accountId, characterId);
            OperationRequest debit = LedgerTestSupport.CreateRequest(accountId);
            await new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)).ExecuteAsync(debit,
                async (context, cancellationToken) =>
                {
                    long balance = await LedgerTestSupport.AddCurrencyAsync(context, accountId, -DEBIT_AMOUNT, cancellationToken);
                    await new PostgresEconomyLedgerWriter().AppendAsync(context, LedgerTestSupport.CreateEntry(debit,
                        EconomyAsset.Currency(accountId), -DEBIT_AMOUNT, balance, "currency-debit"), cancellationToken);
                    return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
                });

            IReadOnlyList<EconomyReconciliationMismatch> mismatches =
                await new EconomyLedgerReconciler(database.DataSource).FindMismatchesAsync(LARGE_BATCH);

            Assert.Empty(mismatches);
            Assert.Equal(ASSET_COUNT + 1,
                await LedgerTestSupport.QueryAsync<long>(database, "SELECT count(*) FROM economy_ledger;"));
            Assert.Equal(LedgerTestSupport.GRANT_AMOUNT - DEBIT_AMOUNT,
                await LedgerTestSupport.QueryAsync<long>(database, "SELECT currency FROM account_wallets;"));
        }

        [PostgresFact]
        public async Task FindMismatchesAsync_IndependentWalletCharacterAndBindingMismatches_PaginatesEachExactlyOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid characterId = Guid.NewGuid();
            await SeedBalancedAssetsAsync(database, accountId, characterId);
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE account_wallets SET currency = currency + 1, tickets = tickets + 1;
                    UPDATE characters SET essence = essence + 1, shards = shards + 1;
                    UPDATE account_items SET quantity = quantity + 1;
                    """);
            }
            EconomyLedgerReconciler reconciler = new EconomyLedgerReconciler(database.DataSource);
            List<EconomyReconciliationMismatch> rows = new List<EconomyReconciliationMismatch>();
            EconomyReconciliationCursor? cursor = null;
            for (int page = 0; page <= ASSET_COUNT; ++page)
            {
                IReadOnlyList<EconomyReconciliationMismatch> next =
                    await reconciler.FindMismatchesAsync(PAGE_SIZE, cursor);
                if (next.Count == 0)
                {
                    break;
                }
                Assert.InRange(next.Count, 1, PAGE_SIZE);
                rows.AddRange(next);
                cursor = next[^1].Cursor;
            }

            Assert.Equal(ASSET_COUNT, rows.Count);
            Assert.Equal(ASSET_COUNT, rows.Select(row =>
                $"{row.AccountId}/{row.CharacterId}/{row.AssetKind}/{row.AssetId}/{row.BindingKind}").Distinct().Count());
            Assert.All(rows, row => Assert.Equal(accountId, row.AccountId));
            AssertMismatch(rows, EconomyAsset.CURRENCY, null, string.Empty, string.Empty, LedgerTestSupport.GRANT_AMOUNT);
            AssertMismatch(rows, EconomyAsset.TICKETS, null, string.Empty, string.Empty, TICKETS);
            AssertMismatch(rows, EconomyAsset.ESSENCE, characterId, string.Empty, string.Empty, ESSENCE);
            AssertMismatch(rows, EconomyAsset.SHARDS, characterId, string.Empty, string.Empty, SHARDS);
            AssertMismatch(rows, EconomyAsset.ACCOUNT_ITEM, null, LedgerTestSupport.ITEM_DEFINITION,
                LedgerTestSupport.UNBOUND, UNBOUND_ITEMS);
            AssertMismatch(rows, EconomyAsset.ACCOUNT_ITEM, null, LedgerTestSupport.ITEM_DEFINITION,
                LedgerTestSupport.ACCOUNT_BOUND, BOUND_ITEMS);
        }

        [PostgresFact]
        public async Task FindMismatchesAsync_BalanceWithoutLedger_ReportsZeroLedgerTotal()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("UPDATE account_wallets SET tickets = @Tickets;", new { Tickets = TICKETS });
            }

            EconomyReconciliationMismatch row = Assert.Single(
                await new EconomyLedgerReconciler(database.DataSource).FindMismatchesAsync(LARGE_BATCH));

            Assert.Equal(accountId, row.AccountId);
            Assert.Equal(EconomyAsset.TICKETS, row.AssetKind);
            Assert.Equal<long?>(TICKETS, row.CurrentBalance);
            Assert.Equal("0", row.LedgerTotal);
        }

        [PostgresFact]
        public async Task FindMismatchesAsync_LedgerWithoutItemBalance_ReportsMissingCurrentBalance()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            await new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)).ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    await new PostgresEconomyLedgerWriter().AppendAsync(context, LedgerTestSupport.CreateEntry(request,
                        EconomyAsset.AccountItem(accountId, LedgerTestSupport.ITEM_DEFINITION, LedgerTestSupport.ACCOUNT_BOUND),
                        BOUND_ITEMS, BOUND_ITEMS, "orphaned-item"), cancellationToken);
                    return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
                });

            EconomyReconciliationMismatch row = Assert.Single(
                await new EconomyLedgerReconciler(database.DataSource).FindMismatchesAsync(LARGE_BATCH));

            Assert.Equal(accountId, row.AccountId);
            Assert.Equal(EconomyAsset.ACCOUNT_ITEM, row.AssetKind);
            Assert.Equal(LedgerTestSupport.ITEM_DEFINITION, row.AssetId);
            Assert.Equal(LedgerTestSupport.ACCOUNT_BOUND, row.BindingKind);
            Assert.Null(row.CurrentBalance);
            Assert.Equal(BOUND_ITEMS.ToString(CultureInfo.InvariantCulture), row.LedgerTotal);
        }

        [PostgresFact]
        public async Task FindMismatchesAsync_TotalBeyondBigint_ReportsExactNumericString()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            await new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)).ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    PostgresEconomyLedgerWriter writer = new PostgresEconomyLedgerWriter();
                    // 불일치 데이터를 고의로 넣어 대사 진단의 합계 계산이 오버플로되지 않는지 확인
                    await writer.AppendAsync(context, LedgerTestSupport.CreateEntry(request, EconomyAsset.Currency(accountId),
                        long.MaxValue, long.MaxValue, "overflow-first"), cancellationToken);
                    await writer.AppendAsync(context, LedgerTestSupport.CreateEntry(request, EconomyAsset.Currency(accountId),
                        long.MaxValue, long.MaxValue, "overflow-second"), cancellationToken);
                    return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
                });

            EconomyReconciliationMismatch row = Assert.Single(
                await new EconomyLedgerReconciler(database.DataSource).FindMismatchesAsync(LARGE_BATCH));

            Assert.Equal<long?>(0L, row.CurrentBalance);
            Assert.Equal(OVERFLOW_TOTAL, row.LedgerTotal);
        }

        private static void AssertMismatch(IEnumerable<EconomyReconciliationMismatch> rows, string assetKind,
            Guid? characterId, string assetId, string bindingKind, long ledgerBalance)
        {
            EconomyReconciliationMismatch row = Assert.Single(rows, candidate =>
                (candidate.AssetKind == assetKind) && (candidate.CharacterId == characterId)
                && (candidate.AssetId == assetId) && (candidate.BindingKind == bindingKind));
            Assert.Equal<long?>(ledgerBalance + 1L, row.CurrentBalance);
            Assert.Equal(ledgerBalance.ToString(CultureInfo.InvariantCulture), row.LedgerTotal);
        }

        private static async Task SeedBalancedAssetsAsync(PostgresTestDatabase database, Guid accountId, Guid characterId)
        {
            await LedgerTestSupport.SeedAccountAsync(database, accountId, characterId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            await new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)).ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    if ((context.Connection == null) || (context.Transaction == null))
                    {
                        throw new InvalidOperationException("Asset seeding must share the operation transaction.");
                    }
                    await context.Connection.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE account_wallets SET currency = @Currency, tickets = @Tickets WHERE account_id = @AccountId;
                        UPDATE characters SET essence = @Essence, shards = @Shards WHERE character_id = @CharacterId;
                        INSERT INTO account_items (account_id, definition_id, binding_kind, quantity)
                        VALUES (@AccountId, @DefinitionId, @Unbound, @UnboundItems),
                               (@AccountId, @DefinitionId, @Bound, @BoundItems);
                        """, new
                        {
                            AccountId = accountId,
                            CharacterId = characterId,
                            Currency = LedgerTestSupport.GRANT_AMOUNT,
                            Tickets = TICKETS,
                            Essence = ESSENCE,
                            Shards = SHARDS,
                            DefinitionId = LedgerTestSupport.ITEM_DEFINITION,
                            Unbound = LedgerTestSupport.UNBOUND,
                            UnboundItems = UNBOUND_ITEMS,
                            Bound = LedgerTestSupport.ACCOUNT_BOUND,
                            BoundItems = BOUND_ITEMS
                        }, context.Transaction, cancellationToken: cancellationToken));
                    PostgresEconomyLedgerWriter writer = new PostgresEconomyLedgerWriter();
                    await writer.AppendAsync(context, LedgerTestSupport.CreateEntry(request, EconomyAsset.Currency(accountId)),
                        cancellationToken);
                    await writer.AppendAsync(context, LedgerTestSupport.CreateEntry(request, EconomyAsset.Tickets(accountId),
                        TICKETS, TICKETS, "tickets"), cancellationToken);
                    await writer.AppendAsync(context, LedgerTestSupport.CreateEntry(request, EconomyAsset.Essence(accountId, characterId),
                        ESSENCE, ESSENCE, "essence"), cancellationToken);
                    await writer.AppendAsync(context, LedgerTestSupport.CreateEntry(request, EconomyAsset.Shards(accountId, characterId),
                        SHARDS, SHARDS, "shards"), cancellationToken);
                    await writer.AppendAsync(context, LedgerTestSupport.CreateEntry(request,
                        EconomyAsset.AccountItem(accountId, LedgerTestSupport.ITEM_DEFINITION, LedgerTestSupport.UNBOUND),
                        UNBOUND_ITEMS, UNBOUND_ITEMS, "unbound-item"), cancellationToken);
                    await writer.AppendAsync(context, LedgerTestSupport.CreateEntry(request,
                        EconomyAsset.AccountItem(accountId, LedgerTestSupport.ITEM_DEFINITION, LedgerTestSupport.ACCOUNT_BOUND),
                        BOUND_ITEMS, BOUND_ITEMS, "bound-item"), cancellationToken);
                    return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
                });
        }
    }
}

