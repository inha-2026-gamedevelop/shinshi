// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 게임 전용 하위 세션, 런처 세션이 폐기되면 함께 폐기된다 </summary>
    [DataContract]
    public sealed class GameSessionIssueResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]        private string _accountId;

        // 런처가 파이프로 한 번만 넘긴다. 실행 인자, 환경 변수, 로그에 넣지 않는다
        [DataMember(Name = "gameSessionToken")] private string _gameSessionToken;
        [DataMember(Name = "expiresAtUnixMs")]  private long   _expiresAtUnixMs;

        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string AccountId        => _accountId;
        public string GameSessionToken => _gameSessionToken;
        public long   ExpiresAtUnixMs  => _expiresAtUnixMs;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public GameSessionIssueResponse(string accountId, string gameSessionToken, long expiresAtUnixMs,
            long serverTimeUnixMs)
        {
            _accountId        = accountId;
            _gameSessionToken = gameSessionToken;
            _expiresAtUnixMs  = expiresAtUnixMs;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private GameSessionIssueResponse() { }
    }
}
