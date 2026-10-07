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
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;
using Shinshi.Data.Market;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Trade
{
    /// <summary> 1:1 거래 실행. prepare가 동결한 제안을 player_trade_executions에 남기고(경제 효과 없음), commit이 한 트랜잭션에서 양방향을
    /// 이동하며, resolve와 방치 정리가 결과 불명과 유실된 종결을 지연 보정한다. 멱등 actor는 호출한 마을 서버 인스턴스다.
    /// 잠금 순서: 실행 행 -> 계정(uuid 오름차순) -> 캐릭터 -> 지갑 -> account_items -> 장비(item_id) -> 원장.
    /// prepare는 실행 행을 잠그지 않고 계정부터 잠그므로 commit(실행 행 -> 계정)과 순서가 거꾸로 맞물리지 않는다 </summary>
    public sealed partial class PostgresTradeRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long NO_REVISION = 0;

        // operations.status
        private const string OPERATION_REJECTED = "Rejected";

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly IEconomyLedgerEntryWriter _ledger;
        private readonly PostgresItemHistoryWriter _history;
        private readonly GrowthContent _content;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresTradeRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            IEconomyLedgerEntryWriter ledger, PostgresItemHistoryWriter history, GrowthContent content)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(ledger);
            ArgumentNullException.ThrowIfNull(history);
            ArgumentNullException.ThrowIfNull(content);
            _dataSource = dataSource;
            _executor   = executor;
            _ledger     = ledger;
            _history    = history;
            _content    = content;
        }

        /// <summary> 제안을 동결해 Prepared 행을 남긴다. 어느 계정이든 이미 Prepared 실행이 있으면 TradeBusy다(그 행이 이미 종결됐는지는
        /// 먼저 지연 보정으로 확인한다). replayOnly면 효과 없이 저장된 결과만 찾는다 </summary>
        public async Task<TradeOutcome> PrepareAsync(Guid townInstanceId, TradePrepareInput input, bool replayOnly,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            OperationRequest request = new OperationRequest(townInstanceId, input.ExecutionId, TradeOperationKinds.PREPARE,
                SessionDb.ToRequestJson(new
                {
                    tradeId = input.TradeId.ToString("D"),
                    commitOperationId = input.CommitOperationId.ToString("D"),
                    sideA = ToRequestSide(input.AccountA, input.CharacterA, input.OfferA),
                    sideB = ToRequestSide(input.AccountB, input.CharacterB, input.OfferB)
                }));
            OperationResult? result;
            if (replayOnly)
            {
                result = await _executor.FindStoredAsync(request, cancellationToken);
            }
            else
            {
                await ReconcileStalePreparedAsync(new[] { input.AccountA, input.AccountB }, cancellationToken);
                result = await _executor.ExecuteAsync(request, (context, token) => PrepareEffectAsync(townInstanceId,
                    input, context, token), cancellationToken);
            }

            TradeExecutionRow? row = result == null ? null : await ReadRowAsync(input.ExecutionId, cancellationToken);
            return new TradeOutcome(ETradeLookup.Ok, result, row?.ToView());
        }

        /// <summary> 실행 행의 현재 상태. 없거나 다른 마을 인스턴스의 행이면 NotFound다 </summary>
        public async Task<TradeOutcome> FindAsync(Guid townInstanceId, Guid executionId,
            CancellationToken cancellationToken = default)
        {
            TradeExecutionRow? row = await ReadRowAsync(executionId, cancellationToken);
            return (row == null) || (row.TownInstanceId != townInstanceId)
                ? new TradeOutcome(ETradeLookup.NotFound, null, null)
                : new TradeOutcome(ETradeLookup.Ok, null, row.ToView());
        }

        /// <summary> 저장된 commit 결과로 실행 행을 종결하거나(확정 거절을 못 적은 경우), 만료됐으면 Abandoned로 끝내고, 아직 유효하면
        /// Prepared 그대로 돌려준다. 결과 불명일 때 commit 재전송 대신 쓴다. 반환하는 결과의 operationId는 commit 작업 ID다 </summary>
        public async Task<TradeOutcome> ResolveAsync(Guid townInstanceId, Guid executionId,
            CancellationToken cancellationToken = default)
        {
            TradeExecutionRow? row = await ResolveRowAsync(townInstanceId, executionId, false, cancellationToken);
            if (row == null)
            {
                return new TradeOutcome(ETradeLookup.NotFound, null, null);
            }

            return new TradeOutcome(ETradeLookup.Ok, ToResolveResult(row), row.ToView());
        }

        /// <summary> 만료된 Prepared 행을 resolve와 같은 규칙으로 종결한다. 다른 작업이 잡고 있는 행은 건너뛴다.
        /// 한 행의 실패가 나머지를 막지 않게 DB 연결 실패가 아닌 오류는 세고 계속한다 </summary>
        public async Task<int> AbandonExpiredAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            IReadOnlyList<Guid> due;
            await using (DbConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                due = (await connection.QueryAsync<Guid>(new CommandDefinition(
                    """
                    SELECT execution_id FROM player_trade_executions
                    WHERE state = 'Prepared' AND prepared_at <= clock_timestamp() - @Ttl
                    ORDER BY prepared_at, execution_id
                    LIMIT @Batch;
                    """, new { Ttl, Batch = batchSize }, cancellationToken: cancellationToken))).ToList();
            }

            int finished = 0;
            foreach (Guid executionId in due)
            {
                try
                {
                    TradeExecutionRow? row = await ResolveRowAsync(null, executionId, true, cancellationToken);
                    if ((row != null) && (row.State != TradeCodes.ExecutionState.PREPARED))
                    {
                        ++finished;
                    }
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                    && !PostgresTransactionPolicy.IsConnectionFailure(exception))
                {
                    // 다음 주기에 같은 행을 다시 처리한다
                }
            }

            return finished;
        }

        private static TimeSpan Ttl => TimeSpan.FromSeconds(TradeCodes.TRADE_PREPARE_TTL_SECONDS);

        private static object ToRequestSide(Guid accountId, Guid characterId, TradeSideOffer offer)
        {
            return new
            {
                accountId = accountId.ToString("D"),
                characterId = characterId.ToString("D"),
                currency = offer.Currency,
                items = (offer.Items ?? Array.Empty<TradeItemLine>()).Select(line => new
                {
                    assetKind = line?.AssetKind,
                    definitionId = line?.DefinitionId,
                    itemId = line?.ItemId,
                    quantity = line?.Quantity
                }).ToArray()
            };
        }

        // prepare 전에 두 계정의 Prepared 행을 지연 보정한다. 이미 끝난 실행(commit 거절을 적지 못한 것, 만료된 것)을 Busy로 세지 않는다
        private async Task ReconcileStalePreparedAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
        {
            IReadOnlyList<Guid> pending;
            await using (DbConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                pending = (await connection.QueryAsync<Guid>(new CommandDefinition(
                    """
                    SELECT execution_id FROM player_trade_executions
                    WHERE state = 'Prepared' AND (account_a = ANY(@Accounts) OR account_b = ANY(@Accounts));
                    """, new { Accounts = accountIds.ToArray() }, cancellationToken: cancellationToken))).ToList();
            }

            foreach (Guid executionId in pending)
            {
                await ResolveRowAsync(null, executionId, false, cancellationToken);
            }
        }

        // 행을 잠가 지연 보정하고 최신 행을 돌려준다. 행이 없거나 다른 마을의 행이거나(townInstanceId 지정 시)
        // skipLocked로 건너뛰었으면 null이다
        private Task<TradeExecutionRow?> ResolveRowAsync(Guid? townInstanceId, Guid executionId, bool skipLocked,
            CancellationToken cancellationToken)
        {
            return SessionDb.RetryAsync(token => ResolveOnceAsync(townInstanceId, executionId, skipLocked, token),
                cancellationToken);
        }

        private async Task<TradeExecutionRow?> ResolveOnceAsync(Guid? townInstanceId, Guid executionId, bool skipLocked,
            CancellationToken cancellationToken)
        {
            (NpgsqlConnection connection, NpgsqlTransaction transaction) = await SessionDb.BeginAsync(_dataSource,
                cancellationToken);
            await using (connection)
            await using (transaction)
            {
                TradeExecutionRow? row = await connection.QuerySingleOrDefaultAsync<TradeExecutionRow>(new CommandDefinition(
                    "SELECT " + TradeExecutionRow.COLUMNS + " FROM player_trade_executions WHERE execution_id = @ExecutionId"
                    + (skipLocked ? " FOR UPDATE SKIP LOCKED;" : " FOR UPDATE;"), new { ExecutionId = executionId },
                    transaction, cancellationToken: cancellationToken));
                if ((row == null) || (townInstanceId.HasValue && (row.TownInstanceId != townInstanceId.Value)))
                {
                    return null;
                }
                if (row.State != TradeCodes.ExecutionState.PREPARED)
                {
                    return row;
                }

                // commit 작업 결과가 있으면 그 결과가 진실이다. 거절을 저장했는데 실행 행 종결이 빠진 경우만 여기에 온다
                // (Committed는 commit 트랜잭션이 실행 행도 함께 바꾸므로 Prepared로 남지 않는다)
                OperationRow? operation = await connection.QuerySingleOrDefaultAsync<OperationRow>(new CommandDefinition(
                    """
                    SELECT status AS "Status", COALESCE((result_json ->> 'reasonCode')::integer, 0) AS "ReasonCode"
                    FROM operations WHERE actor_id = @TownInstanceId AND operation_id = @OperationId;
                    """, new { row.TownInstanceId, OperationId = row.CommitOperationId }, transaction,
                    cancellationToken: cancellationToken));
                int finishReason = ContractCodes.RejectReason.NONE;
                if ((operation != null) && (operation.Status == OPERATION_REJECTED) && (operation.ReasonCode > 0))
                {
                    finishReason = operation.ReasonCode;
                }
                else if (operation == null)
                {
                    bool expired = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                        "SELECT clock_timestamp() > prepared_at + @Ttl FROM player_trade_executions WHERE execution_id = @ExecutionId;",
                        new { ExecutionId = executionId, Ttl }, transaction, cancellationToken: cancellationToken));
                    if (expired)
                    {
                        finishReason = ContractCodes.RejectReason.TRADE_EXPIRED;
                    }
                }

                if (finishReason != ContractCodes.RejectReason.NONE)
                {
                    await FinishAsync(connection, transaction, executionId, finishReason, cancellationToken);
                }

                TradeExecutionRow fresh = await connection.QuerySingleAsync<TradeExecutionRow>(new CommandDefinition(
                    "SELECT " + TradeExecutionRow.COLUMNS + " FROM player_trade_executions WHERE execution_id = @ExecutionId;",
                    new { ExecutionId = executionId }, transaction, cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return fresh;
            }
        }

        // Prepared에서만 종결한다(조건부 갱신). 만료 사유는 Abandoned, 나머지 확정 거절은 Rejected다
        private static async Task<bool> FinishAsync(DbConnection connection, DbTransaction? transaction, Guid executionId,
            int reasonCode, CancellationToken cancellationToken)
        {
            int updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE player_trade_executions
                SET state = @State, reason_code = @Reason, finished_at = clock_timestamp(), revision = revision + 1
                WHERE execution_id = @ExecutionId AND state = 'Prepared';
                """, new
                {
                    ExecutionId = executionId,
                    Reason = reasonCode,
                    State = reasonCode == ContractCodes.RejectReason.TRADE_EXPIRED
                        ? TradeCodes.ExecutionState.ABANDONED
                        : TradeCodes.ExecutionState.REJECTED
                }, transaction, cancellationToken: cancellationToken));
            return updated == 1;
        }

        private async Task<TradeExecutionRow?> ReadRowAsync(Guid executionId, CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<TradeExecutionRow>(new CommandDefinition(
                "SELECT " + TradeExecutionRow.COLUMNS + " FROM player_trade_executions WHERE execution_id = @ExecutionId;",
                new { ExecutionId = executionId }, cancellationToken: cancellationToken));
        }

        // resolve 응답의 작업 결과. 실행 행의 상태를 작업 상태로 옮긴다(Prepared는 확정이 아니라 Pending)
        private static OperationResult ToResolveResult(TradeExecutionRow row)
        {
            string operationId = row.CommitOperationId.ToString("D");
            switch (row.State)
            {
                case TradeCodes.ExecutionState.COMMITTED:
                    return new OperationResult(operationId, ContractCodes.OperationStatus.COMMITTED,
                        ContractCodes.RejectReason.NONE, row.Revision, row.ServerTimeUnixMs, row.ExecutionId.ToString("D"));
                case TradeCodes.ExecutionState.PREPARED:
                    return new OperationResult(operationId, ContractCodes.OperationStatus.PENDING,
                        ContractCodes.RejectReason.NONE, row.Revision, row.ServerTimeUnixMs, string.Empty);
                default:
                    return new OperationResult(operationId, ContractCodes.OperationStatus.REJECTED, row.ReasonCode,
                        row.Revision, row.ServerTimeUnixMs, string.Empty);
            }
        }

        private async Task<OperationDecision> PrepareEffectAsync(Guid townInstanceId, TradePrepareInput input,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            if (input.TownInstanceId != townInstanceId)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.TRADE_NOT_IN_TOWN);
            }
            if (input.AccountA == input.AccountB)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.TRADE_SAME_ACCOUNT);
            }

            int format = TradeOfferRules.Validate(input.OfferA, input.OfferB);
            if (format != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(format);
            }

            // 계정 잠금이 prepare끼리를 직렬화한다. 두 열(account_a, account_b)을 모두 보고 부분 유일 인덱스는 보조 방어선이다
            Guid[] accounts = { input.AccountA, input.AccountB };
            if (await SessionDb.LockAccountsAsync(connection, transaction, accounts, cancellationToken) != accounts.Length)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }

            int access = await CheckPartiesAsync(connection, transaction, townInstanceId, input.AccountA, input.CharacterA,
                input.AccountB, input.CharacterB, cancellationToken);
            if (access != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(access);
            }

            bool busy = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM player_trade_executions
                    WHERE state = 'Prepared' AND (account_a = ANY(@Accounts) OR account_b = ANY(@Accounts)));
                """, new { Accounts = accounts }, transaction, cancellationToken: cancellationToken));
            if (busy)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.TRADE_BUSY);
            }

            // 장비 사전 검사는 잠그지 않은 읽기다. 확정 판정은 commit이 잠근 행으로 다시 한다
            int equipment = await PrecheckEquipmentAsync(connection, input, cancellationToken);
            if (equipment != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(equipment);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO player_trade_executions (execution_id, trade_id, commit_operation_id, town_instance_id,
                    account_a, character_a, account_b, character_b, offer_a, offer_b, state, prepared_at)
                VALUES (@ExecutionId, @TradeId, @CommitOperationId, @TownInstanceId, @AccountA, @CharacterA, @AccountB,
                    @CharacterB, CAST(@OfferA AS jsonb), CAST(@OfferB AS jsonb), 'Prepared', clock_timestamp());
                """, new
                {
                    input.ExecutionId,
                    input.TradeId,
                    input.CommitOperationId,
                    TownInstanceId = townInstanceId,
                    input.AccountA,
                    input.CharacterA,
                    input.AccountB,
                    input.CharacterB,
                    OfferA = StoredOffer.From(input.OfferA).ToJson(),
                    OfferB = StoredOffer.From(input.OfferB).ToJson()
                }, transaction, cancellationToken: cancellationToken));
            return OperationDecision.Committed(NO_REVISION, input.ExecutionId.ToString("D"));
        }

        // 두 캐릭터가 각자 계정의 살아 있는 캐릭터이고 두 계정 모두 호출 마을에 접속 임대가 있는지 본다
        private static async Task<int> CheckPartiesAsync(DbConnection connection, DbTransaction transaction,
            Guid townInstanceId, Guid accountA, Guid characterA, Guid accountB, Guid characterB,
            CancellationToken cancellationToken)
        {
            // 캐릭터는 character_id 순서로 잠근다
            List<Guid> characters = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT character_id FROM characters
                WHERE deleted_at IS NULL AND ((character_id = @CharacterA AND account_id = @AccountA)
                    OR (character_id = @CharacterB AND account_id = @AccountB))
                ORDER BY character_id FOR UPDATE;
                """, new { CharacterA = characterA, AccountA = accountA, CharacterB = characterB, AccountB = accountB },
                transaction, cancellationToken: cancellationToken))).ToList();
            if (characters.Count != 2)
            {
                return ContractCodes.RejectReason.INVALID_TARGET;
            }

            int leases = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT count(*)::integer FROM account_leases
                WHERE account_id = ANY(@Accounts) AND instance_id = @Town AND role = @TownRole
                    AND expires_at > clock_timestamp();
                """, new { Accounts = new[] { accountA, accountB }, Town = townInstanceId,
                    TownRole = ContractCodes.ServerRole.TOWN }, transaction, cancellationToken: cancellationToken));
            return leases == 2 ? ContractCodes.RejectReason.NONE : ContractCodes.RejectReason.TRADE_NOT_IN_TOWN;
        }

        private static async Task<int> PrecheckEquipmentAsync(DbConnection connection, TradePrepareInput input,
            CancellationToken cancellationToken)
        {
            List<(TradeSideOffer Offer, Guid Account, Guid Character)> sides = new List<(TradeSideOffer, Guid, Guid)>
            {
                (input.OfferA, input.AccountA, input.CharacterA),
                (input.OfferB, input.AccountB, input.CharacterB)
            };
            List<Guid> ids = sides.SelectMany(side => EquipmentLines(side.Offer)).Select(line => Guid.Parse(line.ItemId)).ToList();
            if (ids.Count == 0)
            {
                return ContractCodes.RejectReason.NONE;
            }

            Dictionary<Guid, EquipmentRecord> items = await EquipmentRows.ReadAsync(connection, ids, cancellationToken);
            foreach ((TradeSideOffer offer, Guid account, Guid character) in sides)
            {
                foreach (TradeItemLine line in EquipmentLines(offer))
                {
                    int reason = CheckEquipment(items, Guid.Parse(line.ItemId), line.DefinitionId, account, character);
                    if (reason != ContractCodes.RejectReason.NONE)
                    {
                        return reason;
                    }
                }
            }

            return ContractCodes.RejectReason.NONE;
        }

        private static IEnumerable<TradeItemLine> EquipmentLines(TradeSideOffer offer)
        {
            return (offer.Items ?? Array.Empty<TradeItemLine>())
                .Where(line => line.AssetKind == MarketCodes.ASSET_EQUIPMENT);
        }

        // 소유 계정, 캐릭터가 다르거나 없으면 제안이 바뀐 것이고, 거래 가능 조건(보관 중 포함)을 어기면 거래 불가다
        private static int CheckEquipment(IReadOnlyDictionary<Guid, EquipmentRecord> items, Guid itemId,
            string definitionId, Guid accountId, Guid characterId)
        {
            if (!items.TryGetValue(itemId, out EquipmentRecord? item) || (item.AccountId != accountId)
                || (item.CharacterId != characterId) || !string.Equals(item.DefinitionId, definitionId, StringComparison.Ordinal))
            {
                return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
            }

            return MarketTradeRules.IsTradableEquipment(item.Grade, item.Enhancement, item.Equipped, item.Bound,
                item.Locked, item.IsTraining, item.Origin, item.EscrowKind != null)
                ? ContractCodes.RejectReason.NONE
                : ContractCodes.RejectReason.TRADE_NOT_TRADABLE;
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class OperationRow
        {
            public string Status     { get; set; } = string.Empty;
            public int    ReasonCode { get; set; }
        }
    }
}
