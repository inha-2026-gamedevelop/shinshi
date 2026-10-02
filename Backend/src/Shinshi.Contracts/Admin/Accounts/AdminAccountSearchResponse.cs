// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 검색 결과(Viewer). UUID는 계정 ID와 캐릭터 ID로, 그 밖은 초대 코드 원문으로 찾는다. 없으면 빈 목록이다 </summary>
    [DataContract]
    public sealed class AdminAccountSearchResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accounts")] private AdminAccountSummary[] _accounts;

        /****************************************
        *              Properties
        ****************************************/

        public AdminAccountSummary[] Accounts => _accounts;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountSearchResponse(AdminAccountSummary[] accounts)
        {
            _accounts = accounts;
        }

        private AdminAccountSearchResponse() { }
    }
}
