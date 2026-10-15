// System
using System.Globalization;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 장비 옵션 재설정 창이 쓰는 순수 계산, 성장 스냅숏과 콘텐츠 값을 화면 글자로 바꾼다 </summary>
    public static class EquipmentOptionViewRules
    {
        // 옵션 값은 만분율 정수라 100으로 나눠 퍼센트로 보여 준다. 뽑기와 비용 차감은 마을 서버와 API가 하고 여기는 표시만 한다

        /****************************************
        *                Fields
        ****************************************/

        private const float  BASIS_POINTS_PER_PERCENT = 100f;
        private const string NO_OPTION_TEXT = "옵션 없음";
        private const string EMPTY_SLOT_TEXT = "-";
        private const string LINE_FORMAT    = "{0} +{1:0.##}%";
        private const string RANGE_FORMAT   = "{0:0.##}% ~ {1:0.##}%";
        private const string COST_FORMAT    = "정기 {0:N0} / {1:N0}    정련편 {2:N0} / {3:N0}";
        private const string PITY_FORMAT    = "상급 확정까지 {0}회";
        private const string PITY_READY     = "이번 재설정에서 첫 줄 최고값 확정";
        private const string SLOT_FORMAT    = "{0}  {1}";
        private const string ENHANCE_FORMAT = "{0}  {1} +{2}";

        private static readonly string[] G_KindNames = { string.Empty, "공격력", "최대 체력", "방어력", "치명타 확률" };
        private static readonly string[] G_SlotNames = { "무기", "머리", "몸통", "손", "발", "장신구" };

        /****************************************
        *                Methods
        ****************************************/

        public static string KindName(int kind)
        {
            return ((kind > 0) && (kind < G_KindNames.Length)) ? G_KindNames[kind] : string.Empty;
        }

        public static string SlotName(int slot)
        {
            return ((slot >= 0) && (slot < G_SlotNames.Length)) ? G_SlotNames[slot] : string.Empty;
        }

        /// <summary> 옵션 한 줄 글자 "공격력 +2.5%", 종류가 없으면 "옵션 없음" </summary>
        public static string LineText(int kind, int value)
        {
            string name = KindName(kind);
            if (string.IsNullOrEmpty(name))
            {
                return NO_OPTION_TEXT;
            }

            return string.Format(CultureInfo.InvariantCulture, LINE_FORMAT, name, value / BASIS_POINTS_PER_PERCENT);
        }

        /// <summary> 그 종류와 등급에서 나올 수 있는 범위 글자 "1% ~ 2%" </summary>
        public static string RangeText(int min, int max)
        {
            return string.Format(CultureInfo.InvariantCulture, RANGE_FORMAT, min / BASIS_POINTS_PER_PERCENT,
                max / BASIS_POINTS_PER_PERCENT);
        }

        /// <summary> 비용과 보유량 글자, 앞이 비용이고 뒤가 보유량이다 </summary>
        public static string CostText(int essenceCost, int shardCost, long essence, long shards)
        {
            return string.Format(CultureInfo.InvariantCulture, COST_FORMAT, essenceCost, essence, shardCost, shards);
        }

        public static bool CanAfford(int essenceCost, int shardCost, long essence, long shards)
        {
            return (essence >= essenceCost) && (shards >= shardCost);
        }

        /// <summary> 천장까지 남은 횟수 글자, 누계가 천장 직전이면 이번에 확정된다 </summary>
        public static string PityText(int misses, int threshold)
        {
            int remaining = threshold - misses;
            return (remaining <= 1)
                ? PITY_READY
                : string.Format(CultureInfo.InvariantCulture, PITY_FORMAT, remaining);
        }

        /// <summary> 부위 목록 한 줄 글자 "무기  equip_weapon_rare +17", 비었으면 "무기  -" </summary>
        public static string SlotText(int slot, bool hasItem, string definitionId, int enhancement)
        {
            if (!hasItem)
            {
                return string.Format(CultureInfo.InvariantCulture, SLOT_FORMAT, SlotName(slot), EMPTY_SLOT_TEXT);
            }

            return (enhancement > 0)
                ? string.Format(CultureInfo.InvariantCulture, ENHANCE_FORMAT, SlotName(slot), definitionId, enhancement)
                : string.Format(CultureInfo.InvariantCulture, SLOT_FORMAT, SlotName(slot), definitionId);
        }

        /// <summary> 그 부위에 착용한 장비, 없으면 거짓 </summary>
        public static bool TryFindEquipped(TownEquipmentEntry[] equipment, int slot, out TownEquipmentEntry entry)
        {
            entry = default;
            if (equipment == null)
            {
                return false;
            }

            for (int i = 0; i < equipment.Length; ++i)
            {
                if ((equipment[i].Equipped) && (equipment[i].Slot == slot))
                {
                    entry = equipment[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary> 재설정할 수 있는 장비인지, 수련용과 경매장 보관 중인 장비는 서버가 거절한다 </summary>
        public static bool CanReroll(in TownEquipmentEntry entry)
        {
            return (!entry.IsTraining) && (!entry.InMarket);
        }

        /// <summary> 값이 범위의 상급 구간인지, 서버의 천장 판정과 같은 식이다 </summary>
        public static bool IsHighTier(int kind, int value, int min, int max, int ratioBasisPoints)
        {
            return (kind != EquipmentOptionRules.KIND_NONE) && (max > min)
                && (EquipmentOptionRules.IsHighTier(value, min, max, ratioBasisPoints));
        }
    }
}
