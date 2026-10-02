// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정의 캐릭터 한 명과 캐릭터 단위 재화 </summary>
    [DataContract]
    public sealed class AdminCharacterView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")] private string _characterId;

        // ContractCodes.PlayerClass
        [DataMember(Name = "classId")]     private int    _classId;
        [DataMember(Name = "essence")]     private long   _essence;
        [DataMember(Name = "shards")]      private long   _shards;
        [DataMember(Name = "revision")]    private long   _revision;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId => _characterId;
        public int    ClassId     => _classId;
        public long   Essence     => _essence;
        public long   Shards      => _shards;
        public long   Revision    => _revision;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterView(string characterId, int classId, long essence, long shards, long revision)
        {
            _characterId = characterId;
            _classId     = classId;
            _essence     = essence;
            _shards      = shards;
            _revision    = revision;
        }

        private AdminCharacterView() { }
    }
}
