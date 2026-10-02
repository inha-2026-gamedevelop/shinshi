// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Ledger
{
    /// <summary> 결과와 같은 트랜잭션에 통지를 넣는다. event ID를 다른 출처나 내용으로 재사용하지 못한다 </summary>
    public sealed class PostgresOutboxWriter
    {
        /****************************************
        *                Methods
        ****************************************/

        public async Task<bool> EnqueueAsync(OperationExecutionContext context, OutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context, out NpgsqlTransaction transaction);
            int inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO outbox (event_id, aggregate_id, consumer_key, event_kind, payload, actor_id, operation_id)
                SELECT @EventId, @AggregateId, @ConsumerKey, @EventKind, CAST(@PayloadJson AS jsonb), @ActorId, @OperationId
                FROM operations
                WHERE actor_id = @ActorId AND operation_id = @OperationId AND status = 'Pending'
                ON CONFLICT (event_id) DO NOTHING;
                """, message, transaction, cancellationToken: cancellationToken));
            if (inserted != 0)
            {
                return true;
            }

            bool identical = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (
                    SELECT 1 FROM outbox
                    WHERE event_id = @EventId AND aggregate_id = @AggregateId AND consumer_key = @ConsumerKey
                        AND event_kind = @EventKind AND payload = CAST(@PayloadJson AS jsonb)
                        AND actor_id = @ActorId AND operation_id = @OperationId);
                """, message, transaction, cancellationToken: cancellationToken));
            if (!identical)
            {
                throw new InvalidOperationException("Outbox event identity conflicts or the operation is not writable.");
            }

            return false;
        }
    }
}
