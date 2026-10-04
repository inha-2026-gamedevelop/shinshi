// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 출정 준비 확인 결과. 요청 순서대로 참가자 상태를 담는다. 확인 뒤 예약 전에 값이 바뀔 수 있으므로 확정은 예약이 한다 </summary>
    [DataContract]
    public sealed class ReadinessResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "participants")]     private ReadinessParticipantStatus[] _participants;
        [DataMember(Name = "serverTimeUnixMs")] private long                         _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public ReadinessParticipantStatus[] Participants     => _participants;
        public long                         ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public ReadinessResponse(ReadinessParticipantStatus[] participants, long serverTimeUnixMs)
        {
            _participants     = participants;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private ReadinessResponse() { }
    }
}
