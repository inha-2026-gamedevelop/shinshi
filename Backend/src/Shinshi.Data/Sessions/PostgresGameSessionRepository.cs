// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Settlement;

namespace Shinshi.Data.Sessions
{
    /// <summary> 배정 던전 서버의 /start, 순번 이벤트, 체크포인트. 상태 전이는 조건부 갱신 하나만 이긴다 </summary>
    public sealed class PostgresGameSessionRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string START_KIND  = "game-session.start";
        public const string EVENTS_KIND = "game-session.events";

        private const long TICKET_COST = 1;

        // 보스전 참가자 한 명의 물약 사용 상한(소형, 대형 합산). 공용 계약 상수가 단일 원본이고 던전 서버 PotionServer도 같은 값을 쓴다.
        // 던전 서버가 먼저 막고 API는 침해되거나 어긋난 서버를 막는 이중 방어다. 되돌린 사용은 세지 않는다
        public const long BOSS_POTION_USE_LIMIT = GameSessionCodes.Potion.BOSS_USE_LIMIT;

        // 되돌림 원장 줄은 같은 배치의 소비 줄과 같은 자산이라 키를 나눈다
        private const string POTION_RETURN_ENTRY_SCOPE = "revert";

        // 출정 원장 적립. 추가 정기는 essence_accrued, 완료 웨이브 기본 정기는 base_essence_accrued(0007)에 쌓는다
        private const string ACCRUE_EXTRA_ESSENCE_SQL =
            """
            UPDATE session_participants AS participant
            SET essence_accrued = participant.essence_accrued + accrued.amount
            FROM unnest(@Participants::uuid[], @Amounts::bigint[]) AS accrued(participant_id, amount)
            WHERE participant.session_id = @SessionId AND participant.participant_id = accrued.participant_id
                AND participant.state = 'Active'
                AND participant.essence_accrued + accrued.amount <= @MaxEssence;
            """;
        private const string ACCRUE_BASE_ESSENCE_SQL =
            """
            UPDATE session_participants AS participant
            SET base_essence_accrued = participant.base_essence_accrued + accrued.amount
            FROM unnest(@Participants::uuid[], @Amounts::bigint[]) AS accrued(participant_id, amount)
            WHERE participant.session_id = @SessionId AND participant.participant_id = accrued.participant_id
                AND participant.state = 'Active'
                AND participant.base_essence_accrued + accrued.amount <= @MaxEssence;
            """;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly IEconomyLedgerWriter _ledger;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresGameSessionRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            IEconomyLedgerWriter ledger)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(ledger);
            _dataSource = dataSource;
            _executor   = executor;
            _ledger     = ledger;
        }

        /// <summary> 전원 준비 증거로 Reserved -> Running을 한 번 확정하고 보스 티켓을 소비한다. 만료 해제와 경합하면 한쪽만 이긴다 </summary>
        public Task<OperationResult> StartAsync(Guid dungeonInstanceId, Guid sessionId, Guid operationId,
            IReadOnlyCollection<Guid> readyParticipantIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(readyParticipantIds);
            if ((dungeonInstanceId == Guid.Empty) || (sessionId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Dungeon, session and operation IDs are required.");
            }

            Guid[] ready = readyParticipantIds.Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
            OperationRequest request = new OperationRequest(dungeonInstanceId, operationId, START_KIND,
                SessionDb.ToRequestJson(new
                {
                    sessionId = sessionId.ToString("D"),
                    readyParticipantIds = ready.Select(id => id.ToString("D"))
                }));
            return _executor.ExecuteAsync(request, (context, token) =>
                StartEffectAsync(dungeonInstanceId, sessionId, operationId, ready, context, token), cancellationToken);
        }

        /// <summary> 던전당 단조 순번 배치를 저장한다. 중복, 역행, 건너뜀은 배치 전체를 거절하고 마지막 저장 순번을 돌려준다 </summary>
        public Task<OperationResult> AppendEventsAsync(Guid dungeonInstanceId, Guid sessionId, Guid operationId,
            IReadOnlyList<SessionEventInput> events, CancellationToken cancellationToken = default)
        {
            if ((dungeonInstanceId == Guid.Empty) || (sessionId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Dungeon, session and operation IDs are required.");
            }
            if (!SessionEventValidator.IsValid(events))
            {
                throw new ArgumentException("Session event batch is invalid.", nameof(events));
            }

            OperationRequest request = new OperationRequest(dungeonInstanceId, operationId, EVENTS_KIND,
                SessionDb.ToRequestJson(new
                {
                    sessionId = sessionId.ToString("D"),
                    events = events.Select(entry => new
                    {
                        eventSeq = entry.EventSeq,
                        eventId = entry.EventId.ToString("D"),
                        eventKind = entry.EventKind,
                        payload = SessionEventPayloads.ToJson(entry)
                    })
                }));
            return _executor.ExecuteAsync(request, (context, token) =>
                AppendEffectAsync(dungeonInstanceId, sessionId, operationId, events, context, token), cancellationToken);
        }

        /// <summary> 진단용 체크포인트를 저장한다. 같은 순번 재전송은 Duplicate이며 경제 상태를 바꾸지 않는다 </summary>
        public Task<ECheckpointStatus> SaveCheckpointAsync(Guid dungeonInstanceId, Guid sessionId, CheckpointInput input,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if ((dungeonInstanceId == Guid.Empty) || (sessionId == Guid.Empty) || !input.IsValid())
            {
                throw new ArgumentException("Checkpoint request is invalid.");
            }

            return SessionDb.RetryAsync(token => SaveCheckpointOnceAsync(dungeonInstanceId, sessionId, input, token),
                cancellationToken);
        }

        private async Task<OperationDecision> StartEffectAsync(Guid dungeonInstanceId, Guid sessionId, Guid operationId,
            Guid[] ready, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 잠금 순서: 세션(조건부 갱신) -> 계정(오름차순) -> 지갑 -> 인스턴스. 만료 해제, 정산과 같은 순서다
            StartedRow? started = await connection.QuerySingleOrDefaultAsync<StartedRow>(new CommandDefinition(
                """
                UPDATE game_sessions AS session
                SET state = 'Running', started_at = clock_timestamp(), revision = session.revision + 1
                WHERE session.session_id = @SessionId AND session.state = 'Reserved'
                    AND session.instance_id = @InstanceId AND session.expires_at > clock_timestamp()
                    AND EXISTS (SELECT 1 FROM server_instances AS instance
                        WHERE instance.instance_id = session.instance_id AND instance.boot_id = session.instance_boot_id
                            AND instance.state <> 'Offline'
                            AND instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
                RETURNING session.mode AS "Mode", session.revision AS "Revision",
                    session.instance_boot_id AS "InstanceBootId";
                """, new { SessionId = sessionId, InstanceId = dungeonInstanceId,
                    HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout },
                transaction, cancellationToken: cancellationToken));
            if (started == null)
            {
                // 진 쪽은 확정된 상태를 읽어 따른다. 만료 해제가 이겼으면 예약 충돌, 다른 시작 작업이 이겼으면 단계 오류다
                GameSessionSnapshot? current = await GameSessionSnapshot.ReadAsync(connection, transaction, sessionId,
                    cancellationToken);
                if ((current == null) || (current.InstanceId != dungeonInstanceId))
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }
                if ((current.State == GameSessionStates.RESERVED) && !current.IsExpired
                    && await IsLivenessLapseAsync(connection, transaction, sessionId, cancellationToken))
                {
                    // 배정 boot가 Offline도 교체도 아닌데 heartbeat만 늦다. 지금 거절을 저장하면 같은 operationId가 영구히
                    // 막히므로 효과와 결과를 모두 되돌리고 503으로 다시 시도하게 한다
                    throw TransientRejectionException.LivenessUnknown(ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS);
                }
                if (current.State == GameSessionStates.RESERVED)
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.RESERVATION_CONFLICT, current.Revision);
                }

                return OperationDecision.Rejected(current.ClosedReason(), current.Revision);
            }

            List<ParticipantRow> participants = await ReadParticipantsAsync(connection, transaction, sessionId,
                cancellationToken);
            if (!participants.Select(participant => participant.ParticipantId).ToHashSet().SetEquals(ready))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.NOT_ALL_READY);
            }

            await SessionDb.LockAccountsAsync(connection, transaction,
                participants.Select(participant => participant.AccountId), cancellationToken);
            List<EconomyLedgerLine> ledger = new List<EconomyLedgerLine>();
            if (started.Mode == GameSessionStates.MODE_BOSS)
            {
                List<TicketRow> consumed = (await connection.QueryAsync<TicketRow>(new CommandDefinition(
                    """
                    WITH target AS (
                        SELECT wallet.account_id
                        FROM account_wallets AS wallet
                        JOIN reservations AS reservation ON reservation.account_id = wallet.account_id
                        WHERE reservation.session_id = @SessionId AND reservation.asset_kind = 'Ticket'
                            AND reservation.state = 'Reserved'
                        ORDER BY wallet.account_id
                        FOR UPDATE OF wallet
                    )
                    UPDATE account_wallets AS wallet
                    SET tickets = wallet.tickets - @Cost, tickets_reserved = wallet.tickets_reserved - @Cost,
                        revision = wallet.revision + 1
                    FROM target
                    WHERE wallet.account_id = target.account_id AND wallet.tickets_reserved >= @Cost
                    RETURNING wallet.account_id AS "AccountId", wallet.tickets AS "Tickets";
                    """, new { SessionId = sessionId, Cost = TICKET_COST }, transaction,
                    cancellationToken: cancellationToken))).ToList();
                if (consumed.Count != participants.Count)
                {
                    // 예약 때 확보한 티켓이 사라졌다면 다른 경로가 불변 조건을 깼다. 전체를 되돌린다
                    throw new InvalidOperationException("Reserved boss tickets are missing for a session start.");
                }

                ledger.AddRange(consumed.Select(row => new EconomyLedgerLine(row.AccountId, null,
                    EconomyLedgerCodes.ASSET_TICKET, string.Empty, string.Empty, -TICKET_COST, row.Tickets,
                    EconomyLedgerCodes.REASON_SESSION_START_TICKET, EconomyLedgerCodes.SOURCE_GAME_SESSION,
                    sessionId.ToString("D"), operationId, EconomyLedgerCodes.ACTOR_SERVER, dungeonInstanceId)));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE reservations SET state = 'Consumed', closed_at = clock_timestamp()
                WHERE session_id = @SessionId AND asset_kind = 'Ticket' AND state = 'Reserved';
                UPDATE session_participants SET state = 'Active' WHERE session_id = @SessionId AND state = 'Reserved';
                UPDATE server_instances SET state = 'Active'
                WHERE instance_id = @InstanceId AND boot_id = @BootId AND state = 'Reserved';
                """, new { SessionId = sessionId, InstanceId = dungeonInstanceId, BootId = started.InstanceBootId },
                transaction, cancellationToken: cancellationToken));
            await _ledger.AppendAsync(context, ledger, cancellationToken);
            return OperationDecision.Committed(started.Revision, Guid.NewGuid().ToString("D"));
        }

        private async Task<OperationDecision> AppendEffectAsync(Guid dungeonInstanceId, Guid sessionId, Guid operationId,
            IReadOnlyList<SessionEventInput> events, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 세션 행 잠금으로 같은 세션의 배치를 직렬화한다. 순번 검사와 저장이 같은 잠금 안에서 일어난다
            SessionLockRow? session = await connection.QuerySingleOrDefaultAsync<SessionLockRow>(new CommandDefinition(
                """
                SELECT session.state AS "State", session.instance_id AS "InstanceId", session.mode AS "Mode",
                    session.last_event_seq AS "LastEventSeq",
                    EXISTS (SELECT 1 FROM server_instances AS instance
                        WHERE instance.instance_id = session.instance_id AND instance.boot_id = session.instance_boot_id
                            AND instance.state <> 'Offline'
                            AND instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout) AS "IsInstanceAlive",
                    EXISTS (SELECT 1 FROM server_instances AS instance
                        WHERE instance.instance_id = session.instance_id AND instance.boot_id = session.instance_boot_id
                            AND instance.state <> 'Offline') AS "IsBootOnline"
                FROM game_sessions AS session WHERE session.session_id = @SessionId
                FOR UPDATE OF session;
                """, new { SessionId = sessionId, HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout },
                transaction, cancellationToken: cancellationToken));
            if ((session == null) || (session.InstanceId != dungeonInstanceId))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            if (!session.IsInstanceAlive && session.IsBootOnline)
            {
                // 배정 boot는 Offline도 교체도 아니고 heartbeat만 늦다. 거절을 저장하면 이 배치의 operationId가 영구히 막혀
                // 순번이 멈춘다. 저장하지 않고 503으로 같은 배치를 다시 보내게 한다
                throw TransientRejectionException.LivenessUnknown(ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS);
            }
            if (!session.IsInstanceAlive)
            {
                // Offline으로 확정됐거나 새 boot가 등록했다. 이 출정은 장애 복구가 맡는다
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            long last = session.LastEventSeq;
            if (session.State != GameSessionStates.RUNNING)
            {
                return OperationDecision.Rejected(session.State == GameSessionStates.RESERVED
                    ? ContractCodes.RejectReason.WRONG_PHASE : ContractCodes.RejectReason.SESSION_TERMINAL, last);
            }

            // 중복과 역행은 첫 순번이 마지막 저장 순번 이하, 건너뜀은 초과다. 둘 다 저장된 마지막 순번을 돌려준다
            if (events[0].EventSeq != last + 1)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.STALE_REVISION, last);
            }
            long known = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT count(*) FROM session_events WHERE event_id = ANY(@EventIds);",
                new { EventIds = events.Select(entry => entry.EventId).ToArray() }, transaction,
                cancellationToken: cancellationToken));
            if (known > 0)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.STALE_REVISION, last);
            }

            Dictionary<Guid, ParticipantRow> participants = (await ReadParticipantsAsync(connection, transaction,
                sessionId, cancellationToken)).ToDictionary(row => row.ParticipantId);
            if (!ReferencesActiveParticipants(events, participants))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH, last);
            }

            // 물약 소비, 되돌림과 이탈 처리가 함께 잠글 계정을 먼저 오름차순으로 한 번에 잠근다. 따로 잠그면 배치 안에서
            // 순서가 뒤집힌다(되돌림만 있는 계정을 뒤에서 처음 잠그면 앞서 잠근 큰 ID 계정 뒤에 작은 ID를 잡게 된다)
            await SessionDb.LockAccountsAsync(connection, transaction, events
                .Where(entry => (entry.EventKind == GameSessionCodes.EventKind.POTION_USED)
                    || (entry.EventKind == GameSessionCodes.EventKind.POTION_USE_REVERTED)
                    || (entry.EventKind == GameSessionCodes.EventKind.PARTICIPANT_DEPARTED))
                .Select(entry => participants[entry.ParticipantId!.Value].AccountId), cancellationToken);

            List<EconomyLedgerLine> ledger = new List<EconomyLedgerLine>();
            int? potionReason = await ConsumePotionsAsync(connection, transaction, sessionId, operationId,
                dungeonInstanceId, session.Mode, events, participants, ledger, cancellationToken)
                ?? await RevertPotionsAsync(connection, transaction, sessionId, operationId, dungeonInstanceId, events,
                    participants, ledger, cancellationToken);
            if (potionReason.HasValue)
            {
                return OperationDecision.Rejected(potionReason.Value, last);
            }

            if (!await AccrueEssenceAsync(connection, transaction, sessionId, events,
                    GameSessionCodes.EventKind.ESSENCE_ACCRUED, cancellationToken)
                || !await AccrueEssenceAsync(connection, transaction, sessionId, events,
                    GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED, cancellationToken))
            {
                return OperationDecision.Rejected(SettlementSanityLimits.REJECT_REASON, last);
            }

            SessionEventInput final = events[events.Count - 1];
            bool ended = final.EventKind == GameSessionCodes.EventKind.SESSION_ENDED;

            // 엽전은 보스 처치 성공에서만 발행한다(성장 경제 3장). 성공 여부는 검증기가, 보스전 여부는 여기서 본다
            if (ended && (session.Mode != GameSessionStates.MODE_BOSS) && final.Grants.Any(grant => grant.Currency > 0))
            {
                return OperationDecision.Rejected(SettlementSanityLimits.REJECT_REASON, last);
            }
            long endedEssence = ended ? final.Grants.Sum(grant => grant.Essence) : 0;
            if (ended && await ExceedsInstanceDailyEssenceAsync(connection, transaction, dungeonInstanceId, sessionId,
                endedEssence, cancellationToken))
            {
                return OperationDecision.Rejected(SettlementSanityLimits.REJECT_REASON, last);
            }
            await DepartParticipantsAsync(connection, transaction, dungeonInstanceId, sessionId, events, participants,
                cancellationToken);
            await InsertEventsAsync(connection, transaction, sessionId, operationId, events, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE game_sessions
                SET last_event_seq = @LastSeq, revision = revision + 1,
                    state = CASE WHEN @Ended THEN 'Settling' ELSE state END,
                    settling_at = CASE WHEN @Ended THEN clock_timestamp() ELSE settling_at END,
                    ended_event_seq = CASE WHEN @Ended THEN @LastSeq ELSE ended_event_seq END,
                    end_reason = CASE WHEN @Ended THEN @EndReason ELSE end_reason END,
                    settlement_operation_id = CASE WHEN @Ended THEN @SettlementOperationId ELSE settlement_operation_id END,
                    ended_essence = CASE WHEN @Ended THEN @EndedEssence ELSE ended_essence END
                WHERE session_id = @SessionId AND state = 'Running';
                """, new
                {
                    SessionId = sessionId,
                    LastSeq = final.EventSeq,
                    Ended = ended,
                    EndedEssence = endedEssence,
                    EndReason = ended ? final.EndReason : (int?)null,
                    SettlementOperationId = ended ? final.SettlementOperationId : null
                }, transaction, cancellationToken: cancellationToken));
            await _ledger.AppendAsync(context, ledger, cancellationToken);
            return OperationDecision.Committed(final.EventSeq, Guid.NewGuid().ToString("D"));
        }

        // 배정 boot가 등록 상태 그대로(Offline 아님, 교체 아님)인데 heartbeat만 15초를 넘겼다
        private static Task<bool> IsLivenessLapseAsync(DbConnection connection, DbTransaction transaction, Guid sessionId,
            CancellationToken cancellationToken)
        {
            return connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM game_sessions AS session JOIN server_instances AS instance
                    ON instance.instance_id = session.instance_id AND instance.boot_id = session.instance_boot_id
                    WHERE session.session_id = @SessionId AND instance.state <> 'Offline'
                        AND instance.heartbeat_at <= clock_timestamp() - @HeartbeatTimeout);
                """, new { SessionId = sessionId, HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout },
                transaction, cancellationToken: cancellationToken));
        }

        // 침해된 던전이 출정마다 상한 근처를 지급해 하루 동안 쌓는 것을 막는다. 한 인스턴스는 한 번에 한 출정만 배정받으므로
        // 같은 인스턴스의 종료 정산안이 동시에 저장되지 않아 인스턴스 행을 잠그지 않고 합산한다
        private static async Task<bool> ExceedsInstanceDailyEssenceAsync(DbConnection connection, DbTransaction transaction,
            Guid dungeonInstanceId, Guid sessionId, long endedEssence, CancellationToken cancellationToken)
        {
            long previous = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                SELECT COALESCE(sum(ended_essence), 0)::bigint FROM game_sessions
                WHERE instance_id = @InstanceId AND settling_at > clock_timestamp() - @Window
                    AND session_id <> @SessionId;
                """, new { InstanceId = dungeonInstanceId, SessionId = sessionId,
                    Window = SettlementSanityLimits.InstanceEssenceWindow }, transaction, cancellationToken: cancellationToken));
            return previous + endedEssence > SettlementSanityLimits.MAX_INSTANCE_DAILY_ESSENCE;
        }

        private static bool ReferencesActiveParticipants(IReadOnlyList<SessionEventInput> events,
            IReadOnlyDictionary<Guid, ParticipantRow> participants)
        {
            foreach (SessionEventInput entry in events)
            {
                if (entry.ParticipantId.HasValue && !IsActive(participants, entry.ParticipantId.Value)
                    && (entry.EventKind != GameSessionCodes.EventKind.LOOT))
                {
                    return false;
                }
                if (entry.EventKind != GameSessionCodes.EventKind.SESSION_ENDED)
                {
                    continue;
                }

                // 개인 이탈 정산이 아직 없으므로 종료 정산안은 활성 참가자 전원을 정확히 한 번씩 덮어야 한다
                HashSet<Guid> active = participants.Values.Where(row => row.State == GameSessionStates.PARTICIPANT_ACTIVE)
                    .Select(row => row.ParticipantId).ToHashSet();
                if (!active.SetEquals(entry.Grants.Select(grant => grant.ParticipantId)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsActive(IReadOnlyDictionary<Guid, ParticipantRow> participants, Guid participantId)
        {
            return participants.TryGetValue(participantId, out ParticipantRow? row)
                && (row.State == GameSessionStates.PARTICIPANT_ACTIVE);
        }

        private static async Task<int?> ConsumePotionsAsync(DbConnection connection, DbTransaction transaction,
            Guid sessionId, Guid operationId, Guid dungeonInstanceId, string mode, IReadOnlyList<SessionEventInput> events,
            IReadOnlyDictionary<Guid, ParticipantRow> participants, List<EconomyLedgerLine> ledger,
            CancellationToken cancellationToken)
        {
            List<PotionGroup> groups = events.Where(entry => entry.EventKind == GameSessionCodes.EventKind.POTION_USED)
                .GroupBy(entry => (Participant: entry.ParticipantId!.Value, entry.DefinitionId, entry.BindingKind))
                .Select(group => new PotionGroup(group.Key.Participant, participants[group.Key.Participant].AccountId,
                    participants[group.Key.Participant].CharacterId, group.Key.DefinitionId, group.Key.BindingKind,
                    group.LongCount()))
                .ToList();
            if (groups.Count == 0)
            {
                return null;
            }
            if ((mode == GameSessionStates.MODE_BOSS)
                && await ExceedsBossPotionLimitAsync(connection, transaction, sessionId, events, cancellationToken))
            {
                return ContractCodes.RejectReason.POTION_LIMIT;
            }

            await SessionDb.LockAccountsAsync(connection, transaction, groups.Select(group => group.AccountId),
                cancellationToken);
            int reserved = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                UPDATE reservations AS reservation SET consumed = reservation.consumed + used.used_count
                FROM unnest(@Participants::uuid[], @Definitions::text[], @Bindings::text[], @Counts::bigint[])
                    AS used(participant_id, definition_id, binding_kind, used_count)
                WHERE reservation.session_id = @SessionId AND reservation.participant_id = used.participant_id
                    AND reservation.asset_kind = 'Item' AND reservation.definition_id = used.definition_id
                    AND reservation.binding_kind = used.binding_kind AND reservation.state = 'Reserved'
                    AND reservation.consumed + used.used_count <= reservation.quantity
                RETURNING reservation.reservation_id;
                """, new
                {
                    SessionId = sessionId,
                    Participants = groups.Select(group => group.ParticipantId).ToArray(),
                    Definitions = groups.Select(group => group.DefinitionId).ToArray(),
                    Bindings = groups.Select(group => group.BindingKind).ToArray(),
                    Counts = groups.Select(group => group.Count).ToArray()
                }, transaction, cancellationToken: cancellationToken))).Count();
            if (reserved != groups.Count)
            {
                return ContractCodes.RejectReason.POTION_LIMIT;
            }

            List<ItemBalanceRow> balances = (await connection.QueryAsync<ItemBalanceRow>(new CommandDefinition(
                """
                WITH used AS (
                    SELECT * FROM unnest(@Accounts::uuid[], @Definitions::text[], @Bindings::text[], @Counts::bigint[])
                        AS used(account_id, definition_id, binding_kind, used_count)
                ), target AS (
                    SELECT item.account_id, item.definition_id, item.binding_kind
                    FROM account_items AS item
                    JOIN used ON used.account_id = item.account_id AND used.definition_id = item.definition_id
                        AND used.binding_kind = item.binding_kind
                    ORDER BY item.account_id, item.definition_id, item.binding_kind
                    FOR UPDATE OF item
                )
                UPDATE account_items AS item
                SET quantity = item.quantity - used.used_count, reserved = item.reserved - used.used_count
                FROM used, target
                WHERE item.account_id = target.account_id AND item.definition_id = target.definition_id
                    AND item.binding_kind = target.binding_kind AND used.account_id = target.account_id
                    AND used.definition_id = target.definition_id AND used.binding_kind = target.binding_kind
                RETURNING item.account_id AS "AccountId", item.definition_id AS "DefinitionId",
                    item.binding_kind AS "BindingKind", item.quantity AS "Quantity", used.used_count AS "Delta";
                """, new
                {
                    Accounts = groups.Select(group => group.AccountId).ToArray(),
                    Definitions = groups.Select(group => group.DefinitionId).ToArray(),
                    Bindings = groups.Select(group => group.BindingKind).ToArray(),
                    Counts = groups.Select(group => group.Count).ToArray()
                }, transaction, cancellationToken: cancellationToken))).ToList();
            if (balances.Count != groups.Count)
            {
                throw new InvalidOperationException("Reserved potion stock is missing for a session event.");
            }

            ledger.AddRange(balances.Select(row => new EconomyLedgerLine(row.AccountId, null,
                EconomyLedgerCodes.ASSET_ITEM, row.DefinitionId, row.BindingKind, -row.Delta, row.Quantity,
                EconomyLedgerCodes.REASON_SESSION_POTION_USE, EconomyLedgerCodes.SOURCE_GAME_SESSION,
                sessionId.ToString("D"), operationId, EconomyLedgerCodes.ACTOR_SERVER, dungeonInstanceId)));
            return null;
        }

        // 저장된 사용 - 되돌림에 이 배치의 사용 - 되돌림을 더한 참가자별 순사용이 상한을 넘는지 본다.
        // 세션 행을 잠근 채 읽으므로 같은 세션의 다른 배치가 그 사이 끼어들지 않는다
        private static async Task<bool> ExceedsBossPotionLimitAsync(DbConnection connection, DbTransaction transaction,
            Guid sessionId, IReadOnlyList<SessionEventInput> events, CancellationToken cancellationToken)
        {
            Dictionary<Guid, long> net = (await connection.QueryAsync<PotionNetRow>(new CommandDefinition(
                """
                SELECT participant_id AS "ParticipantId",
                    (count(*) FILTER (WHERE event_kind = @Used) - count(*) FILTER (WHERE event_kind = @Reverted))::bigint
                        AS "Net"
                FROM session_events
                WHERE session_id = @SessionId AND event_kind IN (@Used, @Reverted) AND participant_id IS NOT NULL
                GROUP BY participant_id;
                """, new
                {
                    SessionId = sessionId, Used = GameSessionCodes.EventKind.POTION_USED,
                    Reverted = GameSessionCodes.EventKind.POTION_USE_REVERTED
                }, transaction, cancellationToken: cancellationToken)))
                .ToDictionary(row => row.ParticipantId, row => row.Net);
            foreach (SessionEventInput entry in events)
            {
                int delta = entry.EventKind == GameSessionCodes.EventKind.POTION_USED ? 1
                    : entry.EventKind == GameSessionCodes.EventKind.POTION_USE_REVERTED ? -1 : 0;
                if (delta == 0)
                {
                    continue;
                }

                Guid participant = entry.ParticipantId!.Value;
                net[participant] = (net.TryGetValue(participant, out long current) ? current : 0) + delta;
            }

            return net.Values.Any(count => count > BOSS_POTION_USE_LIMIT);
        }

        // 확정된 POTION_USED를 되돌린다. 원 이벤트는 같은 세션, 같은 참가자의 저장된 사용이거나 이 배치에서 앞선 사용이어야 하고
        // 아직 되돌리지 않았어야 한다. 아니면 배치 전체를 SESSION_MISMATCH로 거절한다. 되돌린 물약은 예약분으로 돌아가므로
        // 출정 예약의 consumed를 줄이고 재고와 예약량을 함께 1씩 늘린다. 정산이 남은 예약분을 돌려줄 때 같은 수가 맞는다
        private static async Task<int?> RevertPotionsAsync(DbConnection connection, DbTransaction transaction,
            Guid sessionId, Guid operationId, Guid dungeonInstanceId, IReadOnlyList<SessionEventInput> events,
            IReadOnlyDictionary<Guid, ParticipantRow> participants, List<EconomyLedgerLine> ledger,
            CancellationToken cancellationToken)
        {
            List<SessionEventInput> reverts = events
                .Where(entry => entry.EventKind == GameSessionCodes.EventKind.POTION_USE_REVERTED).ToList();
            if (reverts.Count == 0)
            {
                return null;
            }

            Guid[] targets = reverts.Select(entry => entry.RevertsEventId!.Value).ToArray();
            long already = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                SELECT count(*) FROM session_events
                WHERE event_kind = @Reverted AND (payload ->> 'revertsEventId') = ANY(@Targets);
                """, new
                {
                    Reverted = GameSessionCodes.EventKind.POTION_USE_REVERTED,
                    Targets = targets.Select(target => target.ToString("D")).ToArray()
                }, transaction, cancellationToken: cancellationToken));
            if (already > 0)
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }

            Dictionary<Guid, OriginalPotionRow> originals = (await connection.QueryAsync<OriginalPotionRow>(
                new CommandDefinition(
                    """
                    SELECT event_id AS "EventId", participant_id AS "ParticipantId",
                        payload ->> 'definitionId' AS "DefinitionId", payload ->> 'bindingKind' AS "BindingKind"
                    FROM session_events
                    WHERE session_id = @SessionId AND event_kind = @Used AND event_id = ANY(@Targets);
                    """, new { SessionId = sessionId, Used = GameSessionCodes.EventKind.POTION_USED, Targets = targets },
                    transaction, cancellationToken: cancellationToken)))
                .ToDictionary(row => row.EventId);
            for (int index = 0; index < events.Count; ++index)
            {
                SessionEventInput entry = events[index];
                if (entry.EventKind == GameSessionCodes.EventKind.POTION_USED)
                {
                    originals[entry.EventId] = new OriginalPotionRow
                    {
                        EventId = entry.EventId, ParticipantId = entry.ParticipantId, DefinitionId = entry.DefinitionId,
                        BindingKind = entry.BindingKind, BatchIndex = index
                    };
                }
            }

            List<(Guid Participant, string DefinitionId, string BindingKind)> returned =
                new List<(Guid, string, string)>(reverts.Count);
            for (int index = 0; index < events.Count; ++index)
            {
                SessionEventInput entry = events[index];
                if (entry.EventKind != GameSessionCodes.EventKind.POTION_USE_REVERTED)
                {
                    continue;
                }

                // 같은 배치의 원 이벤트는 되돌림보다 앞에 있어야 한다
                if (!originals.TryGetValue(entry.RevertsEventId!.Value, out OriginalPotionRow? original)
                    || (original.ParticipantId != entry.ParticipantId) || (original.BatchIndex >= index)
                    || string.IsNullOrEmpty(original.DefinitionId) || string.IsNullOrEmpty(original.BindingKind))
                {
                    return ContractCodes.RejectReason.SESSION_MISMATCH;
                }

                returned.Add((entry.ParticipantId!.Value, original.DefinitionId!, original.BindingKind!));
            }

            List<PotionGroup> groups = returned.GroupBy(line => line)
                .Select(group => new PotionGroup(group.Key.Participant, participants[group.Key.Participant].AccountId,
                    participants[group.Key.Participant].CharacterId, group.Key.DefinitionId, group.Key.BindingKind,
                    group.LongCount()))
                .ToList();
            await SessionDb.LockAccountsAsync(connection, transaction, groups.Select(group => group.AccountId),
                cancellationToken);
            int restored = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                UPDATE reservations AS reservation SET consumed = reservation.consumed - returned.returned_count
                FROM unnest(@Participants::uuid[], @Definitions::text[], @Bindings::text[], @Counts::bigint[])
                    AS returned(participant_id, definition_id, binding_kind, returned_count)
                WHERE reservation.session_id = @SessionId AND reservation.participant_id = returned.participant_id
                    AND reservation.asset_kind = 'Item' AND reservation.definition_id = returned.definition_id
                    AND reservation.binding_kind = returned.binding_kind AND reservation.state = 'Reserved'
                    AND reservation.consumed >= returned.returned_count
                RETURNING reservation.reservation_id;
                """, new
                {
                    SessionId = sessionId,
                    Participants = groups.Select(group => group.ParticipantId).ToArray(),
                    Definitions = groups.Select(group => group.DefinitionId).ToArray(),
                    Bindings = groups.Select(group => group.BindingKind).ToArray(),
                    Counts = groups.Select(group => group.Count).ToArray()
                }, transaction, cancellationToken: cancellationToken))).Count();
            if (restored != groups.Count)
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }

            List<ItemBalanceRow> balances = (await connection.QueryAsync<ItemBalanceRow>(new CommandDefinition(
                """
                WITH returned AS (
                    SELECT * FROM unnest(@Accounts::uuid[], @Definitions::text[], @Bindings::text[], @Counts::bigint[])
                        AS returned(account_id, definition_id, binding_kind, returned_count)
                ), target AS (
                    SELECT item.account_id, item.definition_id, item.binding_kind
                    FROM account_items AS item
                    JOIN returned ON returned.account_id = item.account_id
                        AND returned.definition_id = item.definition_id AND returned.binding_kind = item.binding_kind
                    ORDER BY item.account_id, item.definition_id, item.binding_kind
                    FOR UPDATE OF item
                )
                UPDATE account_items AS item
                SET quantity = item.quantity + returned.returned_count, reserved = item.reserved + returned.returned_count
                FROM returned, target
                WHERE item.account_id = target.account_id AND item.definition_id = target.definition_id
                    AND item.binding_kind = target.binding_kind AND returned.account_id = target.account_id
                    AND returned.definition_id = target.definition_id AND returned.binding_kind = target.binding_kind
                RETURNING item.account_id AS "AccountId", item.definition_id AS "DefinitionId",
                    item.binding_kind AS "BindingKind", item.quantity AS "Quantity", returned.returned_count AS "Delta";
                """, new
                {
                    Accounts = groups.Select(group => group.AccountId).ToArray(),
                    Definitions = groups.Select(group => group.DefinitionId).ToArray(),
                    Bindings = groups.Select(group => group.BindingKind).ToArray(),
                    Counts = groups.Select(group => group.Count).ToArray()
                }, transaction, cancellationToken: cancellationToken))).ToList();
            if (balances.Count != groups.Count)
            {
                throw new InvalidOperationException("Reserved potion stock is missing for a potion revert.");
            }

            ledger.AddRange(balances.Select(row => new EconomyLedgerLine(row.AccountId, null,
                EconomyLedgerCodes.ASSET_ITEM, row.DefinitionId, row.BindingKind, row.Delta, row.Quantity,
                EconomyLedgerCodes.REASON_POTION_RETURN, EconomyLedgerCodes.SOURCE_GAME_SESSION,
                sessionId.ToString("D"), operationId, EconomyLedgerCodes.ACTOR_SERVER, dungeonInstanceId,
                POTION_RETURN_ENTRY_SCOPE)));
            return null;
        }

        // 참가자 하나의 출정 누적이 상한을 넘으면 false다. 호출자가 배치 전체를 거절하고 효과를 되돌린다.
        // ESSENCE_ACCRUED는 essence_accrued(추가 정기), BASE_ESSENCE_ACCRUED는 base_essence_accrued(기본 정기)에 쌓는다
        private static async Task<bool> AccrueEssenceAsync(DbConnection connection, DbTransaction transaction, Guid sessionId,
            IReadOnlyList<SessionEventInput> events, int eventKind, CancellationToken cancellationToken)
        {
            List<(Guid Participant, long Amount)> accrued = events
                .Where(entry => entry.EventKind == eventKind)
                .GroupBy(entry => entry.ParticipantId!.Value)
                .Select(group => (group.Key, group.Sum(entry => entry.Amount)))
                .ToList();
            if (accrued.Count == 0)
            {
                return true;
            }
            if (accrued.Any(entry => entry.Amount > SettlementSanityLimits.MAX_SESSION_ESSENCE))
            {
                return false;
            }

            // 정기는 정산 전 출정 원장(참가자 행)에만 쌓는다. 소비 가능한 잔액에 먼저 더하지 않는다.
            // 대상은 앞에서 활성 참가자로 확인했으므로 갱신 수가 모자라면 누적 상한 초과다.
            string sql = eventKind == GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED
                ? ACCRUE_BASE_ESSENCE_SQL : ACCRUE_EXTRA_ESSENCE_SQL;
            int updated = await connection.ExecuteAsync(new CommandDefinition(sql, new
                {
                    SessionId = sessionId,
                    Participants = accrued.Select(entry => entry.Participant).ToArray(),
                    Amounts = accrued.Select(entry => entry.Amount).ToArray(),
                    MaxEssence = SettlementSanityLimits.MAX_SESSION_ESSENCE
                }, transaction, cancellationToken: cancellationToken));
            return updated == accrued.Count;
        }

        // PARTICIPANT_DEPARTED: 이탈 시각과 이유를 처음 한 번만 기록하고, 이 던전에 남은 그 계정의 접속 임대를 풀어 마을 복귀를 허용하며,
        // 미교환 복귀 토큰을 폐기한다. 참가자 상태는 Active로 남아 경제 정산은 SESSION_ENDED 정산안에서 한 번만 한다.
        // 이미 떠난 참가자의 이탈 이벤트는 기록만 남고 아무것도 바꾸지 않는다(순번이 막히지 않게 거절하지 않는다)
        private static async Task DepartParticipantsAsync(DbConnection connection, DbTransaction transaction,
            Guid dungeonInstanceId, Guid sessionId, IReadOnlyList<SessionEventInput> events,
            IReadOnlyDictionary<Guid, ParticipantRow> participants, CancellationToken cancellationToken)
        {
            List<SessionEventInput> departures = events
                .Where(entry => entry.EventKind == GameSessionCodes.EventKind.PARTICIPANT_DEPARTED)
                .GroupBy(entry => entry.ParticipantId!.Value)
                .Select(group => group.First())
                .ToList();
            if (departures.Count == 0)
            {
                return;
            }

            Guid[] departed = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                UPDATE session_participants AS participant
                SET departed_at = clock_timestamp(), depart_reason = departure.reason
                FROM unnest(@Participants::uuid[], @Reasons::smallint[]) AS departure(participant_id, reason)
                WHERE participant.session_id = @SessionId AND participant.participant_id = departure.participant_id
                    AND participant.state = 'Active' AND participant.departed_at IS NULL
                RETURNING participant.participant_id;
                """, new
                {
                    SessionId = sessionId,
                    Participants = departures.Select(entry => entry.ParticipantId!.Value).ToArray(),
                    Reasons = departures.Select(entry => (short)entry.EndReason).ToArray()
                }, transaction, cancellationToken: cancellationToken))).ToArray();
            if (departed.Length == 0)
            {
                return;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE auth_join_tokens SET revoked_at = clock_timestamp()
                WHERE session_id = @SessionId AND participant_id = ANY(@Participants)
                    AND consumed_at IS NULL AND revoked_at IS NULL;
                """, new { SessionId = sessionId, Participants = departed }, transaction,
                cancellationToken: cancellationToken));
            await PostgresAccountLeaseRepository.ReleaseForClosedSessionAsync(connection, transaction, dungeonInstanceId,
                departed.Select(id => participants[id].AccountId), cancellationToken);
        }

        private static Task InsertEventsAsync(DbConnection connection, DbTransaction transaction, Guid sessionId,
            Guid operationId, IReadOnlyList<SessionEventInput> events, CancellationToken cancellationToken)
        {
            return connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO session_events (session_id, event_seq, event_id, event_kind, participant_id, payload, operation_id)
                SELECT @SessionId, entry.event_seq, entry.event_id, entry.event_kind,
                    NULLIF(entry.participant_id, '00000000-0000-0000-0000-000000000000'::uuid),
                    CAST(entry.payload AS jsonb), @OperationId
                FROM unnest(@Seqs::bigint[], @EventIds::uuid[], @Kinds::integer[], @Participants::uuid[], @Payloads::text[])
                    AS entry(event_seq, event_id, event_kind, participant_id, payload);
                """, new
                {
                    SessionId = sessionId,
                    OperationId = operationId,
                    Seqs = events.Select(entry => entry.EventSeq).ToArray(),
                    EventIds = events.Select(entry => entry.EventId).ToArray(),
                    Kinds = events.Select(entry => entry.EventKind).ToArray(),
                    Participants = events.Select(entry => entry.ParticipantId ?? Guid.Empty).ToArray(),
                    Payloads = events.Select(SessionEventPayloads.ToJson).ToArray()
                }, transaction, cancellationToken: cancellationToken));
        }

        private async Task<ECheckpointStatus> SaveCheckpointOnceAsync(Guid dungeonInstanceId, Guid sessionId,
            CheckpointInput input, CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            long? saved = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                INSERT INTO session_checkpoints (session_id, checkpoint_seq, last_event_seq, schema_version, state_blob)
                SELECT session.session_id, @CheckpointSeq, @LastEventSeq, @SchemaVersion, @State
                FROM game_sessions AS session
                WHERE session.session_id = @SessionId AND session.instance_id = @InstanceId
                    AND session.state IN ('Running', 'Settling') AND session.last_event_seq >= @LastEventSeq
                ON CONFLICT (session_id, checkpoint_seq) DO NOTHING
                RETURNING checkpoint_seq;
                """, new
                {
                    SessionId = sessionId,
                    InstanceId = dungeonInstanceId,
                    input.CheckpointSeq,
                    input.LastEventSeq,
                    input.SchemaVersion,
                    input.State
                }, cancellationToken: cancellationToken));
            if (saved.HasValue)
            {
                return ECheckpointStatus.Saved;
            }

            GameSessionSnapshot? current = await GameSessionSnapshot.ReadAsync(connection, null, sessionId,
                cancellationToken);
            if ((current == null) || (current.InstanceId != dungeonInstanceId))
            {
                return ECheckpointStatus.SessionMismatch;
            }
            if ((current.State != GameSessionStates.RUNNING) && (current.State != GameSessionStates.SETTLING))
            {
                return ECheckpointStatus.WrongPhase;
            }

            return current.LastEventSeq < input.LastEventSeq ? ECheckpointStatus.AheadOfEvents : ECheckpointStatus.Duplicate;
        }

        internal static async Task<List<ParticipantRow>> ReadParticipantsAsync(DbConnection connection,
            DbTransaction transaction, Guid sessionId, CancellationToken cancellationToken)
        {
            return (await connection.QueryAsync<ParticipantRow>(new CommandDefinition(
                """
                SELECT participant_id AS "ParticipantId", account_id AS "AccountId", character_id AS "CharacterId",
                    state AS "State", essence_accrued AS "EssenceAccrued", base_essence_accrued AS "BaseEssenceAccrued"
                FROM session_participants WHERE session_id = @SessionId ORDER BY account_id;
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken))).ToList();
        }

        private sealed record PotionGroup(Guid ParticipantId, Guid AccountId, Guid CharacterId, string DefinitionId,
            string BindingKind, long Count);

        private sealed class StartedRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string Mode           { get; set; } = string.Empty;
            public long   Revision       { get; set; }
            public Guid?  InstanceBootId { get; set; }
        }

        private sealed class SessionLockRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string State           { get; set; } = string.Empty;
            public Guid   InstanceId      { get; set; }
            public string Mode            { get; set; } = string.Empty;
            public long   LastEventSeq    { get; set; }
            public bool   IsInstanceAlive { get; set; }
            public bool   IsBootOnline    { get; set; }
        }

        private sealed class TicketRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AccountId { get; set; }
            public long Tickets   { get; set; }
        }

        private sealed class PotionNetRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid ParticipantId { get; set; }
            public long Net           { get; set; }
        }

        private sealed class OriginalPotionRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid    EventId       { get; set; }
            public Guid?   ParticipantId { get; set; }
            public string? DefinitionId  { get; set; }
            public string? BindingKind   { get; set; }

            // 이 배치 안의 위치. 이미 저장된 원 이벤트는 -1이다
            public int     BatchIndex    { get; set; } = -1;
        }

        private sealed class ItemBalanceRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid   AccountId    { get; set; }
            public string DefinitionId { get; set; } = string.Empty;
            public string BindingKind  { get; set; } = string.Empty;
            public long   Quantity     { get; set; }
            public long   Delta        { get; set; }
        }
    }

    /// <summary> 출정 참가자 행 </summary>
    internal sealed class ParticipantRow
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   ParticipantId  { get; set; }
        public Guid   AccountId      { get; set; }
        public Guid   CharacterId    { get; set; }
        public string State              { get; set; } = string.Empty;

        // 정산 전 출정 원장. 추가 정기와 완료 웨이브 기본 정기 적립 합계다
        public long   EssenceAccrued     { get; set; }
        public long   BaseEssenceAccrued { get; set; }
    }
}
