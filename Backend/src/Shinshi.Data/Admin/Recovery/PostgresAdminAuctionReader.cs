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
using Shinshi.Data.Auctions;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Admin
{
    /// <summary> 경매 단계(Auction) 출정 목록. 멈춘 경매를 운영자가 확인하는 읽기 전용 조회이며 상태를 바꾸지 않는다
    /// (닫기는 유지 작업의 자동 복구가 한다) </summary>
    public sealed class PostgresAdminAuctionReader
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly string[] G_ItemStates = { AdminAuctionPage.STATE_OPEN, AdminAuctionPage.STATE_CLOSING };

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *              Properties
        ****************************************/

        public static IReadOnlyList<string> ItemStates => G_ItemStates;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminAuctionReader(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> itemState가 없으면 멈춘 경매(기한 + 유예 경과 또는 배정 서버 소실), 있으면 그 상태의 진행 중 품목이 있는 경매 단계
        /// 출정을 경매 단계 기한, ID 순으로 읽는다. livenessDeferred면 서버 소실 표시를 모두 false로 내린다 </summary>
        public async Task<AdminAuctionPage> ListAsync(string? itemState, AdminLogCursor? cursor, int limit,
            bool livenessDeferred, CancellationToken cancellationToken = default)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }
            if ((itemState != null) && !G_ItemStates.Contains(itemState, StringComparer.Ordinal))
            {
                throw new ArgumentException("Unknown auction item state.", nameof(itemState));
            }

            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("AuctionGrace", PartyAuctionRules.PhaseRecoveryGrace);
            parameters.Add("CancelTimeout", ServerPoolTimings.ServiceCancelTimeout);
            parameters.Add("HeartbeatTimeout", ServerPoolTimings.HeartbeatTimeout);
            parameters.Add("Fetch", limit + 1);
            string filter = "(" + PostgresAdminRecoveryRepository.AUCTION_DEADLINE_SQL + " OR "
                + PostgresAdminRecoveryRepository.G_AuctionLostSql + ")";
            if (itemState != null)
            {
                filter = "item.state = @ItemState";
                parameters.Add("ItemState", itemState);
            }
            if (cursor != null)
            {
                filter += " AND (session.auction_ends_at, session.session_id) > (@CursorAt, @CursorId)";
                parameters.Add("CursorAt", cursor.At);
                parameters.Add("CursorId", cursor.Id);
            }

            // 순서와 keyset은 ix_game_sessions_auction(auction_ends_at, session_id) WHERE state = 'Auction'을,
            // 품목은 ux_auctions_session_active(session_id) WHERE state IN ('Open', 'Closing')을 쓴다
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            List<AuctionRow> rows = (await connection.QueryAsync<AuctionRow>(new CommandDefinition(
                """
                SELECT session.session_id AS "SessionId", session.instance_id AS "InstanceId",
                    session.auction_ends_at AS "AuctionEndsAt",
                """
                + " " + PostgresAdminRecoveryRepository.AUCTION_DEADLINE_SQL + " AS \"Overdue\", "
                + PostgresAdminRecoveryRepository.G_AuctionLostSql + " AS \"InstanceLost\","
                + """

                    item.auction_id AS "AuctionId", item.state AS "ItemState", item.definition_id AS "DefinitionId",
                    COALESCE(item.bid_seq, 0) AS "BidSeq", item.hard_ends_at AS "HardEndsAt",
                    COALESCE(item.high_bid, 0) AS "ReservedCurrency"
                FROM game_sessions AS session
                LEFT JOIN auctions AS item ON item.session_id = session.session_id AND item.state IN ('Open', 'Closing')
                WHERE session.state = 'Auction' AND
                """
                + " " + filter
                + " ORDER BY session.auction_ends_at, session.session_id LIMIT @Fetch;",
                parameters, cancellationToken: cancellationToken))).ToList();

            bool hasMore = rows.Count > limit;
            List<AuctionRow> page = hasMore ? rows.GetRange(0, limit) : rows;
            string? nextCursor = hasMore
                ? new AdminLogCursor(page[page.Count - 1].AuctionEndsAt, page[page.Count - 1].SessionId).Encode()
                : null;
            return new AdminAuctionPage(page.Select(row => row.ToContract(livenessDeferred)).ToArray(), nextCursor!,
                livenessDeferred);
        }

        private sealed class AuctionRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid      SessionId        { get; set; }
            public Guid      InstanceId       { get; set; }
            public DateTime  AuctionEndsAt    { get; set; }
            public bool      Overdue          { get; set; }
            public bool      InstanceLost     { get; set; }
            public Guid?     AuctionId        { get; set; }
            public string?   ItemState        { get; set; }
            public string?   DefinitionId     { get; set; }
            public long      BidSeq           { get; set; }
            public DateTime? HardEndsAt       { get; set; }
            public long      ReservedCurrency { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            // 판정 보류 중에는 오래된 heartbeat가 DB, API 단절 탓일 수 있어 소실로 표시하지 않는다
            public AdminAuctionSession ToContract(bool livenessDeferred)
            {
                return new AdminAuctionSession(SessionId.ToString("D"), InstanceId.ToString("D"),
                    AdminLogValues.ToUnixMs(AuctionEndsAt), Overdue, InstanceLost && (!livenessDeferred),
                    AdminLogValues.ToText(AuctionId)!, ItemState!, DefinitionId!, BidSeq,
                    HardEndsAt.HasValue ? AdminLogValues.ToUnixMs(HardEndsAt.Value) : 0, ReservedCurrency);
            }
        }
    }
}
