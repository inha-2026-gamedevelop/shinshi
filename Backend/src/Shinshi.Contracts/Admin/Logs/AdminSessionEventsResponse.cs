// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 세션 재생. events는 seq순 전체, ledger는 이 세션을 원인으로 한 원장 줄 전체(시작 티켓, 물약, 정산, 장애 취소)이고
    /// operations는 정산 operation과 그 원장 줄을 만든 operation들이다 </summary>
    [DataContract]
    public sealed class AdminSessionEventsResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "session")]      private AdminSessionSummary       _session;
        [DataMember(Name = "participants")] private AdminSessionParticipant[] _participants;
        [DataMember(Name = "events")]       private AdminSessionEvent[]       _events;
        [DataMember(Name = "operations")]   private AdminOperationRecord[]    _operations;
        [DataMember(Name = "ledger")]       private AdminLedgerEntry[]        _ledger;

        /****************************************
        *              Properties
        ****************************************/

        public AdminSessionSummary       Session      => _session;
        public AdminSessionParticipant[] Participants => _participants;
        public AdminSessionEvent[]       Events       => _events;
        public AdminOperationRecord[]    Operations   => _operations;
        public AdminLedgerEntry[]        Ledger       => _ledger;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionEventsResponse(AdminSessionSummary session, AdminSessionParticipant[] participants,
            AdminSessionEvent[] events, AdminOperationRecord[] operations, AdminLedgerEntry[] ledger)
        {
            _session      = session;
            _participants = participants;
            _events       = events;
            _operations   = operations;
            _ledger       = ledger;
        }

        private AdminSessionEventsResponse() { }
    }
}
