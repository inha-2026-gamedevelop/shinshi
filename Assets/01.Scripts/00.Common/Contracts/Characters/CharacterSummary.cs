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

        [DataMember(Name = "totalExperience")] private long _totalExperience;
        [DataMember(Name = "characterId")] private string _characterId;
        [DataMember(Name = "name")]        private string _name;

        // ContractCodes.PlayerClass
        [DataMember(Name = "classId")]     private int    _classId;

        // 성장 요청이 기대값으로 보내는 캐릭터 revision
        [DataMember(Name = "revision")]    private long   _revision;

        // 선택 화면이 캐릭터 모습을 그릴 때 쓴다
        [DataMember(Name = "appearance")]  private CharacterAppearance _appearance;

        // 삭제 예약 여부와 삭제 가능까지 남은 초, 목록을 읽은 순간의 서버 기준이다. 예약하지 않았으면 false와 0이다
        [DataMember(Name = "deletePending")]          private bool _deletePending;
        [DataMember(Name = "deleteRemainingSeconds")] private long _deleteRemainingSeconds;

        // TODO(진욱): 선택 화면에 보일 캐릭터별 성장 요약 필드는 기획 미정

        /****************************************
        *              Properties
        ****************************************/

        public long TotalExperience => _totalExperience;
        public string              CharacterId => _characterId;
        public string              Name        => _name;
        public int                 ClassId     => _classId;
        public long                Revision    => _revision;
        public CharacterAppearance Appearance  => _appearance;
        public bool                DeletePending          => _deletePending;
        public long                DeleteRemainingSeconds => _deleteRemainingSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterSummary(string characterId, string name, int classId, long revision,
            CharacterAppearance appearance, long totalExperience = 0, bool deletePending = false, long deleteRemainingSeconds = 0)
        {
            _deletePending          = deletePending;
            _deleteRemainingSeconds = deleteRemainingSeconds;
            _totalExperience = totalExperience;
            _characterId = characterId;
            _name        = name;
            _classId     = classId;
            _revision    = revision;
            _appearance  = appearance;
        }

        private CharacterSummary() { }
    }
}
