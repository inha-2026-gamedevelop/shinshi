// System
using System;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 성장창이 공용 쓰기 잠금을 함께 읽는 규칙, 다른 창의 결과 불명 요청이 남아 있으면 새 쓰기를 막는다 </summary>
    public static class GrowthWriteGuard
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OTHER_WINDOW_TEXT = "다른 창의 요청 결과를 기다리는 중입니다";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 잠금이 다른 요청에 걸려 있으면 참, 자기 요청이거나 잠금이 없으면 거짓 </summary>
        public static bool IsBlockedByOther(bool isHeld, Guid heldOperation, Guid ownOperation)
        {
            return (isHeld) && ((ownOperation == Guid.Empty) || (heldOperation != ownOperation));
        }

        /// <summary> 현재 TownGrowthClient 잠금 기준 </summary>
        public static bool IsBlockedByOther(Guid ownOperation)
        {
            return IsBlockedByOther(TownGrowthClient.HasUnresolvedWrite, TownGrowthClient.UnresolvedOperationId, ownOperation);
        }

        /// <summary> 잠금이 이 창의 요청 종류이고 결과 불명이라 이 창이 재시도할 수 있으면 참 </summary>
        public static bool CanRetry(bool isHeld, bool isOutcomeUnknown, EGrowthAction heldAction, Guid heldOperation,
            Guid ownOperation, EGrowthAction[] ownActions)
        {
            if ((!isHeld) || (!isOutcomeUnknown) || (ownActions == null))
            {
                return false;
            }

            if ((ownOperation != Guid.Empty) && (heldOperation != ownOperation))
            {
                return false;
            }

            return Array.IndexOf(ownActions, heldAction) >= 0;
        }
    }
}
