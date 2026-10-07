// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 서버 전투 기록의 슬롯 분류와 시간, 포화 카운터 계산 </summary>
    public static class CombatMeterRules
    {
        /****************************************
        *                Methods
        ****************************************/

        public static ECombatMeterSlot ResolveSlot(string sourceId, bool isSummon,
            Dictionary<string, int> slots, out bool isKnown)
        {
            if (isSummon)
            {
                isKnown = true;
                return ECombatMeterSlot.Summon;
            }
            if ((!string.IsNullOrEmpty(sourceId)) && (slots != null) && (slots.TryGetValue(sourceId, out int slot)))
            {
                isKnown = true;
                return (ECombatMeterSlot)slot;
            }

            isKnown = false;
            return ECombatMeterSlot.Other;
        }

        public static double Elapsed(double start, double end)
        {
            return Math.Max(0d, end - start);
        }

        public static float CalculateDps(float damage, double seconds)
        {
            return (seconds < Constants.CombatMeter.MIN_DPS_SECONDS) ? 0f : (float)(damage / seconds);
        }

        public static ushort Increment(ushort value)
        {
            return (value == ushort.MaxValue) ? value : (ushort)(value + 1);
        }

        public static byte Increment(byte value)
        {
            return (value == byte.MaxValue) ? value : (byte)(value + 1);
        }
    }
}
