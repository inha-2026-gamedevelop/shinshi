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
using Shinshi.Data.Auctions;
using Shinshi.Data.Growth;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Settlement
{
    /// <summary> 복구 작업 한 번의 처리 건수와 실패한 세션 </summary>
    public sealed class SessionRecoveryResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public int                                    Settled   { get; }
        public int                                    Cancelled { get; }
        public IReadOnlyList<SessionRecoveryFailure> Failures  { get; }

        /****************************************
        *                Methods
        ****************************************/

        public SessionRecoveryResult(int settled, int cancelled)
            : this(settled, cancelled, Array.Empty<SessionRecoveryFailure>())
        {
        }

        public SessionRecoveryResult(int settled, int cancelled, IReadOnlyList<SessionRecoveryFailure> failures)
        {
            ArgumentNullException.ThrowIfNull(failures);
            Settled   = settled;
            Cancelled = cancelled;
            Failures  = failures;
        }
    }

    /// <summary> 복구에 실패한 세션 하나. Kind는 예외 종류와 SQLSTATE 또는 저장된 거절 코드이며 메시지는 담지 않는다 </summary>
    public sealed class SessionRecoveryFailure
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   SessionId   { get; }
        public string Kind        { get; }
        public int    Failures    { get; }
        public bool   Quarantined { get; }

        // 잠금/문장 제한 초과, 생존 판정 보류 같은 일시 실패다. 실패 횟수와 백오프에 넣지 않았다
        public bool   Retryable   { get; }

        /****************************************
        *                Methods
        ****************************************/

        public SessionRecoveryFailure(Guid sessionId, string kind, int failures, bool quarantined, bool retryable = false)
        {
            SessionId   = sessionId;
            Kind        = kind;
            Failures    = failures;
            Quarantined = quarantined;
            Retryable   = retryable;
        }
    }

    /// <summary> 저장된 SessionEnded 정산안 적용과 확인된 서비스 장애 취소. 같은 세션은 한 번만 종결한다 </summary>
    public sealed class PostgresSettlementRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SETTLEMENT_KIND     = "game-session.settlement";
        public const string SERVICE_CANCEL_KIND = "game-session.service-cancel";

        // 사용자 결정(2026-10-08): 서비스 장애 취소는 기본 정기(base_essence_accrued)와 정산 전 추가 정기 적립분을
        // 전액 지급한다. 강제 종료(격리 세션)는 정책 미정이라 추가 정기 50% 내림(101 -> 50)을 유지한다
        public const long SERVICE_CANCEL_ESSENCE_PERCENT = 100;
        public const long FORCE_CLOSE_ESSENCE_PERCENT    = 50;
        private const long PERCENT_BASE                  = 100;

        // 같은 세션의 복구가 이만큼 연속 실패하면 자동 복구에서 빼고 운영자 확인을 기다린다
        public const int MAX_RECOVERY_FAILURES = 5;

        private const long   TICKET_REFUND    = 1;
        private const string CANCEL_NAME      = "shinshi:service-cancel:";
        private const string REJECTED_FAILURE = "Rejected:";

        // 진행 중 출정의 배정 boot가 장애 취소 임계(60초) 동안 살아 있다는 증거가 없다. Offline 확정과 새 boot 등록은 바로 해당한다.
        // session 별칭과 @CancelTimeout 매개변수가 필요하다
        private const string RUNNING_LOST_CONDITION =
            """
            NOT EXISTS (SELECT 1 FROM server_instances AS instance
                WHERE instance.instance_id = session.instance_id AND instance.boot_id = session.instance_boot_id
                    AND instance.state <> 'Offline'
                    AND instance.heartbeat_at > clock_timestamp() - @CancelTimeout)
            """;

        // 살아 있는 서버가 하나도 없으면 API나 DB 쪽 단절로 보고 장애 취소(환급)를 하지 않는다. @HeartbeatTimeout 매개변수가 필요하다
        private const string POOL_ALIVE_CONDITION =
            """
            EXISTS (SELECT 1 FROM server_instances AS alive
                WHERE alive.state <> 'Offline' AND alive.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
            """;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly IEconomyLedgerWriter _ledger;
        private readonly PostgresAccountProgressRepository? _progress;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> progress가 null이면(진행 기록이 필요 없는 테스트, 도구) 성공 정산에서 계정 진행을 기록하지 않는다 </summary>
        public PostgresSettlementRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            IEconomyLedgerWriter ledger, PostgresAccountProgressRepository? progress = null)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(ledger);
            _dataSource = dataSource;
            _executor   = executor;
            _ledger     = ledger;
            _progress   = progress;
        }

        /// <summary> SessionEnded의 정산안을 한 번 적용한다. 같은 operationId는 최초 결과, 다른 ID는 ALREADY_SETTLED다 </summary>
        public async Task<OperationResult> SettleAsync(Guid callerInstanceId, Guid sessionId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            if ((callerInstanceId == Guid.Empty) || (sessionId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Caller, session and operation IDs are required.");
            }

            // 종료 이벤트보다 먼저 온 정산은 거절을 저장하지 않는다. 저장하면 같은 ID가 나중에 SessionEnded에 기록돼도
            // 영원히 그 거절만 재생되어 세션이 Settling에 갇힌다
            (GameSessionSnapshot? current, DateTimeOffset serverTime) = await ReadSnapshotAsync(sessionId, cancellationToken);
            if ((current != null) && (current.InstanceId == callerInstanceId)
                && (current.State != GameSessionStates.SETTLING) && (current.State != GameSessionStates.CLOSED)
                && (current.State != GameSessionStates.AUCTION))
            {
                return Unstored(operationId, ContractCodes.RejectReason.WRONG_PHASE, current.Revision, serverTime);
            }

            // 던전과 API 복구 작업이 같은 actor, operationId, 본문을 쓰므로 응답 유실 뒤 어느 쪽이 먼저 와도 결과가 같다
            OperationRequest request = new OperationRequest(callerInstanceId, operationId, SETTLEMENT_KIND,
                SessionDb.ToRequestJson(new { sessionId = sessionId.ToString("D") }));
            return await _executor.ExecuteAsync(request, (context, token) =>
                SettleEffectAsync(callerInstanceId, sessionId, operationId, context, token), cancellationToken);
        }

        /// <summary> 종료 이벤트 없이 프로세스가 사라진 Running 출정을 장애 취소한다. 티켓, 사용 물약을 복구하고
        /// 기본 정기와 추가 정기를 전액 지급한다 </summary>
        public async Task<OperationResult?> ServiceCancelAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            if (sessionId == Guid.Empty)
            {
                throw new ArgumentException("Session ID is required.", nameof(sessionId));
            }

            (GameSessionSnapshot? current, DateTimeOffset serverTime) = await ReadSnapshotAsync(sessionId, cancellationToken);
            if (current == null)
            {
                return null;
            }

            Guid operationId = ServiceCancelOperationId(sessionId);

            // 고정 operationId라 거절도 영구 저장된다. 아직 취소할 수 없는 상태(살아 있는 프로세스, Settling)는
            // 저장하지 않고 돌려줘야 나중에 프로세스가 소실됐을 때 같은 ID로 취소할 수 있다.
            // 진행 중 출정은 heartbeat가 60초 끊기고 풀에 살아 있는 서버가 남아 있을 때만 취소한다
            bool cancellable = (current.State == GameSessionStates.RUNNING)
                && await IsServiceCancellableAsync(sessionId, cancellationToken);
            // 경매 단계(Auction)는 정산이 이미 끝난 출정이라 닫힌 출정과 같이 ALREADY_SETTLED가 된다
            bool settled = (current.State == GameSessionStates.CLOSED) || (current.State == GameSessionStates.AUCTION);
            if (!cancellable && !settled)
            {
                return Unstored(operationId, ContractCodes.RejectReason.WRONG_PHASE, current.Revision, serverTime);
            }

            Guid instanceId = current.InstanceId;
            // 복구 작업이 반복 실행돼도 같은 operationId가 되도록 세션 ID에서 만든다. 보상은 이 키로 한 번만 적용된다
            OperationRequest request = new OperationRequest(instanceId, operationId, SERVICE_CANCEL_KIND,
                SessionDb.ToRequestJson(new { sessionId = sessionId.ToString("D") }));
            return await _executor.ExecuteAsync(request, (context, token) =>
                ServiceCancelEffectAsync(instanceId, sessionId, operationId, context, token), cancellationToken);
        }

        /// <summary> 세션의 장애 취소 작업 ID. actor는 배정 서버이고 자동 복구와 수동 복구가 같은 값을 쓴다 </summary>
        public static Guid ServiceCancelOperationId(Guid sessionId)
        {
            return SessionDb.CreateNameBasedGuid(CANCEL_NAME + sessionId.ToString("D"));
        }

        /// <summary> 배정 프로세스가 소실된(heartbeat 만료, 재시작, Offline) 열린 출정과 /settlement가 오지 않은 Settling 출정을 종결한다.
        /// 한 세션의 실패는 그 세션에만 기록하고 다음 세션을 계속 처리한다 </summary>
        public async Task<SessionRecoveryResult> RecoverLostSessionsAsync(int maxSessions,
            CancellationToken cancellationToken = default)
        {
            if (maxSessions <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSessions));
            }

            List<LostSessionRow> lost;
            await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                // 실패가 적은 세션부터 처리해 반복 실패하는 세션이 묶음 앞을 막지 않게 한다. 한도에 닿은 세션은 격리되어
                // 운영자가 확인할 때까지 자동 복구하지 않는다
                lost = (await connection.QueryAsync<LostSessionRow>(new CommandDefinition(
                    """
                    SELECT session.session_id AS "SessionId", session.state AS "State",
                        session.instance_id AS "InstanceId", session.settlement_operation_id AS "SettlementOperationId"
                    FROM game_sessions AS session
                    WHERE session.state IN ('Running', 'Settling') AND session.recovery_failures < @MaxFailures
                        AND (session.next_recovery_at IS NULL OR session.next_recovery_at <= clock_timestamp())
                        AND ((session.state = 'Running' AND
                    """ + " " + RUNNING_LOST_CONDITION + " AND " + POOL_ALIVE_CONDITION + """
                    )
                            OR (session.state = 'Settling'
                                AND (NOT EXISTS (SELECT 1 FROM server_instances AS instance
                                        WHERE instance.instance_id = session.instance_id
                                            AND instance.boot_id = session.instance_boot_id
                                            AND instance.state <> 'Offline'
                                            AND instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
                                    OR session.settling_at <= clock_timestamp() - @SettlingTimeout)))
                    ORDER BY session.recovery_failures, session.session_id
                    LIMIT @Limit;
                    """, new
                    {
                        HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout,
                        CancelTimeout = ServerPoolTimings.ServiceCancelTimeout,
                        SettlingTimeout = ServerPoolTimings.SettlingRecoveryDelay,
                        MaxFailures = MAX_RECOVERY_FAILURES,
                        Limit = maxSessions
                    }, cancellationToken: cancellationToken))).ToList();
            }

            // 후보 조회는 잠그지 않는다. 각 종결이 상태 조건부 갱신과 멱등 키로 한 번만 적용되므로 여러 작업자가 겹쳐도 안전하다
            int settled = 0;
            int cancelled = 0;
            List<SessionRecoveryFailure> failures = new List<SessionRecoveryFailure>();
            foreach (LostSessionRow row in lost)
            {
                string? failure;
                try
                {
                    OperationResult? result = await RecoverOneAsync(row, cancellationToken);
                    bool committed = (result != null) && (result.Status == ContractCodes.OperationStatus.COMMITTED);
                    settled += committed && (row.State == GameSessionStates.SETTLING) ? 1 : 0;
                    cancelled += committed && (row.State == GameSessionStates.RUNNING) ? 1 : 0;
                    failure = IsStuck(result) ? REJECTED_FAILURE + result!.ReasonCode : null;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                    && !PostgresTransactionPolicy.IsConnectionFailure(exception))
                {
                    // 예외 메시지에는 연결 정보가 섞일 수 있어 종류와 SQLSTATE만 남긴다. DB 연결 실패는 잡지 않는다.
                    // 다음 세션도 같은 이유로 실패하므로 배치를 멈추고 호출자가 복구 보류를 연장한다
                    string kind = exception.GetType().Name + ((exception as PostgresException)?.SqlState is string state
                        ? ":" + state : string.Empty);
                    if (IsTransient(exception))
                    {
                        // 잠금/문장 제한 초과, 생존 판정 보류는 세션 자체의 결함이 아니다. 격리 횟수에 넣지 않고 다음 주기에 다시 본다
                        failures.Add(new SessionRecoveryFailure(row.SessionId, kind, 0, false, retryable: true));
                        continue;
                    }

                    failure = kind;
                }

                if (failure != null)
                {
                    int count = await RecordRecoveryFailureAsync(row.SessionId, cancellationToken);
                    failures.Add(new SessionRecoveryFailure(row.SessionId, failure, count,
                        count >= MAX_RECOVERY_FAILURES));
                }
            }

            return new SessionRecoveryResult(settled, cancelled, failures);
        }

        private async Task<OperationResult?> RecoverOneAsync(LostSessionRow row, CancellationToken cancellationToken)
        {
            // 던전과 같은 actor, operationId라 늦게 온 /settlement와 겹쳐도 한 번만 적용된다
            if ((row.State == GameSessionStates.SETTLING) && row.SettlementOperationId.HasValue)
            {
                return await SettleAsync(row.InstanceId, row.SessionId, row.SettlementOperationId.Value, cancellationToken);
            }

            return await ServiceCancelAsync(row.SessionId, cancellationToken);
        }

        private static bool IsTransient(Exception exception)
        {
            return (exception is TransientRejectionException) || PostgresTransactionPolicy.IsRetryable(exception);
        }

        // 저장된 거절은 다시 시도해도 같은 거절이 재생되어 세션이 열린 채 남는다. 일시 거절(WRONG_PHASE)과
        // 이미 종결(ALREADY_SETTLED)은 실패가 아니다
        private static bool IsStuck(OperationResult? result)
        {
            return (result != null) && (result.Status == ContractCodes.OperationStatus.REJECTED)
                && (result.ReasonCode != ContractCodes.RejectReason.WRONG_PHASE)
                && (result.ReasonCode != ContractCodes.RejectReason.ALREADY_SETTLED);
        }

        // 복구 트랜잭션과 별개의 짧은 갱신이다. 실패 기록이 실패해도 다음 주기에 같은 세션을 다시 시도할 뿐이다
        private async Task<int> RecordRecoveryFailureAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                // 지수 백오프: 기본 간격 * 2^(이전 실패 횟수). 같은 원인의 반복 실패가 매 주기 DB와 로그를 채우지 않게 한다
                return await connection.QuerySingleOrDefaultAsync<int>(new CommandDefinition(
                    """
                    UPDATE game_sessions SET recovery_failures = recovery_failures + 1,
                        next_recovery_at = clock_timestamp()
                            + make_interval(secs => @BaseSeconds * power(2, LEAST(recovery_failures, @MaxShift)))
                    WHERE session_id = @SessionId AND state IN ('Running', 'Settling')
                    RETURNING recovery_failures;
                    """, new { SessionId = sessionId, BaseSeconds = ServerPoolTimings.RECOVERY_BACKOFF_BASE_SECONDS,
                        MaxShift = ServerPoolTimings.RECOVERY_BACKOFF_MAX_SHIFT }, cancellationToken: cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && ((exception is NpgsqlException) || PostgresTransactionPolicy.IsTimeout(exception)))
            {
                return 0;
            }
        }

        private async Task<bool> IsServiceCancellableAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<bool>(new CommandDefinition(
                "SELECT (" + RUNNING_LOST_CONDITION + ") AND (" + POOL_ALIVE_CONDITION + ")"
                + " FROM game_sessions AS session WHERE session.session_id = @SessionId AND session.state = 'Running';",
                new { SessionId = sessionId, CancelTimeout = ServerPoolTimings.ServiceCancelTimeout,
                    HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout }, cancellationToken: cancellationToken));
        }

        private async Task<(GameSessionSnapshot? Snapshot, DateTimeOffset ServerTime)> ReadSnapshotAsync(Guid sessionId,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            GameSessionSnapshot? snapshot = await GameSessionSnapshot.ReadAsync(connection, null, sessionId,
                cancellationToken);
            DateTime serverTime = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", cancellationToken: cancellationToken));
            return (snapshot, new DateTimeOffset(DateTime.SpecifyKind(serverTime, DateTimeKind.Utc)));
        }

        // operations에 저장하지 않는 일시 거절. 같은 operationId로 나중에 다시 시도할 수 있다
        private static OperationResult Unstored(Guid operationId, int reasonCode, long revision, DateTimeOffset serverTime)
        {
            return new OperationResult(operationId.ToString("D"), ContractCodes.OperationStatus.REJECTED, reasonCode,
                revision, serverTime.ToUnixTimeMilliseconds(), string.Empty);
        }

        private async Task<OperationDecision> SettleEffectAsync(Guid callerInstanceId, Guid sessionId, Guid operationId,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 잠금 순서: 세션(조건부 갱신) -> 계정(오름차순) -> 캐릭터 -> 아이템 -> 인스턴스.
            // 보스 성공은 기본 보상(엽전)을 지급한 뒤 같은 던전이 파티 경매를 진행해야 하므로 닫지 않고 Auction으로 둔다.
            // 참가자 정산 완료, 접속 임대 해제, 서버 반환은 경매 finish(PostgresPartyAuctionRepository)가 한다
            ClosedRow? closed = await connection.QuerySingleOrDefaultAsync<ClosedRow>(new CommandDefinition(
                """
                UPDATE game_sessions
                SET state = CASE WHEN mode = 'Boss' AND end_reason = @Success THEN 'Auction' ELSE 'Closed' END,
                    result = CASE WHEN mode = 'Boss' AND end_reason = @Success THEN NULL ELSE 'Settled' END,
                    closed_at = CASE WHEN mode = 'Boss' AND end_reason = @Success THEN NULL ELSE clock_timestamp() END,
                    auction_ends_at = CASE WHEN mode = 'Boss' AND end_reason = @Success
                        THEN clock_timestamp() + @AuctionPhase ELSE NULL END,
                    revision = revision + 1
                WHERE session_id = @SessionId AND instance_id = @InstanceId AND state = 'Settling'
                    AND settlement_operation_id = @OperationId
                RETURNING ended_event_seq AS "EndedEventSeq", instance_boot_id AS "InstanceBootId", revision AS "Revision",
                    state = 'Auction' AS "StartsAuction", (end_reason = @Success) AS "Succeeded",
                    (mode = 'Boss') AS "IsBoss", battle_id AS "BattleId", boss_id AS "BossId", difficulty AS "Difficulty";
                """, new
                {
                    SessionId = sessionId, InstanceId = callerInstanceId, OperationId = operationId,
                    Success = GameSessionCodes.EndReason.SUCCESS, AuctionPhase = PartyAuctionRules.AuctionPhase
                }, transaction, cancellationToken: cancellationToken));
            if (closed == null)
            {
                return await RejectSettlementAsync(connection, transaction, callerInstanceId, sessionId, cancellationToken);
            }

            string payload = await connection.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT payload::text FROM session_events WHERE session_id = @SessionId AND event_seq = @EndedEventSeq;",
                new { SessionId = sessionId, closed.EndedEventSeq }, transaction, cancellationToken: cancellationToken));
            SessionEventPayloads.SessionEndedPayload plan = SessionEventPayloads.ReadEnded(payload);
            Dictionary<Guid, ParticipantRow> participants = (await PostgresGameSessionRepository.ReadParticipantsAsync(
                connection, transaction, sessionId, cancellationToken)).ToDictionary(row => row.ParticipantId);

            await SessionDb.LockAccountsAsync(connection, transaction,
                participants.Values.Select(row => row.AccountId), cancellationToken);
            List<EconomyLedgerLine> ledger = new List<EconomyLedgerLine>();
            LedgerSource source = new LedgerSource(sessionId, operationId, EconomyLedgerCodes.ACTOR_SERVER,
                callerInstanceId);

            // 계정 지갑(티켓, 엽전) -> 캐릭터(정기, 정련편) -> 계정 아이템(보호 전리품, 미등록 부적) 순서로 갱신한다.
            // 보관 상한(티켓, 정련편, 재료, 물약, 부적)을 넘긴 몫은 지급하지 않고 같은 트랜잭션에서 30일 회수함으로 옮긴다
            List<RecoveryLine> overflow = new List<RecoveryLine>();
            List<(ParticipantRow Participant, long Tickets, long Currency)> wallets = await CapWalletsAsync(connection,
                transaction, plan.Grants
                    .Where(grant => (grant.Tickets > 0) || (grant.Currency > 0))
                    .Select(grant => (participants[grant.ParticipantId], grant.Tickets, grant.Currency))
                    .ToList(), overflow, cancellationToken);
            await GrantWalletsAsync(connection, transaction, wallets, source, ledger, cancellationToken);

            List<(ParticipantRow Participant, long Essence, long Shards)> characters = await CapShardsAsync(connection,
                transaction, plan.Grants
                    .Where(grant => (grant.Essence > 0) || (grant.RefineShards > 0))
                    .Select(grant => (participants[grant.ParticipantId], grant.Essence, grant.RefineShards))
                    .ToList(), overflow, cancellationToken);
            await GrantCharacterAssetsAsync(connection, transaction, characters,
                EconomyLedgerCodes.REASON_SESSION_SETTLEMENT, source, ledger, cancellationToken);

            // 부적 줄의 DefinitionId는 효과 ID다. item_ 접두 아이템 ID와 겹치지 않아 같은 표에 효과 ID별로 쌓는다
            List<(ParticipantRow Participant, string Definition, string Binding, long Quantity)> items = await CapItemsAsync(
                connection, transaction, plan.Grants
                    .SelectMany(grant => grant.Items.Concat(grant.Talismans ?? new List<SessionEventPayloads.ItemPayload>())
                        .Select(item => (participants[grant.ParticipantId], item.DefinitionId, item.BindingKind,
                            item.Quantity)))
                    .ToList(), overflow, cancellationToken);
            await GrantItemsAsync(connection, transaction, items, source, ledger, cancellationToken);
            await RecoveryInbox.InsertAsync(connection, transaction, RecoveryInbox.SOURCE_SESSION_REWARD,
                sessionId.ToString("D"), overflow, cancellationToken);

            // 성공 정산만 계정 진행(지역 성공, 보스 첫 처치, 여우의 흔적)을 남긴다. 실패, 장애 취소는 기록하지 않고,
            // 포기나 접속 만료로 떠난 참가자(departed_at)는 성공에 참여하지 않았으므로 뺀다. 계정 행은 위에서 이미 잠갔고,
            // 같은 출정의 정산 효과는 settlement_operation_id 조건부 갱신으로 한 번만 실행되므로 진행도 한 번만 오른다
            if ((_progress != null) && closed.Succeeded)
            {
                Guid[] clearers = (await connection.QueryAsync<Guid>(new CommandDefinition(
                    """
                    SELECT account_id FROM session_participants
                    WHERE session_id = @SessionId AND state = 'Active' AND departed_at IS NULL
                    ORDER BY account_id;
                    """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken))).ToArray();
                await _progress.RecordSessionClearAsync(context, clearers, closed.IsBoss, closed.BattleId, closed.BossId,
                    closed.Difficulty, operationId, cancellationToken);
            }

            // 미사용 지참 물약은 예약만 풀어 반환한다. 총량은 그대로이므로 원장 기록이 없다
            await connection.ExecuteAsync(new CommandDefinition(
                """
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

                UPDATE reservations SET state = 'Settled', closed_at = clock_timestamp()
                WHERE session_id = @SessionId AND asset_kind = 'Item' AND state = 'Reserved';
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));

            // 경매 단계는 참가자와 접속 임대, 서버를 그대로 둔다. 던전 heartbeat가 임대를 계속 연장해 경매 중 마을 접속을 막는다
            if (!closed.StartsAuction)
            {
                await CloseParticipantsAsync(connection, transaction, callerInstanceId, sessionId,
                    participants.Values.Select(row => row.AccountId), closed.InstanceBootId, cancellationToken);
            }
            await _ledger.AppendAsync(context, ledger, cancellationToken);
            return OperationDecision.Committed(closed.Revision, Guid.NewGuid().ToString("D"));
        }

        /// <summary> 출정을 끝낸다: 참가자 정산 완료, 이 던전에 남은 접속 임대 해제, 서버 Draining. 정산과 경매 finish가 함께 쓴다 </summary>
        public static async Task CloseParticipantsAsync(DbConnection connection, DbTransaction transaction, Guid instanceId,
            Guid sessionId, IEnumerable<Guid> accountIds, Guid? instanceBootId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE session_participants SET state = 'Settled', settled_at = clock_timestamp()
                WHERE session_id = @SessionId AND state = 'Active';
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));

            // 출정이 끝났으므로 참가자가 이 던전에 남긴 접속 임대를 푼다. 던전 heartbeat가 계속 연장해 마을 복귀를 막지 않게 한다
            await PostgresAccountLeaseRepository.ReleaseForClosedSessionAsync(connection, transaction, instanceId,
                accountIds, cancellationToken);
            await PostgresServerPoolRepository.ReturnToPoolAsync(connection, transaction, instanceId, instanceBootId,
                cancellationToken);
        }

        private static async Task<OperationDecision> RejectSettlementAsync(DbConnection connection,
            DbTransaction transaction, Guid callerInstanceId, Guid sessionId, CancellationToken cancellationToken)
        {
            GameSessionSnapshot? current = await GameSessionSnapshot.ReadAsync(connection, transaction, sessionId,
                cancellationToken);
            if ((current == null) || (current.InstanceId != callerInstanceId))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            if ((current.State == GameSessionStates.CLOSED) || (current.State == GameSessionStates.AUCTION))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_SETTLED, current.Revision);
            }
            if (current.State == GameSessionStates.SETTLING)
            {
                // 종료 이벤트에 기록된 정산 operationId만 정산할 수 있다
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH, current.Revision);
            }

            return OperationDecision.Rejected(ContractCodes.RejectReason.WRONG_PHASE, current.Revision);
        }

        private async Task<OperationDecision> ServiceCancelEffectAsync(Guid instanceId, Guid sessionId, Guid operationId,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 배정 프로세스가 살아 있으면 취소하지 않는다. 개인 연결 종료나 포기는 장애 환급 사유가 아니다
            ClosedRow? closed = await connection.QuerySingleOrDefaultAsync<ClosedRow>(new CommandDefinition(
                """
                UPDATE game_sessions AS session
                SET state = 'Closed', result = 'ServiceCancelled', end_reason = @EndReason,
                    closed_at = clock_timestamp(), revision = session.revision + 1
                WHERE session.session_id = @SessionId AND session.instance_id = @InstanceId AND session.state = 'Running'
                    AND
                """ + " " + RUNNING_LOST_CONDITION + " AND " + POOL_ALIVE_CONDITION + """

                RETURNING session.instance_boot_id AS "InstanceBootId", session.revision AS "Revision";
                """, new
                {
                    SessionId = sessionId,
                    InstanceId = instanceId,
                    EndReason = GameSessionCodes.EndReason.SERVICE_CANCELLED,
                    CancelTimeout = ServerPoolTimings.ServiceCancelTimeout,
                    HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout
                }, transaction, cancellationToken: cancellationToken));
            if (closed == null)
            {
                GameSessionSnapshot? current = await GameSessionSnapshot.ReadAsync(connection, transaction, sessionId,
                    cancellationToken);
                if (current == null)
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }
                if ((current.State == GameSessionStates.CLOSED) || (current.State == GameSessionStates.AUCTION))
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_SETTLED, current.Revision);
                }

                // 사전 확인 뒤 heartbeat가 돌아왔거나 종료 이벤트가 저장됐다. 고정 operationId라 거절을 저장하면
                // 나중에 진짜 소실돼도 취소할 수 없으므로 저장하지 않고 다음 주기에 다시 본다
                throw TransientRejectionException.LivenessUnknown(ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS);
            }

            List<EconomyLedgerLine> ledger = new List<EconomyLedgerLine>();
            LedgerSource source = new LedgerSource(sessionId, operationId, EconomyLedgerCodes.ACTOR_SERVER, instanceId);
            List<ParticipantRow> participants = await CompensateCancelledSessionAsync(connection, transaction, sessionId,
                source, ledger, SERVICE_CANCEL_ESSENCE_PERCENT, cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE session_participants SET state = 'Settled', settled_at = clock_timestamp()
                WHERE session_id = @SessionId AND state = 'Active';
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));

            // 소실로 확정한 던전이 쥔 참가자의 접속 임대를 풀어 다시 로그인할 수 있게 한다(문서 10장). 계정 다음, 인스턴스 전이다
            await PostgresAccountLeaseRepository.ReleaseForClosedSessionAsync(connection, transaction, instanceId,
                participants.Select(row => row.AccountId), cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE server_instances SET state = 'Offline'
                WHERE instance_id = @InstanceId AND boot_id = @BootId AND state <> 'Offline';
                """, new { InstanceId = instanceId, BootId = closed.InstanceBootId },
                transaction, cancellationToken: cancellationToken));
            await _ledger.AppendAsync(context, ledger, cancellationToken);
            return OperationDecision.Committed(closed.Revision, Guid.NewGuid().ToString("D"));
        }

        /// <summary> 자동 복구가 실패 한도에 닿아 격리한 Settling 출정을 티켓, 물약 환급과 기본 정기 전액,
        /// 추가 정기 50% 내림(정책 미정이라 현행 유지)으로 닫는다. 호출자의 작업 트랜잭션에서 actorKind, actorId, operationId로 원장을 쓴다.
        /// 이미 다른 경로로 닫혔거나 격리되지 않은 세션이면 아무것도 바꾸지 않고 null이다 </summary>
        public async Task<long?> ForceCloseQuarantinedAsync(OperationExecutionContext context, Guid sessionId,
            string actorKind, Guid actorId, Guid operationId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(actorKind);
            if ((sessionId == Guid.Empty) || (actorId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Session, actor and operation IDs are required.");
            }

            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 늦게 온 /settlement와 같은 상태 조건부 갱신이라 세션 행 잠금 순서대로 한쪽만 이긴다. 정산이 먼저 이기면 여기서 null,
            // 여기가 먼저 이기면 정산은 ALREADY_SETTLED다. end_reason은 게임 서버가 기록한 종료 사유라 그대로 둔다
            ClosedRow? closed = await connection.QuerySingleOrDefaultAsync<ClosedRow>(new CommandDefinition(
                """
                UPDATE game_sessions AS session
                SET state = 'Closed', result = 'ServiceCancelled', closed_at = clock_timestamp(),
                    revision = session.revision + 1
                WHERE session.session_id = @SessionId AND session.state = 'Settling'
                    AND session.recovery_failures >= @MaxFailures
                RETURNING session.instance_id AS "InstanceId", session.instance_boot_id AS "InstanceBootId",
                    session.revision AS "Revision";
                """, new { SessionId = sessionId, MaxFailures = MAX_RECOVERY_FAILURES }, transaction,
                cancellationToken: cancellationToken));
            if (closed == null)
            {
                return null;
            }

            List<EconomyLedgerLine> ledger = new List<EconomyLedgerLine>();
            LedgerSource source = new LedgerSource(sessionId, operationId, actorKind, actorId);
            List<ParticipantRow> participants = await CompensateCancelledSessionAsync(connection, transaction, sessionId,
                source, ledger, FORCE_CLOSE_ESSENCE_PERCENT, cancellationToken);

            // 정산을 끝내지 못한 프로세스가 살아 있을 수 있어 Offline으로 덮지 않고 정산 종결과 같이 Draining으로 돌려보낸다
            await CloseParticipantsAsync(connection, transaction, closed.InstanceId, sessionId,
                participants.Select(row => row.AccountId), closed.InstanceBootId, cancellationToken);
            await _ledger.AppendAsync(context, ledger, cancellationToken);
            return closed.Revision;
        }

        // 장애 취소 지급: 참가 계정을 잠그고 티켓과 사용 물약을 환급한 뒤 정기를 지급한다. 참가자 상태와 임대는 호출자가 정리한다
        private static async Task<List<ParticipantRow>> CompensateCancelledSessionAsync(DbConnection connection,
            DbTransaction transaction, Guid sessionId, LedgerSource source, List<EconomyLedgerLine> ledger,
            long extraEssencePercent, CancellationToken cancellationToken)
        {
            List<ParticipantRow> participants = await PostgresGameSessionRepository.ReadParticipantsAsync(connection,
                transaction, sessionId, cancellationToken);
            await SessionDb.LockAccountsAsync(connection, transaction, participants.Select(row => row.AccountId),
                cancellationToken);

            await RefundTicketsAsync(connection, transaction, sessionId, source, ledger, cancellationToken);
            await RefundPotionsAsync(connection, transaction, sessionId, source, ledger, cancellationToken);

            // 기본과 추가는 같은 캐릭터 정기 자산이라 원장 한 줄(ServiceCancelEssence)로 합친다. 구성은 참가자 행의 두 적립 열로 재현한다
            List<(ParticipantRow Participant, long Essence, long Shards)> essence = participants
                .Where(row => row.State == GameSessionStates.PARTICIPANT_ACTIVE)
                .Select(row => (row, CalculateCancelledEssence(row.BaseEssenceAccrued, row.EssenceAccrued,
                    extraEssencePercent), 0L))
                .Where(entry => entry.Item2 > 0)
                .ToList();
            await GrantCharacterAssetsAsync(connection, transaction, essence,
                EconomyLedgerCodes.REASON_SERVICE_CANCEL_GRANT, source, ledger, cancellationToken);
            return participants;
        }

        private static async Task RefundTicketsAsync(DbConnection connection, DbTransaction transaction, Guid sessionId,
            LedgerSource source, List<EconomyLedgerLine> ledger,
            CancellationToken cancellationToken)
        {
            List<WalletRow> refunded = (await connection.QueryAsync<WalletRow>(new CommandDefinition(
                """
                WITH target AS (
                    SELECT wallet.account_id
                    FROM account_wallets AS wallet
                    JOIN reservations AS reservation ON reservation.account_id = wallet.account_id
                    WHERE reservation.session_id = @SessionId AND reservation.asset_kind = 'Ticket'
                        AND reservation.state = 'Consumed'
                    ORDER BY wallet.account_id
                    FOR UPDATE OF wallet
                )
                UPDATE account_wallets AS wallet SET tickets = wallet.tickets + @Refund, revision = wallet.revision + 1
                FROM target WHERE wallet.account_id = target.account_id
                RETURNING wallet.account_id AS "AccountId", wallet.tickets AS "Tickets";
                """, new { SessionId = sessionId, Refund = TICKET_REFUND }, transaction,
                cancellationToken: cancellationToken))).ToList();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE reservations SET state = 'Refunded', closed_at = clock_timestamp()
                WHERE session_id = @SessionId AND asset_kind = 'Ticket' AND state = 'Consumed';
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));
            ledger.AddRange(refunded.Select(row => source.Entry(row.AccountId, null,
                EconomyLedgerCodes.ASSET_TICKET, string.Empty, string.Empty, TICKET_REFUND, row.Tickets,
                EconomyLedgerCodes.REASON_SERVICE_CANCEL_REFUND)));
        }

        private static async Task RefundPotionsAsync(DbConnection connection, DbTransaction transaction, Guid sessionId,
            LedgerSource source, List<EconomyLedgerLine> ledger,
            CancellationToken cancellationToken)
        {
            // 실제 사용분은 총량을 되돌리고 미사용분은 예약만 푼다. 예약 행 상태가 Reserved일 때만 한 번 처리한다
            List<ItemRow> refunded = (await connection.QueryAsync<ItemRow>(new CommandDefinition(
                """
                WITH target AS (
                    SELECT item.account_id, item.definition_id, item.binding_kind, reservation.consumed,
                        reservation.quantity - reservation.consumed AS remaining
                    FROM account_items AS item
                    JOIN reservations AS reservation ON reservation.account_id = item.account_id
                        AND reservation.definition_id = item.definition_id AND reservation.binding_kind = item.binding_kind
                    WHERE reservation.session_id = @SessionId AND reservation.asset_kind = 'Item'
                        AND reservation.state = 'Reserved'
                    ORDER BY item.account_id, item.definition_id, item.binding_kind
                    FOR UPDATE OF item
                )
                UPDATE account_items AS item
                SET quantity = item.quantity + target.consumed, reserved = item.reserved - target.remaining
                FROM target
                WHERE item.account_id = target.account_id AND item.definition_id = target.definition_id
                    AND item.binding_kind = target.binding_kind
                RETURNING item.account_id AS "AccountId", item.definition_id AS "DefinitionId",
                    item.binding_kind AS "BindingKind", item.quantity AS "Quantity", target.consumed AS "Delta";
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken))).ToList();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE reservations SET state = 'Refunded', closed_at = clock_timestamp()
                WHERE session_id = @SessionId AND asset_kind = 'Item' AND state = 'Reserved';
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));
            ledger.AddRange(refunded.Where(row => row.Delta > 0).Select(row => source.Entry(row.AccountId, null,
                EconomyLedgerCodes.ASSET_ITEM, row.DefinitionId, row.BindingKind, row.Delta, row.Quantity,
                EconomyLedgerCodes.REASON_SERVICE_CANCEL_REFUND)));
        }

        /// <summary> 장애 취소 지급 정기. 기본 적립 + 추가 적립 전액이고 출정 정기 상한으로 자른다 </summary>
        public static long CalculateServiceCancelEssence(long baseAccrued, long extraAccrued)
        {
            return CalculateCancelledEssence(baseAccrued, extraAccrued, SERVICE_CANCEL_ESSENCE_PERCENT);
        }

        /// <summary> 강제 종료 지급 정기. 기본 적립 전액 + floor(추가 적립 x 50%)이고 출정 정기 상한으로 자른다 </summary>
        public static long CalculateForceCloseEssence(long baseAccrued, long extraAccrued)
        {
            return CalculateCancelledEssence(baseAccrued, extraAccrued, FORCE_CLOSE_ESSENCE_PERCENT);
        }

        private static long CalculateCancelledEssence(long baseAccrued, long extraAccrued, long extraEssencePercent)
        {
            // 두 적립은 각각 출정 상한 이하라 곱과 합이 long을 넘지 않는다
            long paid = Math.Max(0L, baseAccrued) + (Math.Max(0L, extraAccrued) * extraEssencePercent / PERCENT_BASE);
            return Math.Min(paid, SettlementSanityLimits.MAX_SESSION_ESSENCE);
        }

        // 티켓 보관 상한. 지갑 행을 계정 순서로 잠그고 읽어 남은 공간만 지급하고 넘친 몫은 overflow에 담는다. 엽전은 상한이 없다
        private static async Task<List<(ParticipantRow Participant, long Tickets, long Currency)>> CapWalletsAsync(
            DbConnection connection, DbTransaction transaction,
            List<(ParticipantRow Participant, long Tickets, long Currency)> grants, List<RecoveryLine> overflow,
            CancellationToken cancellationToken)
        {
            if (!grants.Any(grant => grant.Tickets > 0))
            {
                return grants;
            }

            Dictionary<Guid, long> current = (await connection.QueryAsync<(Guid AccountId, long Tickets)>(
                new CommandDefinition(
                    """
                    SELECT account_id, tickets FROM account_wallets WHERE account_id = ANY(@Accounts)
                    ORDER BY account_id FOR UPDATE;
                    """, new { Accounts = grants.Select(grant => grant.Participant.AccountId).ToArray() }, transaction,
                    cancellationToken: cancellationToken)))
                .ToDictionary(row => row.AccountId, row => row.Tickets);
            long? limit = RewardStorageLimits.LimitFor(EconomyAsset.TICKETS, null);
            List<(ParticipantRow Participant, long Tickets, long Currency)> capped =
                new List<(ParticipantRow, long, long)>(grants.Count);
            foreach ((ParticipantRow participant, long tickets, long currency) in grants)
            {
                long fits = RewardStorageLimits.Fits(limit, current.GetValueOrDefault(participant.AccountId), tickets);
                if (tickets > fits)
                {
                    overflow.Add(new RecoveryLine(participant.AccountId, null, EconomyAsset.TICKETS, null, null,
                        tickets - fits));
                }
                if ((fits > 0) || (currency > 0))
                {
                    capped.Add((participant, fits, currency));
                }
            }

            return capped;
        }

        // 정련편 보관 상한(캐릭터별). 캐릭터 행을 ID 순서로 잠그고 읽는다. 정기는 상한이 없다
        private static async Task<List<(ParticipantRow Participant, long Essence, long Shards)>> CapShardsAsync(
            DbConnection connection, DbTransaction transaction,
            List<(ParticipantRow Participant, long Essence, long Shards)> grants, List<RecoveryLine> overflow,
            CancellationToken cancellationToken)
        {
            if (!grants.Any(grant => grant.Shards > 0))
            {
                return grants;
            }

            Dictionary<Guid, long> current = (await connection.QueryAsync<(Guid CharacterId, long Shards)>(
                new CommandDefinition(
                    """
                    SELECT character_id, shards FROM characters WHERE character_id = ANY(@Characters)
                    ORDER BY character_id FOR UPDATE;
                    """, new { Characters = grants.Select(grant => grant.Participant.CharacterId).ToArray() },
                    transaction, cancellationToken: cancellationToken)))
                .ToDictionary(row => row.CharacterId, row => row.Shards);
            long? limit = RewardStorageLimits.LimitFor(EconomyAsset.SHARDS, null);
            List<(ParticipantRow Participant, long Essence, long Shards)> capped =
                new List<(ParticipantRow, long, long)>(grants.Count);
            foreach ((ParticipantRow participant, long essence, long shards) in grants)
            {
                long fits = RewardStorageLimits.Fits(limit, current.GetValueOrDefault(participant.CharacterId), shards);
                if (shards > fits)
                {
                    overflow.Add(new RecoveryLine(participant.AccountId, participant.CharacterId, EconomyAsset.SHARDS,
                        null, null, shards - fits));
                }
                if ((fits > 0) || (essence > 0))
                {
                    capped.Add((participant, essence, fits));
                }
            }

            return capped;
        }

        // 계정 아이템(재료, 물약, 미등록 부적) 보관 상한. 같은 키를 먼저 합치고 아이템 행을 키 순서로 잠근 뒤 읽는다
        private static async Task<List<(ParticipantRow Participant, string Definition, string Binding, long Quantity)>>
            CapItemsAsync(DbConnection connection, DbTransaction transaction,
                List<(ParticipantRow Participant, string Definition, string Binding, long Quantity)> items,
                List<RecoveryLine> overflow, CancellationToken cancellationToken)
        {
            List<(ParticipantRow Participant, string Definition, string Binding, long Quantity)> merged = items
                .GroupBy(item => (item.Participant.AccountId, item.Definition, item.Binding))
                .Select(group => (group.First().Participant, group.Key.Definition, group.Key.Binding,
                    group.Sum(item => item.Quantity)))
                .ToList();
            List<(ParticipantRow Participant, string Definition, string Binding, long Quantity)> limited = merged
                .Where(item => RewardStorageLimits.LimitFor(EconomyAsset.ACCOUNT_ITEM, item.Definition).HasValue)
                .ToList();
            if (limited.Count == 0)
            {
                return merged;
            }

            Dictionary<(Guid, string, string), long> current = (await connection.QueryAsync<ItemRow>(
                new CommandDefinition(
                    """
                    SELECT item.account_id AS "AccountId", item.definition_id AS "DefinitionId",
                        item.binding_kind AS "BindingKind", item.quantity AS "Quantity"
                    FROM account_items AS item
                    JOIN unnest(@Accounts::uuid[], @Definitions::text[], @Bindings::text[])
                        AS wanted(account_id, definition_id, binding_kind)
                        ON wanted.account_id = item.account_id AND wanted.definition_id = item.definition_id
                            AND wanted.binding_kind = item.binding_kind
                    ORDER BY item.account_id, item.definition_id, item.binding_kind
                    FOR UPDATE OF item;
                    """, new
                    {
                        Accounts = limited.Select(item => item.Participant.AccountId).ToArray(),
                        Definitions = limited.Select(item => item.Definition).ToArray(),
                        Bindings = limited.Select(item => item.Binding).ToArray()
                    }, transaction, cancellationToken: cancellationToken)))
                .ToDictionary(row => (row.AccountId, row.DefinitionId, row.BindingKind), row => row.Quantity);
            List<(ParticipantRow Participant, string Definition, string Binding, long Quantity)> capped =
                new List<(ParticipantRow, string, string, long)>(merged.Count);
            foreach ((ParticipantRow participant, string definition, string binding, long quantity) in merged)
            {
                long fits = RewardStorageLimits.Fits(RewardStorageLimits.LimitFor(EconomyAsset.ACCOUNT_ITEM, definition),
                    current.GetValueOrDefault((participant.AccountId, definition, binding)), quantity);
                if (quantity > fits)
                {
                    overflow.Add(new RecoveryLine(participant.AccountId, null, EconomyAsset.ACCOUNT_ITEM, definition,
                        binding, quantity - fits));
                }
                if (fits > 0)
                {
                    capped.Add((participant, definition, binding, fits));
                }
            }

            return capped;
        }

        private static async Task GrantWalletsAsync(DbConnection connection, DbTransaction transaction,
            IReadOnlyList<(ParticipantRow Participant, long Tickets, long Currency)> grants, LedgerSource source,
            List<EconomyLedgerLine> ledger, CancellationToken cancellationToken)
        {
            if (grants.Count == 0)
            {
                return;
            }

            // 출정의 참가자는 계정마다 하나(session_participants 유일 제약)라 한 INSERT에서 같은 지갑을 두 번 갱신하지 않는다.
            // 지갑 행이 없는 계정도 보상을 잃지 않게 만든다. 계정 행은 호출자가 이미 잠갔다
            List<WalletGrantRow> granted = (await connection.QueryAsync<WalletGrantRow>(new CommandDefinition(
                """
                INSERT INTO account_wallets AS wallet (account_id, tickets, currency)
                SELECT granted.account_id, granted.tickets, granted.currency
                FROM unnest(@Accounts::uuid[], @Tickets::bigint[], @Currency::bigint[])
                    AS granted(account_id, tickets, currency)
                ORDER BY granted.account_id
                ON CONFLICT (account_id) DO UPDATE
                    SET tickets = wallet.tickets + EXCLUDED.tickets, currency = wallet.currency + EXCLUDED.currency,
                        revision = wallet.revision + 1
                RETURNING wallet.account_id AS "AccountId", wallet.tickets AS "Tickets", wallet.currency AS "Currency";
                """, new
                {
                    Accounts = grants.Select(grant => grant.Participant.AccountId).ToArray(),
                    Tickets = grants.Select(grant => grant.Tickets).ToArray(),
                    Currency = grants.Select(grant => grant.Currency).ToArray()
                }, transaction, cancellationToken: cancellationToken))).ToList();
            if (granted.Count != grants.Count)
            {
                throw new InvalidOperationException("Settlement wallet target is missing.");
            }

            Dictionary<Guid, (long Tickets, long Currency)> requested = grants
                .ToDictionary(grant => grant.Participant.AccountId, grant => (grant.Tickets, grant.Currency));
            foreach (WalletGrantRow row in granted)
            {
                (long tickets, long currency) = requested[row.AccountId];
                if (tickets > 0)
                {
                    ledger.Add(source.Entry(row.AccountId, null, EconomyLedgerCodes.ASSET_TICKET, string.Empty,
                        string.Empty, tickets, row.Tickets, EconomyLedgerCodes.REASON_SESSION_SETTLEMENT));
                }
                if (currency > 0)
                {
                    ledger.Add(source.Entry(row.AccountId, null, EconomyLedgerCodes.ASSET_CURRENCY, string.Empty,
                        string.Empty, currency, row.Currency, EconomyLedgerCodes.REASON_SESSION_SETTLEMENT));
                }
            }
        }

        private static async Task GrantCharacterAssetsAsync(DbConnection connection, DbTransaction transaction,
            IReadOnlyList<(ParticipantRow Participant, long Essence, long Shards)> grants, string reason,
            LedgerSource source, List<EconomyLedgerLine> ledger, CancellationToken cancellationToken)
        {
            if (grants.Count == 0)
            {
                return;
            }

            List<CharacterGrantRow> granted = (await connection.QueryAsync<CharacterGrantRow>(new CommandDefinition(
                """
                WITH grant_row AS (
                    SELECT * FROM unnest(@Characters::uuid[], @Essence::bigint[], @Shards::bigint[])
                        AS granted(character_id, essence, shards)
                ), target AS (
                    SELECT hero.character_id
                    FROM characters AS hero JOIN grant_row ON grant_row.character_id = hero.character_id
                    ORDER BY hero.character_id
                    FOR UPDATE OF hero
                )
                UPDATE characters AS hero
                SET essence = hero.essence + grant_row.essence, shards = hero.shards + grant_row.shards,
                    revision = hero.revision + 1
                FROM grant_row, target
                WHERE hero.character_id = target.character_id AND grant_row.character_id = target.character_id
                RETURNING hero.account_id AS "AccountId", hero.character_id AS "CharacterId",
                    hero.essence AS "Essence", hero.shards AS "Shards", grant_row.essence AS "EssenceDelta",
                    grant_row.shards AS "ShardsDelta";
                """, new
                {
                    Characters = grants.Select(grant => grant.Participant.CharacterId).ToArray(),
                    Essence = grants.Select(grant => grant.Essence).ToArray(),
                    Shards = grants.Select(grant => grant.Shards).ToArray()
                }, transaction, cancellationToken: cancellationToken))).ToList();
            if (granted.Count != grants.Count)
            {
                throw new InvalidOperationException("Settlement target character is missing.");
            }

            foreach (CharacterGrantRow row in granted)
            {
                if (row.EssenceDelta > 0)
                {
                    ledger.Add(source.Entry(row.AccountId, row.CharacterId, EconomyLedgerCodes.ASSET_ESSENCE,
                        string.Empty, string.Empty, row.EssenceDelta, row.Essence, reason));
                }
                if (row.ShardsDelta > 0)
                {
                    ledger.Add(source.Entry(row.AccountId, row.CharacterId, EconomyLedgerCodes.ASSET_SHARDS,
                        string.Empty, string.Empty, row.ShardsDelta, row.Shards, reason));
                }
            }
        }

        private static async Task GrantItemsAsync(DbConnection connection, DbTransaction transaction,
            IReadOnlyList<(ParticipantRow Participant, string Definition, string Binding, long Quantity)> items,
            LedgerSource source, List<EconomyLedgerLine> ledger, CancellationToken cancellationToken)
        {
            // 같은 키를 한 INSERT에서 두 번 갱신할 수 없으므로 계정, 정의, 귀속별로 먼저 합친다
            List<(Guid Account, string Definition, string Binding, long Quantity)> merged = items
                .GroupBy(item => (item.Participant.AccountId, item.Definition, item.Binding))
                .Select(group => (group.Key.AccountId, group.Key.Definition, group.Key.Binding,
                    group.Sum(item => item.Quantity)))
                .ToList();
            if (merged.Count == 0)
            {
                return;
            }

            List<ItemRow> granted = (await connection.QueryAsync<ItemRow>(new CommandDefinition(
                """
                INSERT INTO account_items AS item (account_id, definition_id, binding_kind, quantity)
                SELECT granted.account_id, granted.definition_id, granted.binding_kind, granted.quantity
                FROM unnest(@Accounts::uuid[], @Definitions::text[], @Bindings::text[], @Quantities::bigint[])
                    AS granted(account_id, definition_id, binding_kind, quantity)
                ORDER BY granted.account_id, granted.definition_id, granted.binding_kind
                ON CONFLICT (account_id, definition_id, binding_kind)
                    DO UPDATE SET quantity = item.quantity + EXCLUDED.quantity
                RETURNING item.account_id AS "AccountId", item.definition_id AS "DefinitionId",
                    item.binding_kind AS "BindingKind", item.quantity AS "Quantity";
                """, new
                {
                    Accounts = merged.Select(item => item.Account).ToArray(),
                    Definitions = merged.Select(item => item.Definition).ToArray(),
                    Bindings = merged.Select(item => item.Binding).ToArray(),
                    Quantities = merged.Select(item => item.Quantity).ToArray()
                }, transaction, cancellationToken: cancellationToken))).ToList();

            Dictionary<(Guid, string, string), long> requested = merged
                .ToDictionary(item => (item.Account, item.Definition, item.Binding), item => item.Quantity);
            ledger.AddRange(granted.Select(row =>
            {
                long quantity = requested[(row.AccountId, row.DefinitionId, row.BindingKind)];
                return source.Entry(row.AccountId, null, EconomyLedgerCodes.ASSET_ITEM, row.DefinitionId,
                    row.BindingKind, quantity, row.Quantity, EconomyLedgerCodes.REASON_SESSION_SETTLEMENT);
            }));
        }

        private sealed class LedgerSource
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly Guid _sessionId;
            private readonly Guid _operationId;
            private readonly string _actorKind;
            private readonly Guid _actorId;

            /****************************************
            *                Methods
            ****************************************/

            public LedgerSource(Guid sessionId, Guid operationId, string actorKind, Guid actorId)
            {
                _sessionId   = sessionId;
                _operationId = operationId;
                _actorKind   = actorKind;
                _actorId     = actorId;
            }

            public EconomyLedgerLine Entry(Guid accountId, Guid? characterId, string assetKind, string assetId,
                string bindingKind, long delta, long balanceAfter, string reason)
            {
                return new EconomyLedgerLine(accountId, characterId, assetKind, assetId, bindingKind, delta, balanceAfter,
                    reason,
                    EconomyLedgerCodes.SOURCE_GAME_SESSION, _sessionId.ToString("D"), _operationId,
                    _actorKind, _actorId);
            }
        }

        private sealed class ClosedRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public long  EndedEventSeq  { get; set; }
            public Guid  InstanceId     { get; set; }
            public Guid? InstanceBootId { get; set; }
            public long  Revision       { get; set; }
            public bool  StartsAuction  { get; set; }

            // 출정 계약(0012). 0012 전 출정은 battle, boss, difficulty가 NULL이다
            public bool    Succeeded  { get; set; }
            public bool    IsBoss     { get; set; }
            public string? BattleId   { get; set; }
            public string? BossId     { get; set; }
            public short?  Difficulty { get; set; }
        }

        private sealed class LostSessionRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid   SessionId             { get; set; }
            public string State                 { get; set; } = string.Empty;
            public Guid   InstanceId            { get; set; }
            public Guid?  SettlementOperationId { get; set; }
        }

        private sealed class WalletRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AccountId { get; set; }
            public long Tickets   { get; set; }
        }

        private sealed class ItemRow
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

        private sealed class CharacterGrantRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AccountId    { get; set; }
            public Guid CharacterId  { get; set; }
            public long Essence      { get; set; }
            public long Shards       { get; set; }
            public long EssenceDelta { get; set; }
            public long ShardsDelta  { get; set; }
        }

        private sealed class WalletGrantRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AccountId { get; set; }
            public long Tickets   { get; set; }
            public long Currency  { get; set; }
        }
    }
}
