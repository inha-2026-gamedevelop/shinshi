// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 인증 교환 성공 응답, 토큰은 메모리나 OS 보호 저장소에만 두고 로그와 PlayerPrefs에 남기지 않는다 </summary>
    [DataContract]
    public sealed class AuthExchangeResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]                   private string _accountId;

        [DataMember(Name = "accessToken")]                 private string _accessToken;
        [DataMember(Name = "accessTokenExpiresAtUnixMs")]  private long   _accessTokenExpiresAtUnixMs;

        // 새로 회전된 토큰. 받은 즉시 이전 토큰은 폐기되어 다시 쓸 수 없다
        [DataMember(Name = "refreshToken")]                private string _refreshToken;
        [DataMember(Name = "refreshTokenExpiresAtUnixMs")] private long   _refreshTokenExpiresAtUnixMs;

        // 만료 판정은 서버 시각 기준이다. 클라이언트는 남은 시간을 이 값과의 차로 계산해 시계 차이를 피한다
        [DataMember(Name = "serverTimeUnixMs")]            private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string AccountId                   => _accountId;
        public string AccessToken                 => _accessToken;
        public long   AccessTokenExpiresAtUnixMs  => _accessTokenExpiresAtUnixMs;
        public string RefreshToken                => _refreshToken;
        public long   RefreshTokenExpiresAtUnixMs => _refreshTokenExpiresAtUnixMs;
        public long   ServerTimeUnixMs            => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AuthExchangeResponse(string accountId, string accessToken, long accessTokenExpiresAtUnixMs,
            string refreshToken, long refreshTokenExpiresAtUnixMs, long serverTimeUnixMs)
        {
            _accountId                   = accountId;
            _accessToken                 = accessToken;
            _accessTokenExpiresAtUnixMs  = accessTokenExpiresAtUnixMs;
            _refreshToken                = refreshToken;
            _refreshTokenExpiresAtUnixMs = refreshTokenExpiresAtUnixMs;
            _serverTimeUnixMs            = serverTimeUnixMs;
        }

        private AuthExchangeResponse() { }
    }
}
