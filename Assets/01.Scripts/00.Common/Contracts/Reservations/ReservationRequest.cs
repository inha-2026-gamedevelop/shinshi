// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 마을 서버의 전원 티켓, 물약 일괄 예약. 하나라도 부족하면 아무것도 예약하지 않는다 </summary>
    [DataContract]
    public sealed class ReservationRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]    private string                   _operationId;

        // GameSessionCodes.Mode, 보스전만 티켓 1장을 예약한다
        [DataMember(Name = "mode")]           private int                      _mode;
        [DataMember(Name = "mapId")]          private string                   _mapId;
        [DataMember(Name = "contentVersion")] private string                   _contentVersion;
        [DataMember(Name = "participants")]   private ReservationParticipant[] _participants;

        // 일반전 전투 정의 ID. 없으면 빈 값이다
        [DataMember(Name = "battleId")]       private string                   _battleId;

        // 보스전에만 넣는다. 보스전인데 bossId가 없거나 일반전인데 있으면 요청 형식 오류(400)다
        [DataMember(Name = "bossId")]         private string                   _bossId;

        // GameSessionCodes.Difficulty. 키가 없는 이전 요청은 NONE으로 읽는다. 보스전이 아니면 NONE이어야 한다
        [DataMember(Name = "difficulty")]     private int                      _difficulty = GameSessionCodes.Difficulty.NONE;

        /****************************************
        *              Properties
        ****************************************/

        public string                   OperationId    => _operationId;
        public int                      Mode           => _mode;
        public string                   MapId          => _mapId;
        public string                   ContentVersion => _contentVersion;
        public ReservationParticipant[] Participants   => _participants;
        public string                   BattleId       => _battleId;
        public string                   BossId         => _bossId;
        public int                      Difficulty     => _difficulty;

        /****************************************
        *                Methods
        ****************************************/

        public ReservationRequest(string operationId, int mode, string mapId, string contentVersion,
            ReservationParticipant[] participants)
            : this(operationId, mode, mapId, contentVersion, participants, null, null, GameSessionCodes.Difficulty.NONE)
        {
        }

        public ReservationRequest(string operationId, int mode, string mapId, string contentVersion,
            ReservationParticipant[] participants, string battleId, string bossId, int difficulty)
        {
            _operationId    = operationId;
            _mode           = mode;
            _mapId          = mapId;
            _contentVersion = contentVersion;
            _participants   = participants;
            _battleId       = battleId;
            _bossId         = bossId;
            _difficulty     = difficulty;
        }

        private ReservationRequest() { }
    }
}
