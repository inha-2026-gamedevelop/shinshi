// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 출정 종료, 경매, 귀환, 단계 판정 규칙, 호스트가 틱 순서와 부수효과를 맡고 판정만 여기서 한다 </summary>
    public static class SortieEndRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> API는 보스 성공 정산 뒤 출정을 Auction으로 두므로 품목이 없어도 경매 진행기를 시작해야 한다 </summary>
        public static bool ShouldStartAuction(bool hasAuction, ESessionSettlementState settlement, bool isBossSession,
            ESessionEndReason endReason)
        {
            return (!hasAuction) && (settlement == ESessionSettlementState.Settled) && (isBossSession)
                && (endReason == ESessionEndReason.Success);
        }

        // API는 정산 확정 때 참가자 계정 임대를 푼다. 보스 성공은 경매 동안 던전이 임대를 쥐고 경매 닫기(finish)에서 푼다.
        // 장애 취소와 정산 확정 거절은 API 복구 작업이 풀어 기다릴 신호가 없으므로 종료 저장 뒤 허용하고 입장권 재시도에 맡긴다
        /// <summary> 개인 이탈이 아닌 참가자가 종료 뒤 마을로 갈 수 있는지, 종료 이벤트 저장 확인 전에는 항상 거짓이다 </summary>
        public static bool CanReturnAfterEnd(bool isEndStored, ESessionEndReason endReason, ESessionSettlementState settlement,
            bool hasAuction, bool isAuctionFinished, bool isBossSession)
        {
            if (!isEndStored)
            {
                return false;
            }

            if ((endReason == ESessionEndReason.ServiceCancelled) || (settlement == ESessionSettlementState.Abandoned))
            {
                return true;
            }

            if (settlement != ESessionSettlementState.Settled)
            {
                return false;
            }

            if (hasAuction)
            {
                return isAuctionFinished;
            }

            // 정산 응답은 틱 사이(Pump)에 오므로 경매가 다음 틱에 시작되기 전일 수 있다
            return (!isBossSession) || (endReason != ESessionEndReason.Success);
        }

        /// <summary> 종료된 세션의 종결 사유, 아직 닫을 수 없으면 None </summary>
        public static ESortieTerminalReason DecideTerminal(ESessionEndReason endReason, bool hasTerminalStorage, bool isEndStored,
            ESessionSettlementState settlement, bool isAuctionPending)
        {
            if (endReason == ESessionEndReason.ServiceCancelled)
            {
                // 확정 거절로 멈춘 큐는 더 저장하지 않는다. 그 밖에는 종료 기록이 저장된 뒤 내린다
                return ((hasTerminalStorage) || (isEndStored)) ? ESortieTerminalReason.ServiceCancelled : ESortieTerminalReason.None;
            }

            if (hasTerminalStorage)
            {
                // 종료 이벤트 저장 전에 큐가 멈췄다. 결과를 확정할 수 없어 API 장애 취소가 맡는다
                return ESortieTerminalReason.ServiceCancelled;
            }

            if (settlement == ESessionSettlementState.Settled)
            {
                // 보스 성공은 파티 경매와 출정 닫기(finish)까지 끝나야 내린다
                return isAuctionPending ? ESortieTerminalReason.None : ESortieTerminalReason.Settled;
            }

            return (settlement == ESessionSettlementState.Abandoned) ? ESortieTerminalReason.SettlementAbandoned
                : ESortieTerminalReason.None;
        }

        public static ESortiePhase DecidePhase(bool isTerminal, bool hasSession, bool isRunning, bool isAuctionPending)
        {
            if (isTerminal)
            {
                return ESortiePhase.Closed;
            }

            if (!hasSession)
            {
                return ESortiePhase.Waiting;
            }

            if (isRunning)
            {
                return ESortiePhase.Running;
            }

            return isAuctionPending ? ESortiePhase.Auction : ESortiePhase.Ended;
        }

        /// <summary> 종료된 세션의 정산 상태를 클라이언트 표시용으로 바꾼다, 확정 거절은 Deferred다 </summary>
        public static ESortieSettlementState DecideSettlementState(ESessionSettlementState settlement)
        {
            switch (settlement)
            {
                case ESessionSettlementState.Settled:
                    return ESortieSettlementState.Settled;
                case ESessionSettlementState.Abandoned:
                    return ESortieSettlementState.Deferred;
                default:
                    return ESortieSettlementState.Pending;
            }
        }
    }
}
