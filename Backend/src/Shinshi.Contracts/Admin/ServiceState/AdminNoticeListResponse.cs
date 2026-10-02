// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 어드민 공지 목록(Viewer). 진행 중, 예정, 최근 7일 안에 끝난 공지를 시작 시각 내림차순으로 준다 </summary>
    [DataContract]
    public sealed class AdminNoticeListResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "notices")] private AdminNoticeSummary[] _notices;

        /****************************************
        *              Properties
        ****************************************/

        public AdminNoticeSummary[] Notices => _notices;

        /****************************************
        *                Methods
        ****************************************/

        public AdminNoticeListResponse(AdminNoticeSummary[] notices)
        {
            _notices = notices;
        }

        private AdminNoticeListResponse() { }
    }
}
