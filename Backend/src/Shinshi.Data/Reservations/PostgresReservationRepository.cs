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
using Shinshi.Data.Auth;
using Shinshi.Data.Growth;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Reservations
{
    /// <summary> 출정 예약, 취소, 만료 해제, 접속 토큰과 임대 이관. 예약은 총량을 바꾸지 않고 예약량만 바꾼다 </summary>
    public sealed class PostgresReservationRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string RESERVE_KIND = "game-session.reserve";
        public const string CANCEL_KIND  = "game-session.cancel";

        private const string ASSET_TICKET = "Ticket";
        private const string ASSET_ITEM   = "Item";
        private const long   TICKET_COST  = 1;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly PostgresAccountLeaseRepository _leases;
        private readonly IJoinTokenIssuer _joinTokens;

        // null이면 해금을 판정하지 않는다(저장소 단위 테스트). API는 항상 넣는다(AddShinshiGameSessions)
        private readonly SortieUnlockPolicy? _unlocks;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresReservationRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            PostgresAccountLeaseRepository leases, IJoinTokenIssuer joinTokens)
            : this(dataSource, executor, leases, joinTokens, null)
        {
        }

        public PostgresReservationRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            PostgresAccountLeaseRepository leases, IJoinTokenIssuer joinTokens, SortieUnlockPolicy? unlocks)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(leases);
            ArgumentNullException.ThrowIfNull(joinTokens);
            _dataSource = dataSource;
            _executor   = executor;
            _leases     = leases;
            _joinTokens = joinTokens;
            _unlocks    = unlocks;
        }

        /// <summary> 전원의 보스 티켓 1장과 지참 물약을 한 트랜잭션으로 예약하고 유휴 던전을 배정한다. 하나라도 부족하면 아무것도 예약하지 않는다 </summary>
        public async Task<ReservationOutcome> ReserveAsync(ReservationInput input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!input.IsValid())
            {
                throw new ArgumentException("Reservation request is invalid.", nameof(input));
            }

            OperationRequest request = new OperationRequest(input.TownInstanceId, input.OperationId, RESERVE_KIND,
                input.ToRequestJson());
            OperationResult result = await _executor.ExecuteAsync(request,
                (context, token) => ReserveEffectAsync(input, context, token), cancellationToken);
            return await ToOutcomeAsync(result, cancellationToken);
        }

        /// <summary> 같은 예약 요청의 저장된 결과만 읽고 아무것도 예약하지 않는다. 처음 보는 operationId면 null이다 </summary>
        public async Task<ReservationOutcome?> FindStoredAsync(ReservationInput input,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!input.IsValid())
            {
                throw new ArgumentException("Reservation request is invalid.", nameof(input));
            }

            OperationRequest request = new OperationRequest(input.TownInstanceId, input.OperationId, RESERVE_KIND,
                input.ToRequestJson());
            OperationResult? stored = await _executor.FindStoredAsync(request, cancellationToken);
            return stored == null ? null : await ToOutcomeAsync(stored, cancellationToken);
        }

        private async Task<ReservationOutcome> ToOutcomeAsync(OperationResult result, CancellationToken cancellationToken)
        {
            if ((result.Status != ContractCodes.OperationStatus.COMMITTED)
                || !Guid.TryParse(result.ReceiptId, out Guid sessionId))
            {
                return new ReservationOutcome(result, null);
            }

            // 재전송도 같은 세션을 다시 읽는다. 이후 만료나 시작으로 상태가 바뀌었을 수 있다
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            ReservedSessionView? session = await ReadReservedSessionAsync(connection, sessionId, cancellationToken);
            return new ReservationOutcome(result, session);
        }

        /// <summary> 시작 전 출정을 예약한 마을 서버가 취소한다. /start, 만료 해제와 경합하면 한쪽만 이긴다 </summary>
        public Task<OperationResult> CancelAsync(Guid townInstanceId, Guid sessionId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            if ((townInstanceId == Guid.Empty) || (sessionId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Town, session and operation IDs are required.");
            }

            OperationRequest request = new OperationRequest(townInstanceId, operationId, CANCEL_KIND,
                SessionDb.ToRequestJson(new { sessionId = sessionId.ToString("D") }));
            return _executor.ExecuteAsync(request, async (context, token) =>
            {
                (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
                long? revision = await ReleaseReservedAsync(connection, transaction, sessionId, townInstanceId,
                    onlyExpired: false, GameSessionStates.RESULT_RELEASED, token);
                if (revision.HasValue)
                {
                    return OperationDecision.Committed(revision.Value, Guid.NewGuid().ToString("D"));
                }

                GameSessionSnapshot? current = await GameSessionSnapshot.ReadAsync(connection, transaction, sessionId, token);
                if ((current == null) || (current.ReservedByInstanceId != townInstanceId))
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                return OperationDecision.Rejected(current.ClosedReason(), current.Revision);
            }, cancellationToken);
        }

        /// <summary> expires_at이 지난 Reserved 출정을 한 건씩 해제한다. 다른 작업자나 /start가 잡은 행은 건너뛴다 </summary>
        public async Task<int> ReleaseExpiredAsync(int maxSessions, CancellationToken cancellationToken = default)
        {
            if (maxSessions <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSessions));
            }

            int released = 0;
            for (int index = 0; index < maxSessions; ++index)
            {
                bool releasedOne = await SessionDb.RetryAsync(ReleaseOneExpiredAsync, cancellationToken);
                if (!releasedOne)
                {
                    break;
                }
                ++released;
            }

            return released;
        }

        /// <summary> 예약한 마을 서버에 참가자 전원의 60초 일회용 입장권을 발급한다. 다시 부르면 미교환 토큰을 새로 바꾼다 </summary>
        public async Task<JoinTicketsOutcome> IssueJoinTicketsAsync(Guid townInstanceId, Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            if ((townInstanceId == Guid.Empty) || (sessionId == Guid.Empty))
            {
                throw new ArgumentException("Town and session IDs are required.");
            }

            ReservedSessionView? session;
            await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                session = await ReadReservedSessionAsync(connection, sessionId, cancellationToken);
            }
            if ((session == null) || (session.ReservedByInstanceId != townInstanceId))
            {
                return new JoinTicketsOutcome(ContractCodes.RejectReason.SESSION_MISMATCH, Array.Empty<JoinTicket>());
            }
            if (session.State != GameSessionStates.RESERVED)
            {
                return new JoinTicketsOutcome(ContractCodes.RejectReason.WRONG_PHASE, Array.Empty<JoinTicket>());
            }
            if (session.IsExpired)
            {
                return new JoinTicketsOutcome(ContractCodes.RejectReason.RESERVATION_CONFLICT, Array.Empty<JoinTicket>());
            }

            // 토큰 발급은 인증 작업의 경계라 DB 트랜잭션 밖에서 참가자마다 부른다
            List<JoinTicket> tickets = new List<JoinTicket>(session.Participants.Count);
            foreach (ReservedParticipantView participant in session.Participants)
            {
                DungeonJoinGrant grant = new DungeonJoinGrant(session.SessionId, participant.ParticipantId,
                    participant.AccountId, participant.CharacterId, session.InstanceId, session.Address, session.Port,
                    ServerPoolTimings.JoinTokenLifetime, session.ExpiresAt);
                try
                {
                    tickets.Add(await _joinTokens.IssueDungeonTicketAsync(grant, cancellationToken));
                }
                catch (JoinTokenIssuanceException exception)
                {
                    // 일부 참가자만 발급돼도 호출자에게 부분 성공을 반환하지 않는다
                    return new JoinTicketsOutcome(exception.ReasonCode, Array.Empty<JoinTicket>());
                }
            }

            return new JoinTicketsOutcome(ContractCodes.RejectReason.NONE, tickets);
        }

        /// <summary> 토큰을 교환한 참가자의 접속 임대를 마을에서 배정 던전으로 옮긴다. 같은 newLeaseId 재시도는 성공으로 본다 </summary>
        public async Task<LeaseTransferOutcome> TransferParticipantLeaseAsync(Guid dungeonInstanceId, Guid sessionId,
            Guid participantId, Guid newLeaseId, CancellationToken cancellationToken = default)
        {
            if ((dungeonInstanceId == Guid.Empty) || (sessionId == Guid.Empty) || (participantId == Guid.Empty)
                || (newLeaseId == Guid.Empty))
            {
                throw new ArgumentException("Dungeon, session, participant and lease IDs are required.");
            }

            LeaseSourceRow? source;
            await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                source = await connection.QuerySingleOrDefaultAsync<LeaseSourceRow>(new CommandDefinition(
                    """
                    SELECT session.state AS "SessionState", session.instance_id AS "InstanceId",
                        session.reserved_by_instance_id AS "ReservedByInstanceId",
                        participant.account_id AS "AccountId", participant.state AS "ParticipantState",
                        lease.lease_id AS "LeaseId", lease.instance_id AS "LeaseInstanceId",
                        lease.expires_at AS "LeaseExpiresAt",
                        COALESCE(lease.expires_at > clock_timestamp(), false) AS "LeaseAlive"
                    FROM game_sessions AS session
                    JOIN session_participants AS participant
                        ON participant.session_id = session.session_id AND participant.participant_id = @ParticipantId
                    LEFT JOIN account_leases AS lease ON lease.account_id = participant.account_id
                    WHERE session.session_id = @SessionId;
                    """, new { SessionId = sessionId, ParticipantId = participantId },
                    cancellationToken: cancellationToken));
            }

            if ((source == null) || (source.InstanceId != dungeonInstanceId))
            {
                return LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            // 같은 newLeaseId 재시도의 단축 경로다. 만료된 임대를 성공으로 돌려주면 던전이 끊긴 소유권을 살아 있다고 믿는다
            if ((source.LeaseId == newLeaseId) && (source.LeaseInstanceId == dungeonInstanceId)
                && source.LeaseExpiresAt.HasValue)
            {
                if (!source.LeaseAlive)
                {
                    return LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED);
                }

                return LeaseTransferOutcome.Transferred(newLeaseId, ToUtc(source.LeaseExpiresAt.Value));
            }
            if ((source.SessionState != GameSessionStates.RESERVED) || (source.ParticipantState != GameSessionStates.PARTICIPANT_RESERVED))
            {
                return LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.WRONG_PHASE);
            }
            if (!source.LeaseId.HasValue || !source.LeaseInstanceId.HasValue
                || (source.LeaseInstanceId != source.ReservedByInstanceId))
            {
                return LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            AccountLeaseOutcome transfer = await _leases.TransferAsync(source.AccountId, source.LeaseId.Value,
                source.LeaseInstanceId.Value, newLeaseId, dungeonInstanceId, ContractCodes.ServerRole.DUNGEON,
                ServerPoolTimings.DungeonLease, cancellationToken);
            return transfer.Status switch
            {
                EAccountLeaseStatus.Transferred => LeaseTransferOutcome.Transferred(newLeaseId, transfer.Lease!.ExpiresAt),
                EAccountLeaseStatus.LeaseExpired => LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED),
                EAccountLeaseStatus.LeaseIdConflict => LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT),
                EAccountLeaseStatus.TransientFailure => LeaseTransferOutcome.Retry(),
                EAccountLeaseStatus.ServerUnavailable => LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.NO_CAPACITY),
                EAccountLeaseStatus.SourceServerUnavailable => LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.NO_CAPACITY),
                _ => LeaseTransferOutcome.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH)
            };
        }

        /// <summary> 예약 세션과 참가자를 읽는다. 없으면 null </summary>
        public static async Task<ReservedSessionView?> ReadReservedSessionAsync(DbConnection connection, Guid sessionId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            SessionRow? row = await connection.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition(
                """
                SELECT session.session_id AS "SessionId", session.instance_id AS "InstanceId",
                    instance.address AS "Address", instance.port AS "Port", session.state AS "State",
                    COALESCE(session.expires_at, session.created_at) AS "ExpiresAt",
                    COALESCE(session.expires_at <= clock_timestamp(), true) AS "IsExpired",
                    session.reserved_by_instance_id AS "ReservedByInstanceId"
                FROM game_sessions AS session
                JOIN server_instances AS instance ON instance.instance_id = session.instance_id
                WHERE session.session_id = @SessionId;
                """, new { SessionId = sessionId }, cancellationToken: cancellationToken));
            if (row == null)
            {
                return null;
            }

            IEnumerable<ReservedParticipantView> participants = await connection.QueryAsync<ReservedParticipantView>(
                new CommandDefinition(
                    """
                    SELECT participant_id AS "ParticipantId", account_id AS "AccountId", character_id AS "CharacterId"
                    FROM session_participants WHERE session_id = @SessionId ORDER BY account_id;
                    """, new { SessionId = sessionId }, cancellationToken: cancellationToken));
            return new ReservedSessionView(row.SessionId, row.InstanceId, row.Address, row.Port, row.State,
                ToUtc(row.ExpiresAt), row.IsExpired, row.ReservedByInstanceId, participants.ToList());
        }

        /// <summary> Reserved 출정 하나를 해제한다. 상태 조건부 갱신에 진 경우 null이며 호출자가 확정 상태를 읽는다 </summary>
        public static async Task<long?> ReleaseReservedAsync(DbConnection connection, DbTransaction transaction,
            Guid sessionId, Guid? reservedBy, bool onlyExpired, string result, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentException.ThrowIfNullOrWhiteSpace(result);

            // 잠금 순서: 세션 -> 계정(오름차순) -> 지갑, 아이템 -> 인스턴스. /start, 정산과 같은 순서다
            ReleasedRow? released = await connection.QuerySingleOrDefaultAsync<ReleasedRow>(new CommandDefinition(
                """
                UPDATE game_sessions
                SET state = 'Closed', result = @Result, closed_at = clock_timestamp(), revision = revision + 1
                WHERE session_id = @SessionId AND state = 'Reserved'
                    AND (CAST(@ReservedBy AS uuid) IS NULL OR reserved_by_instance_id = @ReservedBy)
                    AND (NOT @OnlyExpired OR expires_at <= clock_timestamp())
                RETURNING instance_id AS "InstanceId", instance_boot_id AS "InstanceBootId", revision AS "Revision";
                """, new { SessionId = sessionId, ReservedBy = reservedBy, OnlyExpired = onlyExpired, Result = result },
                transaction, cancellationToken: cancellationToken));
            if (released == null)
            {
                return null;
            }

            Guid[] accounts = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT account_id FROM session_participants WHERE session_id = @SessionId;",
                new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken))).ToArray();
            await SessionDb.LockAccountsAsync(connection, transaction, accounts, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
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
                SET tickets_reserved = wallet.tickets_reserved - 1, revision = wallet.revision + 1
                FROM target WHERE wallet.account_id = target.account_id;

                WITH target AS (
                    SELECT item.account_id, item.definition_id, item.binding_kind,
                        reservation.quantity - reservation.consumed AS remaining
                    FROM account_items AS item
                    JOIN reservations AS reservation ON reservation.account_id = item.account_id
                        AND reservation.definition_id = item.definition_id AND reservation.binding_kind = item.binding_kind
                    WHERE reservation.session_id = @SessionId AND reservation.asset_kind = 'Item'
                        AND reservation.state = 'Reserved' AND reservation.quantity > reservation.consumed
                    ORDER BY item.account_id, item.definition_id, item.binding_kind
                    FOR UPDATE OF item
                )
                UPDATE account_items AS item SET reserved = item.reserved - target.remaining
                FROM target
                WHERE item.account_id = target.account_id AND item.definition_id = target.definition_id
                    AND item.binding_kind = target.binding_kind;

                UPDATE reservations SET state = 'Released', closed_at = clock_timestamp()
                WHERE session_id = @SessionId AND state = 'Reserved';
                UPDATE session_participants SET state = 'Released', settled_at = clock_timestamp()
                WHERE session_id = @SessionId AND state = 'Reserved';
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));

            // 시작 전에 던전으로 이관된 접속 임대가 있으면 푼다. 마을 임대는 그대로 둔다(마을 연결은 끊기지 않았다)
            await PostgresAccountLeaseRepository.ReleaseForClosedSessionAsync(connection, transaction, released.InstanceId,
                accounts, cancellationToken);
            await PostgresServerPoolRepository.ReturnToPoolAsync(connection, transaction, released.InstanceId,
                released.InstanceBootId, cancellationToken);
            return released.Revision;
        }

        private async Task<OperationDecision> ReserveEffectAsync(ReservationInput input, OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            Guid[] accounts = input.Participants.Select(participant => participant.AccountId).ToArray();
            bool boss = input.Mode == GameSessionCodes.Mode.BOSS;

            bool townAlive = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM server_instances WHERE instance_id = @Town AND role = @TownRole
                    AND state <> 'Offline' AND heartbeat_at > clock_timestamp() - @HeartbeatTimeout);
                """, new { Town = input.TownInstanceId, TownRole = ContractCodes.ServerRole.TOWN,
                    HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout },
                transaction, cancellationToken: cancellationToken));
            if (!townAlive)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            // 잠금 순서: 계정(오름차순) -> 캐릭터 -> 지갑, 아이템 -> 인스턴스(SKIP LOCKED). 성장 쓰기도 계정을 먼저 잠가
            // 열린 출정 검사와 이 예약이 서로 끼어들지 못한다
            int locked = await SessionDb.LockAccountsAsync(connection, transaction, accounts, cancellationToken);
            if (locked != accounts.Length)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            int? characterReason = await CheckCharactersAsync(connection, transaction, input, cancellationToken);
            if (characterReason.HasValue)
            {
                return OperationDecision.Rejected(characterReason.Value);
            }

            long leased = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                SELECT count(*) FROM account_leases
                WHERE account_id = ANY(@Accounts) AND instance_id = @Town AND expires_at > clock_timestamp();
                """, new { Accounts = accounts, Town = input.TownInstanceId }, transaction,
                cancellationToken: cancellationToken));
            if (leased != accounts.Length)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            // 출시 프로필은 성장 콘텐츠에 없는 전장(테스트용 지도, 빈 battleId)의 출정을 열지 않는다
            int knownReason = _unlocks?.CheckKnownBattle(input.Mode, input.BattleId) ?? ContractCodes.RejectReason.NONE;
            if (knownReason != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(knownReason);
            }

            // 해금 최종 판정. 계정 행을 잠근 뒤 읽으므로 진행 기록(정산, 안내 완료)과 엇갈리지 않는다
            if ((_unlocks != null) && (_unlocks.TargetOf(input.Mode, input.BattleId) != null))
            {
                Dictionary<Guid, AccountProgressState> progress = await PostgresAccountProgressRepository.ReadStatesAsync(
                    connection, transaction, accounts, cancellationToken);
                int unlockReason = _unlocks.Check(input.Mode, input.BattleId, progress.Values);
                if (unlockReason != ContractCodes.RejectReason.NONE)
                {
                    return OperationDecision.Rejected(unlockReason);
                }
            }

            bool busy = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM session_participants
                    WHERE account_id = ANY(@Accounts) AND state IN ('Reserved', 'Active'));
                """, new { Accounts = accounts }, transaction, cancellationToken: cancellationToken));
            if (busy)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.RESERVATION_CONFLICT);
            }

            if (boss && !await ReserveTicketsAsync(connection, transaction, accounts, cancellationToken))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_TICKET);
            }
            if (!await ReservePotionsAsync(connection, transaction, input, cancellationToken))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }

            AllocatedDungeon? dungeon = await PostgresServerPoolRepository.TryAllocateIdleDungeonAsync(connection,
                transaction, input.ContentVersion, cancellationToken);
            if (dungeon == null)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.NO_CAPACITY);
            }

            // 캐릭터 행을 공유 잠금한 채 고정하므로 성장 쓰기(캐릭터 행 배타 잠금)가 끼어들지 못한다
            Dictionary<Guid, string> loadouts = await CombatLoadoutBuilder.BuildAsync(connection, transaction,
                input.Participants.Select(participant => participant.CharacterId).ToArray(), input.ContentVersion,
                cancellationToken);
            Guid sessionId = Guid.NewGuid();
            long revision = await InsertSessionAsync(connection, transaction, input, sessionId, dungeon, loadouts,
                cancellationToken);
            return OperationDecision.Committed(revision, sessionId.ToString("D"));
        }

        private static async Task<int?> CheckCharactersAsync(DbConnection connection, DbTransaction transaction,
            ReservationInput input, CancellationToken cancellationToken)
        {
            Guid[] characters = input.Participants.Select(participant => participant.CharacterId).ToArray();
            Dictionary<Guid, CharacterRow> rows = (await connection.QueryAsync<CharacterRow>(new CommandDefinition(
                """
                SELECT character_id AS "CharacterId", account_id AS "AccountId", loadout_revision AS "Revision"
                FROM characters WHERE character_id = ANY(@Characters) AND deleted_at IS NULL
                ORDER BY character_id FOR SHARE;
                """, new { Characters = characters }, transaction, cancellationToken: cancellationToken)))
                .ToDictionary(row => row.CharacterId);

            foreach (ParticipantReservationInput participant in input.Participants)
            {
                if (!rows.TryGetValue(participant.CharacterId, out CharacterRow? row) || (row.AccountId != participant.AccountId))
                {
                    return ContractCodes.RejectReason.SESSION_MISMATCH;
                }
                // 전투 준비(장비, 수련, 부적)만 올리는 loadout_revision과 대조한다. 정산의 정기 지급처럼 준비와 무관한
                // 잔액 변경은 이 값을 바꾸지 않아 마을이 확인한 준비가 보상 지급만으로 깨지지 않는다
                if (row.Revision != participant.CharacterRevision)
                {
                    return ContractCodes.RejectReason.STALE_REVISION;
                }
            }

            return null;
        }

        private static async Task<bool> ReserveTicketsAsync(DbConnection connection, DbTransaction transaction,
            Guid[] accounts, CancellationToken cancellationToken)
        {
            // 검사와 증가를 한 문장으로 묶어 갱신 손실을 막는다. 한 명이라도 빠지면 호출자가 효과 전체를 되돌린다
            IEnumerable<Guid> reserved = await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                WITH target AS (
                    SELECT account_id FROM account_wallets WHERE account_id = ANY(@Accounts)
                    ORDER BY account_id FOR UPDATE
                )
                UPDATE account_wallets AS wallet
                SET tickets_reserved = wallet.tickets_reserved + @Cost, revision = wallet.revision + 1
                FROM target
                WHERE wallet.account_id = target.account_id AND wallet.tickets - wallet.tickets_reserved >= @Cost
                RETURNING wallet.account_id;
                """, new { Accounts = accounts, Cost = TICKET_COST }, transaction, cancellationToken: cancellationToken));
            return reserved.Count() == accounts.Length;
        }

        private static async Task<bool> ReservePotionsAsync(DbConnection connection, DbTransaction transaction,
            ReservationInput input, CancellationToken cancellationToken)
        {
            List<(Guid Account, PotionReservationInput Potion)> potions = input.Participants
                .SelectMany(participant => participant.Potions.Select(potion => (participant.AccountId, potion)))
                .ToList();
            if (potions.Count == 0)
            {
                return true;
            }

            IEnumerable<Guid> reserved = await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                WITH request AS (
                    SELECT * FROM unnest(@Accounts::uuid[], @Definitions::text[], @Bindings::text[], @Quantities::bigint[])
                        AS requested(account_id, definition_id, binding_kind, quantity)
                ), target AS (
                    SELECT item.account_id, item.definition_id, item.binding_kind
                    FROM account_items AS item
                    JOIN request ON request.account_id = item.account_id AND request.definition_id = item.definition_id
                        AND request.binding_kind = item.binding_kind
                    ORDER BY item.account_id, item.definition_id, item.binding_kind
                    FOR UPDATE OF item
                )
                UPDATE account_items AS item SET reserved = item.reserved + request.quantity
                FROM request, target
                WHERE item.account_id = target.account_id AND item.definition_id = target.definition_id
                    AND item.binding_kind = target.binding_kind AND request.account_id = target.account_id
                    AND request.definition_id = target.definition_id AND request.binding_kind = target.binding_kind
                    AND item.quantity - item.reserved >= request.quantity
                RETURNING item.account_id;
                """, new
                {
                    Accounts = potions.Select(entry => entry.Account).ToArray(),
                    Definitions = potions.Select(entry => entry.Potion.DefinitionId).ToArray(),
                    Bindings = potions.Select(entry => entry.Potion.BindingKind).ToArray(),
                    Quantities = potions.Select(entry => entry.Potion.Quantity).ToArray()
                }, transaction, cancellationToken: cancellationToken));
            return reserved.Count() == potions.Count;
        }

        private static async Task<long> InsertSessionAsync(DbConnection connection, DbTransaction transaction,
            ReservationInput input, Guid sessionId, AllocatedDungeon dungeon, IReadOnlyDictionary<Guid, string> loadouts,
            CancellationToken cancellationToken)
        {
            bool boss = input.Mode == GameSessionCodes.Mode.BOSS;
            List<(Guid Participant, ParticipantReservationInput Input)> participants = input.Participants
                .Select(participant => (Guid.NewGuid(), participant)).ToList();
            List<(Guid Participant, Guid Account, string Kind, string Definition, string Binding, long Quantity)> rows =
                new List<(Guid, Guid, string, string, string, long)>();
            foreach ((Guid participantId, ParticipantReservationInput participant) in participants)
            {
                if (boss)
                {
                    rows.Add((participantId, participant.AccountId, ASSET_TICKET, string.Empty, string.Empty, TICKET_COST));
                }
                foreach (PotionReservationInput potion in participant.Potions)
                {
                    rows.Add((participantId, participant.AccountId, ASSET_ITEM, potion.DefinitionId, potion.BindingKind,
                        potion.Quantity));
                }
            }

            long revision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT INTO game_sessions (session_id, mode, map_id, state, starting_count, content_version, instance_id,
                    server_lease, reserved_by_instance_id, instance_boot_id, expires_at, battle_id, boss_id, difficulty)
                VALUES (@SessionId, @Mode, @MapId, 'Reserved', @StartingCount, @ContentVersion, @InstanceId,
                    @ServerLease, @Town, @BootId, clock_timestamp() + @Lifetime, @BattleId, @BossId, @Difficulty)
                RETURNING revision;
                """, new
                {
                    SessionId = sessionId,
                    Mode = boss ? GameSessionStates.MODE_BOSS : GameSessionStates.MODE_NORMAL,
                    BattleId = input.BattleId.Length == 0 ? null : input.BattleId,
                    BossId = input.BossId.Length == 0 ? null : input.BossId,
                    Difficulty = input.Difficulty == GameSessionCodes.Difficulty.NONE ? (short?)null : (short)input.Difficulty,
                    input.MapId,
                    StartingCount = input.Participants.Count,
                    input.ContentVersion,
                    dungeon.InstanceId,
                    ServerLease = dungeon.ServerLeaseId,
                    Town = input.TownInstanceId,
                    dungeon.BootId,
                    Lifetime = ServerPoolTimings.ReservationLifetime
                }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO session_participants (session_id, participant_id, account_id, character_id, state,
                    loadout_snapshot)
                SELECT @SessionId, participant.participant_id, participant.account_id, participant.character_id, 'Reserved',
                    CAST(participant.loadout AS jsonb)
                FROM unnest(@ParticipantIds::uuid[], @Accounts::uuid[], @Characters::uuid[], @Loadouts::text[])
                    AS participant(participant_id, account_id, character_id, loadout);
                """, new
                {
                    SessionId = sessionId,
                    ParticipantIds = participants.Select(entry => entry.Participant).ToArray(),
                    Accounts = participants.Select(entry => entry.Input.AccountId).ToArray(),
                    Characters = participants.Select(entry => entry.Input.CharacterId).ToArray(),
                    Loadouts = participants.Select(entry => loadouts[entry.Input.CharacterId]).ToArray()
                }, transaction, cancellationToken: cancellationToken));

            if (rows.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO reservations (reservation_id, session_id, participant_id, account_id, asset_kind,
                        definition_id, binding_kind, quantity, state)
                    SELECT gen_random_uuid(), @SessionId, row.participant_id, row.account_id, row.asset_kind,
                        row.definition_id, row.binding_kind, row.quantity, 'Reserved'
                    FROM unnest(@Participants::uuid[], @Accounts::uuid[], @Kinds::text[], @Definitions::text[],
                        @Bindings::text[], @Quantities::bigint[])
                        AS row(participant_id, account_id, asset_kind, definition_id, binding_kind, quantity);
                    """, new
                    {
                        SessionId = sessionId,
                        Participants = rows.Select(row => row.Participant).ToArray(),
                        Accounts = rows.Select(row => row.Account).ToArray(),
                        Kinds = rows.Select(row => row.Kind).ToArray(),
                        Definitions = rows.Select(row => row.Definition).ToArray(),
                        Bindings = rows.Select(row => row.Binding).ToArray(),
                        Quantities = rows.Select(row => row.Quantity).ToArray()
                    }, transaction, cancellationToken: cancellationToken));
            }

            return revision;
        }

        private async Task<bool> ReleaseOneExpiredAsync(CancellationToken cancellationToken)
        {
            (NpgsqlConnection connection, NpgsqlTransaction transaction) = await SessionDb.BeginAsync(_dataSource,
                cancellationToken);
            await using (connection)
            await using (transaction)
            {
                Guid? sessionId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                    """
                    SELECT session_id FROM game_sessions
                    WHERE state = 'Reserved' AND expires_at <= clock_timestamp()
                    ORDER BY expires_at, session_id
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED;
                    """, transaction: transaction, cancellationToken: cancellationToken));
                if (!sessionId.HasValue)
                {
                    return false;
                }

                long? revision = await ReleaseReservedAsync(connection, transaction, sessionId.Value, null,
                    onlyExpired: true, GameSessionStates.RESULT_EXPIRED, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return revision.HasValue;
            }
        }

        private static DateTimeOffset ToUtc(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private sealed class CharacterRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid CharacterId { get; set; }
            public Guid AccountId   { get; set; }
            public long Revision    { get; set; }
        }

        private sealed class SessionRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid     SessionId            { get; set; }
            public Guid     InstanceId           { get; set; }
            public string   Address              { get; set; } = string.Empty;
            public int      Port                 { get; set; }
            public string   State                { get; set; } = string.Empty;
            public DateTime ExpiresAt            { get; set; }
            public bool     IsExpired            { get; set; }
            public Guid?    ReservedByInstanceId { get; set; }
        }

        private sealed class ReleasedRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid  InstanceId     { get; set; }
            public Guid? InstanceBootId { get; set; }
            public long  Revision       { get; set; }
        }

        private sealed class LeaseSourceRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string    SessionState         { get; set; } = string.Empty;
            public Guid      InstanceId           { get; set; }
            public Guid?     ReservedByInstanceId { get; set; }
            public Guid      AccountId            { get; set; }
            public string    ParticipantState     { get; set; } = string.Empty;
            public Guid?     LeaseId              { get; set; }
            public Guid?     LeaseInstanceId      { get; set; }
            public DateTime? LeaseExpiresAt       { get; set; }
            public bool      LeaseAlive           { get; set; }
        }
    }
}
