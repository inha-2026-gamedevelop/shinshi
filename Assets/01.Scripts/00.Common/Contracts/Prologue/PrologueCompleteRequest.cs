// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 던전 서버가 보내는 프롤로그 완료 기록 요청, 세션은 경로에 넣는다 </summary>
    [DataContract]
    public sealed class PrologueCompleteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 응답을 잃으면 같은 ID로 다시 보낸다
        [DataMember(Name = "operationId")] private string _operationId;
        [DataMember(Name = "characterId")] private string _characterId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;
        public string CharacterId => _characterId;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueCompleteRequest(string operationId, string characterId)
        {
            _operationId = operationId;
            _characterId = characterId;
        }

        private PrologueCompleteRequest() { }
    }
}
