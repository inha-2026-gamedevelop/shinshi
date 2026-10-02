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
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Data.Auctions
{
    // 보스 성공 정산 뒤 Auction 상태인 출정의 파티 경매. 마감 순간의 판정(연장, 접속 여부)은 던전 서버가 하고, API는 경제 조건과
    // 소유 관계만 검증한다. 모든 경로의 잠금 순서는 세션 행 -> 경매 행 -> 계정(uuid 오름차순) -> 지갑, 아이템 -> 원장 -> 인스턴스다.
    //
    // 마감은 두 작업이다. close가 종료 시각과 접속 집합을 먼저 저장(Closing)하고, settle이 그 저장값만 써서 낙찰과 분배를 끝낸다.
    // settle의 operationId는 품목 ID에서 만든 고정값이라 던전 재시도와 복구 작업이 같은 결과를 받는다.
    // 종료 기록 없이 버려진 Open 품목은 복구가 예약금을 풀고 Cancelled로 끝낸다. 확인할 수 없는 분배금을 만들지 않는다
    /// <summary> 파티 경매 개시, 입찰, 마감, 경매 단계 종료와 복구 </summary>
    public sealed class PostgresPartyAuctionRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OPEN_KIND   = "party-auction.open";
        public const string BID_KIND    = "party-auction.bid";
        public const string CLOSE_KIND  = "party-auction.close";
        public const string SETTLE_KIND = "party-auction.settle";
        public const string CANCEL_KIND = "party-auction.cancel";
        public const string FINISH_KIND = "party-auction.finish";

        private const string SETTLE_NAME = "shinshi:party-auction-settle:";
        private const string CANCEL_NAME = "shinshi:party-auction-cancel:";
        private const string FINISH_NAME = "shinshi:party-auction-finish:";

        private const long ITEM_QUANTITY = 1;

        // 배정 boot가 장애 취소 임계 동안 살아 있다는 증거가 없고, 다른 살아 있는 서버는 있다(API, DB 단절과 구분).
        // 정산 저장소의 서비스 장애 취소와 같은 조건이다
        private const string LOST_CONDITION =
            """
            NOT EXISTS (SELECT 1 FROM server_instances AS instance
                WHERE instance.instance_id = session.instance_id AND instance.boot_id = session.instance_boot_id
                    AND instance.state <> 'Offline'
                    AND instance.heartbeat_at > clock_timestamp() - @CancelTimeout)
            AND EXISTS (SELECT 1 FROM server_instances AS alive
                WHERE alive.state <> 'Offline' AND alive.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
            """;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly IEconomyLedgerWriter _ledger;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresPartyAuctionRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            IEconomyLedgerWriter ledger)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(ledger);
            _dataSource = dataSource;
            _executor   = executor;
            _ledger     = ledger;
        }

        /// <summary> 품목 하나를 연다. 자격 집합을 동결하고 자격자 수로 단독/경쟁 모드를 정한다 </summary>
        public Task<OperationResult> OpenAsync(Guid instanceId, Guid? bootId, AuctionOpenInput input,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            Guid[] eligible = input.EligibleParticipantIds.ToArray();
            OperationRequest request = new OperationRequest(instanceId, input.OperationId, OPEN_KIND,
                SessionDb.ToRequestJson(new
                {
                    sessionId = input.SessionId.ToString("D"),
                    auctionId = input.AuctionId.ToString("D"),
                    definitionId = input.DefinitionId,
                    eligible = SortedIds(eligible)
                }));
            return _executor.ExecuteAsync(request, (context, token) =>
                OpenEffectAsync(instanceId, bootId, input, eligible, context, token), cancellationToken);
        }

        /// <summary> 입찰 한 건. 행위자는 참가자의 계정이며 승인되면 이전 최고가 예약을 풀고 새 금액을 예약한다 </summary>
        public async Task<OperationResult> BidAsync(Guid instanceId, Guid? bootId, AuctionBidInput input,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            // 행위자(참가자 계정)의 멱등 기록을 조회, 저장하기 전에 호출 던전이 이 출정의 배정 boot인지 본다. 다른 던전이 같은 ID로
            // 결과를 재생하거나 거절을 먼저 저장해 정상 요청을 막지 못하게 한다. 효과 안에서 같은 검사를 잠금과 함께 다시 한다
            BidOwnerRow? owner;
            DateTimeOffset serverTime;
            await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                owner = await connection.QuerySingleOrDefaultAsync<BidOwnerRow>(new CommandDefinition(
                    """
                    SELECT session.instance_id AS "InstanceId", session.instance_boot_id AS "InstanceBootId",
                        participant.account_id AS "AccountId"
                    FROM game_sessions AS session
                    LEFT JOIN session_participants AS participant ON participant.session_id = session.session_id
                        AND participant.participant_id = @ParticipantId
                    WHERE session.session_id = @SessionId;
                    """, new { input.SessionId, input.ParticipantId }, cancellationToken: cancellationToken));
                serverTime = await ReadTimeAsync(connection, cancellationToken);
            }
            if ((owner == null) || (owner.InstanceId != instanceId)
                || (owner.InstanceBootId.HasValue && (owner.InstanceBootId != bootId)))
            {
                return Unstored(input.OperationId, ContractCodes.RejectReason.SESSION_MISMATCH, serverTime);
            }
            Guid? accountId = owner.AccountId;
            if (!accountId.HasValue)
            {
                return Unstored(input.OperationId, ContractCodes.RejectReason.NOT_ELIGIBLE, serverTime);
            }

            Guid account = accountId.Value;
            OperationRequest request = new OperationRequest(account, input.OperationId, BID_KIND,
                SessionDb.ToRequestJson(new
                {
                    sessionId = input.SessionId.ToString("D"),
                    auctionId = input.AuctionId.ToString("D"),
                    participantId = input.ParticipantId.ToString("D"),
                    amount = input.Amount,
                    expectedBidSeq = input.ExpectedBidSeq,
                    acceptedAtUnixMs = input.AcceptedAtUnixMs
                }));
            return await _executor.ExecuteAsync(request, (context, token) =>
                BidEffectAsync(instanceId, bootId, account, input, context, token), cancellationToken);
        }

        /// <summary> 마감. 종료 시각과 접속 집합을 저장한 뒤 그 값으로 정산한다. 정산이 실패해도 종료 기록은 남아 재시도와 복구가 잇는다 </summary>
        public async Task<OperationResult> CloseAsync(Guid instanceId, Guid? bootId, AuctionCloseInput input,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            Guid[] connected = input.ConnectedParticipantIds.ToArray();
            OperationRequest request = new OperationRequest(instanceId, input.OperationId, CLOSE_KIND,
                SessionDb.ToRequestJson(new
                {
                    sessionId = input.SessionId.ToString("D"),
                    auctionId = input.AuctionId.ToString("D"),
                    connected = SortedIds(connected),
                    endedAtUnixMs = input.EndedAtUnixMs
                }));
            OperationResult closed = await _executor.ExecuteAsync(request, (context, token) =>
                CloseEffectAsync(instanceId, bootId, input, connected, context, token), cancellationToken);
            if (closed.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return closed;
            }

            // 응답은 요청 operationId로 돌려준다. 같은 close 재시도도 저장된 Closing 뒤 같은 정산 결과를 받아 응답이 같다
            OperationResult settled = await SettleAsync(instanceId, input.AuctionId, cancellationToken);
            return new OperationResult(input.OperationId.ToString("D"), settled.Status, settled.ReasonCode,
                settled.CommittedRevision, settled.ServerTimeUnixMs, settled.ReceiptId);
        }

        /// <summary> 경매 단계를 끝내고 출정을 닫는다. 열린 품목이나 정산 중인 품목이 있으면 WRONG_PHASE다 </summary>
        public Task<OperationResult> FinishAsync(Guid instanceId, Guid? bootId, Guid sessionId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            OperationRequest request = new OperationRequest(instanceId, operationId, FINISH_KIND,
                SessionDb.ToRequestJson(new { sessionId = sessionId.ToString("D") }));
            return _executor.ExecuteAsync(request, (context, token) =>
                FinishEffectAsync(instanceId, bootId, true, sessionId, context, token), cancellationToken);
        }

        /// <summary> 던전이 사라졌거나 경매 단계 기한을 넘긴 출정을 끝낸다. 종료 기록이 있는 품목은 그 값으로 정산하고
        /// 없는 품목은 예약금을 풀어 취소한 뒤 출정을 닫는다. API 시작, DB 장애 직후에는 호출자가 부르지 않는다 </summary>
        public async Task<SessionRecoveryResult> RecoverAsync(int maxSessions, CancellationToken cancellationToken = default)
        {
            if (maxSessions <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSessions));
            }

            List<RecoveryRow> rows;
            await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                rows = (await connection.QueryAsync<RecoveryRow>(new CommandDefinition(
                    $"""
                    SELECT session.session_id AS "SessionId", session.instance_id AS "InstanceId"
                    FROM game_sessions AS session
                    WHERE session.state = 'Auction'
                        AND (session.auction_ends_at + @Grace < clock_timestamp() OR ({LOST_CONDITION}))
                    ORDER BY session.auction_ends_at, session.session_id
                    LIMIT @Limit;
                    """, new
                    {
                        Grace = PartyAuctionRules.PhaseRecoveryGrace,
                        CancelTimeout = ServerPoolTimings.ServiceCancelTimeout,
                        HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout,
                        Limit = maxSessions
                    }, cancellationToken: cancellationToken))).ToList();
            }

            int finished = 0;
            int cancelled = 0;
            List<SessionRecoveryFailure> failures = new List<SessionRecoveryFailure>();
            foreach (RecoveryRow row in rows)
            {
                try
                {
                    cancelled += await RecoverAuctionsAsync(row, cancellationToken);
                    OperationRequest request = new OperationRequest(row.InstanceId,
                        SessionDb.CreateNameBasedGuid(FINISH_NAME + row.SessionId.ToString("D")), FINISH_KIND,
                        SessionDb.ToRequestJson(new { sessionId = row.SessionId.ToString("D") }));
                    OperationResult result = await _executor.ExecuteAsync(request, (context, token) =>
                        FinishEffectAsync(row.InstanceId, null, false, row.SessionId, context, token), cancellationToken);
                    finished += (result.Status == ContractCodes.OperationStatus.COMMITTED) ? 1 : 0;
                }
                catch (Exception exception) when (IsTransient(exception))
                {
                    failures.Add(new SessionRecoveryFailure(row.SessionId, "Auction:" + exception.GetType().Name, 0,
                        false, true));
                }
            }

            return new SessionRecoveryResult(finished, cancelled, failures);
        }

        private async Task<int> RecoverAuctionsAsync(RecoveryRow row, CancellationToken cancellationToken)
        {
            List<ActiveRow> active;
            await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                active = (await connection.QueryAsync<ActiveRow>(new CommandDefinition(
                    """
                    SELECT auction_id AS "AuctionId", state AS "State" FROM auctions
                    WHERE session_id = @SessionId AND state IN ('Open', 'Closing') ORDER BY item_seq;
                    """, new { row.SessionId }, cancellationToken: cancellationToken))).ToList();
            }

            int cancelled = 0;
            foreach (ActiveRow auction in active)
            {
                if (auction.State == PartyAuctionRules.STATE_OPEN)
                {
                    Guid cancelId = SessionDb.CreateNameBasedGuid(CANCEL_NAME + auction.AuctionId.ToString("D"));
                    OperationRequest request = new OperationRequest(row.InstanceId, cancelId, CANCEL_KIND,
                        SessionDb.ToRequestJson(new { auctionId = auction.AuctionId.ToString("D") }));
                    OperationResult result = await _executor.ExecuteAsync(request, (context, token) =>
                        CancelEffectAsync(auction.AuctionId, cancelId, context, token), cancellationToken);
                    if (result.Status == ContractCodes.OperationStatus.COMMITTED)
                    {
                        ++cancelled;
                        continue;
                    }
                }

                // 취소 직전에 던전이 마감을 저장했으면 종료 기록이 있으므로 그 값으로 정산한다
                await SettleAsync(row.InstanceId, auction.AuctionId, cancellationToken);
            }

            return cancelled;
        }

        private Task<OperationResult> SettleAsync(Guid instanceId, Guid auctionId, CancellationToken cancellationToken)
        {
            Guid settleId = SessionDb.CreateNameBasedGuid(SETTLE_NAME + auctionId.ToString("D"));
            OperationRequest request = new OperationRequest(instanceId, settleId, SETTLE_KIND,
                SessionDb.ToRequestJson(new { auctionId = auctionId.ToString("D") }));
            return _executor.ExecuteAsync(request, (context, token) =>
                SettleEffectAsync(auctionId, instanceId, settleId, context, token), cancellationToken);
        }

        private static async Task<OperationDecision> OpenEffectAsync(Guid instanceId, Guid? bootId, AuctionOpenInput input,
            Guid[] eligible, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            SessionGuard guard = await LockSessionAsync(connection, transaction, input.SessionId, cancellationToken);
            int reject = guard.Check(instanceId, bootId, ContractCodes.RejectReason.WRONG_PHASE);
            if (reject != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(reject, guard.Revision);
            }
            if (!guard.InWindow)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CLOSED, guard.Revision);
            }

            // 다른 출정이나 다른 개시 작업이 같은 품목 ID를 쓰면 같은 작업의 재시도가 아니다
            bool taken = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM auctions WHERE auction_id = @AuctionId);",
                new { input.AuctionId }, transaction, cancellationToken: cancellationToken));
            if (taken)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH, guard.Revision);
            }

            ItemCountRow counts = await connection.QuerySingleAsync<ItemCountRow>(new CommandDefinition(
                """
                SELECT count(*) AS "Items", count(*) FILTER (WHERE state IN ('Open', 'Closing')) AS "Active"
                FROM auctions WHERE session_id = @SessionId;
                """, new { input.SessionId }, transaction, cancellationToken: cancellationToken));
            if ((counts.Items >= PartyAuctionRules.MAX_ITEMS_PER_SESSION) || (counts.Active > 0))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.WRONG_PHASE, guard.Revision);
            }

            // 자격자는 이 출정의 원래 참가자이며 경매 단계에서 아직 Active인 사람이다
            long members = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                SELECT count(*) FROM session_participants
                WHERE session_id = @SessionId AND participant_id = ANY(@Ids) AND state = 'Active';
                """, new { input.SessionId, Ids = eligible }, transaction, cancellationToken: cancellationToken));
            if (members != eligible.Length)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.NOT_ELIGIBLE, guard.Revision);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO auctions (auction_id, session_id, item_seq, definition_id, binding_kind, mode, state,
                    eligible_participant_ids, open_operation_id, hard_ends_at)
                VALUES (@AuctionId, @SessionId, @ItemSeq, @DefinitionId, @Binding, @Mode, 'Open', @Eligible,
                    @OperationId, clock_timestamp() + @HardLimit);
                """, new
                {
                    input.AuctionId, input.SessionId, ItemSeq = counts.Items + 1, input.DefinitionId,
                    Binding = PartyAuctionRules.BINDING_KIND, Mode = PartyAuctionRules.ModeFor(eligible.Length),
                    Eligible = eligible, input.OperationId, HardLimit = PartyAuctionRules.ItemHardLimit
                }, transaction, cancellationToken: cancellationToken));
            return OperationDecision.Committed(0, Guid.NewGuid().ToString("D"));
        }

        private static async Task<OperationDecision> BidEffectAsync(Guid instanceId, Guid? bootId, Guid accountId,
            AuctionBidInput input, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            SessionGuard guard = await LockSessionAsync(connection, transaction, input.SessionId, cancellationToken);
            int reject = guard.Check(instanceId, bootId, ContractCodes.RejectReason.CLOSED);
            if (reject != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(reject, guard.Revision);
            }

            AuctionRow? auction = await LockAuctionAsync(connection, transaction, input.SessionId, input.AuctionId,
                cancellationToken);
            if (auction == null)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            // 상한 판정은 DB 시계와 던전 접수 시각 둘 다 본다. 연장 여부는 던전이 이미 판정했다
            DateTime acceptedAt = DateTimeOffset.FromUnixTimeMilliseconds(input.AcceptedAtUnixMs).UtcDateTime;
            DateTime limit = auction.HardEndsAt.Add(PartyAuctionRules.ClockSkew);
            if ((auction.State != PartyAuctionRules.STATE_OPEN) || (auction.Now > limit) || (acceptedAt > limit))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CLOSED, auction.BidSeq);
            }
            if (!auction.Eligible.Contains(input.ParticipantId))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.NOT_ELIGIBLE, auction.BidSeq);
            }
            if (input.ExpectedBidSeq != auction.BidSeq)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.STALE_REVISION, auction.BidSeq);
            }
            if (!PartyAuctionRules.IsAcceptableBid(auction.Mode, auction.HighBid, input.Amount))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.BID_TOO_LOW, auction.BidSeq);
            }

            // 이전 최고 입찰자와 새 입찰자의 계정을 순서대로 잠그고 가용 엽전을 다시 본다
            Guid[] accounts = auction.HighAccountId.HasValue
                ? new[] { accountId, auction.HighAccountId.Value } : new[] { accountId };
            await SessionDb.LockAccountsAsync(connection, transaction, accounts, cancellationToken);
            WalletRow? wallet = await connection.QuerySingleOrDefaultAsync<WalletRow>(new CommandDefinition(
                """
                SELECT currency AS "Currency", currency_reserved AS "Reserved" FROM account_wallets
                WHERE account_id = @AccountId FOR UPDATE;
                """, new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            long ownHeld = (auction.HighAccountId == accountId) ? auction.HighBid!.Value : 0;
            long available = (wallet == null) ? 0 : wallet.Currency - wallet.Reserved + ownHeld;
            if (available < input.Amount)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_CURRENCY, auction.BidSeq);
            }

            // 같은 계정이 최고가를 올리면 두 문장이 같은 행에 순변화를 만든다
            if (auction.HighAccountId.HasValue)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE account_wallets SET currency_reserved = currency_reserved - @Held, revision = revision + 1
                    WHERE account_id = @AccountId;
                    """, new { Held = auction.HighBid!.Value, AccountId = auction.HighAccountId.Value }, transaction,
                    cancellationToken: cancellationToken));
            }
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE account_wallets SET currency_reserved = currency_reserved + @Amount, revision = revision + 1
                WHERE account_id = @AccountId;
                """, new { input.Amount, AccountId = accountId }, transaction, cancellationToken: cancellationToken));

            long bidSeq = auction.BidSeq + 1;
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE auctions SET bid_seq = @BidSeq, high_bid = @Amount, high_participant_id = @ParticipantId,
                    high_account_id = @AccountId, revision = revision + 1
                WHERE auction_id = @AuctionId;

                INSERT INTO auction_bids (auction_id, bid_seq, participant_id, account_id, amount, operation_id, accepted_at)
                VALUES (@AuctionId, @BidSeq, @ParticipantId, @AccountId, @Amount, @OperationId, @AcceptedAt);
                """, new
                {
                    BidSeq = bidSeq, input.Amount, input.ParticipantId, AccountId = accountId, input.AuctionId,
                    input.OperationId, AcceptedAt = acceptedAt
                }, transaction, cancellationToken: cancellationToken));
            return OperationDecision.Committed(bidSeq, Guid.NewGuid().ToString("D"));
        }

        private static async Task<OperationDecision> CloseEffectAsync(Guid instanceId, Guid? bootId, AuctionCloseInput input,
            Guid[] connected, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            SessionGuard guard = await LockSessionAsync(connection, transaction, input.SessionId, cancellationToken);
            int reject = guard.Check(instanceId, bootId, ContractCodes.RejectReason.CLOSED);
            if (reject != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(reject, guard.Revision);
            }

            AuctionRow? auction = await LockAuctionAsync(connection, transaction, input.SessionId, input.AuctionId,
                cancellationToken);
            if (auction == null)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            if (auction.State != PartyAuctionRules.STATE_OPEN)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CLOSED, auction.Revision);
            }

            // 접속 집합에는 중간 복귀자처럼 자격 밖 참가자도 들어올 수 있다. 분배는 자격과의 교집합만 쓴다
            long members = await connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT count(*) FROM session_participants WHERE session_id = @SessionId AND participant_id = ANY(@Ids);",
                new { input.SessionId, Ids = connected }, transaction, cancellationToken: cancellationToken));
            if (members != connected.Length)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH, auction.Revision);
            }

            // 던전이 상한을 넘겨 늦게 마감해도 종료 시각은 상한으로 기록한다. 늦은 마감을 거절하면 품목을 닫을 수 없다
            DateTime endedAt = DateTimeOffset.FromUnixTimeMilliseconds(input.EndedAtUnixMs).UtcDateTime;
            long revision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                UPDATE auctions SET state = 'Closing', connected_participant_ids = @Connected,
                    ended_at = LEAST(@EndedAt, hard_ends_at), close_operation_id = @OperationId, revision = revision + 1
                WHERE auction_id = @AuctionId AND state = 'Open'
                RETURNING revision;
                """, new { Connected = connected, EndedAt = endedAt, input.OperationId, input.AuctionId }, transaction,
                cancellationToken: cancellationToken));
            return OperationDecision.Committed(revision, Guid.NewGuid().ToString("D"));
        }

        private async Task<OperationDecision> SettleEffectAsync(Guid auctionId, Guid actorId, Guid operationId,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            Guid? sessionId = await ReadAuctionSessionAsync(connection, transaction, auctionId, cancellationToken);
            if (!sessionId.HasValue)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            // finish는 Closing 품목이 있으면 출정을 닫지 않으므로 여기서는 항상 Auction이다. 세션 행은 잠금 순서만 맞춘다
            await LockSessionAsync(connection, transaction, sessionId.Value, cancellationToken);
            AuctionRow? auction = await LockAuctionAsync(connection, transaction, sessionId.Value, auctionId,
                cancellationToken);
            if ((auction == null) || (auction.State != PartyAuctionRules.STATE_CLOSING))
            {
                // 고정 operationId라 저장되는 거절이다. 호출자는 Closing 품목에만 정산을 부른다
                return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_SETTLED, auction?.Revision ?? 0);
            }

            if (!auction.HighBid.HasValue)
            {
                long unsold = await connection.QuerySingleAsync<long>(new CommandDefinition(
                    """
                    UPDATE auctions SET state = 'Unsold', closed_at = clock_timestamp(), revision = revision + 1
                    WHERE auction_id = @AuctionId RETURNING revision;
                    """, new { AuctionId = auctionId }, transaction, cancellationToken: cancellationToken));
                return OperationDecision.Committed(unsold, Guid.NewGuid().ToString("D"));
            }

            // 분배 대상 = 품목 시작 자격 ∩ 종료 순간 접속 - 낙찰자. 저장된 집합만 쓰고 다시 계산하지 않는다
            Guid winnerParticipant = auction.HighParticipantId!.Value;
            Guid winner = auction.HighAccountId!.Value;
            long price = auction.HighBid.Value;
            HashSet<Guid> connected = new HashSet<Guid>(auction.Connected ?? Array.Empty<Guid>());
            Guid[] recipientParticipants = auction.Eligible
                .Where(id => connected.Contains(id) && (id != winnerParticipant)).ToArray();
            List<Guid> recipients = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT account_id FROM session_participants
                WHERE session_id = @SessionId AND participant_id = ANY(@Ids);
                """, new { SessionId = sessionId.Value, Ids = recipientParticipants }, transaction,
                cancellationToken: cancellationToken))).ToList();
            (long each, long burned) = PartyAuctionRules.Split(price, recipients.Count);

            await SessionDb.LockAccountsAsync(connection, transaction, recipients.Append(winner), cancellationToken);
            List<EconomyLedgerLine> ledger = new List<EconomyLedgerLine>();

            // 낙찰자는 예약금을 그대로 소비한다. 예약이 없으면 불변식이 깨진 것이라 전체를 되돌린다
            long? winnerBalance = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                UPDATE account_wallets SET currency = currency - @Price, currency_reserved = currency_reserved - @Price,
                    revision = revision + 1
                WHERE account_id = @AccountId AND currency_reserved >= @Price
                RETURNING currency;
                """, new { Price = price, AccountId = winner }, transaction, cancellationToken: cancellationToken));
            if (!winnerBalance.HasValue)
            {
                throw new InvalidOperationException("Auction winner reservation is missing.");
            }
            ledger.Add(Entry(winner, EconomyLedgerCodes.ASSET_CURRENCY, string.Empty, string.Empty, -price,
                winnerBalance.Value, EconomyLedgerCodes.REASON_AUCTION_SETTLE, auctionId, operationId, actorId));

            long quantity = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT INTO account_items AS item (account_id, definition_id, binding_kind, quantity)
                VALUES (@AccountId, @DefinitionId, @Binding, @Quantity)
                ON CONFLICT (account_id, definition_id, binding_kind) DO UPDATE SET quantity = item.quantity + EXCLUDED.quantity
                RETURNING item.quantity;
                """, new
                {
                    AccountId = winner, auction.DefinitionId, Binding = auction.BindingKind, Quantity = ITEM_QUANTITY
                }, transaction, cancellationToken: cancellationToken));
            ledger.Add(Entry(winner, EconomyLedgerCodes.ASSET_ITEM, auction.DefinitionId, auction.BindingKind,
                ITEM_QUANTITY, quantity, EconomyLedgerCodes.REASON_AUCTION_SETTLE, auctionId, operationId, actorId));

            if ((recipients.Count > 0) && (each > 0))
            {
                IEnumerable<PaidRow> paid = await connection.QueryAsync<PaidRow>(new CommandDefinition(
                    """
                    INSERT INTO account_wallets AS wallet (account_id, currency)
                    SELECT paid.account_id, @Each FROM unnest(@Accounts::uuid[]) AS paid(account_id)
                    ORDER BY paid.account_id
                    ON CONFLICT (account_id) DO UPDATE
                        SET currency = wallet.currency + EXCLUDED.currency, revision = wallet.revision + 1
                    RETURNING wallet.account_id AS "AccountId", wallet.currency AS "Currency";
                    """, new { Each = each, Accounts = recipients.ToArray() }, transaction,
                    cancellationToken: cancellationToken));
                ledger.AddRange(paid.Select(row => Entry(row.AccountId, EconomyLedgerCodes.ASSET_CURRENCY, string.Empty,
                    string.Empty, each, row.Currency, EconomyLedgerCodes.REASON_AUCTION_PAYOUT, auctionId, operationId,
                    actorId)));
            }

            long revision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                UPDATE auctions SET state = 'Sold', payout_each = @Each, payout_count = @Count, burned = @Burned,
                    closed_at = clock_timestamp(), revision = revision + 1
                WHERE auction_id = @AuctionId RETURNING revision;
                """, new { Each = each, Count = recipients.Count, Burned = burned, AuctionId = auctionId }, transaction,
                cancellationToken: cancellationToken));
            await _ledger.AppendAsync(context, ledger, cancellationToken);
            return OperationDecision.Committed(revision, Guid.NewGuid().ToString("D"));
        }

        // 종료 기록이 없는 품목. 최고가 예약만 풀고 재료와 분배금은 만들지 않는다
        private static async Task<OperationDecision> CancelEffectAsync(Guid auctionId, Guid operationId,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            Guid? sessionId = await ReadAuctionSessionAsync(connection, transaction, auctionId, cancellationToken);
            if (!sessionId.HasValue)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            await LockSessionAsync(connection, transaction, sessionId.Value, cancellationToken);
            await RequireRecoverableAsync(connection, transaction, sessionId.Value, cancellationToken);
            AuctionRow? auction = await LockAuctionAsync(connection, transaction, sessionId.Value, auctionId,
                cancellationToken);
            if ((auction == null) || (auction.State != PartyAuctionRules.STATE_OPEN))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CLOSED, auction?.Revision ?? 0);
            }

            if (auction.HighAccountId.HasValue)
            {
                await SessionDb.LockAccountsAsync(connection, transaction, new[] { auction.HighAccountId.Value },
                    cancellationToken);
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE account_wallets SET currency_reserved = currency_reserved - @Held, revision = revision + 1
                    WHERE account_id = @AccountId;
                    """, new { Held = auction.HighBid!.Value, AccountId = auction.HighAccountId.Value }, transaction,
                    cancellationToken: cancellationToken));
            }

            // 종료 이벤트가 없었다는 사실은 connected_participant_ids가 비어 있는 것으로 남는다
            long revision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                UPDATE auctions SET state = 'Cancelled', ended_at = clock_timestamp(), close_operation_id = @OperationId,
                    closed_at = clock_timestamp(), revision = revision + 1
                WHERE auction_id = @AuctionId RETURNING revision;
                """, new { OperationId = operationId, AuctionId = auctionId }, transaction,
                cancellationToken: cancellationToken));
            return OperationDecision.Committed(revision, Guid.NewGuid().ToString("D"));
        }

        private static async Task<OperationDecision> FinishEffectAsync(Guid instanceId, Guid? bootId, bool fromDungeon,
            Guid sessionId, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 세션 행을 먼저 별도 문장으로 독점 잠근다. 개시는 세션을 공유 잠금한 채 품목을 넣으므로 이 잠금은 개시 커밋 뒤에 잡히고,
            // 다음 문장의 새 snapshot이 그 품목을 본다. 한 문장의 UPDATE ... NOT EXISTS로 하면 미커밋 품목을 놓치고 출정을 닫는다
            SessionGuard guard = await LockSessionForUpdateAsync(connection, transaction, sessionId, cancellationToken);
            if ((!guard.Exists) || (guard.InstanceId != instanceId))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            if (guard.State == GameSessionStates.CLOSED)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_SETTLED, guard.Revision);
            }
            if (fromDungeon && guard.InstanceBootId.HasValue && (guard.InstanceBootId != bootId))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH, guard.Revision);
            }
            if (guard.State != GameSessionStates.AUCTION)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.WRONG_PHASE, guard.Revision);
            }
            if (!fromDungeon)
            {
                await RequireRecoverableAsync(connection, transaction, sessionId, cancellationToken);
            }

            // 열린 품목이나 정산 중인 품목이 있으면 아직 닫을 수 없다. 품목이 끝나면 같은 operationId로 다시 시도해야 하므로
            // 거절을 저장하지 않는다(복구의 고정 operationId가 이 거절을 영구 재생하지 않게 한다)
            bool active = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM auctions WHERE session_id = @SessionId AND state IN ('Open', 'Closing'));",
                new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));
            if (active)
            {
                throw new TransientRejectionException(ContractCodes.RejectReason.WRONG_PHASE,
                    ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS);
            }

            long revision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                UPDATE game_sessions
                SET state = 'Closed', result = 'Settled', closed_at = clock_timestamp(), revision = revision + 1
                WHERE session_id = @SessionId
                RETURNING revision;
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));

            List<Guid> accounts = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT account_id FROM session_participants WHERE session_id = @SessionId;", new { SessionId = sessionId },
                transaction, cancellationToken: cancellationToken))).ToList();
            await SessionDb.LockAccountsAsync(connection, transaction, accounts, cancellationToken);
            await PostgresSettlementRepository.CloseParticipantsAsync(connection, transaction, instanceId, sessionId,
                accounts, guard.InstanceBootId, cancellationToken);
            return OperationDecision.Committed(revision, Guid.NewGuid().ToString("D"));
        }

        // 복구 후보 조건(경매 단계 기한 초과 또는 배정 boot 소실)을 효과 안에서 다시 본다. 후보를 고른 뒤 같은 boot가 돌아왔으면
        // 저장하지 않고 다음 주기로 미룬다. 고정 operationId라 거절을 저장하면 나중에 진짜 소실돼도 처리할 수 없다
        private static async Task RequireRecoverableAsync(DbConnection connection, DbTransaction transaction, Guid sessionId,
            CancellationToken cancellationToken)
        {
            bool recoverable = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                $"""
                SELECT (session.auction_ends_at + @Grace < clock_timestamp()) OR ({LOST_CONDITION})
                FROM game_sessions AS session WHERE session.session_id = @SessionId;
                """, new
                {
                    SessionId = sessionId, Grace = PartyAuctionRules.PhaseRecoveryGrace,
                    CancelTimeout = ServerPoolTimings.ServiceCancelTimeout,
                    HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout
                }, transaction, cancellationToken: cancellationToken));
            if (!recoverable)
            {
                throw TransientRejectionException.LivenessUnknown(ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS);
            }
        }

        private static async Task<SessionGuard> LockSessionForUpdateAsync(DbConnection connection, DbTransaction transaction,
            Guid sessionId, CancellationToken cancellationToken)
        {
            SessionGuard? guard = await connection.QuerySingleOrDefaultAsync<SessionGuard>(new CommandDefinition(
                """
                SELECT TRUE AS "Exists", state AS "State", instance_id AS "InstanceId", instance_boot_id AS "InstanceBootId",
                    FALSE AS "InWindow", revision AS "Revision"
                FROM game_sessions WHERE session_id = @SessionId FOR UPDATE;
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));
            return guard ?? new SessionGuard();
        }

        private static async Task<SessionGuard> LockSessionAsync(DbConnection connection, DbTransaction transaction,
            Guid sessionId, CancellationToken cancellationToken)
        {
            SessionGuard? guard = await connection.QuerySingleOrDefaultAsync<SessionGuard>(new CommandDefinition(
                """
                SELECT TRUE AS "Exists", state AS "State", instance_id AS "InstanceId", instance_boot_id AS "InstanceBootId",
                    (auction_ends_at IS NOT NULL AND auction_ends_at - @HardLimit > clock_timestamp()) AS "InWindow",
                    revision AS "Revision"
                FROM game_sessions WHERE session_id = @SessionId FOR SHARE;
                """, new { SessionId = sessionId, HardLimit = PartyAuctionRules.ItemHardLimit }, transaction,
                cancellationToken: cancellationToken));
            return guard ?? new SessionGuard();
        }

        private static Task<AuctionRow?> LockAuctionAsync(DbConnection connection, DbTransaction transaction, Guid sessionId,
            Guid auctionId, CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<AuctionRow>(new CommandDefinition(
                """
                SELECT state AS "State", mode AS "Mode", definition_id AS "DefinitionId", binding_kind AS "BindingKind",
                    eligible_participant_ids AS "Eligible", connected_participant_ids AS "Connected", bid_seq AS "BidSeq",
                    high_bid AS "HighBid", high_participant_id AS "HighParticipantId", high_account_id AS "HighAccountId",
                    hard_ends_at AS "HardEndsAt", clock_timestamp() AS "Now", revision AS "Revision"
                FROM auctions WHERE auction_id = @AuctionId AND session_id = @SessionId FOR UPDATE;
                """, new { AuctionId = auctionId, SessionId = sessionId }, transaction,
                cancellationToken: cancellationToken));
        }

        private static Task<Guid?> ReadAuctionSessionAsync(DbConnection connection, DbTransaction transaction, Guid auctionId,
            CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT session_id FROM auctions WHERE auction_id = @AuctionId;", new { AuctionId = auctionId },
                transaction, cancellationToken: cancellationToken));
        }

        private static EconomyLedgerLine Entry(Guid accountId, string assetKind, string assetId, string bindingKind,
            long delta, long balanceAfter, string reason, Guid auctionId, Guid operationId, Guid actorId)
        {
            return new EconomyLedgerLine(accountId, null, assetKind, assetId, bindingKind, delta, balanceAfter, reason,
                EconomyLedgerCodes.SOURCE_PARTY_AUCTION, auctionId.ToString("D"), operationId,
                EconomyLedgerCodes.ACTOR_SERVER, actorId);
        }

        private static string[] SortedIds(IEnumerable<Guid> ids)
        {
            return ids.Select(id => id.ToString("D")).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        private static async Task<DateTimeOffset> ReadTimeAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            DateTime serverTime = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", cancellationToken: cancellationToken));
            return new DateTimeOffset(DateTime.SpecifyKind(serverTime, DateTimeKind.Utc));
        }

        // operations에 저장하지 않는 거절. 행위자를 정할 수 없을 때만 쓴다
        private static OperationResult Unstored(Guid operationId, int reasonCode, DateTimeOffset serverTime)
        {
            return new OperationResult(operationId.ToString("D"), ContractCodes.OperationStatus.REJECTED, reasonCode, 0,
                serverTime.ToUnixTimeMilliseconds(), string.Empty);
        }

        // DB 연결 실패는 후보별 실패로 삼키지 않는다. 유지 서비스가 받아 생존 판정 복구를 보류해야 한다
        private static bool IsTransient(Exception exception)
        {
            return (exception is TransientRejectionException) || PostgresTransactionPolicy.IsRetryable(exception);
        }

        /****************************************
        *               Rows
        ****************************************/

        private sealed class SessionGuard
        {
            public bool   Exists         { get; set; }
            public string State          { get; set; } = string.Empty;
            public Guid   InstanceId     { get; set; }
            public Guid?  InstanceBootId { get; set; }
            public bool   InWindow       { get; set; }
            public long   Revision       { get; set; }

            /// <summary> 배정 던전의 같은 boot가 Auction 단계에서 부른 요청인가. 아니면 거절 사유 </summary>
            public int Check(Guid instanceId, Guid? bootId, int wrongPhaseReason)
            {
                if ((!Exists) || (InstanceId != instanceId))
                {
                    return ContractCodes.RejectReason.SESSION_MISMATCH;
                }
                if (InstanceBootId.HasValue && (InstanceBootId != bootId))
                {
                    return ContractCodes.RejectReason.SESSION_MISMATCH;
                }

                return (State == GameSessionStates.AUCTION) ? ContractCodes.RejectReason.NONE : wrongPhaseReason;
            }
        }

        private sealed class AuctionRow
        {
            public string   State             { get; set; } = string.Empty;
            public string   Mode              { get; set; } = string.Empty;
            public string   DefinitionId      { get; set; } = string.Empty;
            public string   BindingKind       { get; set; } = string.Empty;
            public Guid[]   Eligible          { get; set; } = Array.Empty<Guid>();
            public Guid[]?  Connected         { get; set; }
            public long     BidSeq            { get; set; }
            public long?    HighBid           { get; set; }
            public Guid?    HighParticipantId { get; set; }
            public Guid?    HighAccountId     { get; set; }
            public DateTime HardEndsAt        { get; set; }
            public DateTime Now               { get; set; }
            public long     Revision          { get; set; }
        }

        private sealed class BidOwnerRow
        {
            public Guid  InstanceId     { get; set; }
            public Guid? InstanceBootId { get; set; }
            public Guid? AccountId      { get; set; }
        }

        private sealed class ItemCountRow
        {
            public long Items  { get; set; }
            public long Active { get; set; }
        }

        private sealed class WalletRow
        {
            public long Currency { get; set; }
            public long Reserved { get; set; }
        }

        private sealed class PaidRow
        {
            public Guid AccountId { get; set; }
            public long Currency  { get; set; }
        }

        private sealed class FinishRow
        {
            public Guid? InstanceBootId { get; set; }
            public long  Revision       { get; set; }
        }

        private sealed class ActiveRow
        {
            public Guid   AuctionId { get; set; }
            public string State     { get; set; } = string.Empty;
        }

        private sealed class RecoveryRow
        {
            public Guid SessionId  { get; set; }
            public Guid InstanceId { get; set; }
        }
    }
}
