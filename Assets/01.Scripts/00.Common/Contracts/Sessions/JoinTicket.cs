// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 마을이나 던전 서버에 들어갈 일회용 입장권, 서버 인증기가 한 번만 교환한다 </summary>
    [DataContract]
    public sealed class JoinTicket
    {
        /****************************************
        *                Fields
        ****************************************/

        // 참가자와 배정 인스턴스에 묶여 있어 다른 서버나 다른 캐릭터로는 쓸 수 없다. 장기 토큰 대신 이것만 Mirror로 보낸다
        [DataMember(Name = "joinToken")]        private string _joinToken;
        [DataMember(Name = "expiresAtUnixMs")]  private long   _expiresAtUnixMs;

        // ContractCodes.ServerRole
        [DataMember(Name = "serverRole")]       private int    _serverRole;
        [DataMember(Name = "instanceId")]       private string _instanceId;
        [DataMember(Name = "address")]          private string _address;
        [DataMember(Name = "port")]             private int    _port;

        [DataMember(Name = "characterId")]      private string _characterId;

        // 던전 출정의 영구 참가자 ID, 마을 입장권에서는 빈 값이다
        [DataMember(Name = "participantId")]    private string _participantId;

        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string JoinToken        => _joinToken;
        public long   ExpiresAtUnixMs  => _expiresAtUnixMs;
        public int    ServerRole       => _serverRole;
        public string InstanceId       => _instanceId;
        public string Address          => _address;
        public int    Port             => _port;
        public string CharacterId      => _characterId;
        public string ParticipantId    => _participantId;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTicket(string joinToken, long expiresAtUnixMs, int serverRole, string instanceId, string address,
            int port, string characterId, string participantId, long serverTimeUnixMs)
        {
            _joinToken        = joinToken;
            _expiresAtUnixMs  = expiresAtUnixMs;
            _serverRole       = serverRole;
            _instanceId       = instanceId;
            _address          = address;
            _port             = port;
            _characterId      = characterId;
            _participantId    = participantId;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private JoinTicket() { }
    }
}
