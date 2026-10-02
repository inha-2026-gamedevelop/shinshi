// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Auth
{
    /// <summary> 성공 토큰과 기존 공유 거절 코드를 함께 표현한다 </summary>
    public sealed class AuthExchangeOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public AuthExchangeResponse? Response { get; }
        public int ReasonCode { get; }
        public bool IsSuccess => Response != null;

        /****************************************
        *                Methods
        ****************************************/

        private AuthExchangeOutcome(AuthExchangeResponse? response, int reasonCode)
        {
            Response   = response;
            ReasonCode = reasonCode;
        }

        public static AuthExchangeOutcome Success(AuthExchangeResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            return new AuthExchangeOutcome(response, ContractCodes.RejectReason.NONE);
        }

        public static AuthExchangeOutcome Rejected(int reasonCode)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reasonCode);
            return new AuthExchangeOutcome(null, reasonCode);
        }
    }
}
