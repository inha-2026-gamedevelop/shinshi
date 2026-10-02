// System
using System;

namespace Shinshi.Data.Auth
{
    /// <summary> 내부 입장권 발급 거절의 공유 사유를 HTTP 경계까지 전달한다 </summary>
    public sealed class JoinTokenIssuanceException : Exception
    {
        /****************************************
        *              Properties
        ****************************************/

        public int ReasonCode { get; }

        /****************************************
        *                Methods
        ****************************************/

        public JoinTokenIssuanceException(int reasonCode)
            : base("The dungeon join ticket could not be issued.")
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reasonCode);
            ReasonCode = reasonCode;
        }
    }
}