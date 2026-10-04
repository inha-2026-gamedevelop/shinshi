// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 마을 서버의 출정 준비 확인 요청. 아무것도 예약하거나 바꾸지 않는다 </summary>
    [DataContract]
    public sealed class ReadinessRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 파티 1~4명. 같은 계정이나 캐릭터가 두 번 오면 요청 형식 오류(400)다
        [DataMember(Name = "participants")] private ReadinessParticipant[] _participants;

        /****************************************
        *              Properties
        ****************************************/

        public ReadinessParticipant[] Participants => _participants;

        /****************************************
        *                Methods
        ****************************************/

        public ReadinessRequest(ReadinessParticipant[] participants)
        {
            _participants = participants;
        }

        private ReadinessRequest() { }
    }
}
