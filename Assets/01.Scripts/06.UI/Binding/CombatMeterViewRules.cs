// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 딜미터 값을 화면에 보여줄 형태로 바꾸는 순수 규칙, 서버 값을 읽기만 하고 판정하지 않는다 </summary>
    // 행 순서는 본인 맨 위 고정 + 나머지는 서버가 보낸 순서(설계 11장 결정 2). 피해 내림차순으로 다시 정렬하지 않아 순위 인상을 줄인다
    public static class CombatMeterViewRules
    {
        /// <summary> 1-float 포맷(FormatDamage, FormatShare 등) 결과를 값이 바뀔 때만 다시 만들어 재사용하는 캐시 </summary>
        public struct FloatTextCache
        {
            private float  _lastValue;
            private string _lastText;
            private bool   _hasValue;

            public bool TryGet(float value, out string text)
            {
                if ((_hasValue) && (_lastValue == value))
                {
                    text = _lastText;
                    return true;
                }

                text = null;
                return false;
            }

            public void Set(float value, string text)
            {
                _hasValue  = true;
                _lastValue = value;
                _lastText  = text;
            }
        }

        /// <summary> FormatDps처럼 두 float(damage, seconds)로 포맷하는 결과를 값이 바뀔 때만 다시 만들어 재사용하는 캐시 </summary>
        public struct Float2TextCache
        {
            private float  _lastA;
            private float  _lastB;
            private string _lastText;
            private bool   _hasValue;

            public bool TryGet(float a, float b, out string text)
            {
                if ((_hasValue) && (_lastA == a) && (_lastB == b))
                {
                    text = _lastText;
                    return true;
                }

                text = null;
                return false;
            }

            public void Set(float a, float b, string text)
            {
                _hasValue = true;
                _lastA    = a;
                _lastB    = b;
                _lastText = text;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string NUMBER_FORMAT  = "N0";
        private const string SECONDS_FORMAT = "F1";
        private const string NO_DPS_TEXT    = "-";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 파티 메시지 참가자 색인을 본인 먼저, 나머지는 받은 순서로 담는다 </summary>
        public static void BuildPartyRowOrder(int localCombatId, IReadOnlyList<CombatMeterPartyEntry> entries, List<int> order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            order.Clear();
            if (entries == null)
            {
                return;
            }

            int localIndex = FindLocalIndex(localCombatId, entries);
            AppendOrder(localIndex, entries.Count, order);
        }

        /// <summary> 결과 메시지 참가자 색인을 본인 먼저, 나머지는 받은 순서로 담는다 </summary>
        public static void BuildResultRowOrder(int localCombatId, IReadOnlyList<CombatMeterResultEntry> entries, List<int> order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            order.Clear();
            if (entries == null)
            {
                return;
            }

            int localIndex = FindLocalIndex(localCombatId, entries);
            AppendOrder(localIndex, entries.Count, order);
        }

        /// <summary> 구간 보기면 PhaseDamage, 보스전 전체 보기면 EncounterDamage </summary>
        public static float SelectPartyDamage(in CombatMeterPartyEntry entry, bool showEncounterTotal)
        {
            return showEncounterTotal ? entry.EncounterDamage : entry.PhaseDamage;
        }

        public static float SelectSelfDamage(in CombatMeterSelfMessage self, bool showEncounterTotal)
        {
            return showEncounterTotal ? self.EncounterDamage : self.PhaseDamage;
        }

        public static float SelectSelfSeconds(in CombatMeterSelfMessage self, bool showEncounterTotal)
        {
            return showEncounterTotal ? self.EncounterElapsedSeconds : self.PhaseElapsedSeconds;
        }

        /// <summary> 파티 메시지 참가자들의 선택한 피해를 더한다 </summary>
        public static float SumPartyDamage(IReadOnlyList<CombatMeterPartyEntry> entries, bool showEncounterTotal)
        {
            if (entries == null)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < entries.Count; ++i)
            {
                sum += SelectPartyDamage(entries[i], showEncounterTotal);
            }

            return sum;
        }

        /// <summary> 구간 초가 Constants.CombatMeter.MIN_DPS_SECONDS보다 작으면 거짓, dps는 그때 0 </summary>
        public static bool TryComputeDps(float damage, float seconds, out float dps)
        {
            if (seconds < (float)Constants.CombatMeter.MIN_DPS_SECONDS)
            {
                dps = 0f;
                return false;
            }

            dps = damage / seconds;
            return true;
        }

        /// <summary> 파티 합계가 0 이하면 0, 아니면 0~100 사이 점유율 </summary>
        public static float ComputeShare(float damage, float partyTotalDamage)
        {
            if (partyTotalDamage <= 0f)
            {
                return 0f;
            }

            float share = damage / partyTotalDamage * 100f;
            return Math.Min(Math.Max(share, 0f), 100f);
        }

        public static string FormatDamage(float value)
        {
            return value.ToString(NUMBER_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary> value가 바뀌지 않았으면 이전에 만든 문자열을 그대로 돌려준다 </summary>
        public static string FormatDamageCached(float value, ref FloatTextCache cache)
        {
            if (cache.TryGet(value, out string text))
            {
                return text;
            }

            text = FormatDamage(value);
            cache.Set(value, text);
            return text;
        }

        /// <summary> 분모가 MIN_DPS_SECONDS보다 작으면 "-" </summary>
        public static string FormatDps(float damage, float seconds)
        {
            return TryComputeDps(damage, seconds, out float dps) ? FormatDamage(dps) : NO_DPS_TEXT;
        }

        /// <summary> damage, seconds가 바뀌지 않았으면 이전에 만든 문자열을 그대로 돌려준다 </summary>
        public static string FormatDpsCached(float damage, float seconds, ref Float2TextCache cache)
        {
            if (cache.TryGet(damage, seconds, out string text))
            {
                return text;
            }

            text = FormatDps(damage, seconds);
            cache.Set(damage, seconds, text);
            return text;
        }

        public static string FormatShare(float sharePercent)
        {
            return sharePercent.ToString(NUMBER_FORMAT, CultureInfo.InvariantCulture) + "%";
        }

        /// <summary> sharePercent가 바뀌지 않았으면 이전에 만든 문자열을 그대로 돌려준다 </summary>
        public static string FormatShareCached(float sharePercent, ref FloatTextCache cache)
        {
            if (cache.TryGet(sharePercent, out string text))
            {
                return text;
            }

            text = FormatShare(sharePercent);
            cache.Set(sharePercent, text);
            return text;
        }

        public static string FormatSeconds(float seconds)
        {
            return seconds.ToString(SECONDS_FORMAT, CultureInfo.InvariantCulture) + "초";
        }

        public static string PhaseLabel(byte phase)
        {
            return phase.ToString(CultureInfo.InvariantCulture) + "단계";
        }

        /// <summary> 받은 피해 상위, 사망 요약 한 줄의 양과 사망까지 남은 시간을 합친 표시 문구 </summary>
        public static string FormatDeathAmount(float amount, float secondsBeforeDeath)
        {
            return FormatDamage(amount) + " (" + FormatSeconds(secondsBeforeDeath) + " 전)";
        }

        public static string SlotLabel(ECombatMeterSlot slot)
        {
            switch (slot)
            {
                case ECombatMeterSlot.BasicAttack:
                    return "기본 공격";
                case ECombatMeterSlot.Skill1:
                    return "기술 1";
                case ECombatMeterSlot.Skill2:
                    return "기술 2";
                case ECombatMeterSlot.Skill3:
                    return "기술 3";
                case ECombatMeterSlot.Skill4:
                    return "기술 4";
                case ECombatMeterSlot.Summon:
                    return "소환물";
                case ECombatMeterSlot.Extra:
                    return "추가 기술";
                default:
                    return "기타";
            }
        }

        // 람다로 넘기면 호출마다 클로저가 할당되므로 타입별 오버로드로 나눠 직접 순회한다
        private static int FindLocalIndex(int localCombatId, IReadOnlyList<CombatMeterPartyEntry> entries)
        {
            for (int i = 0; i < entries.Count; ++i)
            {
                if (entries[i].CombatId == localCombatId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindLocalIndex(int localCombatId, IReadOnlyList<CombatMeterResultEntry> entries)
        {
            for (int i = 0; i < entries.Count; ++i)
            {
                if (entries[i].CombatId == localCombatId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AppendOrder(int localIndex, int count, List<int> order)
        {
            if (localIndex >= 0)
            {
                order.Add(localIndex);
            }

            for (int i = 0; i < count; ++i)
            {
                if (i != localIndex)
                {
                    order.Add(i);
                }
            }
        }
    }
}
