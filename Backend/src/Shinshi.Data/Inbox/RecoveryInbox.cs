// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Inbox
{
    /// <summary> 회수함으로 옮길 획득 한 줄. 캐릭터 자산(정련편, 정기)은 획득한 캐릭터에 묶인다 </summary>
    public sealed class RecoveryLine
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   AccountId   { get; }
        public Guid?  CharacterId { get; }
        public string AssetKind   { get; }
        public string AssetId     { get; }
        public string BindingKind { get; }
        public long   Quantity    { get; }

        /****************************************
        *                Methods
        ****************************************/

        public RecoveryLine(Guid accountId, Guid? characterId, string assetKind, string? assetId, string? bindingKind,
            long quantity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assetKind);
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            AccountId   = accountId;
            CharacterId = characterId;
            AssetKind   = assetKind;
            AssetId     = assetId ?? string.Empty;
            BindingKind = bindingKind ?? string.Empty;
            Quantity    = quantity;
        }
    }

    /// <summary> 30일 보상 회수함 행과 줄을 쓰고 읽는다. 호출자의 작업 트랜잭션 안에서만 부른다 </summary>
    public static class RecoveryInbox
    {
        /****************************************
        *                Fields
        ****************************************/

        // reward_inbox.source_kind(0012)
        public const string SOURCE_SESSION_REWARD = "SessionReward";
        public const string SOURCE_AUCTION_REWARD = "AuctionReward";

        private static readonly string[] G_UpdateOrder =
        {
            EconomyAsset.CURRENCY, EconomyAsset.TICKETS, EconomyAsset.ESSENCE, EconomyAsset.SHARDS,
            EconomyAsset.ACCOUNT_ITEM
        };

        private const string READ_CURRENCY_SQL =
            "SELECT currency FROM account_wallets WHERE account_id = @AccountId FOR UPDATE;";
        private const string READ_TICKETS_SQL =
            "SELECT tickets FROM account_wallets WHERE account_id = @AccountId FOR UPDATE;";
        private const string READ_ESSENCE_SQL =
            """
            SELECT essence FROM characters
            WHERE character_id = @CharacterId AND account_id = @AccountId AND deleted_at IS NULL FOR UPDATE;
            """;
        private const string READ_SHARDS_SQL =
            """
            SELECT shards FROM characters
            WHERE character_id = @CharacterId AND account_id = @AccountId AND deleted_at IS NULL FOR UPDATE;
            """;
        private const string READ_ITEM_SQL =
            """
            SELECT quantity FROM account_items
            WHERE account_id = @AccountId AND definition_id = @AssetId AND binding_kind = @BindingKind FOR UPDATE;
            """;

        /****************************************
        *                Methods
        ****************************************/

        public static bool IsRecoverySource(string? sourceKind)
        {
            return (sourceKind == SOURCE_SESSION_REWARD) || (sourceKind == SOURCE_AUCTION_REWARD);
        }

        /// <summary> 계정마다 회수함 행 하나와 그 줄을 넣는다. 획득 시각과 만료(+30일)는 같은 DB 시각에서 정한다.
        /// 같은 출처(source_kind, source_ref, 계정)는 유일 인덱스가 두 번째 행을 막는다 </summary>
        public static async Task<int> InsertAsync(DbConnection connection, DbTransaction transaction, string sourceKind,
            string sourceRef, IReadOnlyList<RecoveryLine> lines, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(lines);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);
            if (!IsRecoverySource(sourceKind))
            {
                throw new ArgumentException("Unsupported recovery source.", nameof(sourceKind));
            }
            if (lines.Count == 0)
            {
                return 0;
            }

            // 같은 자산 키는 한 줄로 합친다. 줄 순서는 계정, 자산 갱신 순서와 같게 둔다
            List<(Guid Account, List<(RecoveryLine Line, long Quantity)> Lines)> byAccount = lines
                .GroupBy(line => line.AccountId)
                .OrderBy(group => group.Key)
                .Select(group => (group.Key, group
                    .GroupBy(line => (line.AssetKind, line.AssetId, line.BindingKind, line.CharacterId))
                    .Select(merged => (merged.First(), merged.Sum(line => line.Quantity)))
                    .OrderBy(entry => KindRank(entry.Item1.AssetKind))
                    .ThenBy(entry => entry.Item1.AssetId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Item1.BindingKind, StringComparer.Ordinal)
                    .ToList()))
                .ToList();

            Guid[] inboxIds = byAccount.Select(_ => Guid.NewGuid()).ToArray();
            string[] summaries = byAccount.Select(entry => RewardLines.Serialize(entry.Lines
                .Select(line => new RewardLine(line.Line.AssetKind, line.Line.AssetId, line.Line.BindingKind,
                    line.Quantity)).ToList())).ToArray();
            int inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                WITH acquired AS (SELECT clock_timestamp() AS at)
                INSERT INTO reward_inbox (inbox_id, account_id, source_kind, grant_id, source_ref, rewards, created_at,
                    expires_at, acquired_at)
                SELECT entry.inbox_id, entry.account_id, @SourceKind, NULL, @SourceRef, CAST(entry.rewards AS jsonb),
                    acquired.at, acquired.at + @Period, acquired.at
                FROM unnest(@InboxIds::uuid[], @Accounts::uuid[], @Rewards::text[]) AS entry(inbox_id, account_id, rewards)
                CROSS JOIN acquired;
                """, new
                {
                    SourceKind = sourceKind,
                    SourceRef = sourceRef,
                    Period = RewardStorageLimits.RecoveryPeriod,
                    InboxIds = inboxIds,
                    Accounts = byAccount.Select(entry => entry.Account).ToArray(),
                    Rewards = summaries
                }, transaction, cancellationToken: cancellationToken));

            List<(Guid Inbox, int LineNo, Guid Account, RecoveryLine Line, long Quantity)> rows =
                new List<(Guid, int, Guid, RecoveryLine, long)>();
            for (int index = 0; index < byAccount.Count; ++index)
            {
                for (int lineIndex = 0; lineIndex < byAccount[index].Lines.Count; ++lineIndex)
                {
                    (RecoveryLine line, long quantity) = byAccount[index].Lines[lineIndex];
                    rows.Add((inboxIds[index], lineIndex + 1, byAccount[index].Account, line, quantity));
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO reward_inbox_lines (inbox_id, line_no, account_id, asset_kind, definition_id, binding_kind,
                    character_id, original_qty, remaining_qty)
                SELECT line.inbox_id, line.line_no, line.account_id, line.asset_kind, line.definition_id, line.binding_kind,
                    NULLIF(line.character_id, '00000000-0000-0000-0000-000000000000'::uuid), line.quantity, line.quantity
                FROM unnest(@Inboxes::uuid[], @LineNos::integer[], @Accounts::uuid[], @Kinds::text[], @Definitions::text[],
                    @Bindings::text[], @Characters::uuid[], @Quantities::bigint[])
                    AS line(inbox_id, line_no, account_id, asset_kind, definition_id, binding_kind, character_id, quantity);
                """, new
                {
                    Inboxes = rows.Select(row => row.Inbox).ToArray(),
                    LineNos = rows.Select(row => row.LineNo).ToArray(),
                    Accounts = rows.Select(row => row.Account).ToArray(),
                    Kinds = rows.Select(row => row.Line.AssetKind).ToArray(),
                    Definitions = rows.Select(row => row.Line.AssetId).ToArray(),
                    Bindings = rows.Select(row => row.Line.BindingKind).ToArray(),
                    Characters = rows.Select(row => row.Line.CharacterId ?? Guid.Empty).ToArray(),
                    Quantities = rows.Select(row => row.Quantity).ToArray()
                }, transaction, cancellationToken: cancellationToken));
            return inserted;
        }

        /// <summary> 잔액 행을 잠그고 현재 보유량을 읽는다. 지갑, 아이템 행이 없으면 0, 캐릭터 자산의 캐릭터가 없거나 삭제됐으면 null이다 </summary>
        public static async Task<long?> ReadBalanceForUpdateAsync(DbConnection connection, DbTransaction transaction,
            EconomyAsset asset, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(asset);
            string sql = asset.Kind switch
            {
                EconomyAsset.CURRENCY     => READ_CURRENCY_SQL,
                EconomyAsset.TICKETS      => READ_TICKETS_SQL,
                EconomyAsset.ESSENCE      => READ_ESSENCE_SQL,
                EconomyAsset.SHARDS       => READ_SHARDS_SQL,
                EconomyAsset.ACCOUNT_ITEM => READ_ITEM_SQL,
                _ => throw new ArgumentException("Unsupported asset kind.", nameof(asset))
            };
            long? balance = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(sql, new
            {
                asset.AccountId,
                asset.CharacterId,
                asset.AssetId,
                asset.BindingKind
            }, transaction, cancellationToken: cancellationToken));
            bool characterAsset = (asset.Kind == EconomyAsset.ESSENCE) || (asset.Kind == EconomyAsset.SHARDS);
            return (balance.HasValue || characterAsset) ? balance : 0;
        }

        /// <summary> 목록에 보일 회수함 행의 남은 줄. 남은 수량이 없는 줄은 뺀다 </summary>
        public static async Task<Dictionary<Guid, List<RewardLine>>> ReadOpenLinesAsync(DbConnection connection,
            IReadOnlyCollection<Guid> inboxIds, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(inboxIds);
            Dictionary<Guid, List<RewardLine>> lines = new Dictionary<Guid, List<RewardLine>>();
            if (inboxIds.Count == 0)
            {
                return lines;
            }

            IEnumerable<LineRow> rows = await connection.QueryAsync<LineRow>(new CommandDefinition(
                """
                SELECT inbox_id AS "InboxId", asset_kind AS "AssetKind", definition_id AS "DefinitionId",
                    binding_kind AS "BindingKind", remaining_qty AS "Remaining"
                FROM reward_inbox_lines WHERE inbox_id = ANY(@Ids) AND remaining_qty > 0
                ORDER BY inbox_id, line_no;
                """, new { Ids = inboxIds.ToArray() }, cancellationToken: cancellationToken));
            foreach (LineRow row in rows)
            {
                if (!lines.TryGetValue(row.InboxId, out List<RewardLine>? list))
                {
                    list = new List<RewardLine>();
                    lines.Add(row.InboxId, list);
                }
                list.Add(new RewardLine(row.AssetKind, row.DefinitionId, row.BindingKind, row.Remaining));
            }

            return lines;
        }

        /// <summary> 잔액 갱신 순서(지갑 -> 캐릭터 -> 아이템). 정산, 우편 수령과 같다 </summary>
        public static int KindRank(string assetKind)
        {
            int rank = Array.IndexOf(G_UpdateOrder, assetKind);
            return rank >= 0 ? rank : G_UpdateOrder.Length;
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class LineRow
        {
            public Guid   InboxId      { get; set; }
            public string AssetKind    { get; set; } = string.Empty;
            public string DefinitionId { get; set; } = string.Empty;
            public string BindingKind  { get; set; } = string.Empty;
            public long   Remaining    { get; set; }
        }
    }
}
