// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 검색 결과 한 줄. 내부 ID, 활성 캐릭터 수, 접속 여부와 매칭 캐릭터 삭제 상태를 싣는다 </summary>
    [DataContract]
    public sealed class AdminAccountSummary
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]       private string _accountId;

        // AdminAccountQuery.MATCH_* 값
        [DataMember(Name = "matchedBy")]       private string _matchedBy;

        // 캐릭터 ID나 초대 ID처럼 찾은 근거의 ID. 계정 ID로 찾았으면 계정 ID다
        [DataMember(Name = "matchedId")]       private string _matchedId;
        [DataMember(Name = "createdAtUnixMs")] private long   _createdAtUnixMs;
        [DataMember(Name = "characterCount")]  private int    _characterCount;

        // 만료되지 않은 계정 접속 임대가 있으면 true
        [DataMember(Name = "online")]          private bool   _online;

        // 이름·캐릭터 ID 매칭이면 false=활성, true=삭제. 계정 ID·초대 매칭이면 null이다
        [DataMember(Name = "matchedCharacterDeleted")] private bool? _matchedCharacterDeleted;

        [DataMember(Name = "matchedCharacterId")]      private string _matchedCharacterId;
        [DataMember(Name = "matchedCharacterName")]    private string _matchedCharacterName;
        [DataMember(Name = "matchedCharacterClassId")] private int?   _matchedCharacterClassId;

        /****************************************
        *              Properties
        ****************************************/

        public string AccountId               => _accountId;
        public string MatchedBy               => _matchedBy;
        public string MatchedId               => _matchedId;
        public long   CreatedAtUnixMs         => _createdAtUnixMs;
        public int    CharacterCount          => _characterCount;
        public bool   Online                  => _online;
        public bool?  MatchedCharacterDeleted => _matchedCharacterDeleted;
        public string MatchedCharacterId      => _matchedCharacterId;
        public string MatchedCharacterName    => _matchedCharacterName;
        public int?   MatchedCharacterClassId => _matchedCharacterClassId;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountSummary(string accountId, string matchedBy, string matchedId, long createdAtUnixMs,
            int characterCount, bool online, bool? matchedCharacterDeleted = null,
            string matchedCharacterId = null, string matchedCharacterName = null, int? matchedCharacterClassId = null)
        {
            _accountId               = accountId;
            _matchedBy               = matchedBy;
            _matchedId               = matchedId;
            _createdAtUnixMs         = createdAtUnixMs;
            _characterCount          = characterCount;
            _online                  = online;
            _matchedCharacterDeleted = matchedCharacterDeleted;
            _matchedCharacterId      = matchedCharacterId;
            _matchedCharacterName    = matchedCharacterName;
            _matchedCharacterClassId = matchedCharacterClassId;
        }

        private AdminAccountSummary() { }
    }
}
