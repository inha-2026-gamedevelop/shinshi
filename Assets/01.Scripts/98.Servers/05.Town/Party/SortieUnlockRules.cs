// System
using System;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 준비 확인이 돌려준 계정 진행으로 지역, 보스전 해금을 판정한다 </summary>
    public static class SortieUnlockRules
    {
        // 백엔드 ProgressionRules와 같은 순서로 판정한다: 명시 해금 기록 -> 체험판 포함 여부, 사전 해금 -> 요구 조건.
        // 체험판 여부는 GameDB의 TrialEdition을 보고 API는 Growth:ContentProfile을 본다. 둘이 다르면 예약이 최종 판정한다

        /****************************************
        *                Fields
        ****************************************/

        // 지역 요구 조건이 다른 지역 해금을 요구하는 사슬의 최대 깊이, 잘못된 데이터의 순환을 끊는다(백엔드와 같은 값)
        private const int MAX_REQUIREMENT_DEPTH = 8;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지역 일반전이 열렸는지, 콘텐츠에 없는 지역이면 거짓 </summary>
        public static bool IsRegionUnlocked(ProgressionCatalogSO catalog, ReadinessProgress progress, string regionId)
        {
            if ((catalog == null) || (progress == null) || (string.IsNullOrEmpty(regionId)))
            {
                return false;
            }

            return (catalog.TryGetRegion(regionId, out ProgressionCatalogSO.RegionDefinition region))
                && (IsRegionUnlocked(catalog, progress, region, 0));
        }

        /// <summary> 보스전이 열렸는지, 콘텐츠에 없는 보스전이면 거짓 </summary>
        public static bool IsBattleUnlocked(ProgressionCatalogSO catalog, ReadinessProgress progress, string battleId)
        {
            if ((catalog == null) || (progress == null) || (string.IsNullOrEmpty(battleId)))
            {
                return false;
            }

            if (!catalog.TryGetBattle(battleId, out ProgressionCatalogSO.BossBattleDefinition battle))
            {
                return false;
            }

            if (Contains(progress.UnlockedBattleIds, battle.DefinitionId))
            {
                return true;
            }

            if (catalog.TrialEdition)
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

            return Meets(catalog, progress, battle.Requirement, 0);
        }

        private static bool IsRegionUnlocked(ProgressionCatalogSO catalog, ReadinessProgress progress,
            ProgressionCatalogSO.RegionDefinition region, int depth)
        {
            if (Contains(progress.UnlockedRegionIds, region.DefinitionId))
            {
                return true;
            }

            if (catalog.TrialEdition)
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

            return Meets(catalog, progress, region.Requirement, depth);
        }

        // 빈 칸은 검사하지 않고 채운 조건은 모두 만족해야 한다
        private static bool Meets(ProgressionCatalogSO catalog, ReadinessProgress progress,
            ProgressionCatalogSO.UnlockRequirement requirement, int depth)
        {
            if (requirement == null)
            {
                return true;
            }

            if ((requirement.RequiresIntro) && (!progress.IntroCompleted))
            {
                return false;
            }

            if ((!string.IsNullOrEmpty(requirement.ClearedRegionId))
                && (!Contains(progress.ClearedRegionIds, requirement.ClearedRegionId)))
            {
                return false;
            }

            if ((!string.IsNullOrEmpty(requirement.ClearedBattleId))
                && (!Contains(progress.ClearedBattleIds, requirement.ClearedBattleId)))
            {
                return false;
            }

            if (progress.FoxTraces < requirement.FoxTraceCount)
            {
                return false;
            }

            if (string.IsNullOrEmpty(requirement.UnlockedRegionId))
            {
                return true;
            }

            return (depth < MAX_REQUIREMENT_DEPTH)
                && (catalog.TryGetRegion(requirement.UnlockedRegionId, out ProgressionCatalogSO.RegionDefinition required))
                && (IsRegionUnlocked(catalog, progress, required, depth + 1));
        }

        private static bool Contains(string[] ids, string id)
        {
            if (ids == null)
            {
                return false;
            }

            for (int i = 0; i < ids.Length; ++i)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
