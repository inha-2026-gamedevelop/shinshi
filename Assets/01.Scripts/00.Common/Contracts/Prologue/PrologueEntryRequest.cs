// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 프롤로그 던전 입장권 요청, 캐릭터는 이 계정의 것이어야 한다 </summary>
    [DataContract]
    public sealed class PrologueEntryRequest
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

        public PrologueEntryRequest(string operationId, string characterId)
        {
            _operationId = operationId;
            _characterId = characterId;
        }

        private PrologueEntryRequest() { }
    }
}
