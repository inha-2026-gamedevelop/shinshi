// System
using System;

namespace Shinshi.Data.Accounts
{
    /// <summary> 계정 정보 조회 결과. 자격 행이 없는 계정(초대 코드 계정)은 아이디와 이메일이 null이다 </summary>
    public sealed class AccountProfileRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId { get; }
        public string? LoginId { get; }
        public string? Email { get; }
        public bool EmailVerified { get; }
        public DateTimeOffset CreatedAt { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AccountProfileRecord(Guid accountId, string? loginId, string? email, bool emailVerified,
            DateTimeOffset createdAt)
        {
            AccountId     = accountId;
            LoginId       = loginId;
            Email         = email;
            EmailVerified = emailVerified;
            CreatedAt     = createdAt;
        }
    }
}
