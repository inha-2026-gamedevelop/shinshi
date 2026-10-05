// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Ledger
{
    /// <summary> 일일 작업에서 호출할 읽기 전용 대사. 계정 ID 순서의 계정 창마다 그 계정들의 잔액과 원장 합계만 집계해 비교한다.
    /// 페이지마다 전체 원장을 다시 묶지 않으므로 대사 비용이 계정 수에 비례한다. 각 창은 한 SQL snapshot이다 </summary>
    public sealed class EconomyLedgerReconciler
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_BATCH_SIZE = 1000;

        // 한 번에 집계하는 계정 수. 원장은 (account_id, ...) 인덱스(ix_economy_ledger_asset)로 이 계정들 행만 읽는다
        public const int DEFAULT_ACCOUNT_WINDOW = 200;

        private readonly NpgsqlDataSource _dataSource;
        private readonly int _accountWindow;

        /****************************************
        *                Methods
        ****************************************/

        public EconomyLedgerReconciler(NpgsqlDataSource dataSource, int accountWindow = DEFAULT_ACCOUNT_WINDOW)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            if (accountWindow <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(accountWindow));
            }

            _dataSource    = dataSource;
            _accountWindow = accountWindow;
        }

        public Task<IReadOnlyList<EconomyReconciliationMismatch>> FindMismatchesAsync(int batchSize,
            EconomyReconciliationCursor? cursor = null, CancellationToken cancellationToken = default)
        {
            return FindMismatchesAsync(batchSize, cursor, null, cancellationToken);
        }

        /// <summary> cursor 뒤의 불일치를 batchSize개까지 돌려준다. batchSize보다 적으면 끝이다.
        /// continueAfterWindow는 계정 창 하나를 마칠 때마다 부르며(선점 heartbeat), false면 지금까지 찾은 것만 돌려주고 멈춘다 </summary>
        public async Task<IReadOnlyList<EconomyReconciliationMismatch>> FindMismatchesAsync(int batchSize,
            EconomyReconciliationCursor? cursor, Func<CancellationToken, Task<bool>>? continueAfterWindow,
            CancellationToken cancellationToken)
        {
            if ((batchSize <= 0) || (batchSize > MAX_BATCH_SIZE))
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            List<EconomyReconciliationMismatch> mismatches = new List<EconomyReconciliationMismatch>();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            // 첫 창은 cursor 계정을 포함하고(같은 계정의 남은 자산), 다음 창부터는 직전 창의 마지막 계정 뒤에서 시작한다
            bool inclusive = cursor != null;
            Guid? after = cursor?.AccountId;
            while (mismatches.Count < batchSize)
            {
                Guid[] accounts = (await connection.QueryAsync<Guid>(new CommandDefinition(
                    """
                    SELECT account_id FROM accounts
                    WHERE NOT @HasAfter OR account_id > @After OR (@Inclusive AND account_id = @After)
                    ORDER BY account_id
                    LIMIT @Window;
                    """, new
                    {
                        HasAfter = after.HasValue,
                        After = after ?? Guid.Empty,
                        Inclusive = inclusive,
                        Window = _accountWindow
                    }, cancellationToken: cancellationToken))).ToArray();
                if (accounts.Length == 0)
                {
                    break;
                }

                foreach (MismatchRow row in await QueryWindowAsync(connection, accounts, cursor, batchSize - mismatches.Count,
                    cancellationToken))
                {
                    mismatches.Add(new EconomyReconciliationMismatch(row.AccountId, row.CharacterId, row.AssetKind,
                        row.AssetId, row.BindingKind, row.CurrentBalance, row.LedgerTotal));
                }

                if (accounts.Length < _accountWindow)
                {
                    break;
                }
                if ((continueAfterWindow != null) && !await continueAfterWindow(cancellationToken))
                {
                    break;
                }

                after     = accounts[accounts.Length - 1];
                inclusive = false;
            }

            return mismatches;
        }

        private static async Task<IEnumerable<MismatchRow>> QueryWindowAsync(NpgsqlConnection connection, Guid[] accounts,
            EconomyReconciliationCursor? cursor, int limit, CancellationToken cancellationToken)
        {
            return await connection.QueryAsync<MismatchRow>(new CommandDefinition(
                """
                WITH balances AS (
                    SELECT wallets.account_id, NULL::uuid AS character_id, assets.asset_kind,
                           ''::text AS asset_id, ''::text AS binding_kind, assets.balance
                    FROM account_wallets AS wallets
                    CROSS JOIN LATERAL (VALUES ('Currency', wallets.currency), ('Tickets', wallets.tickets))
                        AS assets(asset_kind, balance)
                    WHERE wallets.account_id = ANY(@Accounts)
                    UNION ALL
                    SELECT characters.account_id, characters.character_id, assets.asset_kind,
                           '', '', assets.balance
                    FROM characters
                    CROSS JOIN LATERAL (VALUES ('Essence', characters.essence), ('Shards', characters.shards))
                        AS assets(asset_kind, balance)
                    WHERE characters.account_id = ANY(@Accounts)
                    UNION ALL
                    SELECT account_id, NULL::uuid, 'AccountItem', definition_id, binding_kind, quantity
                    FROM account_items
                    WHERE account_id = ANY(@Accounts)
                ), totals AS (
                    SELECT account_id, character_id, asset_kind, asset_id, binding_kind, SUM(delta) AS ledger_total
                    FROM economy_ledger
                    WHERE account_id = ANY(@Accounts)
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
                    Accounts = accounts,
                    EmptyId = Guid.Empty,
                    HasCursor = cursor != null,
                    CursorAccountId = cursor?.AccountId ?? Guid.Empty,
                    CursorCharacterId = cursor?.CharacterId ?? Guid.Empty,
                    CursorAssetKind = cursor?.AssetKind ?? string.Empty,
                    CursorAssetId = cursor?.AssetId ?? string.Empty,
                    CursorBindingKind = cursor?.BindingKind ?? string.Empty,
                    BatchSize = limit
                }, cancellationToken: cancellationToken));
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
