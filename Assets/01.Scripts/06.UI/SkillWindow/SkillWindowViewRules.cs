// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    // 클라이언트는 미리보기와 버튼 활성만 정한다. 비용, 성공 판정, 조합 유효성의 최종 판정은 마을 서버와 API가 한다.
    // 그래서 여기서 막는 조건은 서버보다 넓거나 같아야 하고, 서버가 받는 요청을 여기서 막지 않도록 서버 규칙과 같은 값을 쓴다
    /// <summary> 스킬창이 쓰는 순수 계산, 성장 스냅숏과 카탈로그 값을 슬롯 상태, 버튼 활성, 글자로 바꾼다 </summary>
    public static class SkillWindowViewRules
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 스킬창이 쓸 수 있는 상황 </summary>
        public enum EMode
        {
            // 서버에 연결되지 않았다. 기술 정보만 보인다
            Offline      = 0,
            // 마을, 출정 진행 없음
            Writable     = 1,
            // 마을이지만 출정 카운트다운, 예약, 대기열, 입장 중이거나 서버를 옮기는 중
            SortieLocked = 2,
            // 던전에서는 보유 효과와 조합을 받을 경로가 없어 부적 영역을 숨긴다
            Dungeon      = 3
        }

        /// <summary> 부적 효과 하나의 보유 상태 </summary>
        public enum EOwnState
        {
            Unowned    = 0,
            CardOnly   = 1,
            Registered = 2
        }

        /// <summary> 임시 조합 검증 결과 </summary>
        public enum ELoadoutError
        {
            None          = 0,
            TooMany       = 1,
            DuplicateTier = 2,
            SkillFull     = 3,
            Unregistered  = 4,
            InvalidEntry  = 5
        }

        /****************************************
        *                Fields
        ****************************************/

        public const int TAB_COUNT        = 5; // 기본 공격과 Q, W, E, R
        public const int BASIC_TAB        = 0;
        public const int TIERS_PER_SKILL  = 3; // ETalismanTier 값 수, 기술 하나에 단계마다 한 칸
        public const int CHOICES_PER_TIER = 2; // ETalismanChoice 값 수, 대표 기술만 두 후보
        public const int FIRST_LEVEL      = 1;

        private const float PERCENT = 100f;

        private static readonly string[] G_TabLabels  = { "기본", "Q", "W", "E", "R" };
        private static readonly string[] G_TierLabels = { "1단계 보조", "2단계 보조", "3단계 핵심" };

        public static readonly EGrowthAction[] G_OwnActions =
        {
            EGrowthAction.TalismanRegister, EGrowthAction.TalismanUpgrade, EGrowthAction.TalismanLoadout,
            EGrowthAction.TalismanDisassemble
        };

        /****************************************
        *                Methods
        ****************************************/

        public static string TabLabel(int tab)
        {
            return ((tab >= 0) && (tab < G_TabLabels.Length)) ? G_TabLabels[tab] : string.Empty;
        }

        public static string TierLabel(int tier)
        {
            return ((tier >= 0) && (tier < G_TierLabels.Length)) ? G_TierLabels[tier] : string.Empty;
        }

        /// <summary> 탭 번호를 기술 슬롯(Q가 0)으로 바꾼다, 기본 공격 탭이면 -1 </summary>
        public static int SlotOfTab(int tab)
        {
            return ((tab > BASIC_TAB) && (tab < TAB_COUNT)) ? tab - 1 : -1;
        }

        // ClientRoles.IsTown은 연결과 역할만 본다. 출정 대기 중에도 마을 연결이 남아 참이므로 방 단계와 서버 전환을 함께 본다
        /// <summary> 접속 상태와 파티 출정 단계로 스킬창 상황을 정한다 </summary>
        public static EMode ResolveMode(bool isTown, bool isDungeon, bool inRoom, EPartyPhase phase, bool isTransferring)
        {
            if (isDungeon)
            {
                return EMode.Dungeon;
            }

            if (!isTown)
            {
                return EMode.Offline;
            }

            if ((isTransferring) || ((inRoom) && (phase != EPartyPhase.Open)))
            {
                return EMode.SortieLocked;
            }

            return EMode.Writable;
        }

        public static bool ShowsTalismans(EMode mode)
        {
            return (mode == EMode.Writable) || (mode == EMode.SortieLocked);
        }

        /// <summary> 보유 효과 행을 찾는다. 행이 없으면 레벨 0, 실패 0 </summary>
        public static bool TryFindTalisman(TownTalismanEntry[] talismans, string effectId, out TownTalismanEntry entry)
        {
            entry = default;
            if ((talismans == null) || (string.IsNullOrEmpty(effectId)))
            {
                return false;
            }

            for (int i = 0; i < talismans.Length; ++i)
            {
                if (string.Equals(talismans[i].EffectId, effectId, StringComparison.Ordinal))
                {
                    entry = talismans[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary> 계정 보관함의 그 효과 부적 카드 중 쓸 수 있는 장수, 거래나 경매에 묶인 수는 뺀다 </summary>
        public static long AvailableCards(TownItemEntry[] items, string effectId)
        {
            if ((items == null) || (string.IsNullOrEmpty(effectId)))
            {
                return 0;
            }

            long total = 0;
            for (int i = 0; i < items.Length; ++i)
            {
                if (string.Equals(items[i].DefinitionId, effectId, StringComparison.Ordinal))
                {
                    total += Math.Max(0L, items[i].Quantity - items[i].Reserved);
                }
            }

            return total;
        }

        // 백엔드는 등록 실패도 레벨 0 행으로 저장하므로 행이 있다는 것만으로 등록됐다고 보지 않는다
        public static bool IsRegistered(int level)
        {
            return level >= FIRST_LEVEL;
        }

        public static EOwnState OwnState(int level, long cards)
        {
            if (IsRegistered(level))
            {
                return EOwnState.Registered;
            }

            return (cards > 0) ? EOwnState.CardOnly : EOwnState.Unowned;
        }

        public static bool CanRegister(int level, long cards, long essence, int cardCost, int essenceCost)
        {
            return (!IsRegistered(level)) && (cards >= cardCost) && (cards > 0) && (essence >= essenceCost);
        }

        public static bool CanUpgrade(int level, int maxLevel, long cards, long essence, int cardCost, int essenceCost)
        {
            return (IsRegistered(level)) && (level < maxLevel) && (cards >= cardCost) && (essence >= essenceCost);
        }

        /// <summary> 분해 수량을 1부터 보유 장수 사이로 맞춘다, 카드가 없으면 0 </summary>
        public static int ClampDisassembleQuantity(int quantity, long cards)
        {
            if (cards <= 0)
            {
                return 0;
            }

            int max = (int)Math.Min(cards, int.MaxValue);
            return Math.Max(1, Math.Min(quantity, max));
        }

        public static long DisassembleEssence(int quantity, int essencePerCard)
        {
            return (quantity <= 0) ? 0 : (long)quantity * essencePerCard;
        }

        /// <summary> 그 기술, 단계, 선택의 효과 정의를 찾는다 </summary>
        public static bool TryFindEffect(TalismanCatalogSO catalog, string skillId, int tier, int choice,
            out TalismanCatalogSO.TalismanEffectDefinition effect)
        {
            effect = null;
            if ((catalog == null) || (string.IsNullOrEmpty(skillId)))
            {
                return false;
            }

            IReadOnlyList<TalismanCatalogSO.TalismanEffectDefinition> effects = catalog.Effects;
            for (int i = 0; i < effects.Count; ++i)
            {
                TalismanCatalogSO.TalismanEffectDefinition candidate = effects[i];
                if ((candidate != null) && ((int)candidate.Tier == tier) && ((int)candidate.Choice == choice)
                    && (string.Equals(candidate.SkillId, skillId, StringComparison.Ordinal)))
                {
                    effect = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary> 조합에 그 효과가 그 기술, 단계로 들어 있는지 </summary>
        public static bool IsEquipped(IReadOnlyList<TownLoadoutEntry> loadout, string skillId, int tier, string effectId)
        {
            return IndexOfSlot(loadout, skillId, tier, effectId) >= 0;
        }

        // 같은 단계에는 하나만 둔다. 이미 있으면 빼고, 같은 단계의 다른 효과가 있으면 바꾼다
        /// <summary> 임시 조합에서 장착을 뒤집는다, 장착된 상태가 되면 참 </summary>
        public static bool ToggleEquip(List<TownLoadoutEntry> loadout, string skillId, int tier, string effectId)
        {
            if (loadout == null)
            {
                return false;
            }

            if (IndexOfSlot(loadout, skillId, tier, effectId) >= 0)
            {
                loadout.RemoveAt(IndexOfSlot(loadout, skillId, tier, effectId));
                return false;
            }

            int sameTier = IndexOfSlot(loadout, skillId, tier, null);
            if (sameTier >= 0)
            {
                loadout.RemoveAt(sameTier);
            }

            loadout.Add(new TownLoadoutEntry(skillId, tier, effectId));
            return true;
        }

        /// <summary> 서버 조합 규칙(최대 12칸, 단계마다 하나, 기술마다 3칸, 등록한 효과만)으로 임시 조합을 미리 본다 </summary>
        public static ELoadoutError ValidateLoadout(IReadOnlyList<TownLoadoutEntry> loadout, Func<string, int> levelOf)
        {
            if (loadout == null)
            {
                return ELoadoutError.None;
            }

            if (loadout.Count > Constants.Town.MAX_GROWTH_LOADOUT_SLOTS)
            {
                return ELoadoutError.TooMany;
            }

            for (int i = 0; i < loadout.Count; ++i)
            {
                TownLoadoutEntry entry = loadout[i];
                if ((string.IsNullOrEmpty(entry.SkillId)) || (string.IsNullOrEmpty(entry.EffectId)) || (entry.Tier < 0)
                    || (entry.Tier >= TIERS_PER_SKILL))
                {
                    return ELoadoutError.InvalidEntry;
                }

                int perSkill = 0;
                for (int j = 0; j < loadout.Count; ++j)
                {
                    if (!string.Equals(loadout[j].SkillId, entry.SkillId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ++perSkill;
                    if ((j != i) && (loadout[j].Tier == entry.Tier))
                    {
                        return ELoadoutError.DuplicateTier;
                    }
                }

                if (perSkill > TIERS_PER_SKILL)
                {
                    return ELoadoutError.SkillFull;
                }

                if ((levelOf != null) && (!IsRegistered(levelOf(entry.EffectId))))
                {
                    return ELoadoutError.Unregistered;
                }
            }

            return ELoadoutError.None;
        }

        public static string LoadoutErrorText(ELoadoutError error)
        {
            switch (error)
            {
                case ELoadoutError.TooMany:
                    return "조합은 최대 12개까지입니다";
                case ELoadoutError.DuplicateTier:
                    return "한 단계에는 효과 하나만 장착할 수 있습니다";
                case ELoadoutError.SkillFull:
                    return "기술 하나에는 최대 3개까지 장착할 수 있습니다";
                case ELoadoutError.Unregistered:
                    return "등록하지 않은 효과는 장착할 수 없습니다";
                case ELoadoutError.InvalidEntry:
                    return "조합에 잘못된 칸이 있습니다";
                default:
                    return string.Empty;
            }
        }

        /// <summary> 두 조합이 순서와 상관없이 같은지, 저장하지 않은 변경을 판정한다 </summary>
        public static bool SameLoadout(IReadOnlyList<TownLoadoutEntry> left, IReadOnlyList<TownLoadoutEntry> right)
        {
            int leftCount  = (left == null) ? 0 : left.Count;
            int rightCount = (right == null) ? 0 : right.Count;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int i = 0; i < leftCount; ++i)
            {
                if (IndexOfSlot(right, left[i].SkillId, left[i].Tier, left[i].EffectId) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        public static int CountSkill(IReadOnlyList<TownLoadoutEntry> loadout, string skillId)
        {
            int count = 0;
            int total = (loadout == null) ? 0 : loadout.Count;
            for (int i = 0; i < total; ++i)
            {
                if (string.Equals(loadout[i].SkillId, skillId, StringComparison.Ordinal))
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary> 강화 항목 단위로 보너스 글자를 만든다, 비율은 퍼센트로 보인다 </summary>
        public static string BonusText(ETalismanUpgradeStat stat, float value)
        {
            switch (stat)
            {
                case ETalismanUpgradeStat.CooldownRate:
                case ETalismanUpgradeStat.EnergyCostRate:
                case ETalismanUpgradeStat.AttackIntervalRate:
                    return string.Format(CultureInfo.InvariantCulture, "-{0:0.#}%", value * PERCENT);
                case ETalismanUpgradeStat.DamageRate:
                    return string.Format(CultureInfo.InvariantCulture, "+{0:0.#}%", value * PERCENT);
                case ETalismanUpgradeStat.DamageBonusPoint:
                    return string.Format(CultureInfo.InvariantCulture, "+{0:0.#}%p", value * PERCENT);
                case ETalismanUpgradeStat.ArcAngle:
                    return string.Format(CultureInfo.InvariantCulture, "+{0:0.#}도", value);
                case ETalismanUpgradeStat.AddedCoefficient:
                    return string.Format(CultureInfo.InvariantCulture, "+{0:0.##}A", value);
                case ETalismanUpgradeStat.GuardDuration:
                case ETalismanUpgradeStat.EffectDuration:
                case ETalismanUpgradeStat.RootDuration:
                    return string.Format(CultureInfo.InvariantCulture, "+{0:0.##}초", value);
                case ETalismanUpgradeStat.ResonanceGain:
                    return string.Format(CultureInfo.InvariantCulture, "+{0:0.#} 공명", value);
                default:
                    return string.Format(CultureInfo.InvariantCulture, "+{0:0.##}m", value);
            }
        }

        /// <summary> 기술 수치 한 줄 "기력 20  쿨다운 6초  사거리 3m" </summary>
        public static string StatsText(SkillCatalogSO.SkillDefinition skill)
        {
            if (skill == null)
            {
                return string.Empty;
            }

            string range = RangeText(skill.Area);
            return string.Format(CultureInfo.InvariantCulture, "기력 {0:0.#}    쿨다운 {1:0.#}초    {2}", skill.EnergyCost,
                skill.Cooldown, range);
        }

        private static string RangeText(SkillCatalogSO.SkillArea area)
        {
            if ((area == null) || (area.AimMode == ESkillAimMode.Self))
            {
                return "대상 자신";
            }

            if (area.CastRange > 0f)
            {
                return string.Format(CultureInfo.InvariantCulture, "사거리 {0:0.#}m", area.CastRange);
            }

            return string.Format(CultureInfo.InvariantCulture, "범위 {0:0.#}m", area.Radius);
        }

        /// <summary> 기본 공격 단계표, 콤보가 없으면 한 줄 </summary>
        public static string BasicAttackText(PlayerConfigSO.BasicAttackSpec spec)
        {
            if (spec == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            string reach = (spec.ProjectileSpeed > 0f)
                ? string.Format(CultureInfo.InvariantCulture, "원거리 {0:0.#}m", spec.Range)
                : string.Format(CultureInfo.InvariantCulture, "근접 {0:0.#}m, {1:0}도", spec.Range, spec.ArcAngle);
            builder.Append(reach);
            for (int step = 0; step < spec.ComboStepCount; ++step)
            {
                builder.Append('\n');
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0}단  {1:0.##}A  주기 {2:0.##}초", step + 1,
                    spec.GetDamageCoefficient(step), spec.GetInterval(step));
                if (spec.IsHitImmune(step))
                {
                    builder.Append("  피격이상 면역");
                }
            }

            return builder.ToString();
        }

        /// <summary> 직업 스택 규칙 한 줄, 스택이 없는 직업이면 빈 문자열 </summary>
        public static string StackRuleText(string rule, int stackMax)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                return string.Empty;
            }

            return string.Format(CultureInfo.InvariantCulture, "스택 최대 {0}. {1}", stackMax, rule);
        }

        public static string FooterText(long essence, long cards, int loadoutCount)
        {
            return string.Format(CultureInfo.InvariantCulture, "보유 정기 {0:N0}    카드 {1:N0}장    조합 {2}/{3}", essence, cards,
                loadoutCount, Constants.Town.MAX_GROWTH_LOADOUT_SLOTS);
        }

        public static string RegisterLabel(int cardCost, int essenceCost, float successRate)
        {
            return string.Format(CultureInfo.InvariantCulture, "등록  카드 {0} + 정기 {1}  ({2:0}%)", cardCost, essenceCost,
                successRate * PERCENT);
        }

        public static string UpgradeLabel(int cardCost, int essenceCost)
        {
            return string.Format(CultureInfo.InvariantCulture, "강화  카드 {0} + 정기 {1}", cardCost, essenceCost);
        }

        public static string DisassembleLabel(int quantity, long essence)
        {
            return string.Format(CultureInfo.InvariantCulture, "분해 {0}장  정기 +{1:N0}", quantity, essence);
        }

        public static string LevelText(int level, int maxLevel)
        {
            return IsRegistered(level)
                ? string.Format(CultureInfo.InvariantCulture, "Lv {0}/{1}", level, maxLevel)
                : "미등록";
        }

        /// <summary> 선택 버튼 아래 상태 글자 </summary>
        public static string OwnStateText(EOwnState state, int level, long cards)
        {
            switch (state)
            {
                case EOwnState.Registered:
                    return string.Format(CultureInfo.InvariantCulture, "Lv {0}", level);
                case EOwnState.CardOnly:
                    return string.Format(CultureInfo.InvariantCulture, "카드 {0}장", cards);
                default:
                    return "미보유";
            }
        }

        // effectId가 null이면 효과와 상관없이 그 기술과 단계의 칸을 찾는다
        private static int IndexOfSlot(IReadOnlyList<TownLoadoutEntry> loadout, string skillId, int tier, string effectId)
        {
            int count = (loadout == null) ? 0 : loadout.Count;
            for (int i = 0; i < count; ++i)
            {
                TownLoadoutEntry entry = loadout[i];
                if ((entry.Tier == tier) && (string.Equals(entry.SkillId, skillId, StringComparison.Ordinal))
                    && ((effectId == null) || (string.Equals(entry.EffectId, effectId, StringComparison.Ordinal))))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
