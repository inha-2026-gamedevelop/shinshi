// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 플레이할 캐릭터를 고르고 마을 입장권을 받는 요청, 계정당 활성 캐릭터는 하나다 </summary>
    [DataContract]
    public sealed class CharacterSelectRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 접근 토큰의 계정이 소유한 캐릭터만 허용한다. 다른 계정의 ID는 거절한다
        [DataMember(Name = "characterId")] private string _characterId;

        // TODO(민성): 같은 계정이 이미 마을에 있을 때 기존 연결 대체와 새 선택 거절 중 기획 미정

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId => _characterId;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterSelectRequest(string characterId)
        {
            _characterId = characterId;
        }

        private CharacterSelectRequest() { }
    }
}
