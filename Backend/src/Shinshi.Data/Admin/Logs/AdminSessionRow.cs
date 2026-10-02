// System
using System;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Data.Admin
{
    /// <summary> game_sessions 조회 행. SQL은 FROM game_sessions AS session 별칭을 쓴다. 복구 목록 행이 상속해 서버 열을 더한다 </summary>
    internal class AdminSessionRow
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SELECT_COLUMNS =
            """
            session.session_id AS "SessionId", session.state AS "State", session.result AS "Result",
                session.mode AS "Mode", session.map_id AS "MapId", session.starting_count AS "StartingCount",
                session.content_version AS "ContentVersion", session.instance_id AS "InstanceId",
                session.instance_boot_id AS "InstanceBootId", session.end_reason AS "EndReason",
                session.last_event_seq AS "LastEventSeq", session.ended_event_seq AS "EndedEventSeq",
                session.settlement_operation_id AS "SettlementOperationId",
                session.recovery_failures AS "RecoveryFailures", session.next_recovery_at AS "NextRecoveryAt",
                session.created_at AS "CreatedAt", session.started_at AS "StartedAt",
                session.settling_at AS "SettlingAt", session.closed_at AS "ClosedAt", session.revision AS "Revision"
            """;

        /****************************************
        *              Properties
        ****************************************/

        public Guid      SessionId             { get; set; }
        public string    State                 { get; set; } = string.Empty;
        public string?   Result                { get; set; }
        public string    Mode                  { get; set; } = string.Empty;
        public string    MapId                 { get; set; } = string.Empty;
        public int       StartingCount         { get; set; }
        public string    ContentVersion        { get; set; } = string.Empty;
        public Guid      InstanceId            { get; set; }
        public Guid?     InstanceBootId        { get; set; }
        public int?      EndReason             { get; set; }
        public long      LastEventSeq          { get; set; }
        public long?     EndedEventSeq         { get; set; }
        public Guid?     SettlementOperationId { get; set; }
        public int       RecoveryFailures      { get; set; }
        public DateTime? NextRecoveryAt        { get; set; }
        public DateTime  CreatedAt             { get; set; }
        public DateTime? StartedAt             { get; set; }
        public DateTime? SettlingAt            { get; set; }
        public DateTime? ClosedAt              { get; set; }
        public long      Revision              { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionSummary ToContract()
        {
            return new AdminSessionSummary(SessionId.ToString("D"), State, Result, Mode, MapId, StartingCount,
                ContentVersion, InstanceId.ToString("D"), AdminLogValues.ToText(InstanceBootId), EndReason, LastEventSeq,
                EndedEventSeq, AdminLogValues.ToText(SettlementOperationId), RecoveryFailures,
                AdminLogValues.ToUnixMs(NextRecoveryAt), AdminLogValues.ToUnixMs(CreatedAt),
                AdminLogValues.ToUnixMs(StartedAt), AdminLogValues.ToUnixMs(SettlingAt), AdminLogValues.ToUnixMs(ClosedAt),
                Revision);
        }
    }
}
