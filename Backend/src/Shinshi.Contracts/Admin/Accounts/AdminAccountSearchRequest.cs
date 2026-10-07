// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> POST /admin/v1/accounts/search 본문(Viewer). query(UUID 또는 초대 코드)와 characterName 중 하나만 받는다.
    /// 초대 코드와 이름 원문은 요청 로그의 조회 문자열에 남지 않게 본문으로만 보낸다 </summary>
    [DataContract]
    public sealed class AdminAccountSearchRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "query")]                    private string _query;
        [DataMember(Name = "characterName")]            private string _characterName;

        // 이름 검색에만 적용한다. 생략하면 삭제된 캐릭터는 제외한다
        [DataMember(Name = "includeDeletedCharacters")] private bool   _includeDeletedCharacters;

        /****************************************
        *              Properties
        ****************************************/

        public string Query                    => _query;
        public string CharacterName            => _characterName;
        public bool   IncludeDeletedCharacters => _includeDeletedCharacters;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountSearchRequest(string query, string characterName = null, bool includeDeletedCharacters = false)
        {
            _query                    = query;
            _characterName            = characterName;
            _includeDeletedCharacters = includeDeletedCharacters;
        }

        private AdminAccountSearchRequest() { }
    }
}
