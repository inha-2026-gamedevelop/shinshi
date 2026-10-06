// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 마을 서버가 보내는 신시 안내 완료 기록 요청, 대상 accountId는 경로에 넣는다. 응답은 OperationResult다 </summary>
    [DataContract]
    public sealed class IntroCompleteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 응답을 잃으면 같은 ID로 다시 보낸다. 이미 완료한 계정도 확정으로 답한다
        [DataMember(Name = "operationId")] private string _operationId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;

        /****************************************
        *                Methods
        ****************************************/

        public IntroCompleteRequest(string operationId)
        {
            _operationId = operationId;
        }

        private IntroCompleteRequest() { }
    }
}
