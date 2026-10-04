// System
using System;
using System.Linq;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 정기 수련 투자와 재분배의 비용, 상한 판정. 상한을 넘는 요청은 비용 없이 거절한다 </summary>
    public static class TrainingRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 총 투자 순번 stepNumber(1부터)번째 단계 비용. 항목별로 다시 시작하지 않는다 </summary>
        public static long StepCost(GrowthContent content, int stepNumber)
        {
            ArgumentNullException.ThrowIfNull(content);
            if ((stepNumber < 1) || (stepNumber > content.Growth.TrainingTotalCap))
            {
                throw new ArgumentOutOfRangeException(nameof(stepNumber));
            }

            return content.Growth.TrainingBaseCost + (content.Growth.TrainingCostStep * (stepNumber - 1));
        }

        /// <summary> 현재 총 투자 currentTotal에서 steps단계를 더 투자하는 비용 합계 </summary>
        public static long InvestCost(GrowthContent content, int currentTotal, int steps)
        {
            ArgumentNullException.ThrowIfNull(content);
            // int 덧셈이 넘쳐 음수가 되면 상한 검사를 지나므로 long으로 비교한다
            if ((currentTotal < 0) || (steps <= 0) || ((long)currentTotal + steps > content.Growth.TrainingTotalCap))
            {
                throw new ArgumentOutOfRangeException(nameof(steps));
            }

            long total = 0;
            int last = currentTotal + steps;
            for (int step = currentTotal + 1; step <= last; ++step)
            {
                total += StepCost(content, step);
            }

            return total;
        }

        /// <summary> 한 항목 투자 요청의 판정. 통과면 NONE, 항목 또는 총 상한을 넘으면 INVALID_TARGET </summary>
        public static int CheckInvest(GrowthContent content, int[] allocation, int stat, int steps)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(allocation);
            if ((stat < 0) || (stat >= GrowthContent.TRAINING_STAT_COUNT) || (steps <= 0)
                || (steps > content.Growth.TrainingTotalCap))
            {
                return ContractCodes.RejectReason.INVALID_TARGET;
            }

            // steps가 int 끝 근처면 int 덧셈이 넘쳐 음수가 되어 상한을 지나고 비용이 0이 된다. long으로 더한다
            long allocated = allocation.Sum(value => (long)value);
            if (((long)allocation[stat] + steps > content.Growth.TrainingStatCap)
                || (allocated + steps > content.Growth.TrainingTotalCap))
            {
                return ContractCodes.RejectReason.INVALID_TARGET;
            }

            return ContractCodes.RejectReason.NONE;
        }

        /// <summary> 재분배 비용. 총 투자 0이면 0이다. 배분 변화가 없으면 호출자가 비용을 받지 않는다 </summary>
        public static long RespecCost(GrowthContent content, int totalInvested)
        {
            ArgumentNullException.ThrowIfNull(content);
            if ((totalInvested < 0) || (totalInvested > content.Growth.TrainingTotalCap))
            {
                throw new ArgumentOutOfRangeException(nameof(totalInvested));
            }

            return totalInvested == 0
                ? 0
                : content.Growth.RespecBaseCost + (content.Growth.RespecCostPerStep * totalInvested);
        }

        /// <summary> 재분배 목표가 현재 총 투자와 같고 항목 상한 안인지 본다. 아니면 INVALID_TARGET </summary>
        public static int CheckRespec(GrowthContent content, int[] current, int[] requested)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(current);
            if ((requested == null) || (requested.Length != GrowthContent.TRAINING_STAT_COUNT)
                || requested.Any(value => (value < 0) || (value > content.Growth.TrainingStatCap))
                || (requested.Sum() != current.Sum()))
            {
                return ContractCodes.RejectReason.INVALID_TARGET;
            }

            return ContractCodes.RejectReason.NONE;
        }
    }
}
