// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 비밀번호 변경, 또는 아이디가 없는 계정의 아이디, 이메일, 비밀번호 설정 </summary>
    [DataContract]
    public sealed class AccountCredentialsRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 아이디가 있는 계정만 보낸다. 아이디가 없는 계정(초대, 복구 코드로 들어온 계정)은 null이다
        [DataMember(Name = "currentPassword")] private string _currentPassword;
        [DataMember(Name = "newPassword")]     private string _newPassword;

        // 아이디가 없는 계정만 보낸다. 아이디가 있는 계정이 보내면 거절한다
        [DataMember(Name = "loginId")]         private string _loginId;
        [DataMember(Name = "email")]           private string _email;

        /****************************************
        *              Properties
        ****************************************/

        public string CurrentPassword => _currentPassword;
        public string NewPassword     => _newPassword;
        public string LoginId         => _loginId;
        public string Email           => _email;

        /****************************************
        *                Methods
        ****************************************/

        public AccountCredentialsRequest(string currentPassword, string newPassword, string loginId, string email)
        {
            _currentPassword = currentPassword;
            _newPassword     = newPassword;
            _loginId         = loginId;
            _email           = email;
        }

        // 아이디가 있는 계정의 비밀번호 변경
        public static AccountCredentialsRequest ForPasswordChange(string currentPassword, string newPassword)
        {
            return new AccountCredentialsRequest(currentPassword, newPassword, null, null);
        }

        // 아이디가 없는 계정의 첫 자격 설정
        public static AccountCredentialsRequest ForInitialCredentials(string loginId, string email, string newPassword)
        {
            return new AccountCredentialsRequest(null, newPassword, loginId, email);
        }

        private AccountCredentialsRequest() { }
    }
}
