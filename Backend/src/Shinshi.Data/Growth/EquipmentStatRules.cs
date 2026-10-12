// System
using System;

namespace Shinshi.Data.Growth
{
    /// <summary> 장비 하나의 공격, 생명, 방어와 고정 치명. 등급 배율과 강화 배율은 공격, 생명, 방어에만 곱한다 </summary>
    public static class EquipmentStatRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 소수를 유지한 장비 능력치. 수련용은 같은 부위 일반 제작값에 수련용 비율을 곱한다 </summary>
        public static EquipmentStats Compute(GrowthContent content, GrowthContent.EquipmentDefinition definition,
            int enhancement)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(definition);
            if ((enhancement < 0) || (enhancement > GrowthContent.MAX_ENHANCEMENT))
            {
                throw new ArgumentOutOfRangeException(nameof(enhancement));
            }

            GrowthContent.SlotBaseStats slot = content.GetSlotBase(definition.Slot);
            decimal training = definition.IsTraining ? content.Equipment.TrainingStatRate : 1m;
            decimal scale = content.GetGradeMultiplier(definition.Grade)
                * (1m + (content.Equipment.EnhancementStatRate * enhancement)) * training;
            // 흡수는 기본 흡수가 있는 부위에만 등급 단계마다 더한다. 등급 배율과 강화는 곱하지 않는다
            decimal lifeSteal = (slot.LifeSteal > 0m)
                ? (slot.LifeSteal + (content.Equipment.LifeStealPerGrade * definition.Grade)) * training
                : 0m;
            return new EquipmentStats(slot.Attack * scale, slot.Health * scale, slot.Defense * scale,
                slot.CritChance * training, lifeSteal);
        }
    }
}
