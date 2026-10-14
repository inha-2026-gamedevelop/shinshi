// System
using System;
using System.Collections.Generic;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 장비 옵션 재설정 판정. 뽑기와 상급 판정은 EquipmentOptionRules의 식을 그대로 써서 전투 서버의 검증과 어긋나지 않는다.
    /// 난수는 마을 서버가 뽑은 입력이며 여기서 만들지 않으므로 같은 입력은 항상 같은 결과다 </summary>
    public static class OptionRerollRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 수련용 장비는 옵션을 가질 수 없다 </summary>
        public static bool CanReroll(GrowthContent.EquipmentDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return !definition.IsTraining;
        }

        /// <summary> 한 번 재설정하는 비용. 등급, 부위와 무관하게 같다 </summary>
        public static (long Essence, long Shards) Cost(GrowthContent content)
        {
            ArgumentNullException.ThrowIfNull(content);
            return (content.Equipment.OptionRerollEssenceCost, content.Equipment.OptionRerollShardCost);
        }

        /// <summary> 누계가 이 값 이상인 상태에서 하는 재설정은 줄1의 값을 최댓값으로 확정한다 </summary>
        public static bool IsPityReroll(GrowthContent content, int misses)
        {
            ArgumentNullException.ThrowIfNull(content);
            return misses >= (content.Equipment.OptionPityThreshold - 1);
        }

        /// <summary> rolls(줄1 종류, 줄1 값, 줄2 종류, 줄2 값)와 현재 누계로 새 옵션 두 줄과 다음 누계를 정한다 </summary>
        public static OptionRerollDecision Roll(GrowthContent content, GrowthContent.EquipmentDefinition definition,
            IReadOnlyList<int> rolls, int misses)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(definition);
            if (!EquipmentOptionRules.IsValidRolls(rolls))
            {
                throw new ArgumentException("Option rolls must be four values from 0 to 9999.", nameof(rolls));
            }
            if (misses < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(misses));
            }
            if (!CanReroll(definition))
            {
                throw new ArgumentException("Training equipment cannot hold options.", nameof(definition));
            }

            // 천장은 줄1의 값만 확정한다. 종류는 난수대로 두어 같은 입력이 같은 종류를 낸다
            bool pity = IsPityReroll(content, misses);
            EquipmentOptionLine line1 = PickLine(content, definition, rolls[EquipmentOptionRules.ROLL_LINE1_KIND],
                rolls[EquipmentOptionRules.ROLL_LINE1_VALUE], pity);
            EquipmentOptionLine line2 = PickLine(content, definition, rolls[EquipmentOptionRules.ROLL_LINE2_KIND],
                rolls[EquipmentOptionRules.ROLL_LINE2_VALUE], false);
            bool highTier = IsHighTier(content, definition.Grade, line1) || IsHighTier(content, definition.Grade, line2);
            return new OptionRerollDecision(line1, line2, highTier ? 0 : misses + 1, pity);
        }

        /// <summary> 줄의 값이 그 종류, 등급 범위의 상급 구간에 드는지. 옵션 없는 줄은 상급이 아니다 </summary>
        public static bool IsHighTier(GrowthContent content, int grade, EquipmentOptionLine line)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (line.IsNone || !content.TryGetOptionRange(line.Kind, grade, out int min, out int max))
            {
                return false;
            }

            return EquipmentOptionRules.IsHighTier(line.Value, min, max, content.Equipment.OptionHighTierBasisPoints);
        }

        private static EquipmentOptionLine PickLine(GrowthContent content, GrowthContent.EquipmentDefinition definition,
            int kindRoll, int valueRoll, bool forceMax)
        {
            int kind = EquipmentOptionRules.PickKind(kindRoll, content.GetOptionPool(definition.Slot));
            if (!content.TryGetOptionRange(kind, definition.Grade, out int min, out int max))
            {
                // 콘텐츠 검증이 후보와 범위를 묶어 두므로 여기 오면 콘텐츠와 코드가 어긋난 것이다
                throw new InvalidOperationException("Option kind has no value range for this grade.");
            }

            int value = forceMax ? max : EquipmentOptionRules.PickValue(valueRoll, min, max, content.Equipment.OptionValueStep);
            return new EquipmentOptionLine(kind, value);
        }
    }
}
