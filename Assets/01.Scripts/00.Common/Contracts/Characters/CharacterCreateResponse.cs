// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 캐릭터 생성 결과, result.status가 COMMITTED일 때만 character를 읽는다 </summary>
    [DataContract]
    public sealed class CharacterCreateResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "result")]    private OperationResult  _result;

        // 거절이면 null로 올 수 있다
        [DataMember(Name = "character")] private CharacterSummary _character;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult  Result    => _result;
        public CharacterSummary Character => _character;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterCreateResponse(OperationResult result, CharacterSummary character)
        {
            _result    = result;
            _character = character;
        }

        private CharacterCreateResponse() { }
    }
}
