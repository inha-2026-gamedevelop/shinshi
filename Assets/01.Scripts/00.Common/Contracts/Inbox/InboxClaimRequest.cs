// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 우편 하나 수령. 응답은 OperationResult이고 영수증 ID는 우편 ID다 </summary>
    [DataContract]
    public sealed class InboxClaimRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 응답을 잃으면 같은 ID로 다시 보낸다. 새 ID로 보내면 이미 수령한 우편이라 거절된다
        [DataMember(Name = "operationId")] private string _operationId;
        [DataMember(Name = "inboxId")]     private string _inboxId;

        // 우편에 Essence, Shards가 있을 때만 필요하다. 자기 계정의 캐릭터여야 한다
        [DataMember(Name = "characterId")] private string _characterId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;
        public string InboxId     => _inboxId;
        public string CharacterId => _characterId;

        /****************************************
        *                Methods
        ****************************************/

        public InboxClaimRequest(string operationId, string inboxId, string characterId)
        {
            _operationId = operationId;
            _inboxId     = inboxId;
            _characterId = characterId;
        }

        private InboxClaimRequest() { }
    }
}
