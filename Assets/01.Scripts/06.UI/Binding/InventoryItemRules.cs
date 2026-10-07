// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 마을 서버의 성장, 생활 스냅숏을 인벤토리 칸 목록으로 옮긴다. 재화는 칸에 넣지 않고 따로 돌려준다 </summary>
    // 표시 이름은 카탈로그의 DisplayKey가 번역 키이고 번역 표가 아직 없어 정의 ID를 쓴다
    public static class InventoryItemRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ENHANCE_FORMAT = "+{0} 강화";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 스냅숏이 없으면 그 부분을 건너뛴다. 카탈로그가 null이면 등급은 서버 값, 분류는 기타로 둔다 </summary>
        public static void Build(bool hasGrowth, in GrowthSnapshotMessage growth, bool hasLife, in LifeSnapshotMessage life,
            RewardCatalogSO rewards, EquipmentCatalogSO equipment, LifeConfigSO lifeConfig, List<InventoryItemData> into,
            out long yeopjeon, out long essence)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            yeopjeon = 0;
            essence  = 0;
            long order = 0;

            if (hasGrowth)
            {
                essence = growth.Essence;
                AddEquipment(growth.Equipment, equipment, into, ref order);
                if ((growth.Shards > 0) && (rewards != null))
                {
                    AddStack(into, ref order, rewards.RefineShardItemId, growth.Shards, EInventoryCategory.Material,
                        FindRewardGrade(rewards, rewards.RefineShardItemId));
                }

                TownItemEntry[] items = growth.AccountItems;
                for (int i = 0; i < items.Length; ++i)
                {
                    TownItemEntry item      = items[i];
                    long          available = item.Quantity - item.Reserved;
                    if ((rewards != null) && (item.DefinitionId == rewards.TradeCurrencyItemId))
                    {
                        yeopjeon += Math.Max(0L, available);
                        continue;
                    }

                    if ((rewards != null) && (item.DefinitionId == rewards.EssenceItemId))
                    {
                        continue;
                    }

                    AddStack(into, ref order, item.DefinitionId, available, Classify(item.DefinitionId, rewards, lifeConfig),
                        FindRewardGrade(rewards, item.DefinitionId));
                }
            }

            if (hasLife)
            {
                AddStack(into, ref order, life.HerbId, life.HerbQuantity, EInventoryCategory.Material, EItemGrade.Common);
                TownItemEntry[] potions = life.Potions;
                for (int i = 0; i < potions.Length; ++i)
                {
                    AddStack(into, ref order, potions[i].DefinitionId, potions[i].Quantity - potions[i].Reserved,
                        EInventoryCategory.Consumable, EItemGrade.Common);
                }
            }
        }

        /// <summary> 서버 등급 값이 정의에 없으면 Common </summary>
        public static EItemGrade ToGrade(byte grade)
        {
            return Enum.IsDefined(typeof(EItemGrade), (int)grade) ? (EItemGrade)grade : EItemGrade.Common;
        }

        public static EInventoryCategory Classify(string definitionId, RewardCatalogSO rewards, LifeConfigSO lifeConfig)
        {
            if (lifeConfig != null)
            {
                if (lifeConfig.TryGetPotion(definitionId, out _))
                {
                    return EInventoryCategory.Consumable;
                }

                if (lifeConfig.TryGetHerb(definitionId, out _))
                {
                    return EInventoryCategory.Material;
                }
            }

            if ((rewards == null) || (definitionId == rewards.BossTicketItemId))
            {
                return EInventoryCategory.Etc;
            }

            return rewards.ContainsItem(definitionId) ? EInventoryCategory.Material : EInventoryCategory.Etc;
        }

        private static void AddEquipment(TownEquipmentEntry[] entries, EquipmentCatalogSO catalog, List<InventoryItemData> into,
            ref long order)
        {
            for (int i = 0; i < entries.Length; ++i)
            {
                TownEquipmentEntry entry = entries[i];
                if (string.IsNullOrEmpty(entry.DefinitionId))
                {
                    continue;
                }

                EItemGrade grade = ToGrade(entry.Grade);
                if ((catalog != null)
                    && (catalog.TryGetEquipment(entry.DefinitionId, out EquipmentCatalogSO.EquipmentDefinition definition)))
                {
                    grade = definition.Grade;
                }

                string description = (entry.Enhancement > 0)
                    ? string.Format(CultureInfo.InvariantCulture, ENHANCE_FORMAT, entry.Enhancement)
                    : string.Empty;
                var slot = new ItemSlotData(entry.DefinitionId, grade, 1, entry.DefinitionId, description, entry.Locked,
                    entry.Equipped);
                into.Add(new InventoryItemData(slot, EInventoryCategory.Equipment, order++));
            }
        }

        private static void AddStack(List<InventoryItemData> into, ref long order, string definitionId, long quantity,
            EInventoryCategory category, EItemGrade grade)
        {
            if ((string.IsNullOrEmpty(definitionId)) || (quantity <= 0))
            {
                return;
            }

            int count = (quantity > int.MaxValue) ? int.MaxValue : (int)quantity;
            var slot  = new ItemSlotData(definitionId, grade, count, definitionId, string.Empty);
            into.Add(new InventoryItemData(slot, category, order++));
        }

        private static EItemGrade FindRewardGrade(RewardCatalogSO rewards, string definitionId)
        {
            if ((rewards != null) && (rewards.TryGetItem(definitionId, out RewardCatalogSO.ItemDefinition definition)))
            {
                return definition.Grade;
            }

            return EItemGrade.Common;
        }
    }
}
