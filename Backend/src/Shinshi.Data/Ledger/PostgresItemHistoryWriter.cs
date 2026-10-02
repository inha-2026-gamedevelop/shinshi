// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Ledger
{
    /// <summary> 장비 상태 변경과 같은 트랜잭션에서 일련번호별 이전/이후 상태를 추가한다 </summary>
    public sealed class PostgresItemHistoryWriter
    {
        /****************************************
        *                Methods
        ****************************************/

        public async Task<bool> AppendAsync(OperationExecutionContext context, ItemHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context, out NpgsqlTransaction transaction);
            int inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO item_history
                    (item_id, seq, event_kind, before_state, after_state, owner_id, actor_id, operation_id)
                SELECT @ItemId, @Sequence, @EventKind, CAST(@BeforeJson AS jsonb), CAST(@AfterJson AS jsonb),
                       @OwnerId, @ActorId, @OperationId
                FROM operations
                WHERE actor_id = @ActorId AND operation_id = @OperationId AND status = 'Pending'
                ON CONFLICT (item_id, seq) DO NOTHING;
                """, entry, transaction, cancellationToken: cancellationToken));
            if (inserted != 0)
            {
                return true;
            }

            bool identical = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (
                    SELECT 1 FROM item_history
                    WHERE item_id = @ItemId AND seq = @Sequence AND event_kind = @EventKind
                        AND before_state IS NOT DISTINCT FROM CAST(@BeforeJson AS jsonb)
                        AND after_state IS NOT DISTINCT FROM CAST(@AfterJson AS jsonb)
                        AND owner_id = @OwnerId AND actor_id = @ActorId AND operation_id = @OperationId);
                """, entry, transaction, cancellationToken: cancellationToken));
            if (!identical)
            {
                throw new InvalidOperationException("Item history identity conflicts or the operation is not writable.");
            }

            return false;
        }
    }
}
