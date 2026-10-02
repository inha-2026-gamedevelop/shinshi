// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Ledger
{
    /// <summary> 잔액을 직접 변경하지 않는다. 동일 트랜잭션의 Pending 작업에 추가 전용 원장만 기록한다 </summary>
    public sealed class PostgresEconomyLedgerWriter : IEconomyLedgerEntryWriter
    {
        /****************************************
        *                Methods
        ****************************************/

        public async Task<bool> AppendAsync(OperationExecutionContext context, EconomyLedgerEntry entry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context, out NpgsqlTransaction transaction);
            object parameters = new
            {
                entry.LedgerId,
                entry.EntryKey,
                entry.Asset.AccountId,
                entry.Asset.CharacterId,
                AssetKind = entry.Asset.Kind,
                entry.Asset.AssetId,
                entry.Asset.BindingKind,
                entry.Delta,
                entry.BalanceAfter,
                Reason = entry.ReasonCode,
                entry.SourceType,
                entry.SourceId,
                entry.ActorKind,
                entry.ActorId,
                entry.OperationId
            };

            int inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO economy_ledger
                    (ledger_id, entry_key, account_id, character_id, asset_kind, asset_id, binding_kind,
                     delta, balance_after, reason, source_type, source_id, actor_kind, actor_id, operation_id)
                SELECT @LedgerId, @EntryKey, @AccountId, @CharacterId, @AssetKind, @AssetId, @BindingKind,
                       @Delta, @BalanceAfter, @Reason, @SourceType, @SourceId, @ActorKind, @ActorId, @OperationId
                FROM operations
                WHERE actor_id = @ActorId AND operation_id = @OperationId AND status = 'Pending'
                ON CONFLICT DO NOTHING;
                """, parameters, transaction, cancellationToken: cancellationToken));
            if (inserted != 0)
            {
                return true;
            }

            // ON CONFLICT로 행을 갱신하면 추가 전용 원장이 깨지므로 기존 내용을 읽어 재전달만 허용한다.
            bool identical = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                SELECT count(*) = 1 AND COALESCE(bool_and(
                    ledger_id = @LedgerId AND entry_key = @EntryKey AND account_id = @AccountId
                    AND character_id IS NOT DISTINCT FROM @CharacterId AND asset_kind = @AssetKind
                    AND asset_id = @AssetId AND binding_kind = @BindingKind AND delta = @Delta
                    AND balance_after = @BalanceAfter AND reason = @Reason
                    AND source_type = @SourceType AND source_id = @SourceId AND actor_kind = @ActorKind
                    AND actor_id = @ActorId AND operation_id = @OperationId), false)
                FROM economy_ledger
                WHERE ledger_id = @LedgerId
                    OR (actor_id = @ActorId AND operation_id = @OperationId AND entry_key = @EntryKey);
                """, parameters, transaction, cancellationToken: cancellationToken));
            if (!identical)
            {
                throw new InvalidOperationException("Ledger identity conflicts or the operation is not writable.");
            }

            return false;
        }
    }
}
