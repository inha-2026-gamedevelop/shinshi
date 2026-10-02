using System;
using System.Collections.Generic;
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
    public sealed class PostgresOutboxDispatcherTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int EVENT_COUNT           = 12;
        private const int WORKER_COUNT          = 3;
        private const int BATCH_SIZE            = 4;
        private const int TIMEOUT_SECONDS       = 30;
        private const int CANCEL_EVENT_COUNT    = 2;
        private const int TIMEOUT_EVENT_COUNT   = 2;
        private const int LOCKED_EVENT_COUNT    = BATCH_SIZE * 2;
        private const double SLEEP_SECONDS      = 0.1d;
        private const string OTHER_CONSUMER     = "other-test-consumer";
        private const string SHORT_TIMEOUT_SQL  = "SET LOCAL statement_timeout = '25ms';";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task ProcessBatchAsync_ParallelWorkersAndRepeatedPoll_DeliversEachEventOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            OperationRequest request = await EnqueueAsync(database, EVENT_COUNT);
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            int callbacks = 0;
            int delivered = 0;

            async Task Consume(OperationExecutionContext context, OutboxDelivery delivery, CancellationToken cancellationToken)
            {
                Assert.Equal(request.ActorId, delivery.ActorId);
                Assert.Equal(request.OperationId, delivery.OperationId);
                Assert.Equal(request.ActorId, delivery.AggregateId);
                Assert.Equal(LedgerTestSupport.CONSUMER_KEY, delivery.ConsumerKey);
                Assert.Equal(LedgerTestSupport.EVENT_KIND, delivery.EventKind);
                Assert.Equal(0, delivery.RetryCount);
                Assert.Contains("currency", delivery.PayloadJson);
                await LedgerTestSupport.InsertConsumerEffectAsync(context, delivery, cancellationToken);
                Interlocked.Increment(ref callbacks);
            }

            for (int round = 0; round < EVENT_COUNT; ++round)
            {
                OutboxBatchResult[] results = await Task.WhenAll(Enumerable.Range(0, WORKER_COUNT)
                    .Select(_ => new PostgresOutboxDispatcher(database.DataSource).ProcessBatchAsync(
                        LedgerTestSupport.CONSUMER_KEY, BATCH_SIZE, Consume, timeout.Token)));
                Assert.All(results, result => Assert.Equal(0, result.FailedCount));
                int roundDelivered = results.Sum(result => result.DeliveredCount);
                delivered += roundDelivered;
                if (roundDelivered == 0)
                {
                    break;
                }
            }

            Assert.Equal(EVENT_COUNT, delivered);
            Assert.Equal(EVENT_COUNT, callbacks);
            await AssertDeliveryCountsAsync(database, EVENT_COUNT);
            OutboxBatchResult repeated = await new PostgresOutboxDispatcher(database.DataSource).ProcessBatchAsync(
                LedgerTestSupport.CONSUMER_KEY, BATCH_SIZE, Consume);
            Assert.Equal(0, repeated.DeliveredCount);
            Assert.Equal(0, repeated.FailedCount);
            Assert.Equal(EVENT_COUNT, callbacks);
        }

        [PostgresFact]
        public async Task ProcessBatchAsync_FirstWorkerHoldsBatchLocks_SecondWorkerDeliversDifferentEventsBeforeRelease()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            await EnqueueAsync(database, LOCKED_EVENT_COUNT);
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            TaskCompletionSource<bool> entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            List<Guid> firstEvents = new List<Guid>();
            List<Guid> secondEvents = new List<Guid>();

            async Task ConsumeFirst(OperationExecutionContext context, OutboxDelivery delivery, CancellationToken cancellationToken)
            {
                firstEvents.Add(delivery.EventId);
                await LedgerTestSupport.InsertConsumerEffectAsync(context, delivery, cancellationToken);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }

            async Task ConsumeSecond(OperationExecutionContext context, OutboxDelivery delivery, CancellationToken cancellationToken)
            {
                secondEvents.Add(delivery.EventId);
                await LedgerTestSupport.InsertConsumerEffectAsync(context, delivery, cancellationToken);
            }

            Task<OutboxBatchResult> firstTask = new PostgresOutboxDispatcher(database.DataSource).ProcessBatchAsync(
                LedgerTestSupport.CONSUMER_KEY, BATCH_SIZE, ConsumeFirst, timeout.Token);
            OutboxBatchResult first;
            try
            {
                await entered.Task.WaitAsync(timeout.Token);
                Assert.False(firstTask.IsCompleted);
                OutboxBatchResult second = await new PostgresOutboxDispatcher(database.DataSource).ProcessBatchAsync(
                    LedgerTestSupport.CONSUMER_KEY, BATCH_SIZE, ConsumeSecond, timeout.Token);

                Assert.Equal(BATCH_SIZE, second.DeliveredCount);
                Assert.Equal(0, second.FailedCount);
                Assert.Equal(BATCH_SIZE, secondEvents.Count);
                Assert.Single(firstEvents);
                Assert.False(firstTask.IsCompleted);
                await AssertDeliveryCountsAsync(database, BATCH_SIZE);
            }
            finally
            {
                release.TrySetResult(true);
                first = await firstTask.WaitAsync(timeout.Token);
            }

            Assert.Equal(BATCH_SIZE, first.DeliveredCount);
            Assert.Equal(0, first.FailedCount);
            Assert.Equal(BATCH_SIZE, firstEvents.Count);
            Assert.Empty(firstEvents.Intersect(secondEvents));
            Assert.Equal(LOCKED_EVENT_COUNT, firstEvents.Concat(secondEvents).Distinct().Count());
            await AssertDeliveryCountsAsync(database, LOCKED_EVENT_COUNT);
        }

        [PostgresFact]
        public async Task ProcessBatchAsync_FirstEventStatementTimesOut_DefersEventAndCommitsSuccessfulSibling()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            await EnqueueAsync(database, TIMEOUT_EVENT_COUNT);
            Guid poisonEventId = await LedgerTestSupport.QueryAsync<Guid>(database,
                "SELECT event_id FROM outbox ORDER BY next_attempt_at, created_at, event_id LIMIT 1;");
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            List<Guid> callbackEvents = new List<Guid>();
            string timeoutSqlState = string.Empty;
            string originalTimeout = string.Empty;
            string restoredTimeout = string.Empty;
            DateTimeOffset siblingStartedAt = default;

            OutboxBatchResult result = await new PostgresOutboxDispatcher(database.DataSource).ProcessBatchAsync(
                LedgerTestSupport.CONSUMER_KEY, BATCH_SIZE, async (context, delivery, cancellationToken) =>
                {
                    NpgsqlConnection connection = Assert.IsType<NpgsqlConnection>(context.Connection);
                    NpgsqlTransaction transaction = Assert.IsType<NpgsqlTransaction>(context.Transaction);
                    callbackEvents.Add(delivery.EventId);
                    await LedgerTestSupport.InsertConsumerEffectAsync(context, delivery, cancellationToken);
                    if (delivery.EventId == poisonEventId)
                    {
                        originalTimeout = await connection.QuerySingleAsync<string>(new CommandDefinition(
                            "SHOW statement_timeout;", transaction: transaction, cancellationToken: cancellationToken));
                        await connection.ExecuteAsync(new CommandDefinition(SHORT_TIMEOUT_SQL,
                            transaction: transaction, cancellationToken: cancellationToken));
                        try
                        {
                            await connection.ExecuteAsync(new CommandDefinition("SELECT pg_sleep(@SleepSeconds);",
                                new { SleepSeconds = SLEEP_SECONDS }, transaction, cancellationToken: cancellationToken));
                        }
                        catch (PostgresException exception)
                        {
                            timeoutSqlState = exception.SqlState;
                            throw;
                        }
                    }
                    else
                    {
                        siblingStartedAt = context.ServerTime;
                        restoredTimeout = await connection.QuerySingleAsync<string>(new CommandDefinition(
                            "SHOW statement_timeout;", transaction: transaction, cancellationToken: cancellationToken));
                        await connection.ExecuteAsync(new CommandDefinition("SELECT pg_sleep(@SleepSeconds);",
                            new { SleepSeconds = SLEEP_SECONDS }, transaction, cancellationToken: cancellationToken));
                    }
                }, timeout.Token);

            Assert.Equal(1, result.DeliveredCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(TIMEOUT_EVENT_COUNT, callbackEvents.Count);
            Assert.Equal(poisonEventId, callbackEvents[0]);
            Assert.Equal(PostgresErrorCodes.QueryCanceled, timeoutSqlState);
            Assert.False(string.IsNullOrWhiteSpace(originalTimeout));
            Assert.Equal(originalTimeout, restoredTimeout);
            Assert.True(await LedgerTestSupport.QueryAsync<bool>(database,
                """
                SELECT retries = 1 AND delivered_at IS NULL AND next_attempt_at > @SiblingStartedAt
                    AND NOT EXISTS (SELECT 1 FROM outbox_inbox WHERE event_id = @EventId)
                    AND NOT EXISTS (SELECT 1 FROM test_outbox_effects WHERE event_id = @EventId)
                FROM outbox WHERE event_id = @EventId;
                """, new { EventId = poisonEventId, SiblingStartedAt = siblingStartedAt }));
            await AssertDeliveryCountsAsync(database, 1);
        }

        [PostgresFact]
        public async Task ProcessBatchAsync_ConsumerFailsAfterEffect_RollsBackEffectAndInboxThenRetriesOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            await EnqueueAsync(database, 1);
            PostgresOutboxDispatcher dispatcher = new PostgresOutboxDispatcher(database.DataSource);

            OutboxBatchResult failure = await dispatcher.ProcessBatchAsync(LedgerTestSupport.CONSUMER_KEY,
                BATCH_SIZE, async (context, delivery, cancellationToken) =>
                {
                    await LedgerTestSupport.InsertConsumerEffectAsync(context, delivery, cancellationToken);
                    throw new InvalidOperationException("Injected consumer failure after its SQL effect.");
                });

            Assert.Equal(0, failure.DeliveredCount);
            Assert.Equal(1, failure.FailedCount);
            await AssertDeliveryCountsAsync(database, 0);
            Assert.True(await LedgerTestSupport.QueryAsync<bool>(database,
                "SELECT retries = 1 AND delivered_at IS NULL AND next_attempt_at > created_at FROM outbox;"));

            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    "UPDATE outbox SET next_attempt_at = clock_timestamp() - interval '1 second';");
            }
            OutboxBatchResult retry = await dispatcher.ProcessBatchAsync(LedgerTestSupport.CONSUMER_KEY,
                BATCH_SIZE, async (context, delivery, cancellationToken) =>
                {
                    Assert.Equal(1, delivery.RetryCount);
                    await LedgerTestSupport.InsertConsumerEffectAsync(context, delivery, cancellationToken);
                });

            Assert.Equal(1, retry.DeliveredCount);
            Assert.Equal(0, retry.FailedCount);
            await AssertDeliveryCountsAsync(database, 1);
            Assert.Equal(1, await LedgerTestSupport.QueryAsync<int>(database, "SELECT retries FROM outbox;"));
        }

        [PostgresFact]
        public async Task ProcessBatchAsync_AlreadyConsumedInbox_SkipsCallbackAndCompletesDelivery()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            await EnqueueAsync(database, 1);
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
                await connection.ExecuteAsync(
                    """
                    INSERT INTO outbox_inbox (consumer_key, event_id) SELECT consumer_key, event_id FROM outbox;
                    INSERT INTO test_outbox_effects (consumer_key, event_id) SELECT consumer_key, event_id FROM outbox;
                    """, transaction: transaction);
                await transaction.CommitAsync();
            }

            OutboxBatchResult result = await new PostgresOutboxDispatcher(database.DataSource).ProcessBatchAsync(
                LedgerTestSupport.CONSUMER_KEY, BATCH_SIZE, (context, delivery, cancellationToken) =>
                    throw new InvalidOperationException("An inbox replay must not invoke the consumer."));

            Assert.Equal(1, result.DeliveredCount);
            Assert.Equal(0, result.FailedCount);
            await AssertDeliveryCountsAsync(database, 1);
        }

        [PostgresFact]
        public async Task ProcessBatchAsync_CanceledDuringSecondConsumer_RollsBackEntireBatchAndAllowsRetry()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            await EnqueueAsync(database, CANCEL_EVENT_COUNT);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            int callbacks = 0;
            PostgresOutboxDispatcher dispatcher = new PostgresOutboxDispatcher(database.DataSource);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.ProcessBatchAsync(
                LedgerTestSupport.CONSUMER_KEY, BATCH_SIZE, async (context, delivery, cancellationToken) =>
                {
                    await LedgerTestSupport.InsertConsumerEffectAsync(context, delivery, cancellationToken);
                    if (Interlocked.Increment(ref callbacks) == CANCEL_EVENT_COUNT)
                    {
                        cancellation.Cancel();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }, cancellation.Token));

            Assert.Equal(CANCEL_EVENT_COUNT, callbacks);
            await AssertDeliveryCountsAsync(database, 0);
            Assert.Equal(0L, await LedgerTestSupport.QueryAsync<long>(database, "SELECT sum(retries) FROM outbox;"));
            OutboxBatchResult retry = await dispatcher.ProcessBatchAsync(LedgerTestSupport.CONSUMER_KEY,
                BATCH_SIZE, LedgerTestSupport.InsertConsumerEffectAsync);
            Assert.Equal(CANCEL_EVENT_COUNT, retry.DeliveredCount);
            Assert.Equal(0, retry.FailedCount);
            await AssertDeliveryCountsAsync(database, CANCEL_EVENT_COUNT);
        }

        [PostgresFact]
        public async Task ProcessBatchAsync_OtherConsumerKey_LeavesOtherConsumersUndelivered()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            await EnqueueAsync(database, 1);
            await EnqueueAsync(database, 1, OTHER_CONSUMER);
            PostgresOutboxDispatcher dispatcher = new PostgresOutboxDispatcher(database.DataSource);

            OutboxBatchResult first = await dispatcher.ProcessBatchAsync(LedgerTestSupport.CONSUMER_KEY,
                BATCH_SIZE, LedgerTestSupport.InsertConsumerEffectAsync);

            Assert.Equal(1, first.DeliveredCount);
            Assert.Equal(1L, await LedgerTestSupport.QueryAsync<long>(database,
                "SELECT count(*) FROM outbox WHERE consumer_key = @ConsumerKey AND delivered_at IS NULL;",
                new { ConsumerKey = OTHER_CONSUMER }));
            OutboxBatchResult other = await dispatcher.ProcessBatchAsync(OTHER_CONSUMER,
                BATCH_SIZE, LedgerTestSupport.InsertConsumerEffectAsync);
            Assert.Equal(1, other.DeliveredCount);
            await AssertDeliveryCountsAsync(database, CANCEL_EVENT_COUNT);
        }

        [PostgresFact]
        public async Task ExecuteSql_UpdatesDeletesAndTruncatesInbox_DatabaseRejectsMutation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            await EnqueueAsync(database, 1);
            await new PostgresOutboxDispatcher(database.DataSource).ProcessBatchAsync(
                LedgerTestSupport.CONSUMER_KEY, BATCH_SIZE, LedgerTestSupport.InsertConsumerEffectAsync);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("UPDATE outbox_inbox SET consumed_at = consumed_at;"));
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("DELETE FROM outbox_inbox;"));
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("TRUNCATE outbox_inbox;"));
            await AssertDeliveryCountsAsync(database, 1);
        }

        [PostgresFact]
        public async Task ProcessBatchAsync_OneConsumerFailsWithinBatch_CommitsSuccessfulSiblingOnly()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await LedgerTestSupport.CreateConsumerEffectsAsync(database);
            await EnqueueAsync(database, CANCEL_EVENT_COUNT);
            int callbacks = 0;
            PostgresOutboxDispatcher dispatcher = new PostgresOutboxDispatcher(database.DataSource);

            OutboxBatchResult first = await dispatcher.ProcessBatchAsync(LedgerTestSupport.CONSUMER_KEY,
                BATCH_SIZE, async (context, delivery, cancellationToken) =>
                {
                    await LedgerTestSupport.InsertConsumerEffectAsync(context, delivery, cancellationToken);
                    if (Interlocked.Increment(ref callbacks) == 1)
                    {
                        throw new InvalidOperationException("Only this delivery must be rolled back.");
                    }
                });

            Assert.Equal(1, first.DeliveredCount);
            Assert.Equal(1, first.FailedCount);
            Assert.Equal(CANCEL_EVENT_COUNT, callbacks);
            await AssertDeliveryCountsAsync(database, 1);
            Assert.Equal(1L, await LedgerTestSupport.QueryAsync<long>(database, "SELECT sum(retries) FROM outbox;"));
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    "UPDATE outbox SET next_attempt_at = clock_timestamp() - interval '1 second' WHERE delivered_at IS NULL;");
            }
            OutboxBatchResult retry = await dispatcher.ProcessBatchAsync(LedgerTestSupport.CONSUMER_KEY,
                BATCH_SIZE, LedgerTestSupport.InsertConsumerEffectAsync);
            Assert.Equal(1, retry.DeliveredCount);
            Assert.Equal(0, retry.FailedCount);
            await AssertDeliveryCountsAsync(database, CANCEL_EVENT_COUNT);
        }
        private static async Task<OperationRequest> EnqueueAsync(PostgresTestDatabase database, int count,
            string consumerKey = LedgerTestSupport.CONSUMER_KEY)
        {
            Guid accountId = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, accountId);
            OperationRequest request = LedgerTestSupport.CreateRequest(accountId);
            PostgresOutboxWriter writer = new PostgresOutboxWriter();
            await new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)).ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    for (int index = 0; index < count; ++index)
                    {
                        await writer.EnqueueAsync(context,
                            LedgerTestSupport.CreateMessage(request, Guid.NewGuid(), consumerKey), cancellationToken);
                    }
                    return OperationDecision.Committed(LedgerTestSupport.COMMITTED_REVISION, LedgerTestSupport.RECEIPT_ID);
                });
            return request;
        }

        private static async Task AssertDeliveryCountsAsync(PostgresTestDatabase database, long delivered)
        {
            Assert.Equal(delivered, await LedgerTestSupport.QueryAsync<long>(database,
                "SELECT count(*) FROM outbox WHERE delivered_at IS NOT NULL;"));
            Assert.Equal(delivered, await LedgerTestSupport.QueryAsync<long>(database,
                "SELECT count(*) FROM outbox_inbox;"));
            Assert.Equal(delivered, await LedgerTestSupport.QueryAsync<long>(database,
                "SELECT count(*) FROM test_outbox_effects;"));
        }
    }
}

