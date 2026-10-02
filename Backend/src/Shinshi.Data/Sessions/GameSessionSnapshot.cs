// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Sessions
{
    /// <summary> 조건부 갱신에서 진 쪽이 확정된 상태를 읽어 거절 사유를 정하는 스냅샷 </summary>
    public sealed class GameSessionSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid    SessionId             { get; set; }
        public string  State                 { get; set; } = string.Empty;
        public string? Result                { get; set; }
        public string  Mode                  { get; set; } = string.Empty;
        public Guid    InstanceId            { get; set; }
        public Guid?   InstanceBootId        { get; set; }
        public Guid?   ReservedByInstanceId  { get; set; }
        public bool    IsExpired             { get; set; }
        public bool    IsInstanceAlive       { get; set; }
        public Guid?   SettlementOperationId { get; set; }
        public long    LastEventSeq          { get; set; }
        public long    Revision              { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 현재 트랜잭션에서 잠금 없이 읽는다. 잠금이 필요하면 호출자가 먼저 조건부 갱신으로 잡는다 </summary>
        public static Task<GameSessionSnapshot?> ReadAsync(DbConnection connection, DbTransaction? transaction,
            Guid sessionId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.QuerySingleOrDefaultAsync<GameSessionSnapshot>(new CommandDefinition(
                """
                SELECT session.session_id AS "SessionId", session.state AS "State", session.result AS "Result",
                    session.mode AS "Mode", session.instance_id AS "InstanceId",
                    session.instance_boot_id AS "InstanceBootId",
                    session.reserved_by_instance_id AS "ReservedByInstanceId",
                    COALESCE(session.expires_at <= clock_timestamp(), false) AS "IsExpired",
                    EXISTS (SELECT 1 FROM server_instances AS instance
                        WHERE instance.instance_id = session.instance_id AND instance.boot_id = session.instance_boot_id
                            AND instance.state <> 'Offline'
                            AND instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout) AS "IsInstanceAlive",
                    session.settlement_operation_id AS "SettlementOperationId",
                    session.last_event_seq AS "LastEventSeq", session.revision AS "Revision"
                FROM game_sessions AS session WHERE session.session_id = @SessionId;
                """, new { SessionId = sessionId, HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout },
                transaction, cancellationToken: cancellationToken));
        }

        /// <summary> 열린 세션이 아닐 때 공통 거절 사유. 해제된 예약과 정산된 세션을 구분한다 </summary>
        public int ClosedReason()
        {
            if (State != GameSessionStates.CLOSED)
            {
                return ContractCodes.RejectReason.WRONG_PHASE;
            }

            return ((Result == GameSessionStates.RESULT_RELEASED) || (Result == GameSessionStates.RESULT_EXPIRED))
                ? ContractCodes.RejectReason.RESERVATION_CONFLICT : ContractCodes.RejectReason.SESSION_TERMINAL;
        }
    }
}
