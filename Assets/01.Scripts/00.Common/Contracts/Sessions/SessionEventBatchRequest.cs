// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 1초 단위로 묶은 세션 이벤트, 배치 전체가 한 번에 확정되거나 거절된다 </summary>
    [DataContract]
    public sealed class SessionEventBatchRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")] private string              _operationId;

        // eventSeq 오름차순. 거절 응답의 committedRevision은 저장된 마지막 순번이다
        [DataMember(Name = "events")]      private SessionEventEntry[] _events;

        /****************************************
        *              Properties
        ****************************************/

        public string              OperationId => _operationId;
        public SessionEventEntry[] Events      => _events;

        /****************************************
        *                Methods
        ****************************************/

        public SessionEventBatchRequest(string operationId, SessionEventEntry[] events)
        {
            _operationId = operationId;
            _events      = events;
        }

        private SessionEventBatchRequest() { }
    }
}
