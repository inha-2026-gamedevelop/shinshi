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

        /// <summary> 원본의 옵션 한 줄을 대상 장비로 옮긴 값. 대상이 갖고 있던 옵션은 쓰지 않는다.
        /// 상위 등급 계승이면 값이 대상 등급 범위의 min보다 작을 때 min으로 올리고, 옵션 없는 줄은 그대로 없음이다 </summary>
        public static EquipmentOptionLine CarryOption(GrowthContent content, GrowthContent.EquipmentDefinition source,
            GrowthContent.EquipmentDefinition target, EquipmentOptionLine line)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(target);
            if (line.IsNone)
            {
                return new EquipmentOptionLine(EquipmentOptionRules.KIND_NONE, 0);
            }

            // 등급이 오르면 범위도 올라간다. 범위 위로 벗어나는 값은 콘텐츠 검증이 막으므로 아래쪽만 맞춘다
            if ((target.Grade > source.Grade) && content.TryGetOptionRange(line.Kind, target.Grade, out int min, out _)
                && (line.Value < min))
            {
                return new EquipmentOptionLine(line.Kind, min);
            }

            return line;
        }
    }
}
