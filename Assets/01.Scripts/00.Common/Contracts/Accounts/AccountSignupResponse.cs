// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 가입 결과, 토큰은 주지 않으므로 이어서 AuthExchangeRequest.ForCredentials로 로그인한다 </summary>
    [DataContract]
    public sealed class AccountSignupResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]        private string _accountId;

        // 저장된 소문자 아이디
        [DataMember(Name = "loginId")]          private string _loginId;
        [DataMember(Name = "emailVerified")]    private bool   _emailVerified;
        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string AccountId        => _accountId;
        public string LoginId          => _loginId;
        public bool   EmailVerified    => _emailVerified;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AccountSignupResponse(string accountId, string loginId, bool emailVerified, long serverTimeUnixMs)
        {
            _accountId        = accountId;
            _loginId          = loginId;
            _emailVerified    = emailVerified;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private AccountSignupResponse() { }
    }
}
