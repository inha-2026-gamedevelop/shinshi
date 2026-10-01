// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 캐릭터 선택 화면 한 줄, 직업은 생성 뒤 바뀌지 않는다 </summary>
    [DataContract]
    public sealed class CharacterSummary
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")] private string _characterId;
        [DataMember(Name = "name")]        private string _name;

        // ContractCodes.PlayerClass
        [DataMember(Name = "classId")]     private int    _classId;

        // 성장 요청이 기대값으로 보내는 캐릭터 revision
        [DataMember(Name = "revision")]    private long   _revision;

        // TODO(진욱): 선택 화면에 보일 캐릭터별 성장 요약 필드는 기획 미정

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId => _characterId;
        public string Name        => _name;
        public int    ClassId     => _classId;
        public long   Revision    => _revision;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterSummary(string characterId, string name, int classId, long revision)
        {
            _characterId = characterId;
            _name        = name;
            _classId     = classId;
            _revision    = revision;
        }

        private CharacterSummary() { }
    }
}
