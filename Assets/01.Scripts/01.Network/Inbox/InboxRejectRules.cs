// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 우편 API 응답을 화면용 거절 사유로 바꾼다 </summary>
    public static class InboxRejectRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long STATUS_NONE        = 0;
        private const long STATUS_REQUEST_TIMEOUT = 408;
        private const long STATUS_TOO_MANY    = 429;
        private const long STATUS_SERVER_MIN  = 500;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 사유 코드가 ERejectReason에 있으면 그 값, 없으면 상태 코드로 짐작한다 </summary>
        public static ERejectReason FromReply(int reasonCode, long statusCode)
        {
            if ((reasonCode != ContractCodes.RejectReason.NONE) && (Enum.IsDefined(typeof(ERejectReason), reasonCode)))
            {
                return (ERejectReason)reasonCode;
            }

            return IsUnknownOutcome(statusCode, reasonCode) ? ERejectReason.BackendUnavailable : ERejectReason.InvalidTarget;
        }

        /// <summary> 응답이 없거나 서버 오류이면 결과를 모른다. 사유 코드를 받은 거절은 확정이다 </summary>
        public static bool IsUnknownOutcome(long statusCode, int reasonCode)
        {
            if (reasonCode != ContractCodes.RejectReason.NONE)
            {
                return false;
            }

            return (statusCode == STATUS_NONE) || (statusCode == STATUS_REQUEST_TIMEOUT) || (statusCode == STATUS_TOO_MANY)
                || (statusCode >= STATUS_SERVER_MIN);
        }
    }
}
