// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 접속 토큰 교환 승인 결과, 토큰이 증명한 캐릭터와 배정 인스턴스와 출정 참가자 </summary>
    [DataContract]
    public sealed class JoinTokenExchangeResponse
    {
        // 게임 서버는 instanceId와 serverRole을 자기 실행 설정과 다시 대조한다. API 응답이라도 다른 서버의 입장을 받지 않는다

        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")]      private string _characterId;
        [DataMember(Name = "instanceId")]       private string _instanceId;

        // ContractCodes.ServerRole
        [DataMember(Name = "serverRole")]       private int    _serverRole;

        // 던전 출정의 영구 참가자 ID(JoinTicket.participantId)와 출정 ID. 마을 입장이면 둘 다 빈 값이다
        [DataMember(Name = "participantId")]    private string _participantId;
        [DataMember(Name = "sortieId")]         private string _sortieId;

        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        // 마을 입장 교환이 얻은 계정 접속 임대 ID. 마을 서버가 접속 종료 때 해제 경로(GameServerRoutes.ACCOUNT_LEASE_RELEASE_FORMAT)에
        // 넣는다. 던전 입장이면 빈 값이다(임대는 이관 경로가 옮긴다)
        [DataMember(Name = "accountLeaseId")]   private string _accountLeaseId;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId      => _characterId;
        public string InstanceId       => _instanceId;
        public int    ServerRole       => _serverRole;
        public string ParticipantId    => _participantId;
        public string SortieId         => _sortieId;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;
        public string AccountLeaseId   => _accountLeaseId;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, null)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId)
        {
            _characterId      = characterId;
            _instanceId       = instanceId;
            _serverRole       = serverRole;
            _participantId    = participantId;
            _sortieId         = sortieId;
            _serverTimeUnixMs = serverTimeUnixMs;
            _accountLeaseId   = accountLeaseId;
        }

        private JoinTokenExchangeResponse() { }
    }
}
