// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Auth
{
    /// <summary> 인증에 성공한 DB 주체만 반환하며 입력 본문의 ID를 인증 결과로 사용하지 않는다 </summary>
    public sealed class AuthValidationOutcome<T> where T : class
    {
        /****************************************
        *              Properties
        ****************************************/

        public T? Identity { get; }
        public int ReasonCode { get; }
        public bool IsSuccess => Identity != null;

        /****************************************
        *                Methods
        ****************************************/

        private AuthValidationOutcome(T? identity, int reasonCode)
        {
            Identity   = identity;
            ReasonCode = reasonCode;
        }

        public static AuthValidationOutcome<T> Success(T identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            return new AuthValidationOutcome<T>(identity, ContractCodes.RejectReason.NONE);
        }

        public static AuthValidationOutcome<T> Rejected(int reasonCode)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reasonCode);
            return new AuthValidationOutcome<T>(null, reasonCode);
        }
    }
}
