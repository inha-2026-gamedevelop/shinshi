// System
using System.Globalization;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 상세 창이 쓰는 순수 계산, 성장 스냅숏에서 표시용 능력치와 부위별 장비 글자를 만든다 </summary>
    public static class CharDetailRules
    {
        // 능력치는 표시용이다. 서버 판정은 서버가 같은 계산(CharacterStatsRules)으로 따로 한다.
        // 부적은 능력치 계산에 들어가지 않아 빈 목록으로 넘긴다

        /****************************************
        *                Fields
        ****************************************/

        private const string NO_EQUIPMENT_TEXT = "-";
        private const string ENHANCE_FORMAT    = "+{0}";
        private const string PERCENT_FORMAT    = "{0:0.#}%";
        private const string NUMBER_FORMAT     = "{0:N0}";

        private static readonly string[] G_ClassNames    = { "무사", "도술사", "신령사", "검사", "마검사", "궁수" };
        private static readonly string[] G_SlotNames     = { "무기", "머리", "몸통", "손", "발", "정기" };
        private static readonly string[] G_TrainingNames = { "공격", "체력", "방어", "치명타", "기력" };

        /****************************************
        *                Methods
        ****************************************/

        public static string ClassName(int classId)
        {
            return ((classId >= 0) && (classId < G_ClassNames.Length)) ? G_ClassNames[classId] : string.Empty;
        }

        public static string SlotName(int slot)
        {
            return ((slot >= 0) && (slot < G_SlotNames.Length)) ? G_SlotNames[slot] : string.Empty;
        }

        public static string TrainingName(int stat)
        {
            return ((stat >= 0) && (stat < G_TrainingNames.Length)) ? G_TrainingNames[stat] : string.Empty;
        }

        /// <summary> 스냅숏의 착용 장비와 수련으로 전투 준비를 만든다, 칸 수가 맞지 않으면 거짓 </summary>
        public static bool TryBuildLoadout(in GrowthSnapshotMessage growth, out CharacterCombatLoadout loadout)
        {
            loadout = null;
            int[] training = growth.Training;
            if ((training == null) || (training.Length != Constants.Combat.TRAINING_STAT_COUNT))
            {
                return false;
            }

            var equipped = new CombatEquipmentEntry[Constants.Combat.EQUIPMENT_SLOT_COUNT];
            TownEquipmentEntry[] entries = growth.Equipment;
            for (int i = 0; i < entries.Length; ++i)
            {
                TownEquipmentEntry entry = entries[i];
                if ((!entry.Equipped) || (entry.Slot >= equipped.Length))
                {
                    continue;
                }

                equipped[entry.Slot] = new CombatEquipmentEntry(entry.DefinitionId, entry.Grade, entry.IsTraining, entry.Enhancement);
            }

            loadout = new CharacterCombatLoadout(growth.ClassId, equipped, training, new CombatTalismanEntry[0], growth.ContentVersion);
            return true;
        }

        /// <summary> 표시용 능력치. GameDB가 없거나 계산이 맞지 않으면 거짓 </summary>
        public static bool TryComputeStats(in GrowthSnapshotMessage growth, PlayerConfigSO player, EquipmentCatalogSO equipment,
            GrowthConfigSO growthConfig, out CharacterStats stats, CharacterLevelSettings levels = null)
        {
            stats = default;
            return (TryBuildLoadout(in growth, out CharacterCombatLoadout loadout))
                && (CharacterStatsRules.TryCompute(player, equipment, growthConfig, loadout, levels, growth.TotalExperience, out stats, out _));
        }

        /// <summary> 부위의 착용 장비 이름과 강화 글자. 비었으면 "-" </summary>
        public static void EquippedText(in GrowthSnapshotMessage growth, int slot, out string name, out string enhancement)
        {
            name        = NO_EQUIPMENT_TEXT;
            enhancement = string.Empty;
            TownEquipmentEntry[] entries = growth.Equipment;
            for (int i = 0; i < entries.Length; ++i)
            {
                if ((entries[i].Equipped) && (entries[i].Slot == slot))
                {
                    name        = entries[i].DefinitionId;
                    enhancement = (entries[i].Enhancement > 0)
                        ? string.Format(CultureInfo.InvariantCulture, ENHANCE_FORMAT, entries[i].Enhancement)
                        : string.Empty;
                    return;
                }
            }
        }

        /// <summary> 수련 항목 글자 "공격 +3" </summary>
        public static string TrainingText(int stat, int steps)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} +{1}", TrainingName(stat), steps);
        }

        public static string Number(float value)
        {
            return string.Format(CultureInfo.InvariantCulture, NUMBER_FORMAT, value);
        }

        /// <summary> 0~1 비율을 퍼센트 글자로 </summary>
        public static string Percent(float ratio)
        {
            return string.Format(CultureInfo.InvariantCulture, PERCENT_FORMAT, ratio * 100f);
        }
    }
}
