// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 출정 상태를 결과 창 값으로 옮기는 규칙 </summary>
    public static class SortieResultRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly IReadOnlyList<ItemSlotData> G_NoItems = Array.Empty<ItemSlotData>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 출정이 끝났거나 나만 먼저 빠졌으면 결과 창을 띄운다 </summary>
        public static bool ShouldShow(in SortieStatusMessage status)
        {
            return (status.HasPersonalClose) || (status.Phase == ESortiePhase.Ended) || (status.Phase == ESortiePhase.Auction)
                || (status.Phase == ESortiePhase.Closed);
        }

        /// <summary> 내 종료 사유가 있으면 그것을, 없으면 출정 종료 사유를 쓴다 </summary>
        public static ESessionEndReason ResolveReason(in SortieStatusMessage status)
        {
            return status.HasPersonalClose ? status.PersonalCloseReason : status.EndReason;
        }

        /// <summary> 정산 복구로 넘어간 출정은 다시 확인 중으로 보여 준다 </summary>
        public static ESaveStatus ToSaveStatus(ESortieSettlementState state)
        {
            switch (state)
            {
                case ESortieSettlementState.Settled:
                    return ESaveStatus.Saved;
                case ESortieSettlementState.Deferred:
                    return ESaveStatus.Retrying;
                case ESortieSettlementState.Pending:
                    return ESaveStatus.Saving;
                default:
                    return ESaveStatus.Idle;
            }
        }

        // 정산 보상 내역은 클라이언트로 오는 메시지가 아직 없어 정기와 아이템을 비워 둔다
        public static ResultScreenData Build(in SortieStatusMessage status, string regionName)
        {
            return new ResultScreenData(ResolveReason(in status), regionName ?? string.Empty, 0, 0, 0, G_NoItems, G_NoItems);
        }
    }
}
