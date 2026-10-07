// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 마지막 성장, 생활 스냅숏에서 거래할 수 있는 품목을 미리 걸러 목록으로 만든다 </summary>
    public static class SellableItemRules
    {
        // 표시용 사전 거름이다. 실제 판정은 등록, 거래 API가 잠근 행으로 다시 한다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 스냅숏이 없으면 그 부분은 건너뛴다 </summary>
        public static void Build(bool hasGrowth, in GrowthSnapshotMessage growth, bool hasLife, in LifeSnapshotMessage life,
            List<SellableItem> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            if (hasGrowth)
            {
                TownEquipmentEntry[] equipment = growth.Equipment;
                for (int i = 0; i < equipment.Length; ++i)
                {
                    TownEquipmentEntry entry = equipment[i];
                    if ((!string.IsNullOrEmpty(entry.ItemId)) && (IsTradableEquipment(in entry)))
                    {
                        into.Add(new SellableItem(entry.DefinitionId, entry.ItemId, 1, entry.Grade));
                    }
                }

                TownItemEntry[] items = growth.AccountItems;
                for (int i = 0; i < items.Length; ++i)
                {
                    long available = items[i].Quantity - items[i].Reserved;
                    if ((available > 0) && (MarketTradeRules.IsTalismanEffectId(items[i].DefinitionId)))
                    {
                        into.Add(new SellableItem(items[i].DefinitionId, null, Clamp(available), 0));
                    }
                }
            }

            if ((hasLife) && (life.HerbId == MarketTradeRules.HERB_ID) && (life.HerbQuantity > 0))
            {
                into.Add(new SellableItem(life.HerbId, null, Clamp(life.HerbQuantity), 0));
            }
        }

        public static bool IsTradableEquipment(in TownEquipmentEntry entry)
        {
            return MarketTradeRules.IsTradableEquipment(entry.Grade, entry.Enhancement, entry.Equipped, entry.Bound, entry.Locked,
                entry.IsTraining, entry.Origin, entry.InMarket);
        }

        private static int Clamp(long quantity)
        {
            return (quantity > int.MaxValue) ? int.MaxValue : (int)quantity;
        }
    }
}
