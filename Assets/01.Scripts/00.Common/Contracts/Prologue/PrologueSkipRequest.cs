// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 프롤로그 건너뛰기 요청, 이미 프롤로그를 마친 계정만 보낼 수 있다 </summary>
    [DataContract]
    public sealed class PrologueSkipRequest
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

        public PrologueSkipRequest(string operationId, string characterId)
        {
            _operationId = operationId;
            _characterId = characterId;
        }

        private PrologueSkipRequest() { }
    }
}
