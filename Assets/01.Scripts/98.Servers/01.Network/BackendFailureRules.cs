// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 백엔드 거절이 다시 보내도 결과가 같은 확정 거절인지 가른다, 저장 큐와 정산이 같은 기준을 쓴다 </summary>
    public static class BackendFailureRules
    {
        // 같은 operationId로 다시 보내면 API는 처음 저장한 거절을 그대로 돌려준다. 이런 거절을 일시 실패로 보고 계속 보내면
        // 큐가 영원히 막히고 출정이 끝나지 않는다. 반대로 401(임대 만료), 404/409(아직 반영 전)처럼 상황이 바뀌면 통과할
        // 수 있는 거절까지 확정으로 보면 정상 출정을 장애 취소로 끝낸다. 그래서 400과 저장되는 거절 사유만 확정으로 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int STATUS_BAD_REQUEST = 400;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 확정 거절이면 참, 성공과 일시 실패, 4xx 중 재시도로 바뀔 수 있는 것은 거짓 </summary>
        public static bool IsTerminalRejection(in BackendResponse response)
        {
            if (response.Outcome != EBackendOutcome.Rejected)
            {
                return false;
            }

            return (response.StatusCode == STATUS_BAD_REQUEST) || (IsStoredRejectionReason(response.ReasonCode));
        }

        /// <summary> API가 operationId와 함께 저장해 재시도에도 같은 값을 돌려주는 거절 사유 </summary>
        public static bool IsStoredRejectionReason(int reasonCode)
        {
            switch (reasonCode)
            {
                case ContractCodes.RejectReason.POTION_LIMIT:
                case ContractCodes.RejectReason.STALE_REVISION:
                case ContractCodes.RejectReason.SESSION_MISMATCH:
                case ContractCodes.RejectReason.SESSION_TERMINAL:
                case ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT:
                case ContractCodes.RejectReason.INSUFFICIENT_ITEM:
                case ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED:
                    return true;
                default:
                    return false;
            }
        }
    }
}
