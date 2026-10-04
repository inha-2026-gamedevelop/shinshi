// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 마을 서버의 API 응답 분류, 확정 거절 사유와 다시 보내야 하는 일시 실패를 가른다 </summary>
    public static class TownRejectRules
    {
        // 마을 요청은 플레이어가 기다리는 짧은 작업이다. 401은 서버 임대 갱신 사이의 거절이라 처리되지 않았으므로 일시 실패로 보고,
        // 그 밖의 4xx는 API가 내린 결론으로 본다. 2xx라도 PENDING이면 같은 작업이 처리 중이라 확정이 아니다

        /****************************************
        *                Fields
        ****************************************/

        private const int STATUS_UNAUTHORIZED = 401;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> API 사유 코드를 ERejectReason으로 바꾼다, 0이나 모르는 값은 fallback </summary>
        public static ERejectReason FromApi(int reasonCode, ERejectReason fallback)
        {
            if ((reasonCode == ContractCodes.RejectReason.NONE) || (!Enum.IsDefined(typeof(ERejectReason), reasonCode)))
            {
                return fallback;
            }

            return (ERejectReason)reasonCode;
        }

        /// <summary> API가 결론을 낸 거절이면 참, 다시 보내면 결과가 바뀔 수 있는 실패면 거짓 </summary>
        public static bool IsDefiniteRejection(in BackendResponse response)
        {
            return (response.Outcome == EBackendOutcome.Rejected) && (response.StatusCode != STATUS_UNAUTHORIZED);
        }

        /// <summary> 응답이 확정한 작업 결과, 확정이 아니면(일시 실패, PENDING) null </summary>
        public static OperationResult ReadDefiniteResult(in BackendResponse response, OperationResult nested)
        {
            if ((nested == null) || (nested.Status == ContractCodes.OperationStatus.PENDING))
            {
                return null;
            }

            return ((response.IsSucceeded) || (IsDefiniteRejection(in response))) ? nested : null;
        }

        /// <summary> 거절 사유, 본문 안쪽 result의 사유를 먼저 보고 없으면 거절 본문 사유를 쓴다 </summary>
        public static ERejectReason ReasonOf(in BackendResponse response, OperationResult nested, ERejectReason fallback)
        {
            if ((nested != null) && (nested.ReasonCode != ContractCodes.RejectReason.NONE))
            {
                return FromApi(nested.ReasonCode, fallback);
            }

            return FromApi(response.ReasonCode, fallback);
        }
    }
}
