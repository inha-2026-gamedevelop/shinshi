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
using Shinshi.Contracts;
using Shinshi.Data.Growth;

namespace Shinshi.Data.Market
{
    /// <summary> 유저 경매장 읽기 전용 조회(검색, 등록 목록, 내 등록, 기록). operations와 원장을 만들지 않으며 쓰기 트랜잭션을 열지 않는다.
    /// 모든 조회는 만료 시각이 지난 활성 등록을 빼고(유지 작업이 닫기 전에도 살 수 없다) 인덱스를 따른다 </summary>
    public sealed class PostgresMarketQueries
    {
        /****************************************
        *                Fields
        ****************************************/

        // 페이지 번호 상한. 검색 묶음은 정의 ID 수, 등록 목록은 정의 하나의 활성 등록 수로 닫혀 있어 이 이상은 비어 있다
        public const int MAX_PAGE = MarketLimits.MAX_PAGE;

        // 내 등록 응답의 최대 줄 수. 활성 10건 + 최근 종료분 중 최신순이다
        public const int MAX_MINE_ROWS = 100;

        private const string LISTING_COLUMNS =
            """
            listing_id AS "ListingId", asset_kind AS "AssetKind", definition_id AS "DefinitionId", item_id AS "ItemId",
            grade::integer AS "Grade", class_id::integer AS "ClassId", category::integer AS "Category",
            unit_price AS "UnitPrice", listed_qty AS "ListedQty", remaining_qty AS "RemainingQty", state AS "State",
            gross_total AS "GrossTotal", fee_total AS "FeeTotal", net_total AS "NetTotal",
            (extract(epoch FROM created_at) * 1000)::bigint AS "CreatedAtUnixMs",
            (extract(epoch FROM expires_at) * 1000)::bigint AS "ExpiresAtUnixMs",
            COALESCE((extract(epoch FROM closed_at) * 1000)::bigint, 0) AS "ClosedAtUnixMs",
            (seen_at IS NOT NULL) AS "Seen", (seller_account_id = @Viewer) AS "Mine"
            """;

        private const string SERVER_TIME_SQL = "(extract(epoch FROM clock_timestamp()) * 1000)::bigint";

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresMarketQueries(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 정의 ID 묶음 한 페이지. 정렬 기준이 같으면 정의 ID 순서다 </summary>
        public async Task<MarketSearchResponse> SearchAsync(MarketSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            int page = Math.Clamp(request.Page, 0, MAX_PAGE);
            string[]? ids = ((request.DefinitionIds == null) || (request.DefinitionIds.Length == 0))
                ? null
                : request.DefinitionIds.Take(MarketLimits.MAX_SEARCH_DEFINITIONS).ToArray();
            string order = OrderBy(request.Sort, request.Order);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            List<GroupRow> rows = (await connection.QueryAsync<GroupRow>(new CommandDefinition(
                """
                WITH grouped AS (
                    SELECT definition_id, MAX(grade)::integer AS grade, MIN(unit_price) AS lowest,
                        SUM(remaining_qty)::bigint AS total_qty, COUNT(*)::integer AS listing_count,
                        MAX(created_at) AS newest
                    FROM market_listings
                    WHERE state = 'Active' AND expires_at > clock_timestamp()
                        AND (@Category::integer IS NULL OR category = @Category)
                        AND (@ClassId::integer IS NULL OR class_id = @ClassId OR asset_kind = 'Equipment')
                        AND (@Grade::integer IS NULL OR grade = @Grade)
                        AND (@Ids::text[] IS NULL OR definition_id = ANY(@Ids))
                    GROUP BY definition_id
                )
                SELECT grouped.definition_id AS "DefinitionId", grouped.grade AS "Grade", grouped.lowest AS "Lowest",
                    grouped.total_qty AS "TotalQty", grouped.listing_count AS "ListingCount",
                    COALESCE((SELECT trade.unit_price FROM market_trades AS trade
                        WHERE trade.definition_id = grouped.definition_id
                        ORDER BY trade.created_at DESC LIMIT 1), 0) AS "Recent",
                    (count(*) OVER ())::integer AS "Total"
                FROM grouped
                ORDER BY
                """ + " " + order + """
                 LIMIT @Take OFFSET @Offset;
                """, new
                {
                    Category = request.Category,
                    ClassId = request.ClassId,
                    Grade = request.Grade,
                    Ids = ids,
                    Take = MarketLimits.PAGE_SIZE,
                    Offset = page * MarketLimits.PAGE_SIZE
                }, cancellationToken: cancellationToken))).ToList();
            long now = await connection.QuerySingleAsync<long>(new CommandDefinition("SELECT " + SERVER_TIME_SQL + ";",
                cancellationToken: cancellationToken));
            return new MarketSearchResponse(rows.Select(row => new MarketGroupView(row.DefinitionId, row.Grade,
                row.Lowest, row.TotalQty, row.ListingCount, row.Recent)).ToArray(), rows.Count > 0 ? rows[0].Total : 0,
                page, now);
        }

        /// <summary> 한 정의 ID의 활성 등록 한 페이지(개당 가격 오름차순, 같은 가격은 먼저 등록한 순서)와 최근 24시간 거래 평균 개당 가격.
        /// 조회에는 보는 사람이 없어 mine은 항상 거짓이다. 자기 등록 구매는 구매 쪽이 MarketSelfPurchase로 거절한다 </summary>
        public async Task<MarketListingsResponse> ListingsAsync(MarketListingsQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            int page = Math.Clamp(request.Page, 0, MAX_PAGE);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
                "SELECT " + LISTING_COLUMNS + """
                 FROM market_listings
                WHERE definition_id = @DefinitionId AND state = 'Active' AND expires_at > clock_timestamp()
                ORDER BY unit_price, created_at, listing_id
                LIMIT @Take OFFSET @Offset;
                SELECT count(*)::integer FROM market_listings
                WHERE definition_id = @DefinitionId AND state = 'Active' AND expires_at > clock_timestamp();
                SELECT COALESCE(round(sum(gross)::numeric / NULLIF(sum(quantity), 0))::bigint, 0) FROM market_trades
                WHERE definition_id = @DefinitionId AND created_at > clock_timestamp() - @RecentWindow;
                """ + "SELECT " + SERVER_TIME_SQL + ";", new
                {
                    Viewer = Guid.Empty,
                    request.DefinitionId,
                    Take = MarketLimits.PAGE_SIZE,
                    Offset = page * MarketLimits.PAGE_SIZE,
                    RecentWindow = TimeSpan.FromHours(MarketLimits.RECENT_PRICE_HOURS)
                }, cancellationToken: cancellationToken));
            List<ListingRow> rows = (await grid.ReadAsync<ListingRow>()).ToList();
            int total = await grid.ReadSingleAsync<int>();
            long average = await grid.ReadSingleAsync<long>();
            long now = await grid.ReadSingleAsync<long>();
            return new MarketListingsResponse(rows.Select(ToView).ToArray(), total, page, average, now);
        }

        /// <summary> 내 등록(활성 + 최근 종료)과 가용 엽전, 미확인 수. 캐릭터가 없으면 NotFound, 접속 임대가 이 마을에 없으면 WrongLocation이다 </summary>
        public async Task<(EGrowthAccess Access, MarketMineResponse? Body)> MineAsync(Guid townInstanceId, Guid characterId,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            (EGrowthAccess access, Guid accountId) = await ResolveAccessAsync(connection, townInstanceId, characterId,
                cancellationToken);
            if (access != EGrowthAccess.Allowed)
            {
                return (access, null);
            }

            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
                "SELECT " + LISTING_COLUMNS + """
                 FROM market_listings
                WHERE seller_account_id = @Viewer
                    AND (state = 'Active' OR closed_at > clock_timestamp() - @HistoryWindow)
                ORDER BY created_at DESC, listing_id DESC
                LIMIT @Take;
                SELECT COALESCE((SELECT currency - currency_reserved FROM account_wallets WHERE account_id = @Viewer), 0);
                SELECT count(*)::integer FROM market_listings
                WHERE seller_account_id = @Viewer AND state <> 'Active' AND seen_at IS NULL
                    AND closed_at > clock_timestamp() - @HistoryWindow;
                """ + "SELECT " + SERVER_TIME_SQL + ";", new
                {
                    Viewer = accountId,
                    HistoryWindow = TimeSpan.FromDays(MarketLimits.HISTORY_DAYS),
                    Take = MAX_MINE_ROWS
                }, cancellationToken: cancellationToken));
            List<ListingRow> rows = (await grid.ReadAsync<ListingRow>()).ToList();
            long available = await grid.ReadSingleAsync<long>();
            int unseen = await grid.ReadSingleAsync<int>();
            long now = await grid.ReadSingleAsync<long>();
            return (EGrowthAccess.Allowed, new MarketMineResponse(rows.Select(ToView).ToArray(), available, unseen, now));
        }

        /// <summary> 내 구매와 판매 기록 한 페이지(최근 순서, 최근 30일). 구매와 판매를 각각 인덱스로 읽어 합친다 </summary>
        public async Task<(EGrowthAccess Access, MarketHistoryResponse? Body)> HistoryAsync(Guid townInstanceId,
            Guid characterId, int page, CancellationToken cancellationToken = default)
        {
            int clamped = Math.Clamp(page, 0, MAX_PAGE);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            (EGrowthAccess access, Guid accountId) = await ResolveAccessAsync(connection, townInstanceId, characterId,
                cancellationToken);
            if (access != EGrowthAccess.Allowed)
            {
                return (access, null);
            }

            // 한 줄을 더 읽어 다음 페이지 유무를 안다. 양쪽 인덱스에서 앞쪽 (offset + 페이지 + 1)줄만 가져온다
            int offset = clamped * MarketLimits.PAGE_SIZE;
            int take = offset + MarketLimits.PAGE_SIZE + 1;
            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                SELECT merged.trade_id AS "TradeId", merged.listing_id AS "ListingId", merged.role AS "Role",
                    merged.definition_id AS "DefinitionId", merged.quantity AS "Quantity", merged.unit_price AS "UnitPrice",
                    merged.gross AS "Gross", merged.fee AS "Fee", merged.net AS "Net",
                    (extract(epoch FROM merged.created_at) * 1000)::bigint AS "CreatedAtUnixMs"
                FROM (
                    (SELECT trade_id, listing_id, 'Buyer' AS role, definition_id, quantity, unit_price, gross, fee, net,
                        created_at FROM market_trades
                    WHERE buyer_account_id = @Viewer AND created_at > clock_timestamp() - @HistoryWindow
                    ORDER BY created_at DESC, trade_id DESC LIMIT @Take)
                    UNION ALL
                    (SELECT trade_id, listing_id, 'Seller' AS role, definition_id, quantity, unit_price, gross, fee, net,
                        created_at FROM market_trades
                    WHERE seller_account_id = @Viewer AND created_at > clock_timestamp() - @HistoryWindow
                    ORDER BY created_at DESC, trade_id DESC LIMIT @Take)
                ) AS merged
                ORDER BY merged.created_at DESC, merged.trade_id DESC
                OFFSET @Offset LIMIT @PageRows;
                """ + "SELECT " + SERVER_TIME_SQL + ";", new
                {
                    Viewer = accountId,
                    HistoryWindow = TimeSpan.FromDays(MarketLimits.HISTORY_DAYS),
                    Take = take,
                    Offset = offset,
                    PageRows = MarketLimits.PAGE_SIZE + 1
                }, cancellationToken: cancellationToken));
            List<TradeViewRow> rows = (await grid.ReadAsync<TradeViewRow>()).ToList();
            long now = await grid.ReadSingleAsync<long>();
            bool hasMore = rows.Count > MarketLimits.PAGE_SIZE;
            MarketTradeView[] views = rows.Take(MarketLimits.PAGE_SIZE).Select(row => new MarketTradeView(
                row.TradeId.ToString("D"), row.ListingId.ToString("D"), row.Role, row.DefinitionId, row.Quantity,
                row.UnitPrice, row.Gross, row.Fee, row.Net, row.CreatedAtUnixMs)).ToArray();
            return (EGrowthAccess.Allowed, new MarketHistoryResponse(views, clamped, hasMore, now));
        }

        // 정렬 문장은 요청 값을 이어 붙이지 않고 고정 문자열 중에서 고른다
        private static string OrderBy(int sort, int order)
        {
            bool descending = order == MarketCodes.Order.DESCENDING;
            switch (sort)
            {
                case MarketCodes.Sort.NEWEST:
                    return descending ? "grouped.newest DESC, grouped.definition_id" : "grouped.newest, grouped.definition_id";
                case MarketCodes.Sort.QUANTITY:
                    return descending
                        ? "grouped.total_qty DESC, grouped.definition_id"
                        : "grouped.total_qty, grouped.definition_id";
                default:
                    return descending
                        ? "grouped.lowest DESC, grouped.definition_id"
                        : "grouped.lowest, grouped.definition_id";
            }
        }

        private static async Task<(EGrowthAccess Access, Guid AccountId)> ResolveAccessAsync(NpgsqlConnection connection,
            Guid townInstanceId, Guid characterId, CancellationToken cancellationToken)
        {
            AccessRow? row = await connection.QuerySingleOrDefaultAsync<AccessRow>(new CommandDefinition(
                """
                SELECT character.account_id AS "AccountId",
                    EXISTS (SELECT 1 FROM account_leases AS lease
                        WHERE lease.account_id = character.account_id AND lease.instance_id = @Town
                            AND lease.role = @TownRole AND lease.expires_at > clock_timestamp()) AS "InTown"
                FROM characters AS character WHERE character.character_id = @CharacterId AND character.deleted_at IS NULL;
                """, new { CharacterId = characterId, Town = townInstanceId, TownRole = ContractCodes.ServerRole.TOWN },
                cancellationToken: cancellationToken));
            if (row == null)
            {
                return (EGrowthAccess.NotFound, Guid.Empty);
            }

            return (row.InTown ? EGrowthAccess.Allowed : EGrowthAccess.WrongLocation, row.AccountId);
        }

        private static MarketListingView ToView(ListingRow row)
        {
            return new MarketListingView(row.ListingId.ToString("D"), row.AssetKind, row.DefinitionId,
                row.ItemId?.ToString("D") ?? string.Empty, row.Grade, row.ClassId, row.Category, row.UnitPrice,
                row.ListedQty, row.RemainingQty, row.State, row.GrossTotal, row.FeeTotal, row.NetTotal, row.CreatedAtUnixMs,
                row.ExpiresAtUnixMs, row.ClosedAtUnixMs, row.Seen, row.Mine);
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class AccessRow
        {
            public Guid AccountId { get; set; }
            public bool InTown    { get; set; }
        }

        private sealed class GroupRow
        {
            public string DefinitionId { get; set; } = string.Empty;
            public int    Grade        { get; set; }
            public long   Lowest       { get; set; }
            public long   TotalQty     { get; set; }
            public int    ListingCount { get; set; }
            public long   Recent       { get; set; }
            public int    Total        { get; set; }
        }

        private sealed class ListingRow
        {
            public Guid   ListingId       { get; set; }
            public string AssetKind       { get; set; } = string.Empty;
            public string DefinitionId    { get; set; } = string.Empty;
            public Guid?  ItemId          { get; set; }
            public int    Grade           { get; set; }
            public int?   ClassId         { get; set; }
            public int    Category        { get; set; }
            public long   UnitPrice       { get; set; }
            public int    ListedQty       { get; set; }
            public int    RemainingQty    { get; set; }
            public string State           { get; set; } = string.Empty;
            public long   GrossTotal      { get; set; }
            public long   FeeTotal        { get; set; }
            public long   NetTotal        { get; set; }
            public long   CreatedAtUnixMs { get; set; }
            public long   ExpiresAtUnixMs { get; set; }
            public long   ClosedAtUnixMs  { get; set; }
            public bool   Seen            { get; set; }
            public bool   Mine            { get; set; }
        }

        private sealed class TradeViewRow
        {
            public Guid   TradeId         { get; set; }
            public Guid   ListingId       { get; set; }
            public string Role            { get; set; } = string.Empty;
            public string DefinitionId    { get; set; } = string.Empty;
            public int    Quantity        { get; set; }
            public long   UnitPrice       { get; set; }
            public long   Gross           { get; set; }
            public long   Fee             { get; set; }
            public long   Net             { get; set; }
            public long   CreatedAtUnixMs { get; set; }
        }
    }
}
