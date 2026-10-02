using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Npgsql;
using Xunit;

using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests
{
    public sealed class PostgresEconomyLedgerWriterTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CONCURRENT_CALLS = 12;
        private const int TIMEOUT_SECONDS  = 30;
        private const string CHANGED_BODY  = "{\"grant\":\"changed\"}";
        private const string CHANGED_JSON  = "{\"asset\":\"tickets\"}";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task ExecuteAsync_ConcurrentGrantAndReplay_CommitsBalanceLedgerHistoryAndOutboxOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            EconomyLedgerEntry entry = LedgerTestSupport.CreateEntry(request, EconomyAsset.Currency(accountId));
            OutboxMessage message = LedgerTestSupport.CreateMessage(request, Guid.NewGuid());
            ItemHistoryEntry history = CreateHistory(request);
            IdempotentOperationExecutor executor = CreateExecutor(database);
            int executed = 0;
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));

            async Task<OperationDecision> Grant(OperationExecutionContext context, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref executed);
                long balance = await LedgerTestSupport.AddCurrencyAsync(context, accountId,
                    LedgerTestSupport.GRANT_AMOUNT, cancellationToken);
                Assert.Equal(entry.BalanceAfter, balance);
                PostgresEconomyLedgerWriter ledger = new PostgresEconomyLedgerWriter();
                PostgresOutboxWriter outbox = new PostgresOutboxWriter();
                PostgresItemHistoryWriter items = new PostgresItemHistoryWriter();
                Assert.True(await ledger.AppendAsync(context, entry, cancellationToken));
                Assert.False(await ledger.AppendAsync(context, entry, cancellationToken));
                Assert.True(await outbox.EnqueueAsync(context, message, cancellationToken));
                Assert.False(await outbox.EnqueueAsync(context, message, cancellationToken));
                Assert.True(await items.AppendAsync(context, history, cancellationToken));
                Assert.False(await items.AppendAsync(context, history, cancellationToken));
                return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
            }

            OperationResult[] results = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS)
                .Select(_ => executor.ExecuteAsync(request, Grant, timeout.Token)));
            OperationResult replay = await executor.ExecuteAsync(request, (context, cancellationToken) =>
                throw new InvalidOperationException("A committed replay must not run the grant."));
            Assert.Equal(1, executed);
            Assert.All(results, result =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
                Assert.Equal(results[0].ServerTimeUnixMs, result.ServerTimeUnixMs);
                Assert.Equal(LedgerTestSupport.RECEIPT_ID, result.ReceiptId);
            });
            Assert.Equal(results[0].ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(LedgerTestSupport.GRANT_AMOUNT,
                await LedgerTestSupport.QueryAsync<long>(database, "SELECT currency FROM account_wallets;"));
            Assert.Equal(LedgerTestSupport.GRANT_AMOUNT.ToString(System.Globalization.CultureInfo.InvariantCulture),
                await LedgerTestSupport.QueryAsync<string>(database, "SELECT sum(delta)::text FROM economy_ledger;"));
            await AssertCountsAsync(database, expectedEffects: 1, expectedOperations: 1);
        }

        [PostgresFact]
        public async Task ExecuteAsync_SameOperationWithDifferentBody_RejectsWithoutAdditionalEconomyEffect()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            IdempotentOperationExecutor executor = CreateExecutor(database);
            await executor.ExecuteAsync(request, async (context, cancellationToken) =>
            {
                await AppendEffectsAsync(context, request, cancellationToken);
                return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
            });
            OperationRequest changed = new OperationRequest(accountId, request.OperationId,
                LedgerTestSupport.OPERATION_KIND, CHANGED_BODY);

            OperationResult conflict = await executor.ExecuteAsync(changed, (context, cancellationToken) =>
                throw new InvalidOperationException("A conflicting operation must not run effects."));

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, conflict.Status);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            await AssertCountsAsync(database, expectedEffects: 1, expectedOperations: 1);
        }

        [PostgresFact]
        public async Task ExecuteAsync_RejectsAfterAppending_RollsBackAllEffectsAndReplaysRejection()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            IdempotentOperationExecutor executor = CreateExecutor(database);

            OperationResult first = await executor.ExecuteAsync(request, async (context, cancellationToken) =>
            {
                await AppendEffectsAsync(context, request, cancellationToken);
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            });
            OperationResult replay = await executor.ExecuteAsync(request, (context, cancellationToken) =>
                throw new InvalidOperationException("A rejected replay must not append effects."));

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, replay.Status);
            Assert.Equal(first.ReasonCode, replay.ReasonCode);
            Assert.Equal(first.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(0L, await LedgerTestSupport.QueryAsync<long>(database, "SELECT currency FROM account_wallets;"));
            await AssertCountsAsync(database, expectedEffects: 0, expectedOperations: 1);
        }

        [PostgresFact]
        public async Task ExecuteAsync_ThrowsAfterAppending_RollsBackAllEffectsAndAllowsRetry()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            IdempotentOperationExecutor executor = CreateExecutor(database);

            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    await AppendEffectsAsync(context, request, cancellationToken);
                    throw new InvalidOperationException("Injected failure after all effects.");
                }));
            Assert.Equal(0L, await LedgerTestSupport.QueryAsync<long>(database, "SELECT currency FROM account_wallets;"));
            await AssertCountsAsync(database, expectedEffects: 0, expectedOperations: 0);

            OperationResult retry = await executor.ExecuteAsync(request, async (context, cancellationToken) =>
            {
                await AppendEffectsAsync(context, request, cancellationToken);
                return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
            });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, retry.Status);
            await AssertCountsAsync(database, expectedEffects: 1, expectedOperations: 1);
        }

        [PostgresFact]
        public async Task AppendAsync_SameEntryKeyWithDifferentDelta_RejectsAndRollsBackOperation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            EconomyLedgerEntry entry = LedgerTestSupport.CreateEntry(request, EconomyAsset.Currency(accountId));
            EconomyLedgerEntry changed = new EconomyLedgerEntry(entry.LedgerId, entry.EntryKey, entry.Asset,
                -entry.Delta, entry.BalanceAfter, entry.ReasonCode, entry.SourceType, entry.SourceId,
                entry.ActorKind, entry.ActorId, entry.OperationId);
            PostgresEconomyLedgerWriter writer = new PostgresEconomyLedgerWriter();

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateExecutor(database).ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    await LedgerTestSupport.AddCurrencyAsync(context, accountId, entry.Delta, cancellationToken);
                    await writer.AppendAsync(context, entry, cancellationToken);
                    await writer.AppendAsync(context, changed, cancellationToken);
                    return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
                }));

            Assert.Equal(0L, await LedgerTestSupport.QueryAsync<long>(database, "SELECT currency FROM account_wallets;"));
            await AssertCountsAsync(database, expectedEffects: 0, expectedOperations: 0);
        }

        [PostgresFact]
        public async Task EnqueueAsync_SameEventWithDifferentPayload_RejectsAndRollsBackOperation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            Guid eventId = Guid.NewGuid();
            PostgresOutboxWriter writer = new PostgresOutboxWriter();

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateExecutor(database).ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    await writer.EnqueueAsync(context, LedgerTestSupport.CreateMessage(request, eventId), cancellationToken);
                    await writer.EnqueueAsync(context, LedgerTestSupport.CreateMessage(request, eventId,
                        payloadJson: CHANGED_JSON), cancellationToken);
                    return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
                }));

            await AssertCountsAsync(database, expectedEffects: 0, expectedOperations: 0);
        }

        [PostgresFact]
        public async Task AppendAsync_SameItemSequenceWithDifferentState_RejectsAndRollsBackOperation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            ItemHistoryEntry history = CreateHistory(request);
            ItemHistoryEntry changed = new ItemHistoryEntry(history.ItemId, history.Sequence, history.EventKind,
                null, "{\"enhancement\":1}", accountId, accountId, request.OperationId);
            PostgresItemHistoryWriter writer = new PostgresItemHistoryWriter();

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateExecutor(database).ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    await writer.AppendAsync(context, history, cancellationToken);
                    await writer.AppendAsync(context, changed, cancellationToken);
                    return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
                }));

            await AssertCountsAsync(database, expectedEffects: 0, expectedOperations: 0);
        }

        [PostgresFact]
        public async Task AppendAsync_CommittedOperationInNewTransaction_RejectsLateWrites()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            await CreateExecutor(database).ExecuteAsync(request, (context, cancellationToken) =>
                Task.FromResult(OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID)));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            OperationExecutionContext late = new OperationExecutionContext(connection, transaction, DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresEconomyLedgerWriter()
                .AppendAsync(late, LedgerTestSupport.CreateEntry(request, EconomyAsset.Currency(accountId))));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresOutboxWriter()
                .EnqueueAsync(late, LedgerTestSupport.CreateMessage(request, Guid.NewGuid())));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresItemHistoryWriter()
                .AppendAsync(late, CreateHistory(request)));
            await transaction.RollbackAsync();

            await AssertCountsAsync(database, expectedEffects: 0, expectedOperations: 1);
        }

        [PostgresFact]
        public async Task ExecuteSql_UpdatesDeletesAndTruncatesLedgerOrHistory_DatabaseRejectsMutation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            await CreateExecutor(database).ExecuteAsync(request, async (context, cancellationToken) =>
            {
                await AppendEffectsAsync(context, request, cancellationToken);
                return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
            });
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("UPDATE economy_ledger SET source_type = source_type;"));
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("DELETE FROM economy_ledger;"));
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("TRUNCATE economy_ledger;"));
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("UPDATE item_history SET event_kind = event_kind;"));
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("DELETE FROM item_history;"));
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("TRUNCATE item_history;"));
            await AssertCountsAsync(database, expectedEffects: 1, expectedOperations: 1);
        }

        [PostgresFact]
        public async Task AppendAsync_CompletedTransaction_RejectsLedgerOutboxAndHistoryWrites()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            OperationExecutionContext completed = new OperationExecutionContext(connection, transaction, DateTimeOffset.UtcNow);
            await transaction.RollbackAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresEconomyLedgerWriter()
                .AppendAsync(completed, LedgerTestSupport.CreateEntry(request, EconomyAsset.Currency(accountId))));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresOutboxWriter()
                .EnqueueAsync(completed, LedgerTestSupport.CreateMessage(request, Guid.NewGuid())));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresItemHistoryWriter()
                .AppendAsync(completed, CreateHistory(request)));
            await AssertCountsAsync(database, expectedEffects: 0, expectedOperations: 0);
        }

        [PostgresFact]
        public async Task AppendAsync_EquivalentJsonFormatting_ReturnsReplayWithoutAnotherRow()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            Guid eventId = Guid.NewGuid();
            Guid itemId = Guid.NewGuid();
            await CreateExecutor(database).ExecuteAsync(request, async (context, cancellationToken) =>
            {
                PostgresOutboxWriter outbox = new PostgresOutboxWriter();
                Assert.True(await outbox.EnqueueAsync(context, LedgerTestSupport.CreateMessage(request, eventId,
                    payloadJson: "{\"quantity\":1,\"asset\":\"currency\"}"), cancellationToken));
                Assert.False(await outbox.EnqueueAsync(context, LedgerTestSupport.CreateMessage(request, eventId,
                    payloadJson: "{ \"asset\": \"currency\", \"quantity\": 1 }"), cancellationToken));
                PostgresItemHistoryWriter history = new PostgresItemHistoryWriter();
                Assert.True(await history.AppendAsync(context, new ItemHistoryEntry(itemId, 0, "Created", null,
                    "{\"enhancement\":0,\"grade\":\"Common\"}", accountId, accountId, request.OperationId), cancellationToken));
                Assert.False(await history.AppendAsync(context, new ItemHistoryEntry(itemId, 0, "Created", null,
                    "{ \"grade\": \"Common\", \"enhancement\": 0 }", accountId, accountId, request.OperationId), cancellationToken));
                return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
            });

            Assert.Equal(1L, await LedgerTestSupport.QueryAsync<long>(database, "SELECT count(*) FROM outbox;"));
            Assert.Equal(1L, await LedgerTestSupport.QueryAsync<long>(database, "SELECT count(*) FROM item_history;"));
        }

        private static IdempotentOperationExecutor CreateExecutor(PostgresTestDatabase database)
        {
            return new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource));
        }

        private static ItemHistoryEntry CreateHistory(OperationRequest request)
        {
            return new ItemHistoryEntry(Guid.NewGuid(), 0, "Created", null, "{\"enhancement\":0}",
                request.ActorId, request.ActorId, request.OperationId);
        }

        private static async Task AppendEffectsAsync(OperationExecutionContext context, OperationRequest request,
            CancellationToken cancellationToken)
        {
            long balance = await LedgerTestSupport.AddCurrencyAsync(context, request.ActorId,
                LedgerTestSupport.GRANT_AMOUNT, cancellationToken);
            await new PostgresEconomyLedgerWriter().AppendAsync(context,
                LedgerTestSupport.CreateEntry(request, EconomyAsset.Currency(request.ActorId), balanceAfter: balance),
                cancellationToken);
            await new PostgresOutboxWriter().EnqueueAsync(context,
                LedgerTestSupport.CreateMessage(request, Guid.NewGuid()), cancellationToken);
            await new PostgresItemHistoryWriter().AppendAsync(context, CreateHistory(request), cancellationToken);
        }

        private static async Task AssertCountsAsync(PostgresTestDatabase database, long expectedEffects, long expectedOperations)
        {
            Assert.Equal(expectedEffects, await LedgerTestSupport.QueryAsync<long>(database, "SELECT count(*) FROM economy_ledger;"));
            Assert.Equal(expectedEffects, await LedgerTestSupport.QueryAsync<long>(database, "SELECT count(*) FROM outbox;"));
            Assert.Equal(expectedEffects, await LedgerTestSupport.QueryAsync<long>(database, "SELECT count(*) FROM item_history;"));
            Assert.Equal(expectedOperations, await LedgerTestSupport.QueryAsync<long>(database, "SELECT count(*) FROM operations;"));
        }
    }
}


