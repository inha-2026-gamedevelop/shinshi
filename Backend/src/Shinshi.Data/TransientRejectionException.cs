// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data
{
    /// <summary> 지금은 판정할 수 없어 저장하지 않고 거절한다. 효과와 operations 결과를 모두 되돌리므로 같은 operationId로 다시 시도할 수 있다.
    /// API는 503, reasonCode, Retry-After로 응답한다 </summary>
    public sealed class TransientRejectionException : Exception
    {
        /****************************************
        *              Properties
        ****************************************/

        public int ReasonCode        { get; }
        public int RetryAfterSeconds { get; }

        /****************************************
        *                Methods
        ****************************************/

        public TransientRejectionException(int reasonCode, int retryAfterSeconds)
            : base("The request cannot be decided now and was not stored.")
        {
            if (retryAfterSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retryAfterSeconds));
            }

            ReasonCode        = reasonCode;
            RetryAfterSeconds = retryAfterSeconds;
        }

        /// <summary> 배정 boot는 살아 있을 수 있지만 heartbeat가 끊겨 생존을 확정할 수 없다 </summary>
        public static TransientRejectionException LivenessUnknown(int retryAfterSeconds)
        {
            return new TransientRejectionException(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, retryAfterSeconds);
        }
    }
}
