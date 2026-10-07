// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 같은 계정의 다른 캐릭터. 삭제된 캐릭터도 포함한다 </summary>
    [DataContract]
    public sealed class AdminCharacterSummaryView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")]     private string _characterId;
        [DataMember(Name = "name")]            private string _name;
        [DataMember(Name = "classId")]         private int    _classId;
        [DataMember(Name = "createdAtUnixMs")] private long?  _createdAtUnixMs;
        [DataMember(Name = "deletedAtUnixMs")] private long?  _deletedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId     => _characterId;
        public string Name            => _name;
        public int    ClassId         => _classId;
        public long?  CreatedAtUnixMs => _createdAtUnixMs;
        public long?  DeletedAtUnixMs => _deletedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterSummaryView(string characterId, string name, int classId, long? createdAtUnixMs,
            long? deletedAtUnixMs)
        {
            _characterId     = characterId;
            _name            = name;
            _classId         = classId;
            _createdAtUnixMs = createdAtUnixMs;
            _deletedAtUnixMs = deletedAtUnixMs;
        }

        private AdminCharacterSummaryView() { }
    }
}
