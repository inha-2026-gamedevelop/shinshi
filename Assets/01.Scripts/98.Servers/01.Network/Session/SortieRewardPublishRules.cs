// System
using System;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 정산안과 정기 내역을 참가자에게 보낼 보상 메시지로 옮기는 규칙 </summary>
    public static class SortieRewardPublishRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 정산안 한 명분을 보상 메시지로 만든다. 잃은 추가 정기는 적립 합계에서 받은 양을 뺀 값이다 </summary>
        public static SortieRewardMessage Build(SettlementGrant grant, in SettlementBreakdown breakdown)
        {
            if (grant == null)
            {
                throw new ArgumentNullException(nameof(grant));
            }

            long lost = Math.Max(0L, breakdown.AccruedExtraEssence - breakdown.PaidExtraEssence);
            return new SortieRewardMessage(breakdown.BaseEssence, breakdown.BossEssence, breakdown.PaidExtraEssence, lost,
                grant.Essence, grant.RefineShards, grant.Tickets, grant.Currency, ToLines(grant.Items),
                ToLines(grant.Talismans));
        }

        private static SortieRewardLine[] ToLines(SettlementItemGrant[] grants)
        {
            if ((grants == null) || (grants.Length == 0))
            {
                return Array.Empty<SortieRewardLine>();
            }

            var lines = new SortieRewardLine[grants.Length];
            for (int i = 0; i < grants.Length; ++i)
            {
                lines[i] = new SortieRewardLine(grants[i].DefinitionId, grants[i].Quantity);
            }

            return lines;
        }
    }
}
