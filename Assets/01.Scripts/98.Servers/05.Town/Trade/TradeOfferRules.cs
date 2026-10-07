// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 거래 제안의 형식 검사와 마지막 스냅숏 기준 사전 검사, 순수 계산 </summary>
    public static class TradeOfferRules
    {
        // 사전 검사는 빠르게 알려 주기 위한 것이다. 보관하지 않으므로 실행 때 API가 잠근 행으로 다시 판정한다.
        // 수량은 스냅숏이 없으면 통과시키고(실행 API가 막는다), 장비는 소유를 확인할 수 없으므로 거절한다

        /****************************************
        *                Fields
        ****************************************/

        // 한 품목 ID 길이 상한, API 검증과 같다
        private const int MAX_ID_LENGTH = 64;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 칸 수, 수량, 중복, 거래 가능 여부를 본다. 스냅숏은 보지 않는다 </summary>
        public static ERejectReason ValidateFormat(TradeOfferLine[] lines, long currency)
        {
            if ((currency < 0) || (currency > TradeSettings.MAX_CURRENCY))
            {
                return ERejectReason.InvalidTarget;
            }

            if (lines == null)
            {
                return ERejectReason.None;
            }

            if (lines.Length > TradeCodes.TRADE_MAX_ITEMS)
            {
                return ERejectReason.InvalidTarget;
            }

            for (int i = 0; i < lines.Length; ++i)
            {
                TradeOfferLine line = lines[i];
                if ((string.IsNullOrEmpty(line.DefinitionId)) || (line.DefinitionId.Length > MAX_ID_LENGTH) || (line.Quantity < 1))
                {
                    return ERejectReason.InvalidTarget;
                }

                if (line.IsEquipment)
                {
                    if ((line.Quantity != 1) || (!Guid.TryParse(line.ItemId, out _)))
                    {
                        return ERejectReason.InvalidTarget;
                    }
                }
                else
                {
                    if (line.Quantity > MarketLimits.MAX_QUANTITY)
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    if (!MarketTradeRules.IsTradableAccountItem(line.DefinitionId))
                    {
                        return ERejectReason.TradeNotTradable;
                    }
                }

                for (int j = 0; j < i; ++j)
                {
                    if (IsSameItem(in lines[j], in line))
                    {
                        return ERejectReason.InvalidTarget;
                    }
                }
            }

            return ERejectReason.None;
        }

        /// <summary> 마지막 스냅숏으로 보유량, 장비 상태, 엽전을 본다. 스냅숏이 없으면 그 부분은 통과 </summary>
        public static ERejectReason PreCheck(TradeOfferLine[] lines, long currency, bool hasGrowth, in GrowthSnapshotMessage growth,
            bool hasLife, in LifeSnapshotMessage life, string currencyItemId)
        {
            ERejectReason format = ValidateFormat(lines, currency);
            if (format != ERejectReason.None)
            {
                return format;
            }

            TradeOfferLine[] source = lines ?? Array.Empty<TradeOfferLine>();
            for (int i = 0; i < source.Length; ++i)
            {
                TradeOfferLine line = source[i];
                ERejectReason reason = line.IsEquipment
                    ? CheckEquipment(in line, hasGrowth, in growth)
                    : CheckQuantity(in line, hasGrowth, in growth, hasLife, in life);
                if (reason != ERejectReason.None)
                {
                    return reason;
                }
            }

            if ((currency > 0) && (hasGrowth) && (!string.IsNullOrEmpty(currencyItemId))
                && (AvailableOf(growth.AccountItems, currencyItemId) < currency))
            {
                return ERejectReason.TradeInsufficientCurrency;
            }

            return ERejectReason.None;
        }

        private static ERejectReason CheckEquipment(in TradeOfferLine line, bool hasGrowth, in GrowthSnapshotMessage growth)
        {
            if ((string.IsNullOrEmpty(line.DefinitionId)) || (line.DefinitionId.Length > MAX_ID_LENGTH))
            {
                return ERejectReason.InvalidTarget;
            }

            // 스냅숏이 없으면 장비 소유를 확인할 수 없으므로 올리지 못하게 한다
            if (!hasGrowth)
            {
                return ERejectReason.TradeItemChanged;
            }

            TownEquipmentEntry[] equipment = growth.Equipment;
            for (int i = 0; i < equipment.Length; ++i)
            {
                TownEquipmentEntry entry = equipment[i];
                if (!string.Equals(entry.ItemId, line.ItemId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(entry.DefinitionId, line.DefinitionId, StringComparison.Ordinal))
                {
                    return ERejectReason.TradeItemChanged;
                }

                bool tradable = MarketTradeRules.IsTradableEquipment(entry.Grade, entry.Enhancement, entry.Equipped, entry.Bound,
                    entry.Locked, entry.IsTraining, entry.Origin, entry.InMarket);
                return tradable ? ERejectReason.None : ERejectReason.TradeNotTradable;
            }

            return ERejectReason.TradeItemChanged;
        }

        private static ERejectReason CheckQuantity(in TradeOfferLine line, bool hasGrowth, in GrowthSnapshotMessage growth, bool hasLife,
            in LifeSnapshotMessage life)
        {
            long available;
            if (line.DefinitionId == MarketTradeRules.HERB_ID)
            {
                if (!hasLife)
                {
                    return ERejectReason.None;
                }

                available = (life.HerbId == line.DefinitionId) ? life.HerbQuantity : 0;
            }
            else
            {
                if (!hasGrowth)
                {
                    return ERejectReason.None;
                }

                available = AvailableOf(growth.AccountItems, line.DefinitionId);
            }

            return (available >= line.Quantity) ? ERejectReason.None : ERejectReason.TradeItemChanged;
        }

        private static long AvailableOf(TownItemEntry[] items, string definitionId)
        {
            long total = 0;
            for (int i = 0; i < items.Length; ++i)
            {
                if (string.Equals(items[i].DefinitionId, definitionId, StringComparison.Ordinal))
                {
                    total += Math.Max(0L, items[i].Quantity - items[i].Reserved);
                }
            }

            return total;
        }

        private static bool IsSameItem(in TradeOfferLine first, in TradeOfferLine second)
        {
            if (first.IsEquipment != second.IsEquipment)
            {
                return false;
            }

            return first.IsEquipment
                ? string.Equals(first.ItemId, second.ItemId, StringComparison.OrdinalIgnoreCase)
                : string.Equals(first.DefinitionId, second.DefinitionId, StringComparison.Ordinal);
        }
    }
}
