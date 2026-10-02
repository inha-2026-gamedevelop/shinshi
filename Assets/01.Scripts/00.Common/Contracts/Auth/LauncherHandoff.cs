// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 런처가 파이프로 한 번 넘기는 게임 세션, 게임은 메모리에만 두고 로그와 파일에 남기지 않는다 </summary>
    [DataContract]
    public sealed class LauncherHandoff
    {
        /****************************************
        *                Fields
        ****************************************/

        // LauncherPipeProtocol.PROTOCOL_VERSION과 다르면 게임이 받지 않는다
        [DataMember(Name = "protocolVersion")]  private int    _protocolVersion;

        [DataMember(Name = "accountId")]        private string _accountId;
        [DataMember(Name = "gameSessionToken")] private string _gameSessionToken;
        [DataMember(Name = "expiresAtUnixMs")]  private long   _expiresAtUnixMs;

        // 만료까지 남은 시간은 이 값과의 차로 계산해 PC 시계 차이를 피한다
        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        // 런처가 쓴 API 주소, 게임이 같은 API로 세션을 갱신하고 접속 토큰을 받는다
        [DataMember(Name = "apiBaseUrl")]       private string _apiBaseUrl;

        /****************************************
        *              Properties
        ****************************************/

        public int    ProtocolVersion  => _protocolVersion;
        public string AccountId        => _accountId;
        public string GameSessionToken => _gameSessionToken;
        public long   ExpiresAtUnixMs  => _expiresAtUnixMs;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;
        public string ApiBaseUrl       => _apiBaseUrl;

        /****************************************
        *                Methods
        ****************************************/

        public LauncherHandoff(int protocolVersion, string accountId, string gameSessionToken, long expiresAtUnixMs,
            long serverTimeUnixMs, string apiBaseUrl)
        {
            _protocolVersion  = protocolVersion;
            _accountId        = accountId;
            _gameSessionToken = gameSessionToken;
            _expiresAtUnixMs  = expiresAtUnixMs;
            _serverTimeUnixMs = serverTimeUnixMs;
            _apiBaseUrl       = apiBaseUrl;
        }

        private LauncherHandoff() { }
    }
}
