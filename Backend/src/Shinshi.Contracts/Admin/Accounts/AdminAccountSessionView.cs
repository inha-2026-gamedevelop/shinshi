// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정이 참가한 출정 한 건. 세션과 참가자 상태는 DB 문자열 그대로다 </summary>
    [DataContract]
    public sealed class AdminAccountSessionView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "sessionId")]          private string _sessionId;
        [DataMember(Name = "participantId")]      private string _participantId;
        [DataMember(Name = "characterId")]        private string _characterId;

        // Normal, Boss
        [DataMember(Name = "mode")]               private string _mode;
        [DataMember(Name = "mapId")]              private string _mapId;
        [DataMember(Name = "sessionState")]       private string _sessionState;
        [DataMember(Name = "participantState")]   private string _participantState;

        // 닫히지 않았으면 null. Released, Expired, Settled, ServiceCancelled
        [DataMember(Name = "result")]             private string _result;
        [DataMember(Name = "createdAtUnixMs")]    private long   _createdAtUnixMs;
        [DataMember(Name = "startedAtUnixMs")]    private long?  _startedAtUnixMs;
        [DataMember(Name = "closedAtUnixMs")]     private long?  _closedAtUnixMs;
        [DataMember(Name = "essenceAccrued")]     private long   _essenceAccrued;
        [DataMember(Name = "baseEssenceAccrued")] private long   _baseEssenceAccrued;

        /****************************************
        *              Properties
        ****************************************/

        public string SessionId          => _sessionId;
        public string ParticipantId      => _participantId;
        public string CharacterId        => _characterId;
        public string Mode               => _mode;
        public string MapId              => _mapId;
        public string SessionState       => _sessionState;
        public string ParticipantState   => _participantState;
        public string Result             => _result;
        public long   CreatedAtUnixMs    => _createdAtUnixMs;
        public long?  StartedAtUnixMs    => _startedAtUnixMs;
        public long?  ClosedAtUnixMs     => _closedAtUnixMs;
        public long   EssenceAccrued     => _essenceAccrued;
        public long   BaseEssenceAccrued => _baseEssenceAccrued;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountSessionView(string sessionId, string participantId, string characterId, string mode,
            string mapId, string sessionState, string participantState, string result, long createdAtUnixMs,
            long? startedAtUnixMs, long? closedAtUnixMs, long essenceAccrued, long baseEssenceAccrued)
        {
            _sessionId          = sessionId;
            _participantId      = participantId;
            _characterId        = characterId;
            _mode               = mode;
            _mapId              = mapId;
            _sessionState       = sessionState;
            _participantState   = participantState;
            _result             = result;
            _createdAtUnixMs    = createdAtUnixMs;
            _startedAtUnixMs    = startedAtUnixMs;
            _closedAtUnixMs     = closedAtUnixMs;
            _essenceAccrued     = essenceAccrued;
            _baseEssenceAccrued = baseEssenceAccrued;
        }

        private AdminAccountSessionView() { }
    }
}
