// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Settlement;

namespace Shinshi.Data.Admin
{
    /// <summary> 아이템 하나, 세션 하나, 작업 하나를 따라가는 조회. 각 조회는 한 snapshot에서 읽고 감사하지 않는다 </summary>
    public sealed class PostgresAdminTraceReader
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminTraceReader(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> seq 오름차순 전체 이력. 이력이 없으면 null이다 </summary>
        public async Task<AdminItemHistoryResponse?> ReadItemHistoryAsync(Guid itemId,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            List<AdminItemHistoryRow> rows = (await connection.QueryAsync<AdminItemHistoryRow>(new CommandDefinition(
                "SELECT " + AdminItemHistoryRow.SELECT_COLUMNS
                + " FROM item_history AS history WHERE history.item_id = @ItemId ORDER BY history.seq;",
                new { ItemId = itemId }, cancellationToken: cancellationToken))).ToList();
            if (rows.Count == 0)
            {
                return null;
            }

            return new AdminItemHistoryResponse(itemId.ToString("D"), rows.Select(row => row.ToContract()).ToArray());
        }

        /// <summary> 세션 요약, 참가자, seq순 이벤트, 이 세션이 원인인 원장 줄과 그 작업, 정산 작업. 세션이 없으면 null이다 </summary>
        public async Task<AdminSessionEventsResponse?> ReadSessionEventsAsync(Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            (NpgsqlConnection connection, NpgsqlTransaction transaction) = await AdminLogValues.BeginReadAsync(_dataSource,
                cancellationToken);
            await using (connection)
            await using (transaction)
            {
                AdminSessionRow? session = await connection.QuerySingleOrDefaultAsync<AdminSessionRow>(
                    new CommandDefinition("SELECT " + AdminSessionRow.SELECT_COLUMNS
                        + " FROM game_sessions AS session WHERE session.session_id = @SessionId;",
                        new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));
                if (session == null)
                {
                    return null;
                }

                List<ParticipantRow> participants = (await connection.QueryAsync<ParticipantRow>(new CommandDefinition(
                    """
                    SELECT participant_id AS "ParticipantId", account_id AS "AccountId", character_id AS "CharacterId",
                        state AS "State", essence_accrued AS "EssenceAccrued", base_essence_accrued AS "BaseEssenceAccrued",
                        settled_at AS "SettledAt"
                    FROM session_participants WHERE session_id = @SessionId ORDER BY participant_id;
                    """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken))).ToList();
                List<EventRow> events = (await connection.QueryAsync<EventRow>(new CommandDefinition(
                    """
                    SELECT event_seq AS "EventSeq", event_id AS "EventId", event_kind AS "EventKind",
                        participant_id AS "ParticipantId", payload::text AS "Payload", operation_id AS "OperationId",
                        committed_at AS "CommittedAt"
                    FROM session_events WHERE session_id = @SessionId ORDER BY event_seq;
                    """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken))).ToList();

                // 출정 경로의 원장 줄은 모두 source GameSession, 세션 ID로 남는다(시작 티켓, 물약, 정산, 장애 취소)
                object source = new
                {
                    SourceType = EconomyLedgerCodes.SOURCE_GAME_SESSION,
                    SourceId = sessionId.ToString("D"),
                    session.InstanceId,
                    SettlementOperationId = session.SettlementOperationId ?? Guid.Empty
                };
                List<AdminLedgerRow> ledger = (await connection.QueryAsync<AdminLedgerRow>(new CommandDefinition(
                    "SELECT " + AdminLedgerRow.SELECT_COLUMNS + " FROM economy_ledger AS ledger"
                    + " WHERE ledger.source_type = @SourceType AND ledger.source_id = @SourceId"
                    + " ORDER BY ledger.occurred_at, ledger.ledger_id;",
                    source, transaction, cancellationToken: cancellationToken))).ToList();
                // OR로 묶으면 operations 전체를 훑을 수 있어 두 기본 키 조회를 UNION으로 합친다
                List<AdminOperationRow> operations = (await connection.QueryAsync<AdminOperationRow>(new CommandDefinition(
                    "SELECT " + AdminOperationRow.SELECT_COLUMNS + " FROM operations AS operation"
                    + " WHERE (operation.actor_id, operation.operation_id) IN ("
                    + " SELECT ledger.actor_id, ledger.operation_id FROM economy_ledger AS ledger"
                    + " WHERE ledger.source_type = @SourceType AND ledger.source_id = @SourceId)"
                    + " UNION SELECT " + AdminOperationRow.SELECT_COLUMNS + " FROM operations AS operation"
                    + " WHERE operation.actor_id = @InstanceId AND operation.operation_id = @SettlementOperationId"
                    + " ORDER BY \"CreatedAt\", \"ActorId\", \"OperationId\";",
                    source, transaction, cancellationToken: cancellationToken))).ToList();
                await transaction.CommitAsync(cancellationToken);

                return new AdminSessionEventsResponse(session.ToContract(),
                    participants.Select(row => row.ToContract()).ToArray(),
                    events.Select(row => row.ToContract()).ToArray(),
                    operations.Select(row => row.ToContract()).ToArray(),
                    ledger.Select(row => row.ToContract()).ToArray());
            }
        }

        /// <summary> 작업 행과 같은 작업 키로 남은 원장, 아이템 이력, outbox, 감사 행. 작업이 없으면 null이다 </summary>
        public async Task<AdminOperationDetailResponse?> ReadOperationAsync(Guid actorId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            (NpgsqlConnection connection, NpgsqlTransaction transaction) = await AdminLogValues.BeginReadAsync(_dataSource,
                cancellationToken);
            await using (connection)
            await using (transaction)
            {
                object key = new { ActorId = actorId, OperationId = operationId };
                AdminOperationRow? operation = await connection.QuerySingleOrDefaultAsync<AdminOperationRow>(
                    new CommandDefinition("SELECT " + AdminOperationRow.SELECT_COLUMNS
                        + " FROM operations AS operation"
                        + " WHERE operation.actor_id = @ActorId AND operation.operation_id = @OperationId;",
                        key, transaction, cancellationToken: cancellationToken));
                if (operation == null)
                {
                    return null;
                }

                List<AdminLedgerRow> ledger = (await connection.QueryAsync<AdminLedgerRow>(new CommandDefinition(
                    "SELECT " + AdminLedgerRow.SELECT_COLUMNS + " FROM economy_ledger AS ledger"
                    + " WHERE ledger.actor_id = @ActorId AND ledger.operation_id = @OperationId"
                    + " ORDER BY ledger.occurred_at, ledger.ledger_id;",
                    key, transaction, cancellationToken: cancellationToken))).ToList();
                List<AdminItemHistoryRow> history = (await connection.QueryAsync<AdminItemHistoryRow>(new CommandDefinition(
                    "SELECT " + AdminItemHistoryRow.SELECT_COLUMNS + " FROM item_history AS history"
                    + " WHERE history.actor_id = @ActorId AND history.operation_id = @OperationId"
                    + " ORDER BY history.item_id, history.seq;",
                    key, transaction, cancellationToken: cancellationToken))).ToList();
                List<OutboxRow> outbox = (await connection.QueryAsync<OutboxRow>(new CommandDefinition(
                    """
                    SELECT event_id AS "EventId", aggregate_id AS "AggregateId", consumer_key AS "ConsumerKey",
                        event_kind AS "EventKind", payload::text AS "Payload", created_at AS "CreatedAt",
                        delivered_at AS "DeliveredAt", retries AS "Retries", next_attempt_at AS "NextAttemptAt"
                    FROM outbox WHERE actor_id = @ActorId AND operation_id = @OperationId
                    ORDER BY created_at, event_id;
                    """, key, transaction, cancellationToken: cancellationToken))).ToList();

                // 어드민 작업의 actor는 운영자 ID다. 다른 actor의 작업에는 감사 행이 없다
                List<AdminAuditRow> audit = (await connection.QueryAsync<AdminAuditRow>(new CommandDefinition(
                    "SELECT " + AdminAuditRow.SELECT_COLUMNS + " FROM admin_audit_log AS audit"
                    + " WHERE audit.admin_id = @ActorId AND audit.operation_id = @OperationId"
                    + " ORDER BY audit.occurred_at, audit.audit_id;",
                    key, transaction, cancellationToken: cancellationToken))).ToList();
                await transaction.CommitAsync(cancellationToken);

                return new AdminOperationDetailResponse(operation.ToContract(),
                    ledger.Select(row => row.ToContract()).ToArray(),
                    history.Select(row => row.ToContract()).ToArray(),
                    outbox.Select(row => row.ToContract()).ToArray(),
                    audit.Select(row => row.ToContract()).ToArray());
            }
        }

        private sealed class ParticipantRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid      ParticipantId      { get; set; }
            public Guid      AccountId          { get; set; }
            public Guid      CharacterId        { get; set; }
            public string    State              { get; set; } = string.Empty;
            public long      EssenceAccrued     { get; set; }
            public long      BaseEssenceAccrued { get; set; }
            public DateTime? SettledAt          { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public AdminSessionParticipant ToContract()
            {
                return new AdminSessionParticipant(ParticipantId.ToString("D"), AccountId.ToString("D"),
                    CharacterId.ToString("D"), State, EssenceAccrued, BaseEssenceAccrued,
                    AdminLogValues.ToUnixMs(SettledAt));
            }
        }

        private sealed class EventRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public long     EventSeq      { get; set; }
            public Guid     EventId       { get; set; }
            public int      EventKind     { get; set; }
            public Guid?    ParticipantId { get; set; }
            public string   Payload       { get; set; } = string.Empty;
            public Guid     OperationId   { get; set; }
            public DateTime CommittedAt   { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public AdminSessionEvent ToContract()
            {
                return new AdminSessionEvent(EventSeq, EventId.ToString("D"), EventKind,
                    AdminLogValues.ToText(ParticipantId), AdminLogValues.ToJson(Payload), OperationId.ToString("D"),
                    AdminLogValues.ToUnixMs(CommittedAt));
            }
        }

        private sealed class OutboxRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid      EventId       { get; set; }
            public Guid      AggregateId   { get; set; }
            public string    ConsumerKey   { get; set; } = string.Empty;
            public string    EventKind     { get; set; } = string.Empty;
            public string    Payload       { get; set; } = string.Empty;
            public DateTime  CreatedAt     { get; set; }
            public DateTime? DeliveredAt   { get; set; }
            public int       Retries       { get; set; }
            public DateTime  NextAttemptAt { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public AdminOutboxEntry ToContract()
            {
                return new AdminOutboxEntry(EventId.ToString("D"), AggregateId.ToString("D"), ConsumerKey, EventKind,
                    AdminLogValues.ToJson(Payload), AdminLogValues.ToUnixMs(CreatedAt),
                    AdminLogValues.ToUnixMs(DeliveredAt), Retries, AdminLogValues.ToUnixMs(NextAttemptAt));
            }
        }
    }
}
