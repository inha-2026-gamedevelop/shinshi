// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 검색 결과(Viewer). 이름은 정규화 키로 정확 검색한다. 없으면 빈 목록이며 이름 원문은 돌려주지 않는다 </summary>
    [DataContract]
    public sealed class AdminAccountSearchResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accounts")] private AdminAccountSummary[] _accounts;

        // 이번 이름 검색에서 삭제된 캐릭터도 포함했는지. 기존 UUID·초대 검색에서는 false다
        [DataMember(Name = "includeDeletedCharacters")] private bool _includeDeletedCharacters;

        /****************************************
        *              Properties
        ****************************************/

        public AdminAccountSummary[] Accounts                  => _accounts;
        public bool                  IncludeDeletedCharacters => _includeDeletedCharacters;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountSearchResponse(AdminAccountSummary[] accounts, bool includeDeletedCharacters = false)
        {
            _accounts                 = accounts;
            _includeDeletedCharacters = includeDeletedCharacters;
        }

        private AdminAccountSearchResponse() { }
    }
}
