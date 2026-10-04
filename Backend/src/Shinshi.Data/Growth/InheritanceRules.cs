// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 장비 계승 판정. 같은 등급은 원본 단계를 옮기고, 바로 한 등급 위는 원본 +17부터 환산표로 옮긴다.
    /// 계승은 강화 시도가 아니므로 실패 보정을 복사하지 않으며 확정 성공이다 </summary>
    public static class InheritanceRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 원본과 대상 상태로 계승 가능 여부, 결과 단계, 정기 비용을 정한다. 거절이면 비용을 받지 않는다 </summary>
        public static InheritanceDecision Evaluate(GrowthContent content, InheritanceItem source, InheritanceItem target)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(target);

            // 원본 잠금과 착용은 플레이어가 소모를 막은 상태라 계승 조건 위반이 아니라 묶인 장비로 거절한다
            if (source.Equipped || source.Locked)
            {
                return InheritanceDecision.Rejected(ContractCodes.RejectReason.BOUND_ITEM);
            }
            if ((source.ItemId == target.ItemId) || (source.Definition.Slot != target.Definition.Slot)
                || (target.Enhancement != 0) || target.Equipped)
            {
                return InheritanceDecision.Rejected(ContractCodes.RejectReason.INVALID_INHERITANCE);
            }

            int resultLevel;
            long cost;
            if (target.Definition.Grade == source.Definition.Grade)
            {
                // 옮길 강화가 없는 같은 등급 0강은 단순 교체로 안내한다
                if (source.Enhancement == 0)
                {
                    return InheritanceDecision.Rejected(ContractCodes.RejectReason.INVALID_INHERITANCE);
                }
                resultLevel = source.Enhancement;
                cost = content.Equipment.SameGradeInheritCost;
            }
            else if (target.Definition.Grade == source.Definition.Grade + 1)
            {
                if (!content.TryGetUpperInheritResult(source.Enhancement, out resultLevel))
                {
                    return InheritanceDecision.Rejected(ContractCodes.RejectReason.INVALID_INHERITANCE);
                }
                cost = content.Equipment.UpperGradeInheritCost;
            }
            else
            {
                return InheritanceDecision.Rejected(ContractCodes.RejectReason.INVALID_INHERITANCE);
            }

            EquipmentStats before = EquipmentStatRules.Compute(content, source.Definition, source.Enhancement);
            EquipmentStats after = EquipmentStatRules.Compute(content, target.Definition, resultLevel);
            if (!after.CoversCoreStats(before))
            {
                return InheritanceDecision.Rejected(ContractCodes.RejectReason.INVALID_INHERITANCE);
            }

            return InheritanceDecision.Accepted(resultLevel, cost);
        }
    }
}
