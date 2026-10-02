// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 로그인한 계정의 정보, 아이디가 없는 계정(초대 코드로 만든 계정)은 loginId와 email이 null이다 </summary>
    [DataContract]
    public sealed class AccountProfileResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]       private string _accountId;
        [DataMember(Name = "loginId")]         private string _loginId;
        [DataMember(Name = "email")]           private string _email;

        // 인증 메일이 없는 동안은 항상 false다
        [DataMember(Name = "emailVerified")]   private bool   _emailVerified;
        [DataMember(Name = "createdAtUnixMs")] private long   _createdAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string AccountId       => _accountId;
        public string LoginId         => _loginId;
        public string Email           => _email;
        public bool   EmailVerified   => _emailVerified;
        public long   CreatedAtUnixMs => _createdAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AccountProfileResponse(string accountId, string loginId, string email, bool emailVerified,
            long createdAtUnixMs)
        {
            _accountId       = accountId;
            _loginId         = loginId;
            _email           = email;
            _emailVerified   = emailVerified;
            _createdAtUnixMs = createdAtUnixMs;
        }

        private AccountProfileResponse() { }
    }
}
