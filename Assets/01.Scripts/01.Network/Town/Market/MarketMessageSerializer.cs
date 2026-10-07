// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 유저 경매장 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class MarketMessageSerializer
    {
        // 메시지는 readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 순서를 여기서 고정한다.
        // 한 메시지는 16KB 이하여야 해서 목록 상한을 둔다. 검색 묶음과 등록 상세와 기록은 페이지 크기(20)면 충분하고,
        // 내 등록 목록은 활성 10건에 최근 종료분을 더해 MAX_MINE_LISTINGS로 자른다(마을 서버가 최신순으로 자른다)

        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_GROUPS        = 64;
        public const int MAX_LISTINGS      = 64;
        public const int MAX_MINE_LISTINGS = 48;
        public const int MAX_TRADES        = 64;
        public const int MAX_STRINGS       = 64;

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteMarketRequestMessage(this NetworkWriter writer, MarketRequestMessage value)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte((byte)value.Action);
            writer.WriteInt(value.Category);
            writer.WriteInt(value.ClassId);
            writer.WriteInt(value.Grade);
            WriteStrings(writer, value.DefinitionIds);
            writer.WriteInt(value.Sort);
            writer.WriteInt(value.Order);
            writer.WriteInt(value.Page);
            writer.WriteString(value.DefinitionId);
            writer.WriteString(value.ListingId);
            writer.WriteString(value.AssetKind);
            writer.WriteString(value.ItemId);
            writer.WriteInt(value.Quantity);
            writer.WriteLong(value.UnitPrice);
            WriteStrings(writer, value.ListingIds);
        }

        public static MarketRequestMessage ReadMarketRequestMessage(this NetworkReader reader)
        {
            Guid          requestId     = reader.ReadGuid();
            var           action        = (EMarketAction)reader.ReadByte();
            int           category      = reader.ReadInt();
            int           classId       = reader.ReadInt();
            int           grade         = reader.ReadInt();
            string[]      definitionIds = ReadStrings(reader, MAX_STRINGS);
            int           sort          = reader.ReadInt();
            int           order         = reader.ReadInt();
            int           page          = reader.ReadInt();
            string        definitionId  = reader.ReadString();
            string        listingId     = reader.ReadString();
            string        assetKind     = reader.ReadString();
            string        itemId        = reader.ReadString();
            int           quantity      = reader.ReadInt();
            long          unitPrice     = reader.ReadLong();
            string[]      listingIds    = ReadStrings(reader, MAX_STRINGS);
            return new MarketRequestMessage(requestId, action, category, classId, grade, definitionIds, sort, order, page, definitionId,
                listingId, assetKind, itemId, quantity, unitPrice, listingIds);
        }

        public static void WriteMarketResultMessage(this NetworkWriter writer, MarketResultMessage value)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteByte((byte)value.Action);
            writer.WriteVarUInt((uint)value.Reason);

            MarketGroupEntry[] groups = value.Groups;
            writer.WriteInt(groups.Length);
            for (int i = 0; i < groups.Length; ++i)
            {
                writer.WriteString(groups[i].DefinitionId);
                writer.WriteInt(groups[i].Grade);
                writer.WriteLong(groups[i].LowestUnitPrice);
                writer.WriteLong(groups[i].TotalQty);
                writer.WriteInt(groups[i].ListingCount);
                writer.WriteLong(groups[i].RecentUnitPrice);
            }

            MarketListingEntry[] listings = value.Listings;
            writer.WriteInt(listings.Length);
            for (int i = 0; i < listings.Length; ++i)
            {
                ref readonly MarketListingEntry entry = ref listings[i];
                writer.WriteString(entry.ListingId);
                writer.WriteString(entry.AssetKind);
                writer.WriteString(entry.DefinitionId);
                writer.WriteString(entry.ItemId);
                writer.WriteInt(entry.Grade);
                writer.WriteLong(entry.UnitPrice);
                writer.WriteInt(entry.ListedQty);
                writer.WriteInt(entry.RemainingQty);
                writer.WriteString(entry.State);
                writer.WriteLong(entry.GrossTotal);
                writer.WriteLong(entry.FeeTotal);
                writer.WriteLong(entry.NetTotal);
                writer.WriteLong(entry.CreatedAtUnixMs);
                writer.WriteLong(entry.ExpiresAtUnixMs);
                writer.WriteLong(entry.ClosedAtUnixMs);
                writer.WriteBool(entry.Seen);
                writer.WriteBool(entry.Mine);
            }

            MarketTradeEntry[] trades = value.Trades;
            writer.WriteInt(trades.Length);
            for (int i = 0; i < trades.Length; ++i)
            {
                ref readonly MarketTradeEntry entry = ref trades[i];
                writer.WriteString(entry.TradeId);
                writer.WriteString(entry.Role);
                writer.WriteString(entry.DefinitionId);
                writer.WriteInt(entry.Quantity);
                writer.WriteLong(entry.UnitPrice);
                writer.WriteLong(entry.Gross);
                writer.WriteLong(entry.Fee);
                writer.WriteLong(entry.Net);
                writer.WriteLong(entry.CreatedAtUnixMs);
            }

            writer.WriteInt(value.TotalCount);
            writer.WriteInt(value.Page);
            writer.WriteBool(value.HasMore);
            writer.WriteLong(value.AverageUnitPrice24h);
            writer.WriteLong(value.AvailableYeopjeon);
            writer.WriteInt(value.UnseenCount);
            writer.WriteLong(value.ServerTimeUnixMs);
            writer.WriteString(value.ListingId);
            writer.WriteString(value.TradeId);
            writer.WriteLong(value.Gross);
            writer.WriteLong(value.Fee);
            writer.WriteLong(value.Net);
        }

        public static MarketResultMessage ReadMarketResultMessage(this NetworkReader reader)
        {
            Guid          requestId = reader.ReadGuid();
            var           action    = (EMarketAction)reader.ReadByte();
            var           reason    = (ERejectReason)reader.ReadVarUInt();

            int groupCount = PartyMessageSerializer.ReadCount(reader, MAX_GROUPS);
            var groups     = new MarketGroupEntry[groupCount];
            for (int i = 0; i < groupCount; ++i)
            {
                string definitionId    = reader.ReadString();
                int    grade           = reader.ReadInt();
                long   lowestUnitPrice = reader.ReadLong();
                long   totalQty        = reader.ReadLong();
                int    listingCount    = reader.ReadInt();
                long   recentUnitPrice = reader.ReadLong();
                groups[i] = new MarketGroupEntry(definitionId, grade, lowestUnitPrice, totalQty, listingCount, recentUnitPrice);
            }

            int listingTotal = PartyMessageSerializer.ReadCount(reader, MAX_LISTINGS);
            var listings     = new MarketListingEntry[listingTotal];
            for (int i = 0; i < listingTotal; ++i)
            {
                string listingId    = reader.ReadString();
                string assetKind    = reader.ReadString();
                string definitionId = reader.ReadString();
                string itemId       = reader.ReadString();
                int    grade        = reader.ReadInt();
                long   unitPrice    = reader.ReadLong();
                int    listedQty    = reader.ReadInt();
                int    remainingQty = reader.ReadInt();
                string state        = reader.ReadString();
                long   grossTotal   = reader.ReadLong();
                long   feeTotal     = reader.ReadLong();
                long   netTotal     = reader.ReadLong();
                long   createdAt    = reader.ReadLong();
                long   expiresAt    = reader.ReadLong();
                long   closedAt     = reader.ReadLong();
                bool   seen         = reader.ReadBool();
                bool   mine         = reader.ReadBool();
                listings[i] = new MarketListingEntry(listingId, assetKind, definitionId, itemId, grade, unitPrice, listedQty,
                    remainingQty, state, grossTotal, feeTotal, netTotal, createdAt, expiresAt, closedAt, seen, mine);
            }

            int tradeCount = PartyMessageSerializer.ReadCount(reader, MAX_TRADES);
            var trades     = new MarketTradeEntry[tradeCount];
            for (int i = 0; i < tradeCount; ++i)
            {
                string tradeId      = reader.ReadString();
                string role         = reader.ReadString();
                string definitionId = reader.ReadString();
                int    quantity     = reader.ReadInt();
                long   unitPrice    = reader.ReadLong();
                long   gross        = reader.ReadLong();
                long   fee          = reader.ReadLong();
                long   net          = reader.ReadLong();
                long   createdAt    = reader.ReadLong();
                trades[i] = new MarketTradeEntry(tradeId, role, definitionId, quantity, unitPrice, gross, fee, net, createdAt);
            }

            int    totalCount          = reader.ReadInt();
            int    page                = reader.ReadInt();
            bool   hasMore             = reader.ReadBool();
            long   averageUnitPrice24h = reader.ReadLong();
            long   availableYeopjeon   = reader.ReadLong();
            int    unseenCount         = reader.ReadInt();
            long   serverTimeUnixMs    = reader.ReadLong();
            string resultListingId     = reader.ReadString();
            string resultTradeId       = reader.ReadString();
            long   resultGross         = reader.ReadLong();
            long   resultFee           = reader.ReadLong();
            long   resultNet           = reader.ReadLong();
            return new MarketResultMessage(requestId, action, reason, groups, listings, trades, totalCount, page, hasMore,
                averageUnitPrice24h, availableYeopjeon, unseenCount, serverTimeUnixMs, resultListingId, resultTradeId, resultGross,
                resultFee, resultNet);
        }

        private static void WriteStrings(NetworkWriter writer, string[] values)
        {
            int count = (values != null) ? values.Length : 0;
            writer.WriteInt(count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteString(values[i]);
            }
        }

        private static string[] ReadStrings(NetworkReader reader, int max)
        {
            int count  = PartyMessageSerializer.ReadCount(reader, max);
            var values = new string[count];
            for (int i = 0; i < count; ++i)
            {
                values[i] = reader.ReadString();
            }

            return values;
        }
    }
}
