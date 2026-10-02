// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 참가자의 접속 임대를 마을에서 배정 던전으로 옮긴다, 접속 토큰 교환 뒤 던전 서버가 부른다 </summary>
    [DataContract]
    public sealed class LeaseTransferRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "participantId")] private string _participantId;

        // 재시도에도 같은 값을 쓴다
        [DataMember(Name = "newLeaseId")]    private string _newLeaseId;

        /****************************************
        *              Properties
        ****************************************/

        public string ParticipantId => _participantId;
        public string NewLeaseId    => _newLeaseId;

        /****************************************
        *                Methods
        ****************************************/

        public LeaseTransferRequest(string participantId, string newLeaseId)
        {
            _participantId = participantId;
            _newLeaseId    = newLeaseId;
        }

        private LeaseTransferRequest() { }
    }
}
