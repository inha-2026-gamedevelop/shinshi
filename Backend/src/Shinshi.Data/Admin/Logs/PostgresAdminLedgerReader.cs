// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> 원장 시간순 조회와 같은 필터의 사유, 자산별 합계. 읽기 전용이며 감사하지 않는다 </summary>
    public sealed class PostgresAdminLedgerReader
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CURSOR_CONDITION = "(ledger.occurred_at, ledger.ledger_id) > (@CursorAt, @CursorId)";

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminLedgerReader(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 페이지와 합계를 한 snapshot에서 읽는다. 합계는 cursor가 없는 첫 페이지에만 있고 다음 페이지는 null이다.
        /// 조회 범위 조건이 없으면 ArgumentException이다 </summary>
        public async Task<AdminLedgerPage> ReadPageAsync(AdminLedgerQuery query, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (!query.HasIndexedScope)
            {
                throw new ArgumentException("Ledger query requires an account or a source.", nameof(query));
            }
            if (query.Limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(query));
            }

            DynamicParameters parameters = new DynamicParameters();
            string filter = BuildFilter(query, parameters);
            string pageFilter = filter;
            if (query.Cursor != null)
            {
                pageFilter += " AND " + CURSOR_CONDITION;
                parameters.Add("CursorAt", query.Cursor.At);
                parameters.Add("CursorId", query.Cursor.Id);
            }

            // 다음 페이지가 있는지 한 줄 더 읽어 확인한다
            parameters.Add("Fetch", query.Limit + 1);

            (NpgsqlConnection connection, NpgsqlTransaction transaction) = await AdminLogValues.BeginReadAsync(_dataSource,
                cancellationToken);
            await using (connection)
            await using (transaction)
            {
                List<AdminLedgerRow> rows = (await connection.QueryAsync<AdminLedgerRow>(new CommandDefinition(
                    "SELECT " + AdminLedgerRow.SELECT_COLUMNS + " FROM economy_ledger AS ledger WHERE " + pageFilter
                    + " ORDER BY ledger.occurred_at, ledger.ledger_id LIMIT @Fetch;",
                    parameters, transaction, cancellationToken: cancellationToken))).ToList();
                // 합계는 필터 전체를 훑으므로 첫 페이지에서만 계산한다. 다음 페이지마다 다시 세면 페이지 수만큼 전체 스캔이 반복된다
                List<TotalRow>? totals = null;
                if (query.Cursor == null)
                {
                    totals = (await connection.QueryAsync<TotalRow>(new CommandDefinition(
                        """
                        SELECT ledger.reason AS "Reason", ledger.asset_kind AS "AssetKind", ledger.asset_id AS "AssetId",
                            ledger.binding_kind AS "BindingKind", sum(ledger.delta)::text AS "DeltaTotal", count(*) AS "Count"
                        FROM economy_ledger AS ledger
                        WHERE
                        """ + " " + filter + """

                        GROUP BY ledger.reason, ledger.asset_kind, ledger.asset_id, ledger.binding_kind
                        ORDER BY ledger.reason, ledger.asset_kind, ledger.asset_id, ledger.binding_kind;
                        """, parameters, transaction, cancellationToken: cancellationToken))).ToList();
                }
                await transaction.CommitAsync(cancellationToken);

                bool hasMore = rows.Count > query.Limit;
                List<AdminLedgerRow> page = hasMore ? rows.GetRange(0, query.Limit) : rows;
                string? nextCursor = hasMore
                    ? new AdminLogCursor(page[page.Count - 1].OccurredAt, page[page.Count - 1].LedgerId).Encode()
                    : null;
                return new AdminLedgerPage(page.Select(row => row.ToContract()).ToArray(),
                    totals?.Select(row => row.ToContract()).ToArray()!, nextCursor);
            }
        }

        // 조건 문장은 고정 문자열만 붙이고 값은 모두 매개변수로 넘긴다
        private static string BuildFilter(AdminLedgerQuery query, DynamicParameters parameters)
        {
            List<string> conditions = new List<string>();
            if (query.AccountId.HasValue)
            {
                conditions.Add("ledger.account_id = @AccountId");
                parameters.Add("AccountId", query.AccountId.Value);
            }
            if (query.CharacterId.HasValue)
            {
                conditions.Add("ledger.character_id = @CharacterId");
                parameters.Add("CharacterId", query.CharacterId.Value);
            }
            if (!string.IsNullOrEmpty(query.AssetKind))
            {
                conditions.Add("ledger.asset_kind = @AssetKind");
                parameters.Add("AssetKind", query.AssetKind);
            }
            if (!string.IsNullOrEmpty(query.AssetId))
            {
                conditions.Add("ledger.asset_id = @AssetId");
                parameters.Add("AssetId", query.AssetId);
            }
            if (query.ReasonCode.HasValue)
            {
                conditions.Add("ledger.reason = @ReasonCode");
                parameters.Add("ReasonCode", query.ReasonCode.Value);
            }
            if (!string.IsNullOrEmpty(query.SourceType))
            {
                conditions.Add("ledger.source_type = @SourceType");
                parameters.Add("SourceType", query.SourceType);
            }
            if (!string.IsNullOrEmpty(query.SourceId))
            {
                conditions.Add("ledger.source_id = @SourceId");
                parameters.Add("SourceId", query.SourceId);
            }
            if (query.From.HasValue)
            {
                conditions.Add("ledger.occurred_at >= @From");
                parameters.Add("From", query.From.Value.UtcDateTime);
            }
            if (query.To.HasValue)
            {
                conditions.Add("ledger.occurred_at < @To");
                parameters.Add("To", query.To.Value.UtcDateTime);
            }

            return string.Join(" AND ", conditions);
        }

        private sealed class TotalRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public int    Reason      { get; set; }
            public string AssetKind   { get; set; } = string.Empty;
            public string AssetId     { get; set; } = string.Empty;
            public string BindingKind { get; set; } = string.Empty;
            public string DeltaTotal  { get; set; } = string.Empty;
            public long   Count       { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public AdminLedgerReasonTotal ToContract()
            {
                return new AdminLedgerReasonTotal(Reason, AssetKind, AssetId, BindingKind, DeltaTotal, Count);
            }
        }
    }
}
