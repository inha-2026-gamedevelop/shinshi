// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 인증 수단을 접근 토큰으로 바꾸는 요청, 초대 코드와 갱신 토큰 중 하나만 채운다 </summary>
    [DataContract]
    public sealed class AuthExchangeRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 첫 로그인. 운영자가 발급한 일회용 코드이며 교환과 함께 소비된다
        [DataMember(Name = "inviteCode")]   private string          _inviteCode;

        // 자동 로그인. 회전형이라 응답을 잃으면 같은 토큰을 다시 보내지 않고 재로그인한다
        // TODO(민성): 갱신을 이 요청으로 받을지 별도 경로로 둘지 기획 미정
        [DataMember(Name = "refreshToken")] private string          _refreshToken;

        // 서버와 protocolVersion, contentHash가 다르면 토큰을 발급하지 않고 거절한다
        [DataMember(Name = "build")]        private ClientBuildInfo _build;

        /****************************************
        *              Properties
        ****************************************/

        public string          InviteCode   => _inviteCode;
        public string          RefreshToken => _refreshToken;
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

        private AuthExchangeRequest() { }
    }
}
