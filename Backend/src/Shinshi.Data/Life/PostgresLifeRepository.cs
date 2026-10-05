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
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Life
{
    /// <summary> 마을 서버의 생활 저장(행동력, 채집, 물약 제작). 채집 판정(선점, 거리, 시간)은 하지 않고 계정 임대, 행동력,
    /// 재고와 보관 상한만 확인한다. 작업 actor는 계정이라 재접속이나 다른 마을에서 같은 operationId를 다시 보내도 같은 결과다 </summary>
    public sealed class PostgresLifeRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string GATHER_KIND       = "life.gather";
        public const string POTION_CRAFT_KIND = "life.potion-craft";

        public const string GATHER_SOURCE_TYPE = "gathering";
        public const string CRAFT_SOURCE_TYPE  = "potion_craft";

        // 멱등 actor가 계정이므로 원장 actor_kind는 요청한 플레이어다
        private const string PLAYER_ACTOR      = "Player";
        private const char   SOURCE_SEPARATOR  = ':';
        private const string GATHER_ENTRY_KEY  = "life:gather:herb";
        private const string CRAFT_HERB_KEY    = "life:craft:herb";
        private const string CRAFT_POTION_KEY  = "life:craft:potion";

        // 제작은 지갑을 건드리지 않아 계정 revision 계약이 없다
        private const long NO_REVISION = 0;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly IEconomyLedgerEntryWriter _ledger;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresLifeRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            IEconomyLedgerEntryWriter ledger)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(ledger);
            _dataSource = dataSource;
            _executor   = executor;
            _ledger     = ledger;
        }

        /// <summary> 회복을 반영한 행동력과 약초, 물약 재고를 DB 시각으로 읽는다. 저장 값은 바꾸지 않는다.
        /// 계정 임대가 이 마을 서버의 살아 있는 임대가 아니면 SESSION_MISMATCH다 </summary>
        public async Task<LifeSnapshotOutcome> ReadSnapshotAsync(Guid townInstanceId, Guid accountId, Guid accountLeaseId,
            CancellationToken cancellationToken = default)
        {
            RequireIds(townInstanceId, accountId, accountLeaseId);
            string[] itemIds = new[] { LifeContent.HERB_ID }.Concat(LifeContent.PotionIds).ToArray();

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.RepeatableRead, cancellationToken);
            SnapshotRow row = await connection.QuerySingleAsync<SnapshotRow>(new CommandDefinition(
                """
                SELECT clock_timestamp() AS "Now",
                    EXISTS (SELECT 1 FROM account_leases
                        WHERE account_id = @AccountId AND lease_id = @LeaseId AND instance_id = @TownId AND role = @TownRole
                            AND expires_at > clock_timestamp()) AS "Leased",
                    wallet.action_value AS "ActionValue", wallet.action_anchor AS "ActionAnchor"
                FROM (SELECT 1) AS one
                LEFT JOIN account_wallets AS wallet ON wallet.account_id = @AccountId;
                """, new
                {
                    AccountId = accountId, LeaseId = accountLeaseId, TownId = townInstanceId,
                    TownRole = ContractCodes.ServerRole.TOWN
                }, transaction, cancellationToken: cancellationToken));
            DateTimeOffset now = ToUtc(row.Now);
            if (!row.Leased)
            {
                return LifeSnapshotOutcome.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH, now);
            }

            Dictionary<string, StockRow> stocks = (await connection.QueryAsync<StockRow>(new CommandDefinition(
                """
                SELECT definition_id AS "DefinitionId", quantity AS "Quantity", reserved AS "Reserved"
                FROM account_items
                WHERE account_id = @AccountId AND binding_kind = @Binding AND definition_id = ANY(@Ids);
                """, new { AccountId = accountId, Binding = LifeContent.BINDING_KIND, Ids = itemIds }, transaction,
                cancellationToken: cancellationToken))).ToDictionary(stock => stock.DefinitionId, StringComparer.Ordinal);
            await transaction.CommitAsync(cancellationToken);

            // 지갑 행이 없으면 첫 채집 때 기본값(상한)으로 만들어지므로 상한으로 보인다
            ActionEnergyState energy = row.ActionValue.HasValue
                ? ActionEnergyRules.Recover(row.ActionValue.Value, ToUtc(row.ActionAnchor!.Value), now)
                : new ActionEnergyState(LifeContent.ACTION_MAX, now, 0);
            DateTimeOffset? next = ActionEnergyRules.NextRecoverAt(energy);
            LifeHerbStock herb = new LifeHerbStock(LifeContent.HERB_ID, QuantityOf(stocks, LifeContent.HERB_ID),
                LifeContent.HERB_STACK);
            LifePotionStock[] potions = LifeContent.PotionIds.Select(id => new LifePotionStock(id,
                QuantityOf(stocks, id), stocks.TryGetValue(id, out StockRow? stock) ? stock.Reserved : 0)).ToArray();
            return LifeSnapshotOutcome.Found(new LifeSnapshotResponse(energy.Value, LifeContent.ACTION_MAX,
                next?.ToUnixTimeMilliseconds() ?? 0, herb, potions, now.ToUnixTimeMilliseconds()), now);
        }

        /// <summary> 채집 완료 한 번. 행동력이 모자라면 INSUFFICIENT_ACTION, 약초 보관 상한을 넘으면 INVENTORY_FULL이고
        /// 아무것도 바꾸지 않는다. committedRevision은 지갑 revision이다 </summary>
        public Task<OperationResult> GatherAsync(LifeGatherInput input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return _executor.ExecuteAsync(GatherRequest(input),
                (context, token) => GatherEffectAsync(input, context, token), cancellationToken);
        }

        /// <summary> 같은 채집 요청의 저장된 결과만 읽는다. 처음 보는 operationId면 null이다 </summary>
        public Task<OperationResult?> FindStoredGatherAsync(LifeGatherInput input,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return _executor.FindStoredAsync(GatherRequest(input), cancellationToken);
        }

        /// <summary> 물약 제작. 가용 약초가 모자라면 INSUFFICIENT_ITEM, 완제품 보관 상한을 넘으면 INVENTORY_FULL이고
        /// 아무것도 바꾸지 않는다. 모르는 정의나 count 0 이하는 호출자가 먼저 형식 오류로 막는다 </summary>
        public Task<OperationResult> CraftPotionAsync(LifePotionCraftInput input,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!LifeContent.TryGetPotionCost(input.PotionDefinitionId, out _) || (input.Count <= 0))
            {
                throw new ArgumentException("Potion craft request is invalid.", nameof(input));
            }

            return _executor.ExecuteAsync(CraftRequest(input),
                (context, token) => CraftEffectAsync(input, context, token), cancellationToken);
        }

        /// <summary> 같은 제작 요청의 저장된 결과만 읽는다. 처음 보는 operationId면 null이다 </summary>
        public Task<OperationResult?> FindStoredCraftAsync(LifePotionCraftInput input,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return _executor.FindStoredAsync(CraftRequest(input), cancellationToken);
        }

        // 임대 ID와 마을 인스턴스는 해시에 넣지 않는다. 재접속으로 임대나 마을이 바뀐 뒤의 같은 operationId 재전송이 IDEMPOTENCY_CONFLICT가 되면 안 된다
        private static OperationRequest GatherRequest(LifeGatherInput input)
        {
            RequireIds(input.TownInstanceId, input.AccountId, input.AccountLeaseId);
            return new OperationRequest(input.AccountId, input.OperationId, GATHER_KIND,
                SessionDb.ToRequestJson(new
                {
                    accountId = input.AccountId.ToString("D"),
                    zoneId = input.ZoneId,
                    nodeId = input.NodeId
                }));
        }

        private static OperationRequest CraftRequest(LifePotionCraftInput input)
        {
            RequireIds(input.TownInstanceId, input.AccountId, input.AccountLeaseId);
            return new OperationRequest(input.AccountId, input.OperationId, POTION_CRAFT_KIND,
                SessionDb.ToRequestJson(new
                {
                    accountId = input.AccountId.ToString("D"),
                    potionDefinitionId = input.PotionDefinitionId,
                    count = input.Count
                }));
        }

        private async Task<OperationDecision> GatherEffectAsync(LifeGatherInput input, OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 잠금 순서: 계정 -> 계정 임대(공유) -> 지갑 -> 아이템. 임대 이관(계정 -> 임대)과 정산(계정 -> 지갑 -> 아이템)과 같은 방향이다
            if (!await LockOwnedAccountAsync(connection, transaction, input, cancellationToken))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO account_wallets (account_id) VALUES (@AccountId) ON CONFLICT (account_id) DO NOTHING;",
                new { input.AccountId }, transaction, cancellationToken: cancellationToken));
            WalletRow wallet = await connection.QuerySingleAsync<WalletRow>(new CommandDefinition(
                """
                SELECT action_value AS "ActionValue", action_anchor AS "ActionAnchor", revision AS "Revision"
                FROM account_wallets WHERE account_id = @AccountId FOR UPDATE;
                """, new { input.AccountId }, transaction, cancellationToken: cancellationToken));

            // 지갑 잠금을 얻은 뒤의 DB 시각으로 회복을 계산한다. 잠금 대기 전 시각을 쓰면 앞선 소비의 기준 시각보다 이를 수 있다
            DateTimeOffset now = ToUtc(await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", transaction: transaction, cancellationToken: cancellationToken)));
            if (!ActionEnergyRules.TryConsume(wallet.ActionValue, ToUtc(wallet.ActionAnchor), now,
                    LifeContent.ACTION_COST_PER_GATHER, out ActionEnergyState after))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ACTION, wallet.Revision);
            }

            long herbs = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                SELECT quantity FROM account_items
                WHERE account_id = @AccountId AND definition_id = @HerbId AND binding_kind = @Binding FOR UPDATE;
                """, new { input.AccountId, HerbId = LifeContent.HERB_ID, Binding = LifeContent.BINDING_KIND },
                transaction, cancellationToken: cancellationToken)) ?? 0;
            if (herbs + LifeContent.HERBS_PER_GATHER > LifeContent.HERB_STACK)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVENTORY_FULL, wallet.Revision);
            }

            long revision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                UPDATE account_wallets
                SET action_value = @Value, action_anchor = @Anchor, revision = revision + 1
                WHERE account_id = @AccountId
                RETURNING revision;
                """, new { input.AccountId, after.Value, Anchor = after.Anchor.ToUniversalTime() }, transaction,
                cancellationToken: cancellationToken));
            EconomyAsset herb = EconomyAsset.AccountItem(input.AccountId, LifeContent.HERB_ID, LifeContent.BINDING_KIND);
            long balance = await RewardBalances.CreditAsync(connection, transaction, herb, LifeContent.HERBS_PER_GATHER,
                cancellationToken);
            await _ledger.AppendAsync(context, new EconomyLedgerEntry(Guid.NewGuid(), GATHER_ENTRY_KEY, herb,
                LifeContent.HERBS_PER_GATHER, balance, ContractCodes.EconomyReason.GATHERING, GATHER_SOURCE_TYPE,
                input.ZoneId + SOURCE_SEPARATOR + input.NodeId, PLAYER_ACTOR, input.AccountId, input.OperationId),
                cancellationToken);
            return OperationDecision.Committed(revision, Guid.NewGuid().ToString("D"));
        }

        private async Task<OperationDecision> CraftEffectAsync(LifePotionCraftInput input,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            if (!await LockOwnedAccountAsync(connection, transaction, input, cancellationToken))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            LifeContent.TryGetPotionCost(input.PotionDefinitionId, out long herbCost);
            long herbNeeded = herbCost * input.Count;
            long produced = LifeContent.POTION_OUTPUT * input.Count;

            // 아이템 행은 definition_id 순서로 잠근다(약초 item_herb가 물약 item_potion_*보다 앞). 출정 물약 소비와 같은 순서다
            string[] ids = new[] { LifeContent.HERB_ID, input.PotionDefinitionId }
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Dictionary<string, StockRow> stocks = (await connection.QueryAsync<StockRow>(new CommandDefinition(
                """
                SELECT definition_id AS "DefinitionId", quantity AS "Quantity", reserved AS "Reserved"
                FROM account_items
                WHERE account_id = @AccountId AND binding_kind = @Binding AND definition_id = ANY(@Ids)
                ORDER BY definition_id
                FOR UPDATE;
                """, new { input.AccountId, Binding = LifeContent.BINDING_KIND, Ids = ids }, transaction,
                cancellationToken: cancellationToken))).ToDictionary(stock => stock.DefinitionId, StringComparer.Ordinal);
            long herbAvailable = stocks.TryGetValue(LifeContent.HERB_ID, out StockRow? herbRow)
                ? herbRow.Quantity - herbRow.Reserved : 0;
            if (herbAvailable < herbNeeded)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }
            if (QuantityOf(stocks, input.PotionDefinitionId) + produced > LifeContent.POTION_STACK)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVENTORY_FULL);
            }

            // 예약분(출정 지참)을 남기는 조건을 차감 문장에 함께 둔다
            long herbAfter = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                UPDATE account_items SET quantity = quantity - @Needed
                WHERE account_id = @AccountId AND definition_id = @HerbId AND binding_kind = @Binding
                    AND quantity - reserved >= @Needed
                RETURNING quantity;
                """, new
                {
                    input.AccountId, Needed = herbNeeded, HerbId = LifeContent.HERB_ID, Binding = LifeContent.BINDING_KIND
                }, transaction, cancellationToken: cancellationToken))
                ?? throw new InvalidOperationException("Locked herb stock changed during a potion craft.");
            EconomyAsset herb = EconomyAsset.AccountItem(input.AccountId, LifeContent.HERB_ID, LifeContent.BINDING_KIND);
            EconomyAsset potion = EconomyAsset.AccountItem(input.AccountId, input.PotionDefinitionId,
                LifeContent.BINDING_KIND);
            long potionAfter = await RewardBalances.CreditAsync(connection, transaction, potion, produced,
                cancellationToken);
            await _ledger.AppendAsync(context, new EconomyLedgerEntry(Guid.NewGuid(), CRAFT_HERB_KEY, herb, -herbNeeded,
                herbAfter, ContractCodes.EconomyReason.CRAFT, CRAFT_SOURCE_TYPE, input.PotionDefinitionId, PLAYER_ACTOR,
                input.AccountId, input.OperationId), cancellationToken);
            await _ledger.AppendAsync(context, new EconomyLedgerEntry(Guid.NewGuid(), CRAFT_POTION_KEY, potion, produced,
                potionAfter, ContractCodes.EconomyReason.CRAFT, CRAFT_SOURCE_TYPE, input.PotionDefinitionId,
                PLAYER_ACTOR, input.AccountId, input.OperationId), cancellationToken);
            return OperationDecision.Committed(NO_REVISION, Guid.NewGuid().ToString("D"));
        }

        // 계정 행을 잠근 뒤 그 계정의 접속 임대가 이 마을 서버의 살아 있는 임대인지 FOR SHARE로 확인한다.
        // 공유 잠금이라 같은 계정의 다른 생활 작업은 막지 않고, 확인과 효과 사이에 임대가 옮겨 가는 것만 막는다
        private static async Task<bool> LockOwnedAccountAsync(DbConnection connection, DbTransaction transaction,
            LifeAccountInput input, CancellationToken cancellationToken)
        {
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { input.AccountId },
                    cancellationToken) != 1)
            {
                return false;
            }

            Guid? lease = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                SELECT lease_id FROM account_leases
                WHERE account_id = @AccountId AND lease_id = @LeaseId AND instance_id = @TownId AND role = @TownRole
                    AND expires_at > clock_timestamp()
                FOR SHARE;
                """, new
                {
                    input.AccountId, LeaseId = input.AccountLeaseId, TownId = input.TownInstanceId,
                    TownRole = ContractCodes.ServerRole.TOWN
                }, transaction, cancellationToken: cancellationToken));
            return lease.HasValue;
        }

        private static long QuantityOf(IReadOnlyDictionary<string, StockRow> stocks, string definitionId)
        {
            return stocks.TryGetValue(definitionId, out StockRow? stock) ? stock.Quantity : 0;
        }

        private static void RequireIds(Guid townInstanceId, Guid accountId, Guid accountLeaseId)
        {
            if ((townInstanceId == Guid.Empty) || (accountId == Guid.Empty) || (accountLeaseId == Guid.Empty))
            {
                throw new ArgumentException("Town, account and lease IDs are required.");
            }
        }

        private static DateTimeOffset ToUtc(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private sealed class SnapshotRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public DateTime  Now          { get; set; }
            public bool      Leased       { get; set; }
            public long?     ActionValue  { get; set; }
            public DateTime? ActionAnchor { get; set; }
        }

        private sealed class WalletRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public long     ActionValue  { get; set; }
            public DateTime ActionAnchor { get; set; }
            public long     Revision     { get; set; }
        }

        private sealed class StockRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string DefinitionId { get; set; } = string.Empty;
            public long   Quantity     { get; set; }
            public long   Reserved     { get; set; }
        }
    }
}
