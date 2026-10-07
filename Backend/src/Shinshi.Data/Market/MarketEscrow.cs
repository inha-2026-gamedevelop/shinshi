// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Market
{
    /// <summary> 닫히는 등록(취소, 만료)의 미판매분 반환. 호출자가 등록 행과 판매자 계정을 이미 잠갔다.
    /// 수량형은 보관 상한까지 판매자 account_items로 돌려주고 넘친 몫은 MarketReturn 회수함으로 보낸다(30일, 부분 수령).
    /// 장비는 판매자 캐릭터 행에 그대로 있으므로 보관 표시만 푼다(보관 칸을 계속 차지했으므로 항상 들어간다) </summary>
    internal static class MarketEscrow
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string RETURN_SCOPE = "return";

        /****************************************
        *                Methods
        ****************************************/

        public static async Task ReturnUnsoldAsync(OperationExecutionContext context, DbConnection connection,
            DbTransaction transaction, EconomyMover mover, PostgresItemHistoryWriter history, MarketListingRow listing,
            Guid actorId, Guid operationId, CancellationToken cancellationToken)
        {
            if (listing.AssetKind == MarketCodes.ASSET_EQUIPMENT)
            {
                await ReleaseEquipmentAsync(context, connection, transaction, history, listing, actorId, operationId,
                    cancellationToken);
                return;
            }

            long remaining = listing.RemainingQty;
            if (remaining <= 0)
            {
                return;
            }

            EconomyAsset asset = EconomyAsset.AccountItem(listing.SellerAccountId, listing.DefinitionId,
                GrowthAssets.ACCOUNT_BINDING);
            long? held = await RecoveryInbox.ReadBalanceForUpdateAsync(connection, transaction, asset, cancellationToken);
            long fits = RewardStorageLimits.Fits(RewardStorageLimits.LimitFor(EconomyAsset.ACCOUNT_ITEM,
                listing.DefinitionId), held ?? 0, remaining);
            await mover.CreditAsync(asset, fits, ContractCodes.EconomyReason.MARKET_RETURN, RETURN_SCOPE, cancellationToken);

            long overflow = remaining - fits;
            if (overflow > 0)
            {
                // 같은 등록의 반환은 한 번만 일어나지만 회수함 유일 인덱스(source_kind, source_ref, 계정)가 마지막 방어선이다
                await RecoveryInbox.InsertAsync(connection, transaction, RecoveryInbox.SOURCE_MARKET_RETURN,
                    listing.ListingId.ToString("D"), new List<RecoveryLine>
                    {
                        new RecoveryLine(listing.SellerAccountId, null, EconomyAsset.ACCOUNT_ITEM, listing.DefinitionId,
                            GrowthAssets.ACCOUNT_BINDING, overflow)
                    }, cancellationToken);
            }
        }

        private static async Task ReleaseEquipmentAsync(OperationExecutionContext context, DbConnection connection,
            DbTransaction transaction, PostgresItemHistoryWriter history, MarketListingRow listing, Guid actorId,
            Guid operationId, CancellationToken cancellationToken)
        {
            Guid itemId = listing.ItemId ?? throw new InvalidOperationException("Equipment listing has no item.");
            Dictionary<Guid, EquipmentRecord> items = await EquipmentRows.LockAsync(connection, transaction,
                new[] { itemId }, cancellationToken);
            if (!items.TryGetValue(itemId, out EquipmentRecord? item) || (item.EscrowRef != listing.ListingId))
            {
                throw new InvalidOperationException("Escrowed equipment does not match its listing.");
            }

            EquipmentRecord after = item.Clone();
            after.EscrowKind = null;
            after.EscrowRef  = null;
            await EquipmentRows.UpdateOwnershipAsync(connection, transaction, after, cancellationToken);
            await EquipmentRows.AppendHistoryAsync(connection, transaction, context, history,
                GrowthAssets.HISTORY_MARKET_RETURNED, item, after, listing.SellerAccountId, actorId, operationId,
                cancellationToken);
        }
    }

    /// <summary> market_listings 한 행 </summary>
    internal sealed class MarketListingRow
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string COLUMNS =
            """
            listing_id AS "ListingId", seller_account_id AS "SellerAccountId", seller_character_id AS "SellerCharacterId",
            asset_kind AS "AssetKind", definition_id AS "DefinitionId", item_id AS "ItemId", grade::integer AS "Grade",
            unit_price AS "UnitPrice", listed_qty AS "ListedQty", remaining_qty AS "RemainingQty", state AS "State",
            revision AS "Revision"
            """;

        /****************************************
        *              Properties
        ****************************************/

        public Guid   ListingId         { get; set; }
        public Guid   SellerAccountId   { get; set; }
        public Guid   SellerCharacterId { get; set; }
        public string AssetKind         { get; set; } = string.Empty;
        public string DefinitionId      { get; set; } = string.Empty;
        public Guid?  ItemId            { get; set; }
        public int    Grade             { get; set; }
        public long   UnitPrice         { get; set; }
        public int    ListedQty         { get; set; }
        public int    RemainingQty      { get; set; }
        public string State             { get; set; } = string.Empty;
        public long   Revision          { get; set; }
    }
}
