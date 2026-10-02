// System
using System.Runtime.Serialization;

namespace Shinshi.AdminCli
{
    /// <summary> 로그인 결과를 저장하는 세션 파일 내용. 토큰은 이 파일에만 있고 화면, 로그, 예외 메시지에 싣지 않는다 </summary>
    [DataContract]
    internal sealed class AdminSession
    {
        /****************************************
        *                Fields
        ****************************************/

        // 토큰을 받은 API. 다른 --api로 이 토큰을 보내지 않으려고 함께 저장한다
        [DataMember(Name = "api")]             private string _api = string.Empty;
        [DataMember(Name = "accessToken")]     private string _accessToken = string.Empty;
        [DataMember(Name = "expiresAtUnixMs")] private long   _expiresAtUnixMs;
        [DataMember(Name = "adminId")]         private string _adminId = string.Empty;
        [DataMember(Name = "loginName")]       private string _loginName = string.Empty;

        // AdminContractCodes.Role
        [DataMember(Name = "role")]            private int    _role;

        /****************************************
        *              Properties
        ****************************************/

        public string Api             => _api;
        public string AccessToken     => _accessToken;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;
        public string AdminId         => _adminId;
        public string LoginName       => _loginName;
        public int    Role            => _role;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSession(string api, string accessToken, long expiresAtUnixMs, string adminId, string loginName,
            int role)
        {
            _api             = api;
            _accessToken     = accessToken;
            _expiresAtUnixMs = expiresAtUnixMs;
            _adminId         = adminId;
            _loginName       = loginName;
            _role            = role;
        }

        private AdminSession() { }

        /// <summary> 파일에서 읽은 값이 쓸 수 있는 형태인지 본다. 손상된 파일은 로그인 없음으로 처리한다 </summary>
        public bool IsWellFormed()
        {
            return (!string.IsNullOrEmpty(_api)) && (!string.IsNullOrEmpty(_accessToken)) && (_expiresAtUnixMs > 0);
        }
    }
}
