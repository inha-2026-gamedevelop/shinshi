// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 운영자 로그인. 비밀번호와 TOTP를 함께 보내야 하며 실패 원인은 응답에서 구분하지 않는다 </summary>
    [DataContract]
    public sealed class AdminLoginRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 대소문자를 구분하지 않는다. 서버가 소문자로 바꿔 찾는다
        [DataMember(Name = "loginName")] private string _loginName;
        [DataMember(Name = "password")]  private string _password;

        // 인증 앱의 6자리 코드. 한 번 쓴 코드는 같은 30초 안에서도 다시 받지 않는다
        [DataMember(Name = "totpCode")]  private string _totpCode;

        /****************************************
        *              Properties
        ****************************************/

        public string LoginName => _loginName;
        public string Password  => _password;
        public string TotpCode  => _totpCode;

        /****************************************
        *                Methods
        ****************************************/

        public AdminLoginRequest(string loginName, string password, string totpCode)
        {
            _loginName = loginName;
            _password  = password;
            _totpCode  = totpCode;
        }

        private AdminLoginRequest() { }
    }
}
