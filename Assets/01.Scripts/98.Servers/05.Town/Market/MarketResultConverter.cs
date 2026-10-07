// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> API 응답 DTO를 클라이언트 결과 메시지로 다시 만든다, API 응답을 그대로 넘기지 않는다 </summary>
    public static class MarketResultConverter
    {
        // 한 메시지는 16KB 이하여야 해서 목록을 계약 상한으로 자른다. 내 등록 목록은 활성 등록을 먼저, 나머지는 받은 순서(최신순)로 담는다

        /****************************************
        *                Methods
        ****************************************/

        public static MarketResultMessage FromSearch(Guid requestId, MarketSearchResponse response)
        {
            MarketGroupView[] source = response.Groups ?? Array.Empty<MarketGroupView>();
            int count = Math.Min(source.Length, MarketMessageSerializer.MAX_GROUPS);
            var groups = new MarketGroupEntry[count];
            for (int i = 0; i < count; ++i)
            {
                MarketGroupView view = source[i];
                groups[i] = new MarketGroupEntry(view.DefinitionId, view.Grade, view.LowestUnitPrice, view.TotalQty, view.ListingCount,
                    view.RecentUnitPrice);
            }

            return new MarketResultMessage(requestId, EMarketAction.Search, ERejectReason.None, groups, null, null, response.TotalCount,
                response.Page, false, 0, 0, 0, response.ServerTimeUnixMs, null, null, 0, 0, 0);
        }

        public static MarketResultMessage FromListings(Guid requestId, MarketListingsResponse response)
        {
            MarketListingEntry[] listings = ConvertListings(response.Listings, MarketMessageSerializer.MAX_LISTINGS, false);
            return new MarketResultMessage(requestId, EMarketAction.Listings, ERejectReason.None, null, listings, null,
                response.TotalCount, response.Page, false, response.AverageUnitPrice24h, 0, 0, response.ServerTimeUnixMs, null, null, 0,
                0, 0);
        }

        public static MarketResultMessage FromMine(Guid requestId, MarketMineResponse response)
        {
            MarketListingEntry[] listings = ConvertListings(response.Listings, MarketMessageSerializer.MAX_MINE_LISTINGS, true);
            return new MarketResultMessage(requestId, EMarketAction.Mine, ERejectReason.None, null, listings, null, listings.Length, 0,
                false, 0, response.AvailableYeopjeon, response.UnseenCount, response.ServerTimeUnixMs, null, null, 0, 0, 0);
        }

        public static MarketResultMessage FromHistory(Guid requestId, MarketHistoryResponse response)
        {
            MarketTradeView[] source = response.Trades ?? Array.Empty<MarketTradeView>();
            int count = Math.Min(source.Length, MarketMessageSerializer.MAX_TRADES);
            var trades = new MarketTradeEntry[count];
            for (int i = 0; i < count; ++i)
            {
                MarketTradeView view = source[i];
                trades[i] = new MarketTradeEntry(view.TradeId, view.Role, view.DefinitionId, view.Quantity, view.UnitPrice, view.Gross,
                    view.Fee, view.Net, view.CreatedAtUnixMs);
            }

            return new MarketResultMessage(requestId, EMarketAction.History, ERejectReason.None, null, null, trades, count, response.Page,
                response.HasMore, 0, 0, 0, response.ServerTimeUnixMs, null, null, 0, 0, 0);
        }

        /// <summary> 쓰기 확정, response가 null이어도 확정이면 빈 값으로 돌려준다(확인 표시) </summary>
        public static MarketResultMessage FromWrite(Guid requestId, EMarketAction action, MarketOperationResponse response)
        {
            return new MarketResultMessage(requestId, action, ERejectReason.None, null, null, null, 0, 0, false, 0, 0, 0,
                (response?.Result != null) ? response.Result.ServerTimeUnixMs : 0, response?.ListingId, response?.TradeId,
                response?.Gross ?? 0, response?.Fee ?? 0, response?.Net ?? 0);
        }

        private static MarketListingEntry[] ConvertListings(MarketListingView[] source, int max, bool activeFirst)
        {
            source = source ?? Array.Empty<MarketListingView>();
            var result = new List<MarketListingEntry>(Math.Min(source.Length, max));
            if (activeFirst)
            {
                for (int i = 0; (i < source.Length) && (result.Count < max); ++i)
                {
                    if ((source[i] != null) && (source[i].State == MarketCodes.ListingState.ACTIVE))
                    {
                        result.Add(Convert(source[i]));
                    }
                }

                for (int i = 0; (i < source.Length) && (result.Count < max); ++i)
                {
                    if ((source[i] != null) && (source[i].State != MarketCodes.ListingState.ACTIVE))
                    {
                        result.Add(Convert(source[i]));
                    }
                }

                return result.ToArray();
            }

            for (int i = 0; (i < source.Length) && (result.Count < max); ++i)
            {
                if (source[i] != null)
                {
                    result.Add(Convert(source[i]));
                }
            }

            return result.ToArray();
        }

        private static MarketListingEntry Convert(MarketListingView view)
        {
            return new MarketListingEntry(view.ListingId, view.AssetKind, view.DefinitionId, view.ItemId, view.Grade, view.UnitPrice,
                view.ListedQty, view.RemainingQty, view.State, view.GrossTotal, view.FeeTotal, view.NetTotal, view.CreatedAtUnixMs,
                view.ExpiresAtUnixMs, view.ClosedAtUnixMs, view.Seen, view.Mine);
        }
    }
}
