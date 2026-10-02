// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 배정된 던전 서버의 전원 준비 증거, 승인되면 전원 티켓 소비가 한 번 확정된다 </summary>
    [DataContract]
    public sealed class SessionStartRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 응답을 잃으면 새 ID로 다시 보내지 않고 같은 ID로 재시도한다
        [DataMember(Name = "operationId")]         private string   _operationId;

        // 예약된 참가자 전원이어야 한다. 한 명이라도 빠지면 시작하지 않는다
        [DataMember(Name = "readyParticipantIds")] private string[] _readyParticipantIds;

        /****************************************
        *              Properties
        ****************************************/

        public string   OperationId         => _operationId;
        public string[] ReadyParticipantIds => _readyParticipantIds;

        /****************************************
        *                Methods
        ****************************************/

        public SessionStartRequest(string operationId, string[] readyParticipantIds)
        {
            _operationId         = operationId;
            _readyParticipantIds = readyParticipantIds;
        }

        private SessionStartRequest() { }
    }
}
