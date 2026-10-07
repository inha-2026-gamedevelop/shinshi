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
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Market
{
    public sealed partial class PostgresMarketRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BUY_SCOPE     = "buy";
        private const string SELL_SCOPE    = "sell";
        private const string RECEIVE_SCOPE = "receive";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구매. 등록 한 건에서 수량을 정한다. 구매자 엽전 차감, 판매자 대금 지급(수수료 소각), 구매자 지급, 등록 수량 감소,
        /// 성사 기록이 한 트랜잭션이다. 어느 검사든 실패하면 아무것도 적용하지 않는다 </summary>
        public async Task<MarketOperationOutcome> BuyAsync(Guid townInstanceId, Guid characterId, Guid operationId,
            Guid listingId, int quantity, long expectedUnitPrice, bool replayOnly,
            CancellationToken cancellationToken = default)
        {
            OperationResult? result = await RunPlayerAsync(characterId, operationId, MarketOperationKinds.BUY, new
            {
                listingId = listingId.ToString("D"),
                quantity,
                expectedUnitPrice
            }, replayOnly, (accountId, context, token) => BuyEffectAsync(townInstanceId, accountId, characterId,
                operationId, listingId, quantity, expectedUnitPrice, context, token), cancellationToken);
            return await BuildOutcomeAsync(MarketOperationKinds.BUY, result, cancellationToken);
        }

        private async Task<OperationDecision> BuyEffectAsync(Guid townInstanceId, Guid buyerAccountId, Guid buyerCharacterId,
            Guid operationId, Guid listingId, int quantity, long expectedUnitPrice, OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            if (!MarketTradeRules.IsQuantityInRange(quantity) || !MarketTradeRules.IsUnitPriceInRange(expectedUnitPrice))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_PRICE_OUT_OF_RANGE);
            }

            // 잠금 순서의 첫 단계: 등록 행. 같은 등록을 건드리는 구매, 취소, 만료는 여기서 직렬화된다
            MarketListingRow? listing = await LockListingAsync(connection, transaction, listingId, false, cancellationToken);
            if (listing == null)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (listing.State != MarketCodes.ListingState.ACTIVE)
            {
                return OperationDecision.Rejected(ClosedReason(listing.State), listing.Revision);
            }
            if (await IsExpiredAsync(connection, transaction, listingId, cancellationToken))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_EXPIRED, listing.Revision);
            }
            if (listing.SellerAccountId == buyerAccountId)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_SELF_PURCHASE, listing.Revision);
            }
            if (listing.UnitPrice != expectedUnitPrice)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_PRICE_CHANGED, listing.Revision);
            }
            if (quantity > listing.RemainingQty)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_QUANTITY_CHANGED, listing.Revision);
            }
            if (!MarketFeeRules.TryGross(listing.UnitPrice, quantity, out long gross))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_PRICE_OUT_OF_RANGE, listing.Revision);
            }

            long fee = MarketFeeRules.Fee(gross);
            long net = gross - fee;
            await LockAccountsAsync(connection, transaction, new[] { listing.SellerAccountId, buyerAccountId },
                cancellationToken);
            int access = await LockCharacterAsync(connection, transaction, buyerAccountId, buyerCharacterId,
                townInstanceId, cancellationToken);
            if (access != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(access, listing.Revision);
            }

            // 구매자 엽전은 가용량(총량 - 경매 입찰 예약)에서 정확히 빼고, 검사와 차감은 한 문장이다
            EconomyMover mover = CreateMover(context, MarketOperationKinds.ACTOR_PLAYER, buyerAccountId, operationId,
                listingId);
            if (!await mover.TryDebitAsync(EconomyAsset.Currency(buyerAccountId), gross,
                ContractCodes.EconomyReason.MARKET_PURCHASE, BUY_SCOPE, cancellationToken))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_CURRENCY, listing.Revision);
            }

            // 수수료는 소각한다. 판매자는 미접속이어도 대금을 바로 받는다(엽전 상한 없음). 순수령 0이면 줄을 만들지 않는다
            await mover.CreditAsync(EconomyAsset.Currency(listing.SellerAccountId), net,
                ContractCodes.EconomyReason.MARKET_PROCEEDS, SELL_SCOPE, cancellationToken);

            int received = await ReceiveAsync(context, connection, transaction, mover, listing, buyerAccountId,
                buyerCharacterId, operationId, quantity, cancellationToken);
            if (received != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(received, listing.Revision);
            }

            Guid tradeId = Guid.NewGuid();
            ListingTotals totals = await connection.QuerySingleAsync<ListingTotals>(new CommandDefinition(
                """
                UPDATE market_listings
                SET remaining_qty = remaining_qty - @Quantity,
                    state = CASE WHEN remaining_qty - @Quantity = 0 THEN 'SoldOut' ELSE state END,
                    closed_at = CASE WHEN remaining_qty - @Quantity = 0 THEN clock_timestamp() ELSE closed_at END,
                    gross_total = gross_total + @Gross, fee_total = fee_total + @Fee, net_total = net_total + @Net,
                    revision = revision + 1
                WHERE listing_id = @ListingId AND state = 'Active' AND remaining_qty >= @Quantity
                RETURNING revision AS "Revision";
                """, new { ListingId = listingId, Quantity = quantity, Gross = gross, Fee = fee, Net = net }, transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO market_trades (trade_id, listing_id, buyer_account_id, buyer_character_id, seller_account_id,
                    definition_id, quantity, unit_price, gross, fee, net, actor_id, operation_id, created_at)
                VALUES (@TradeId, @ListingId, @BuyerAccountId, @BuyerCharacterId, @SellerAccountId, @DefinitionId,
                    @Quantity, @UnitPrice, @Gross, @Fee, @Net, @BuyerAccountId, @OperationId, clock_timestamp());
                """, new
                {
                    TradeId = tradeId,
                    ListingId = listingId,
                    BuyerAccountId = buyerAccountId,
                    BuyerCharacterId = buyerCharacterId,
                    listing.SellerAccountId,
                    listing.DefinitionId,
                    Quantity = quantity,
                    listing.UnitPrice,
                    Gross = gross,
                    Fee = fee,
                    Net = net,
                    OperationId = operationId
                }, transaction, cancellationToken: cancellationToken));
            return OperationDecision.Committed(totals.Revision, tradeId.ToString("D"));
        }

        // 구매자 지급. 수량형은 구매자 보관 상한, 장비는 구매 캐릭터의 보관 칸을 넘으면 INVENTORY_FULL로 거절한다(회수함으로 보내지 않는다)
        private async Task<int> ReceiveAsync(OperationExecutionContext context, DbConnection connection,
            DbTransaction transaction, EconomyMover mover, MarketListingRow listing, Guid buyerAccountId,
            Guid buyerCharacterId, Guid operationId, int quantity, CancellationToken cancellationToken)
        {
            if (listing.AssetKind == MarketCodes.ASSET_EQUIPMENT)
            {
                Guid itemId = listing.ItemId ?? throw new InvalidOperationException("Equipment listing has no item.");
                Dictionary<Guid, EquipmentRecord> items = await EquipmentRows.LockAsync(connection, transaction,
                    new[] { itemId }, cancellationToken);
                if (!items.TryGetValue(itemId, out EquipmentRecord? item) || (item.EscrowRef != listing.ListingId))
                {
                    throw new InvalidOperationException("Escrowed equipment does not match its listing.");
                }
                if (await EquipmentRows.CountStoredAsync(connection, transaction, buyerCharacterId, cancellationToken)
                    >= _content.Equipment.StorageSlotsPerCharacter)
                {
                    return ContractCodes.RejectReason.INVENTORY_FULL;
                }

                EquipmentRecord after = item.Clone();
                after.AccountId   = buyerAccountId;
                after.CharacterId = buyerCharacterId;
                after.EscrowKind  = null;
                after.EscrowRef   = null;
                await EquipmentRows.UpdateOwnershipAsync(connection, transaction, after, cancellationToken);
                await EquipmentRows.AppendHistoryAsync(connection, transaction, context, _history,
                    GrowthAssets.HISTORY_MARKET_SOLD, item, after, buyerAccountId, buyerAccountId, operationId,
                    cancellationToken);
                return ContractCodes.RejectReason.NONE;
            }

            EconomyAsset asset = EconomyAsset.AccountItem(buyerAccountId, listing.DefinitionId, GrowthAssets.ACCOUNT_BINDING);
            long? held = await RecoveryInbox.ReadBalanceForUpdateAsync(connection, transaction, asset, cancellationToken);
            long limit = RewardStorageLimits.LimitFor(EconomyAsset.ACCOUNT_ITEM, listing.DefinitionId) ?? long.MaxValue;
            if ((held ?? 0) + quantity > limit)
            {
                return ContractCodes.RejectReason.INVENTORY_FULL;
            }

            await mover.CreditAsync(asset, quantity, ContractCodes.EconomyReason.MARKET_RECEIVE, RECEIVE_SCOPE,
                cancellationToken);
            return ContractCodes.RejectReason.NONE;
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class ListingTotals
        {
            public long Revision { get; set; }
        }
    }
}
