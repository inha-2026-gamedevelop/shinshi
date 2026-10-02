// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 어드민 접근 토큰. 게임 경로에서는 쓸 수 없고 만료되면 다시 로그인한다 </summary>
    [DataContract]
    public sealed class AdminLoginResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        // Authorization: Bearer 로 보낸다. 서버는 해시만 저장하므로 잃으면 다시 로그인한다
        [DataMember(Name = "accessToken")]     private string _accessToken;
        [DataMember(Name = "expiresAtUnixMs")] private long   _expiresAtUnixMs;
        [DataMember(Name = "adminId")]         private string _adminId;

        // AdminContractCodes.Role
        [DataMember(Name = "role")]            private int    _role;

        /****************************************
        *              Properties
        ****************************************/

        public string AccessToken     => _accessToken;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;
        public string AdminId         => _adminId;
        public int    Role            => _role;

        /****************************************
        *                Methods
        ****************************************/

        public AdminLoginResponse(string accessToken, long expiresAtUnixMs, string adminId, int role)
        {
            _accessToken     = accessToken;
            _expiresAtUnixMs = expiresAtUnixMs;
            _adminId         = adminId;
            _role            = role;
        }

        private AdminLoginResponse() { }
    }
}
