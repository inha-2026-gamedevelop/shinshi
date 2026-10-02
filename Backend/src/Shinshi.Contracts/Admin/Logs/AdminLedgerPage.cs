// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 원장 조회 한 페이지. entries는 시각, ledgerId 오름차순이다. totals는 필터 전체의 사유, 자산별 합계이며
    /// cursor 없이 부른 첫 페이지에만 있고 다음 페이지에서는 null이다. nextCursor가 null이면 마지막 페이지다 </summary>
    [DataContract]
    public sealed class AdminLedgerPage
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "entries")]    private AdminLedgerEntry[]       _entries;
        [DataMember(Name = "totals")]     private AdminLedgerReasonTotal[] _totals;
        [DataMember(Name = "nextCursor")] private string                   _nextCursor;

        /****************************************
        *              Properties
        ****************************************/

        public AdminLedgerEntry[]       Entries    => _entries;
        public AdminLedgerReasonTotal[] Totals     => _totals;
        public string                   NextCursor => _nextCursor;

        /****************************************
        *                Methods
        ****************************************/

        public AdminLedgerPage(AdminLedgerEntry[] entries, AdminLedgerReasonTotal[] totals, string nextCursor)
        {
            _entries    = entries;
            _totals     = totals;
            _nextCursor = nextCursor;
        }

        private AdminLedgerPage() { }
    }
}
