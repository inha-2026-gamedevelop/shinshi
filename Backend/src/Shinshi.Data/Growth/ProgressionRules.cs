// System
using System;
using System.Collections.Generic;
using System.Linq;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 계정 진행 기록과 콘텐츠 요구 조건으로 지역, 보스전 해금을 계산한다. 해금 자체는 저장하지 않고 읽을 때 정한다 </summary>
    public static class ProgressionRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 지역 해금 조건이 다른 지역 해금을 요구하는 사슬의 최대 깊이. 잘못된 데이터의 순환을 끊는다
        private const int MAX_REQUIREMENT_DEPTH = 8;

        /****************************************
        *                Methods
        ****************************************/

        public static ProgressTargetView[] Regions(GrowthContent content, EGrowthContentProfile profile,
            AccountProgressState state)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(state);
            return content.Progression.Regions.Select(region => new ProgressTargetView(region.DefinitionId,
                IsRegionUnlocked(content, profile, state, region, 0),
                state.Has(EAccountProgressKind.RegionCleared, region.DefinitionId))).ToArray();
        }

        public static ProgressTargetView[] BossBattles(GrowthContent content, EGrowthContentProfile profile,
            AccountProgressState state)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(state);
            return content.Progression.BossBattles.Select(battle => new ProgressTargetView(battle.DefinitionId,
                IsBattleUnlocked(content, profile, state, battle),
                state.Has(EAccountProgressKind.BattleCleared, battle.DefinitionId))).ToArray();
        }

        /// <summary> 지역 하나의 해금 여부. 콘텐츠에 없는 지역이면 null </summary>
        public static bool? IsRegionUnlocked(GrowthContent content, EGrowthContentProfile profile,
            AccountProgressState state, string regionId)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(state);
            GrowthContent.RegionDefinition? region = content.Progression.Regions
                .FirstOrDefault(entry => entry.DefinitionId == regionId);
            return (region == null) ? null : IsRegionUnlocked(content, profile, state, region, 0);
        }

        /// <summary> 보스전 하나의 해금 여부. 콘텐츠에 없는 보스전이면 null </summary>
        public static bool? IsBattleUnlocked(GrowthContent content, EGrowthContentProfile profile,
            AccountProgressState state, string battleId)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(state);
            GrowthContent.BossBattleDefinition? battle = content.Progression.BossBattles
                .FirstOrDefault(entry => entry.DefinitionId == battleId);
            return (battle == null) ? null : IsBattleUnlocked(content, profile, state, battle);
        }

        /// <summary> 기록할 수 있는 대상 ID인지 본다. 지역 종류는 지역 ID, 보스전 종류는 보스전 ID만 받는다 </summary>
        public static bool IsKnownTarget(GrowthContent content, EAccountProgressKind kind, string targetId)
        {
            ArgumentNullException.ThrowIfNull(content);
            return kind switch
            {
                EAccountProgressKind.RegionCleared or EAccountProgressKind.RegionUnlocked
                    => content.Progression.Regions.Any(region => region.DefinitionId == targetId),
                EAccountProgressKind.BattleCleared or EAccountProgressKind.BattleUnlocked
                    => content.Progression.BossBattles.Any(battle => battle.DefinitionId == targetId),
                _ => false
            };
        }

        private static bool IsRegionUnlocked(GrowthContent content, EGrowthContentProfile profile,
            AccountProgressState state, GrowthContent.RegionDefinition region, int depth)
        {
            if (state.Has(EAccountProgressKind.RegionUnlocked, region.DefinitionId))
            {
                return true;
            }
            if (profile == EGrowthContentProfile.Trial)
            {
                if (!region.AvailableInTrial)
                {
                    return false;
                }
                if (region.TrialPreUnlocked)
                {
                    return true;
                }
            }

            return Meets(content, profile, state, region.Requirement, depth);
        }

        private static bool IsBattleUnlocked(GrowthContent content, EGrowthContentProfile profile,
            AccountProgressState state, GrowthContent.BossBattleDefinition battle)
        {
            if (state.Has(EAccountProgressKind.BattleUnlocked, battle.DefinitionId))
            {
                return true;
            }
            if (profile == EGrowthContentProfile.Trial)
            {
                if (!battle.AvailableInTrial)
                {
                    return false;
                }
                if (battle.TrialPreUnlocked)
                {
                    return true;
                }
            }

            return Meets(content, profile, state, battle.Requirement, 0);
        }

        private static bool Meets(GrowthContent content, EGrowthContentProfile profile, AccountProgressState state,
            GrowthContent.UnlockRequirement requirement, int depth)
        {
            if (requirement.RequiresIntro && !state.IntroCompleted)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(requirement.ClearedRegionId)
                && !state.Has(EAccountProgressKind.RegionCleared, requirement.ClearedRegionId))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(requirement.ClearedBattleId)
                && !state.Has(EAccountProgressKind.BattleCleared, requirement.ClearedBattleId))
            {
                return false;
            }
            if (state.FoxTraces < requirement.FoxTraceCount)
            {
                return false;
            }
            if (string.IsNullOrEmpty(requirement.UnlockedRegionId))
            {
                return true;
            }

            GrowthContent.RegionDefinition? required = content.Progression.Regions
                .FirstOrDefault(region => region.DefinitionId == requirement.UnlockedRegionId);
            return (required != null) && (depth < MAX_REQUIREMENT_DEPTH)
                && IsRegionUnlocked(content, profile, state, required, depth + 1);
        }
    }
}
