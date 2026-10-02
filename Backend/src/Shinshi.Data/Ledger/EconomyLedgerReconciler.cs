// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Ledger
{
    /// <summary> 일일 작업에서 호출할 읽기 전용 대사. 현재 구현된 잔액과 자산별 전체 원장 합계를 한 snapshot에서 비교한다 </summary>
    public sealed class EconomyLedgerReconciler
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_BATCH_SIZE = 1000;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public EconomyLedgerReconciler(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 각 페이지는 한 SQL snapshot이다. 전체 스캔의 고정 snapshot이 필요하면 별도 배치 실행 경계를 둔다 </summary>
        public async Task<IReadOnlyList<EconomyReconciliationMismatch>> FindMismatchesAsync(int batchSize,
            EconomyReconciliationCursor? cursor = null, CancellationToken cancellationToken = default)
        {
            if ((batchSize <= 0) || (batchSize > MAX_BATCH_SIZE))
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            IEnumerable<MismatchRow> rows = await connection.QueryAsync<MismatchRow>(new CommandDefinition(
                """
                WITH balances AS (
                    SELECT wallets.account_id, NULL::uuid AS character_id, assets.asset_kind,
                           ''::text AS asset_id, ''::text AS binding_kind, assets.balance
                    FROM account_wallets AS wallets
                    CROSS JOIN LATERAL (VALUES ('Currency', wallets.currency), ('Tickets', wallets.tickets))
                        AS assets(asset_kind, balance)
                    UNION ALL
                    SELECT characters.account_id, characters.character_id, assets.asset_kind,
                           '', '', assets.balance
                    FROM characters
                    CROSS JOIN LATERAL (VALUES ('Essence', characters.essence), ('Shards', characters.shards))
                        AS assets(asset_kind, balance)
                    UNION ALL
                    SELECT account_id, NULL::uuid, 'AccountItem', definition_id, binding_kind, quantity
                    FROM account_items
                ), totals AS (
                    SELECT account_id, character_id, asset_kind, asset_id, binding_kind, SUM(delta) AS ledger_total
                    FROM economy_ledger
                    GROUP BY account_id, character_id, asset_kind, asset_id, binding_kind
                ), mismatches AS (
                    SELECT COALESCE(balances.account_id, totals.account_id) AS account_id,
                           COALESCE(balances.character_id, totals.character_id) AS character_id,
                           COALESCE(balances.asset_kind, totals.asset_kind) AS asset_kind,
                           COALESCE(balances.asset_id, totals.asset_id) AS asset_id,
                           COALESCE(balances.binding_kind, totals.binding_kind) AS binding_kind,
                           balances.balance, COALESCE(totals.ledger_total, 0) AS ledger_total
                    FROM balances
                    FULL OUTER JOIN totals ON balances.account_id = totals.account_id
                        AND COALESCE(balances.character_id, @EmptyId) = COALESCE(totals.character_id, @EmptyId)
                        AND balances.asset_kind = totals.asset_kind
                        AND balances.asset_id = totals.asset_id AND balances.binding_kind = totals.binding_kind
                    WHERE COALESCE(balances.balance, 0)::numeric <> COALESCE(totals.ledger_total, 0)
                )
                SELECT account_id AS "AccountId", character_id AS "CharacterId", asset_kind AS "AssetKind",
                       asset_id AS "AssetId", binding_kind AS "BindingKind", balance AS "CurrentBalance",
                       ledger_total::text AS "LedgerTotal"
                FROM mismatches
                WHERE NOT @HasCursor OR
                    (account_id, COALESCE(character_id, @EmptyId), asset_kind, asset_id, binding_kind) >
                    (@CursorAccountId, @CursorCharacterId, @CursorAssetKind, @CursorAssetId, @CursorBindingKind)
                ORDER BY account_id, COALESCE(character_id, @EmptyId), asset_kind, asset_id, binding_kind
                LIMIT @BatchSize;
                """, new
                {
                    EmptyId = Guid.Empty,
                    HasCursor = cursor != null,
                    CursorAccountId = cursor?.AccountId ?? Guid.Empty,
                    CursorCharacterId = cursor?.CharacterId ?? Guid.Empty,
                    CursorAssetKind = cursor?.AssetKind ?? string.Empty,
                    CursorAssetId = cursor?.AssetId ?? string.Empty,
                    CursorBindingKind = cursor?.BindingKind ?? string.Empty,
                    BatchSize = batchSize
                }, cancellationToken: cancellationToken));
            List<EconomyReconciliationMismatch> mismatches = new List<EconomyReconciliationMismatch>();
            foreach (MismatchRow row in rows)
            {
                mismatches.Add(new EconomyReconciliationMismatch(row.AccountId, row.CharacterId, row.AssetKind,
                    row.AssetId, row.BindingKind, row.CurrentBalance, row.LedgerTotal));
            }

            return mismatches;
        }

        private sealed class MismatchRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AccountId { get; set; }
            public Guid? CharacterId { get; set; }
            public string AssetKind { get; set; } = string.Empty;
            public string AssetId { get; set; } = string.Empty;
            public string BindingKind { get; set; } = string.Empty;
            public long? CurrentBalance { get; set; }
            public string LedgerTotal { get; set; } = string.Empty;
        }
    }
}
