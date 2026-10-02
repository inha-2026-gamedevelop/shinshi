// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 작업 하나의 결과와 그 작업이 같은 트랜잭션에 남긴 원장, 아이템 이력, outbox 줄. 어드민 작업이면 감사 행도 싣는다 </summary>
    [DataContract]
    public sealed class AdminOperationDetailResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operation")]   private AdminOperationRecord    _operation;
        [DataMember(Name = "ledger")]      private AdminLedgerEntry[]      _ledger;
        [DataMember(Name = "itemHistory")] private AdminItemHistoryEntry[] _itemHistory;
        [DataMember(Name = "outbox")]      private AdminOutboxEntry[]      _outbox;
        [DataMember(Name = "audit")]       private AdminAuditRecord[]      _audit;

        /****************************************
        *              Properties
        ****************************************/

        public AdminOperationRecord    Operation   => _operation;
        public AdminLedgerEntry[]      Ledger      => _ledger;
        public AdminItemHistoryEntry[] ItemHistory => _itemHistory;
        public AdminOutboxEntry[]      Outbox      => _outbox;
        public AdminAuditRecord[]      Audit       => _audit;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperationDetailResponse(AdminOperationRecord operation, AdminLedgerEntry[] ledger,
            AdminItemHistoryEntry[] itemHistory, AdminOutboxEntry[] outbox, AdminAuditRecord[] audit)
        {
            _operation   = operation;
            _ledger      = ledger;
            _itemHistory = itemHistory;
            _outbox      = outbox;
            _audit       = audit;
        }

        private AdminOperationDetailResponse() { }
    }
}
