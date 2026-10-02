// System
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Ledger
{
    /// <summary> 동일 DB 소비 전용. inbox, 소비 효과, 전달 완료를 함께 커밋하며 콜백의 외부 I/O는 금지한다 </summary>
    public sealed class PostgresOutboxDispatcher
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_BATCH_SIZE            = 100;
        private const int MAX_ATTEMPTS              = 3;
        private const int DATABASE_RETRY_DELAY_MS   = 50;
        private const int DELIVERY_RETRY_SECONDS    = 1;
        private const string DELIVERY_SAVEPOINT    = "outbox_delivery";

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresOutboxDispatcher(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 처리 콜백은 짧은 DB 작업만 한다. 네트워크 통지는 별도 임대 기반 전달자가 필요하다 </summary>
        public async Task<OutboxBatchResult> ProcessBatchAsync(string consumerKey, int batchSize,
            Func<OperationExecutionContext, OutboxDelivery, CancellationToken, Task> consume,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
            ArgumentNullException.ThrowIfNull(consume);
            if ((batchSize <= 0) || (batchSize > MAX_BATCH_SIZE))
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            for (int attempt = 1; ; ++attempt)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await ProcessOnceAsync(consumerKey, batchSize, consume, cancellationToken);
                }
                catch (Exception exception) when (PostgresTransactionPolicy.IsRetryable(exception))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt >= MAX_ATTEMPTS)
                    {
                        throw;
                    }

                    await Task.Delay(DATABASE_RETRY_DELAY_MS * attempt, cancellationToken);
                }
            }
        }

        private async Task<OutboxBatchResult> ProcessOnceAsync(string consumerKey, int batchSize,
            Func<OperationExecutionContext, OutboxDelivery, CancellationToken, Task> consume,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);

            IEnumerable<DeliveryRow> selected = await connection.QueryAsync<DeliveryRow>(new CommandDefinition(
                """
                SELECT event_id AS "EventId", aggregate_id AS "AggregateId", consumer_key AS "ConsumerKey",
                       event_kind AS "EventKind", payload::text AS "PayloadJson", actor_id AS "ActorId",
                       operation_id AS "OperationId", retries AS "RetryCount"
                FROM outbox
                WHERE consumer_key = @ConsumerKey AND delivered_at IS NULL AND next_attempt_at <= clock_timestamp()
                    AND EXISTS (SELECT 1 FROM operations WHERE operations.actor_id = outbox.actor_id
                        AND operations.operation_id = outbox.operation_id AND operations.status = 'Committed')
                ORDER BY next_attempt_at, created_at, event_id
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED;
                """, new { ConsumerKey = consumerKey, BatchSize = batchSize }, transaction,
                cancellationToken: cancellationToken));
            int deliveredCount = 0;
            int failedCount = 0;
            foreach (DeliveryRow row in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.SaveAsync(DELIVERY_SAVEPOINT, cancellationToken);
                try
                {
                    int inserted = await connection.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO outbox_inbox (consumer_key, event_id)
                        VALUES (@ConsumerKey, @EventId)
                        ON CONFLICT (consumer_key, event_id) DO NOTHING;
                        """, row, transaction, cancellationToken: cancellationToken));
                    if (inserted != 0)
                    {
                        DateTime serverTime = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                            "SELECT clock_timestamp();", transaction: transaction, cancellationToken: cancellationToken));
                        OperationExecutionContext context = new OperationExecutionContext(connection, transaction,
                            new DateTimeOffset(serverTime));
                        await consume(context, new OutboxDelivery(row.EventId, row.AggregateId, row.ConsumerKey,
                            row.EventKind, row.PayloadJson, row.ActorId, row.OperationId, row.RetryCount), cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    int updated = await connection.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE outbox SET delivered_at = clock_timestamp()
                        WHERE event_id = @EventId AND consumer_key = @ConsumerKey AND delivered_at IS NULL;
                        """, row, transaction, cancellationToken: cancellationToken));
                    if (updated != 1)
                    {
                        throw new InvalidOperationException("A locked outbox event must transition exactly once.");
                    }

                    await transaction.ReleaseAsync(DELIVERY_SAVEPOINT, cancellationToken);
                    ++deliveredCount;
                }
                catch (Exception exception) when (CanRecoverDeliveryFailure(exception, cancellationToken))
                {
                    // 업무 실패의 부분 소비와 inbox 표식만 되돌리고 다음 전달 시각을 같은 잠금 안에서 확정한다.
                    await transaction.RollbackAsync(DELIVERY_SAVEPOINT, cancellationToken);
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE outbox
                        SET retries = CASE WHEN retries < @MaxRetries THEN retries + 1 ELSE retries END,
                            next_attempt_at = clock_timestamp() + @RetrySeconds * interval '1 second'
                        WHERE event_id = @EventId AND consumer_key = @ConsumerKey AND delivered_at IS NULL;
                        """, new
                        {
                            row.EventId,
                            row.ConsumerKey,
                            MaxRetries = int.MaxValue,
                            RetrySeconds = DELIVERY_RETRY_SECONDS
                        }, transaction, cancellationToken: cancellationToken));
                    await transaction.ReleaseAsync(DELIVERY_SAVEPOINT, cancellationToken);
                    ++failedCount;
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new OutboxBatchResult(deliveredCount, failedCount);
        }

        private static bool CanRecoverDeliveryFailure(Exception exception, CancellationToken cancellationToken)
        {
            if ((exception is OperationCanceledException) || (cancellationToken.IsCancellationRequested))
            {
                return false;
            }

            // 서버가 반환한 제한 초과만 savepoint로 복원하고 드라이버 타임아웃은 배치 전체를 재시도한다
            if ((exception is PostgresException postgres)
                && ((postgres.SqlState == PostgresErrorCodes.QueryCanceled)
                    || (postgres.SqlState == PostgresErrorCodes.LockNotAvailable)))
            {
                return true;
            }

            return !PostgresTransactionPolicy.IsRetryable(exception);
        }

        private sealed class DeliveryRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid EventId { get; set; }
            public Guid AggregateId { get; set; }
            public string ConsumerKey { get; set; } = string.Empty;
            public string EventKind { get; set; } = string.Empty;
            public string PayloadJson { get; set; } = string.Empty;
            public Guid ActorId { get; set; }
            public Guid OperationId { get; set; }
            public int RetryCount { get; set; }
        }
    }
}
