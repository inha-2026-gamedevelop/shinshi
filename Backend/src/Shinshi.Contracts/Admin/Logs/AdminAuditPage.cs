// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 감사 로그 한 페이지. 시각, auditId 오름차순이고 nextCursor가 null이면 마지막 페이지다 </summary>
    [DataContract]
    public sealed class AdminAuditPage
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "entries")]    private AdminAuditRecord[] _entries;
        [DataMember(Name = "nextCursor")] private string             _nextCursor;

        /****************************************
        *              Properties
        ****************************************/

        public AdminAuditRecord[] Entries    => _entries;
        public string             NextCursor => _nextCursor;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAuditPage(AdminAuditRecord[] entries, string nextCursor)
        {
            _entries    = entries;
            _nextCursor = nextCursor;
        }

        private AdminAuditPage() { }
    }
}
