// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 인증 수단을 접근 토큰으로 바꾸는 요청, 초대 코드, 갱신 토큰, 아이디와 비밀번호 중 정확히 하나만 채운다 </summary>
    [DataContract]
    public sealed class AuthExchangeRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 첫 로그인. 운영자가 발급한 일회용 코드이며 교환과 함께 소비된다
        [DataMember(Name = "inviteCode")]   private string          _inviteCode;

        // 자동 로그인. 재시도는 최초 전송 전에 보관한 refreshRequestId를 같은 토큰과 함께 보낸다
        [DataMember(Name = "refreshToken")] private string          _refreshToken;
        [DataMember(Name = "refreshRequestId", EmitDefaultValue = false)] private string _refreshRequestId;

        // 런처와 홈페이지의 일반 로그인. 비밀번호는 이 요청에만 싣고 로그와 파일에 남기지 않는다
        [DataMember(Name = "loginId")]      private string          _loginId;
        [DataMember(Name = "password")]     private string          _password;

        // 서버와 protocolVersion, contentHash가 다르면 토큰을 발급하지 않고 거절한다
        [DataMember(Name = "build")]        private ClientBuildInfo _build;

        /****************************************
        *              Properties
        ****************************************/

        public string          InviteCode   => _inviteCode;
        public string          RefreshToken => _refreshToken;
        public string          RefreshRequestId => _refreshRequestId;
        public string          LoginId      => _loginId;
        public string          Password     => _password;
        public ClientBuildInfo Build        => _build;

        /****************************************
        *                Methods
        ****************************************/

        public AuthExchangeRequest(string inviteCode, string refreshToken, ClientBuildInfo build)
        {
            _inviteCode   = inviteCode;
            _refreshToken = refreshToken;
            _build        = build;
        }

        // 기존 생성자와 매개변수 모양이 같아 이름 있는 생성 메서드로 구분한다
        public static AuthExchangeRequest ForCredentials(string loginId, string password, ClientBuildInfo build)
        {
            var request = new AuthExchangeRequest(null, null, build);
            request._loginId  = loginId;
            request._password = password;
            return request;
        }

        private AuthExchangeRequest() { }

        public static AuthExchangeRequest ForRefresh(string refreshToken, string requestId)
        {
            var request = new AuthExchangeRequest(null, refreshToken, null);
            request._refreshRequestId = requestId;
            return request;
        }
    }
}
