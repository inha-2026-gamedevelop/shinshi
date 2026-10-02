// System
using System;

namespace Shinshi.Data.Auth
{
    /// <summary> 신뢰된 내부 발급 호출에만 돌려주는 원문이며 로그나 DB에 원문을 남기지 않는다 </summary>
    public sealed class IssuedAuthToken
    {
        /****************************************
        *              Properties
        ****************************************/

        public string Token { get; }
        public Guid TokenId { get; }
        public long ExpiresAtUnixMs { get; }
        public long ServerTimeUnixMs { get; }

        /****************************************
        *                Methods
        ****************************************/

        public IssuedAuthToken(string token, Guid tokenId, long expiresAtUnixMs, long serverTimeUnixMs)
        {
            Token            = token;
            TokenId          = tokenId;
            ExpiresAtUnixMs  = expiresAtUnixMs;
            ServerTimeUnixMs = serverTimeUnixMs;
        }
    }
}
