// System
using System;
using System.Collections.Generic;
using System.Linq;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 부적 등록 성공률, 확정 강화 비용, 조합 검증 </summary>
    public static class TalismanRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 캐릭터와 효과에 쌓인 등록 실패 횟수로 본 이번 시도 성공률(만분율). 마지막 칸 뒤는 마지막 값을 쓴다 </summary>
        public static int RegisterSuccessBasisPoints(GrowthContent content, int failures)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (failures < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failures));
            }

            decimal[] rates = content.Growth.RegisterSuccessRates;
            return EnhancementRules.ToBasisPoints(rates[Math.Min(failures, rates.Length - 1)]);
        }

        /// <summary> 조합 전체 교체 요청을 검증한다. learnedLevels는 효과 ID별 현재 레벨이다.
        /// 형식이나 직업, 기술, 단계가 맞지 않으면 INVALID_TARGET, 배우지 않은 효과가 있으면 MISSING_UNLOCK </summary>
        public static int CheckLoadout(GrowthContent content, int classId, IReadOnlyList<TalismanLoadoutEntry> entries,
            IReadOnlyDictionary<string, int> learnedLevels)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(learnedLevels);
            if (entries.Count > GrowthCodes.MAX_LOADOUT_ENTRIES)
            {
                return ContractCodes.RejectReason.INVALID_TARGET;
            }
            if (entries.Select(entry => (entry.SkillId, entry.Tier)).Distinct().Count() != entries.Count)
            {
                return ContractCodes.RejectReason.INVALID_TARGET;
            }

            foreach (TalismanLoadoutEntry entry in entries)
            {
                if (!content.TryGetEffect(entry.EffectId, out GrowthContent.TalismanEffect effect)
                    || (effect.PlayerClass != classId) || !string.Equals(effect.SkillId, entry.SkillId, StringComparison.Ordinal)
                    || (effect.Tier != entry.Tier))
                {
                    return ContractCodes.RejectReason.INVALID_TARGET;
                }
            }

            foreach (TalismanLoadoutEntry entry in entries)
            {
                if (!learnedLevels.TryGetValue(entry.EffectId, out int level) || (level < 1))
                {
                    return ContractCodes.RejectReason.MISSING_UNLOCK;
                }
            }

            return ContractCodes.RejectReason.NONE;
        }
    }
}
