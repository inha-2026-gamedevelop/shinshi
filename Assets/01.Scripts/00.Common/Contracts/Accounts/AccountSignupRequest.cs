// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 공개 회원가입 요청, 형식 규칙은 AccountCredentialRules와 같다 </summary>
    [DataContract]
    public sealed class AccountSignupRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 대문자로 보내도 소문자로 바꿔 저장하고 비교한다
        [DataMember(Name = "loginId")]  private string _loginId;

        // 이 요청에만 싣고 로그와 파일에 남기지 않는다
        [DataMember(Name = "password")] private string _password;

        // 인증 메일은 아직 없어 가입 직후에는 emailVerified = false다
        [DataMember(Name = "email")]    private string _email;

        /****************************************
        *              Properties
        ****************************************/

        public string LoginId  => _loginId;
        public string Password => _password;
        public string Email    => _email;

        /****************************************
        *                Methods
        ****************************************/

        public AccountSignupRequest(string loginId, string password, string email)
        {
            _loginId  = loginId;
            _password = password;
            _email    = email;
        }

        private AccountSignupRequest() { }
    }
}
