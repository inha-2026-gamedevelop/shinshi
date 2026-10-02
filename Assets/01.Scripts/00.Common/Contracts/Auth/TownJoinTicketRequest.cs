// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 선택한 캐릭터의 마을 입장을 요청하며 마을 인스턴스는 서버가 결정한다 </summary>
    [DataContract]
    public sealed class TownJoinTicketRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")] private string _characterId;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId => _characterId;

        /****************************************
        *                Methods
        ****************************************/

        public TownJoinTicketRequest(string characterId)
        {
            _characterId = characterId;
        }

        private TownJoinTicketRequest() { }
    }
}
