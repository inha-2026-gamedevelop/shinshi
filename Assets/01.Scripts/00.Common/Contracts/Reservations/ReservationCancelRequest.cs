// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 시작 전 출정 취소, 예약한 티켓과 물약을 모두 해제한다 </summary>
    [DataContract]
    public sealed class ReservationCancelRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")] private string _operationId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;

        /****************************************
        *                Methods
        ****************************************/

        public ReservationCancelRequest(string operationId)
        {
            _operationId = operationId;
        }

        private ReservationCancelRequest() { }
    }
}
