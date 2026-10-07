// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;
using Shinshi.Data.Market;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Trade
{
    public sealed partial class PostgresTradeRepository
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 동결된 제안대로 양방향을 한 트랜잭션에서 이동한다. operationId는 prepare가 저장한 commitOperationId여야 한다.
        /// 검사 하나라도 실패하면 실행기가 효과를 모두 되돌리고 거절만 저장하며, 실행 행의 종결(Rejected, Abandoned)은 그 뒤 별도 짧은
        /// 트랜잭션이 적는다. 그 갱신이 빠져도 resolve, prepare, 방치 정리가 저장된 commit 결과로 같은 종결을 만든다.
        /// replayOnly면 효과 없이 저장된 결과만 찾는다(기능 차단 중 재전송) </summary>
        public async Task<TradeOutcome> CommitAsync(Guid townInstanceId, Guid executionId, Guid operationId,
            bool replayOnly, CancellationToken cancellationToken = default)
        {
            TradeExecutionRow? row = await ReadRowAsync(executionId, cancellationToken);
            if ((row == null) || (row.TownInstanceId != townInstanceId))
            {
                return new TradeOutcome(ETradeLookup.NotFound, null, null);
            }
            if (row.CommitOperationId != operationId)
            {
                return new TradeOutcome(ETradeLookup.OperationMismatch, null, row.ToView());
            }

            OperationRequest request = new OperationRequest(townInstanceId, operationId, TradeOperationKinds.COMMIT,
                SessionDb.ToRequestJson(new { executionId = executionId.ToString("D") }));
            OperationResult? stored = replayOnly ? await _executor.FindStoredAsync(request, cancellationToken) : null;
            if (replayOnly && (stored == null))
            {
                return new TradeOutcome(ETradeLookup.Ok, null, row.ToView());
            }

            OperationResult result = stored ?? await _executor.ExecuteAsync(request, (context, token) => CommitEffectAsync(
                townInstanceId, executionId, operationId, context, token), cancellationToken);

            // 재전송에서도 같은 종결을 시도한다. Prepared가 아니면 아무것도 바꾸지 않는다
            if ((result.Status == ContractCodes.OperationStatus.REJECTED)
                && (result.ReasonCode != ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT))
            {
                await using DbConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                await FinishAsync(connection, null, executionId, result.ReasonCode, cancellationToken);
            }

            TradeExecutionRow? latest = await ReadRowAsync(executionId, cancellationToken);
            return new TradeOutcome(ETradeLookup.Ok, result, latest?.ToView());
        }

        private async Task<OperationDecision> CommitEffectAsync(Guid townInstanceId, Guid executionId, Guid operationId,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 첫 잠금은 실행 행이다. resolve, 방치 정리와 같은 행에서 줄을 서므로 둘 중 하나만 Prepared를 종결한다
            TradeExecutionRow? row = await connection.QuerySingleOrDefaultAsync<TradeExecutionRow>(new CommandDefinition(
                "SELECT " + TradeExecutionRow.COLUMNS + " FROM player_trade_executions WHERE execution_id = @ExecutionId FOR UPDATE;",
                new { ExecutionId = executionId }, transaction, cancellationToken: cancellationToken));
            if ((row == null) || (row.CommitOperationId != operationId))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (row.TownInstanceId != townInstanceId)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.TRADE_NOT_IN_TOWN);
            }
            if (row.State != TradeCodes.ExecutionState.PREPARED)
            {
                // 이미 종결된 실행(방치 정리가 먼저 잠근 경우)이다. 그 종결과 같은 사유로 이 작업의 결과를 확정한다
                return OperationDecision.Rejected(row.State == TradeCodes.ExecutionState.ABANDONED
                    ? ContractCodes.RejectReason.TRADE_EXPIRED
                    : (row.ReasonCode > 0 ? row.ReasonCode : ContractCodes.RejectReason.TRADE_ITEM_CHANGED), row.Revision);
            }

            // 만료 판정은 잠금을 얻은 뒤 별도 문장의 DB 시각으로 한다
            bool expired = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT clock_timestamp() > prepared_at + @Ttl FROM player_trade_executions WHERE execution_id = @ExecutionId;",
                new { ExecutionId = executionId, Ttl }, transaction, cancellationToken: cancellationToken));
            if (expired)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.TRADE_EXPIRED, row.Revision);
            }

            Guid[] accounts = { row.AccountA, row.AccountB };
            if (await SessionDb.LockAccountsAsync(connection, transaction, accounts, cancellationToken) != accounts.Length)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVALID_TARGET, row.Revision);
            }

            int access = await CheckPartiesAsync(connection, transaction, townInstanceId, row.AccountA, row.CharacterA,
                row.AccountB, row.CharacterB, cancellationToken);
            if (access != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(access, row.Revision);
            }

            TradePlan plan = new TradePlan(row, StoredOffer.Parse(row.OfferA), StoredOffer.Parse(row.OfferB));
            int reason = await ApplyAsync(context, connection, transaction, plan, townInstanceId, operationId,
                cancellationToken);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(reason, row.Revision);
            }

            long? revision = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                UPDATE player_trade_executions
                SET state = 'Committed', finished_at = clock_timestamp(), revision = revision + 1
                WHERE execution_id = @ExecutionId AND state = 'Prepared'
                RETURNING revision;
                """, new { ExecutionId = executionId }, transaction, cancellationToken: cancellationToken));
            return OperationDecision.Committed(revision ?? throw new InvalidOperationException(
                "The trade execution was not prepared when committing."), executionId.ToString("D"));
        }

        // 지갑 -> account_items -> 장비 순서로 잠그고 검증한 뒤 이동한다. 이동은 차감을 모두 끝낸 다음 지급해
        // 같은 자산을 주고받는 쪽의 보관 상한이 순증감으로 판정되게 한다
        private async Task<int> ApplyAsync(OperationExecutionContext context, DbConnection connection,
            DbTransaction transaction, TradePlan plan, Guid townInstanceId, Guid operationId,
            CancellationToken cancellationToken)
        {
            Guid[] accounts = { plan.Row.AccountA, plan.Row.AccountB };
            Dictionary<Guid, WalletRow> wallets = (await connection.QueryAsync<WalletRow>(new CommandDefinition(
                """
                SELECT account_id AS "AccountId", currency AS "Currency", currency_reserved AS "Reserved"
                FROM account_wallets WHERE account_id = ANY(@Accounts) ORDER BY account_id FOR UPDATE;
                """, new { Accounts = accounts }, transaction, cancellationToken: cancellationToken)))
                .ToDictionary(wallet => wallet.AccountId);

            string[] definitions = plan.Sides.SelectMany(side => side.Offer.Items)
                .Where(line => line.AssetKind == MarketCodes.ASSET_ACCOUNT_ITEM).Select(line => line.DefinitionId)
                .Distinct(StringComparer.Ordinal).ToArray();
            Dictionary<(Guid, string), ItemRow> held = new Dictionary<(Guid, string), ItemRow>();
            if (definitions.Length > 0)
            {
                IEnumerable<ItemRow> itemRows = await connection.QueryAsync<ItemRow>(new CommandDefinition(
                    """
                    SELECT account_id AS "AccountId", definition_id AS "DefinitionId", quantity AS "Quantity",
                        reserved AS "Reserved"
                    FROM account_items
                    WHERE account_id = ANY(@Accounts) AND definition_id = ANY(@Definitions) AND binding_kind = @Binding
                    ORDER BY account_id, definition_id FOR UPDATE;
                    """, new { Accounts = accounts, Definitions = definitions, Binding = GrowthAssets.ACCOUNT_BINDING },
                    transaction, cancellationToken: cancellationToken));
                held = itemRows.ToDictionary(row => (row.AccountId, row.DefinitionId));
            }

            Guid[] equipmentIds = plan.Sides.SelectMany(side => side.Offer.Items)
                .Where(line => line.AssetKind == MarketCodes.ASSET_EQUIPMENT).Select(line => line.ItemId!.Value).ToArray();
            Dictionary<Guid, EquipmentRecord> equipment = await EquipmentRows.LockAsync(connection, transaction,
                equipmentIds, cancellationToken);

            int reason = await VerifyAsync(connection, transaction, plan, wallets, held, equipment, cancellationToken);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return reason;
            }

            EconomyMover mover = new EconomyMover(context, _ledger, TradeOperationKinds.ACTOR_SERVER, townInstanceId,
                operationId, TradeOperationKinds.LEDGER_SOURCE_TYPE, plan.Row.ExecutionId.ToString("D"));
            foreach (TradeSide side in plan.Sides)
            {
                string scope = TradeOfferRules.ScopeOf(side.IsA);
                if (!await mover.TryDebitAsync(EconomyAsset.Currency(side.AccountId), side.Offer.Currency,
                    ContractCodes.EconomyReason.TRADE_GIVE, scope, cancellationToken))
                {
                    return ContractCodes.RejectReason.TRADE_INSUFFICIENT_CURRENCY;
                }

                foreach (StoredLine line in side.Offer.Items.Where(line => line.AssetKind == MarketCodes.ASSET_ACCOUNT_ITEM)
                    .OrderBy(line => line.DefinitionId, StringComparer.Ordinal))
                {
                    if (!await mover.TryDebitAsync(EconomyAsset.AccountItem(side.AccountId, line.DefinitionId,
                        GrowthAssets.ACCOUNT_BINDING), line.Quantity, ContractCodes.EconomyReason.TRADE_GIVE, scope,
                        cancellationToken))
                    {
                        return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
                    }
                }
            }

            foreach (TradeSide side in plan.Sides)
            {
                TradeSide receiver = side.Other(plan);
                string scope = TradeOfferRules.ScopeOf(side.IsA);
                await mover.CreditAsync(EconomyAsset.Currency(receiver.AccountId), side.Offer.Currency,
                    ContractCodes.EconomyReason.TRADE_RECEIVE, scope, cancellationToken);
                foreach (StoredLine line in side.Offer.Items.Where(line => line.AssetKind == MarketCodes.ASSET_ACCOUNT_ITEM)
                    .OrderBy(line => line.DefinitionId, StringComparer.Ordinal))
                {
                    await mover.CreditAsync(EconomyAsset.AccountItem(receiver.AccountId, line.DefinitionId,
                        GrowthAssets.ACCOUNT_BINDING), line.Quantity, ContractCodes.EconomyReason.TRADE_RECEIVE, scope,
                        cancellationToken);
                }
            }

            foreach (TradeSide side in plan.Sides)
            {
                TradeSide receiver = side.Other(plan);
                foreach (StoredLine line in side.Offer.Items.Where(line => line.AssetKind == MarketCodes.ASSET_EQUIPMENT)
                    .OrderBy(line => line.ItemId))
                {
                    EquipmentRecord before = equipment[line.ItemId!.Value];
                    EquipmentRecord after = before.Clone();
                    after.AccountId   = receiver.AccountId;
                    after.CharacterId = receiver.CharacterId;
                    await EquipmentRows.UpdateOwnershipAsync(connection, transaction, after, cancellationToken);
                    await EquipmentRows.AppendHistoryAsync(connection, transaction, context, _history,
                        GrowthAssets.HISTORY_TRADED, before, after, receiver.AccountId, townInstanceId, operationId,
                        cancellationToken);
                }
            }

            return ContractCodes.RejectReason.NONE;
        }

        // 잠근 행으로 보유량, 엽전 가용량, 장비 소유와 거래 가능 조건, 받는 쪽 공간을 확인한다. 이동은 하지 않는다
        private async Task<int> VerifyAsync(DbConnection connection, DbTransaction transaction, TradePlan plan,
            IReadOnlyDictionary<Guid, WalletRow> wallets, IReadOnlyDictionary<(Guid, string), ItemRow> held,
            IReadOnlyDictionary<Guid, EquipmentRecord> equipment, CancellationToken cancellationToken)
        {
            foreach (TradeSide side in plan.Sides)
            {
                foreach (StoredLine line in side.Offer.Items.Where(line => line.AssetKind == MarketCodes.ASSET_EQUIPMENT))
                {
                    int reason = CheckEquipment(equipment, line.ItemId!.Value, line.DefinitionId, side.AccountId, side.CharacterId);
                    if (reason != ContractCodes.RejectReason.NONE)
                    {
                        return reason;
                    }
                }

                foreach (StoredLine line in side.Offer.Items.Where(line => line.AssetKind == MarketCodes.ASSET_ACCOUNT_ITEM))
                {
                    if (!MarketTradeRules.IsTradableAccountItem(line.DefinitionId))
                    {
                        return ContractCodes.RejectReason.TRADE_NOT_TRADABLE;
                    }
                    if (!held.TryGetValue((side.AccountId, line.DefinitionId), out ItemRow? row)
                        || ((row.Quantity - row.Reserved) < line.Quantity))
                    {
                        return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
                    }
                }

                long available = wallets.TryGetValue(side.AccountId, out WalletRow? wallet)
                    ? wallet.Currency - wallet.Reserved
                    : 0;
                if (available < side.Offer.Currency)
                {
                    return ContractCodes.RejectReason.TRADE_INSUFFICIENT_CURRENCY;
                }
            }

            foreach (TradeSide side in plan.Sides)
            {
                TradeSide giver = side.Other(plan);
                foreach (StoredLine line in giver.Offer.Items.Where(line => line.AssetKind == MarketCodes.ASSET_ACCOUNT_ITEM))
                {
                    // 받는 쪽의 최종 보유량 = 현재 - 자기가 내는 같은 품목 + 받는 수량
                    long current = held.TryGetValue((side.AccountId, line.DefinitionId), out ItemRow? row) ? row.Quantity : 0;
                    long given = side.Offer.Items.Where(own => (own.AssetKind == MarketCodes.ASSET_ACCOUNT_ITEM)
                        && (own.DefinitionId == line.DefinitionId)).Sum(own => (long)own.Quantity);
                    long limit = RewardStorageLimits.LimitFor(EconomyAsset.ACCOUNT_ITEM, line.DefinitionId) ?? long.MaxValue;
                    if ((current - given + line.Quantity) > limit)
                    {
                        return ContractCodes.RejectReason.TRADE_INVENTORY_FULL;
                    }
                }

                int incoming = giver.Offer.Items.Count(line => line.AssetKind == MarketCodes.ASSET_EQUIPMENT);
                if (incoming == 0)
                {
                    continue;
                }

                int outgoing = side.Offer.Items.Count(line => line.AssetKind == MarketCodes.ASSET_EQUIPMENT);
                int stored = await EquipmentRows.CountStoredAsync(connection, transaction, side.CharacterId,
                    cancellationToken);
                if ((stored + incoming - outgoing) > _content.Equipment.StorageSlotsPerCharacter)
                {
                    return ContractCodes.RejectReason.TRADE_INVENTORY_FULL;
                }
            }

            return ContractCodes.RejectReason.NONE;
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class WalletRow
        {
            public Guid AccountId { get; set; }
            public long Currency  { get; set; }
            public long Reserved  { get; set; }
        }

        private sealed class ItemRow
        {
            public Guid   AccountId    { get; set; }
            public string DefinitionId { get; set; } = string.Empty;
            public long   Quantity     { get; set; }
            public long   Reserved     { get; set; }
        }

        // 실행 행과 동결된 제안 두 개. Sides[0]이 A, Sides[1]이 B다
        private sealed class TradePlan
        {
            public TradeExecutionRow Row   { get; }
            public TradeSide[]       Sides { get; }

            public TradePlan(TradeExecutionRow row, StoredOffer offerA, StoredOffer offerB)
            {
                Row = row;
                Sides = new[]
                {
                    new TradeSide(true, row.AccountA, row.CharacterA, offerA),
                    new TradeSide(false, row.AccountB, row.CharacterB, offerB)
                };
            }
        }

        private sealed class TradeSide
        {
            public bool        IsA         { get; }
            public Guid        AccountId   { get; }
            public Guid        CharacterId { get; }
            public StoredOffer Offer       { get; }

            public TradeSide(bool isA, Guid accountId, Guid characterId, StoredOffer offer)
            {
                IsA         = isA;
                AccountId   = accountId;
                CharacterId = characterId;
                Offer       = offer;
            }

            public TradeSide Other(TradePlan plan)
            {
                return IsA ? plan.Sides[1] : plan.Sides[0];
            }
        }
    }
}
