// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 접속 토큰 교환 승인 결과, 토큰이 증명한 캐릭터와 직업과 배정 인스턴스와 출정 참가자 </summary>
    [DataContract]
    public sealed class JoinTokenExchangeResponse
    {
        // 게임 서버는 instanceId와 serverRole을 자기 실행 설정과 다시 대조한다. API 응답이라도 다른 서버의 입장을 받지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // classId 키가 없는 응답을 직업 0(무사)으로 읽지 않도록 값 없음을 따로 둔다. 서버는 이 값을 알 수 없는 직업으로 거절한다
        public const int UNKNOWN_CLASS_ID = -1;

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

        // 토큰이 발급된 캐릭터의 직업(ContractCodes.PlayerClass). 마을과 던전 입장 모두 채운다.
        // 게임 서버는 클라이언트가 아니라 이 값으로 전투 개체의 직업을 정한다
        [DataMember(Name = "classId")]          private int    _classId = UNKNOWN_CLASS_ID;

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
        public int    ClassId          => _classId;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, int classId)
            : this(characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs, null, classId)
        {
        }

        public JoinTokenExchangeResponse(string characterId, string instanceId, int serverRole, string participantId,
            string sortieId, long serverTimeUnixMs, string accountLeaseId, int classId)
        {
            _characterId      = characterId;
            _instanceId       = instanceId;
            _serverRole       = serverRole;
            _participantId    = participantId;
            _sortieId         = sortieId;
            _serverTimeUnixMs = serverTimeUnixMs;
            _accountLeaseId   = accountLeaseId;
            _classId          = classId;
        }

        private JoinTokenExchangeResponse() { }
    }
}
