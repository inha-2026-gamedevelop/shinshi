// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 서버 임대 토큰, 이후 서버 전용 경로는 이 토큰을 Bearer로 싣는다. 만료 전에 같은 경로로 다시 받는다 </summary>
    [DataContract]
    public sealed class ServerLeaseResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "leaseToken")]       private string _leaseToken;
        [DataMember(Name = "expiresAtUnixMs")]  private long   _expiresAtUnixMs;
        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string LeaseToken       => _leaseToken;
        public long   ExpiresAtUnixMs  => _expiresAtUnixMs;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public ServerLeaseResponse(string leaseToken, long expiresAtUnixMs, long serverTimeUnixMs)
        {
            _leaseToken       = leaseToken;
            _expiresAtUnixMs  = expiresAtUnixMs;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private ServerLeaseResponse() { }
    }
}
