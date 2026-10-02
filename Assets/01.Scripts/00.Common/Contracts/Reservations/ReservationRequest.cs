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

        /****************************************
        *              Properties
        ****************************************/

        public string                   OperationId    => _operationId;
        public int                      Mode           => _mode;
        public string                   MapId          => _mapId;
        public string                   ContentVersion => _contentVersion;
        public ReservationParticipant[] Participants   => _participants;

        /****************************************
        *                Methods
        ****************************************/

        public ReservationRequest(string operationId, int mode, string mapId, string contentVersion,
            ReservationParticipant[] participants)
        {
            _operationId    = operationId;
            _mode           = mode;
            _mapId          = mapId;
            _contentVersion = contentVersion;
            _participants   = participants;
        }

        private ReservationRequest() { }
    }
}
