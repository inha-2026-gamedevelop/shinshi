// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 퀵슬롯 물약 개수 계산과 표시 문구, 순수 계산 </summary>
    public static class QuickSlotRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string EMPTY_TEXT = "-";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 정의 ID의 쓸 수 있는 개수(보유 - 예약). 목록이 없거나 정의 ID가 비었으면 0 </summary>
        public static long CountAvailable(TownItemEntry[] potions, string definitionId)
        {
            if ((potions == null) || (string.IsNullOrEmpty(definitionId)))
            {
                return 0;
            }

            long total = 0;
            for (int i = 0; i < potions.Length; ++i)
            {
                if (string.Equals(potions[i].DefinitionId, definitionId, StringComparison.Ordinal))
                {
                    total += Math.Max(0L, potions[i].Quantity - potions[i].Reserved);
                }
            }

            return total;
        }

        /// <summary> 표시 문구, 아직 값을 모르면 대시 </summary>
        public static string Format(bool known, long count)
        {
            return known ? Math.Max(0L, count).ToString(CultureInfo.InvariantCulture) : EMPTY_TEXT;
        }
    }
}
