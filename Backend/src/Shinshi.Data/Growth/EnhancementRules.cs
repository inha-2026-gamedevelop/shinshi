// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 장비 강화 비용과 실패 보정 성공률. 확률은 만분율 정수로 비교해 float 누적 오차로 확정 시도가 밀리지 않게 한다 </summary>
    public static class EnhancementRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 0에서 1 사이 비율을 만분율로 바꾼다. 0.65 + 0.1 x 3 같은 값이 정확히 9500이 된다 </summary>
        public static int ToBasisPoints(decimal rate)
        {
            return (int)Math.Round(rate * GrowthCodes.BASIS_POINTS, MidpointRounding.AwayFromZero);
        }

        /// <summary> 현재 단계에서 한 번 시도하는 비용. 현재 단계가 상한이면 예외이므로 먼저 CanEnhance로 거른다 </summary>
        public static (long Essence, long Shards) AttemptCost(GrowthContent content, int currentLevel)
        {
            ArgumentNullException.ThrowIfNull(content);
            GrowthContent.EnhancementStep step = content.GetEnhancementStep(currentLevel + 1);
            return (step.EssenceCost, step.ShardCost);
        }

        public static bool CanEnhance(int currentLevel)
        {
            return (currentLevel >= 0) && (currentLevel < GrowthContent.MAX_ENHANCEMENT);
        }

        /// <summary> 캐릭터, 부위, 현재 단계에 쌓인 실패 횟수를 반영한 성공률(만분율), 상한 BASIS_POINTS </summary>
        public static int SuccessBasisPoints(GrowthContent content, int currentLevel, int failures)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (failures < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failures));
            }

            GrowthContent.EnhancementStep step = content.GetEnhancementStep(currentLevel + 1);
            long rate = (long)ToBasisPoints(step.BaseSuccessRate) + ((long)ToBasisPoints(step.FailBonusRate) * failures);
            return (int)Math.Min(rate, GrowthCodes.BASIS_POINTS);
        }

        /// <summary> 마을 서버가 뽑은 roll(0에서 9999)이 성공률보다 작으면 성공이다 </summary>
        public static bool IsSuccess(int successBasisPoints, int rollBasisPoints)
        {
            return rollBasisPoints < successBasisPoints;
        }

        public static bool IsValidRoll(int rollBasisPoints)
        {
            return (rollBasisPoints >= 0) && (rollBasisPoints <= GrowthCodes.MAX_ROLL_BASIS_POINTS);
        }
    }
}
