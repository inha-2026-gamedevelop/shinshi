// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> POST /admin/v1/accounts/search 본문(Viewer). 계정 ID, 캐릭터 ID나 초대 코드 원문을 받는다.
    /// 초대 원문이 요청 로그의 조회 문자열에 남지 않게 본문으로만 보낸다 </summary>
    [DataContract]
    public sealed class AdminAccountSearchRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "query")] private string _query;

        /****************************************
        *              Properties
        ****************************************/

        public string Query => _query;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountSearchRequest(string query)
        {
            _query = query;
        }

        private AdminAccountSearchRequest() { }
    }
}
