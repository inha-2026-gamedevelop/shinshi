// System
using System;
using System.Collections.Generic;

namespace Shinshi.Data.Growth
{
    /// <summary> 장비 제작 재료와 분해 반환. 제작은 보스 재료만 쓰고 분해는 일반 보스 재료의 일정 비율만 버림으로 돌려준다 </summary>
    public static class CraftRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 제작할 수 있는 정의인지 본다. 수련용은 제작 대상이 아니다 </summary>
        public static bool IsCraftable(GrowthContent.EquipmentDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return !definition.IsTraining && (definition.BossMaterialCount > 0);
        }

        /// <summary> 제작에 쓰는 재료와 수량. 장신구만 경매 재료를 더한다 </summary>
        public static IReadOnlyList<(string DefinitionId, long Quantity)> CraftCost(
            GrowthContent.EquipmentDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            List<(string, long)> cost = new List<(string, long)>
            {
                (definition.BossMaterialId, definition.BossMaterialCount)
            };
            if (definition.AuctionMaterialCount > 0)
            {
                cost.Add((definition.AuctionMaterialId, definition.AuctionMaterialCount));
            }

            return cost;
        }

        /// <summary> 분해로 돌려받는 일반 보스 재료 수, 버림이라 0일 수 있다. 강화, 계승 비용과 경매 재료는 돌려주지 않는다 </summary>
        public static long DismantleRefund(GrowthContent content, GrowthContent.EquipmentDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(definition);
            if (!IsCraftable(definition))
            {
                return 0;
            }

            return (long)Math.Floor(definition.BossMaterialCount * content.Equipment.DisassembleRefundRate);
        }
    }
}
