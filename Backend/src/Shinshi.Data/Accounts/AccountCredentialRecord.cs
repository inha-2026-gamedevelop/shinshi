// System
using System;

namespace Shinshi.Data.Accounts
{
    /// <summary> 로그인 검증에 필요한 자격 행. 해시는 API가 비교에만 쓰고 응답이나 로그로 내보내지 않는다 </summary>
    public sealed class AccountCredentialRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId { get; }
        public string LoginId { get; }
        public string PasswordHash { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AccountCredentialRecord(Guid accountId, string loginId, string passwordHash)
        {
            ArgumentException.ThrowIfNullOrEmpty(loginId);
            ArgumentException.ThrowIfNullOrEmpty(passwordHash);
            AccountId    = accountId;
            LoginId      = loginId;
            PasswordHash = passwordHash;
        }

        // 해시가 실수로 로그에 직렬화되지 않게 계정 ID만 보인다
        public override string ToString()
        {
            return AccountId.ToString("D");
        }
    }
}
