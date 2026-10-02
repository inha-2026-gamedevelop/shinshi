// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 검색 결과 한 줄. 내부 ID와 접속 여부만 싣는다 </summary>
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

        /****************************************
        *              Properties
        ****************************************/

        public string AccountId       => _accountId;
        public string MatchedBy       => _matchedBy;
        public string MatchedId       => _matchedId;
        public long   CreatedAtUnixMs => _createdAtUnixMs;
        public int    CharacterCount  => _characterCount;
        public bool   Online          => _online;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountSummary(string accountId, string matchedBy, string matchedId, long createdAtUnixMs,
            int characterCount, bool online)
        {
            _accountId       = accountId;
            _matchedBy       = matchedBy;
            _matchedId       = matchedId;
            _createdAtUnixMs = createdAtUnixMs;
            _characterCount  = characterCount;
            _online          = online;
        }

        private AdminAccountSummary() { }
    }
}
