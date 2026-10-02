// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Accounts
{
    /// <summary> 가입 결과. 성공이면 새 계정 ID와 DB 시각, 실패면 LOGIN_ID_TAKEN이다 </summary>
    public sealed class AccountSignupOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId { get; }
        public DateTimeOffset ServerTime { get; }
        public int ReasonCode { get; }
        public bool IsSuccess => ReasonCode == ContractCodes.RejectReason.NONE;

        /****************************************
        *                Methods
        ****************************************/

        private AccountSignupOutcome(Guid accountId, DateTimeOffset serverTime, int reasonCode)
        {
            AccountId  = accountId;
            ServerTime = serverTime;
            ReasonCode = reasonCode;
        }

        public static AccountSignupOutcome Success(Guid accountId, DateTimeOffset serverTime)
        {
            return new AccountSignupOutcome(accountId, serverTime, ContractCodes.RejectReason.NONE);
        }

        public static AccountSignupOutcome Rejected(int reasonCode, DateTimeOffset serverTime)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reasonCode);
            return new AccountSignupOutcome(Guid.Empty, serverTime, reasonCode);
        }
    }
}
