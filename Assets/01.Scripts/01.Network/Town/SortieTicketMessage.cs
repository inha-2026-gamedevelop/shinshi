// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 출정 예약이 확정되면 마을 서버가 API에서 받은 참가자 본인의 일회용 던전 입장권만 그 참가자에게 보낸다.
    // 클라이언트는 마을 연결을 끊고 address, port로 접속해 joinToken으로 인증한다. 다른 참가자의 토큰은 받지 않는다
    /// <summary> 내 던전 입장권 </summary>
    public readonly struct SortieTicketMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _sessionId;
        private readonly string _instanceId;
        private readonly string _address;
        private readonly int    _port;
        private readonly string _joinToken;
        private readonly long   _expiresAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string SessionId       => _sessionId;
        public string InstanceId      => _instanceId;
        public string Address         => _address;
        public int    Port            => _port;
        public string JoinToken       => _joinToken;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public SortieTicketMessage(string sessionId, string instanceId, string address, int port, string joinToken,
            long expiresAtUnixMs)
        {
            _sessionId       = sessionId;
            _instanceId      = instanceId;
            _address         = address;
            _port            = port;
            _joinToken       = joinToken;
            _expiresAtUnixMs = expiresAtUnixMs;
        }
    }
}
