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
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Market
{
    /// <summary> 유저 경매장 쓰기(등록, 구매, 취소, 확인, 만료). 요청 작업은 캐릭터 계정 actor(Player)의 멱등 작업이고 만료는 System actor다.
    /// 잠금 순서: 등록 행 -> 계정(uuid 오름차순) -> 캐릭터 -> 지갑 -> account_items -> 장비(item_id) -> 원장.
    /// 만료 판정은 등록 행을 잠근 뒤 clock_timestamp()로 한다(now()는 트랜잭션 시작 시각이라 잠금 대기 중 만료를 놓친다) </summary>
    public sealed partial class PostgresMarketRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 확정하지 못한 거절이 올리는 revision은 없다
        private const long NO_REVISION = 0;

        private const string ESCROW_SCOPE       = "escrow";
        private const string MARKET_ESCROW_KIND = "Market";

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly IEconomyLedgerEntryWriter _ledger;
        private readonly PostgresItemHistoryWriter _history;
        private readonly GrowthContent _content;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresMarketRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
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

        /// <summary> 판매 등록. 수량형은 판매자 account_items에서 빼 등록 행으로 옮기고(원장 MarketEscrow), 장비는 보관 표시를 한다.
        /// replayOnly면 효과 없이 저장된 결과만 찾는다 </summary>
        public async Task<MarketOperationOutcome> ListAsync(Guid townInstanceId, Guid characterId, MarketListCommand command,
            bool replayOnly, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            OperationResult? result = await RunPlayerAsync(characterId, command.OperationId, MarketOperationKinds.LIST,
                new
                {
                    assetKind = command.AssetKind,
                    definitionId = command.DefinitionId,
                    itemId = command.ItemId?.ToString("D"),
                    quantity = command.Quantity,
                    unitPrice = command.UnitPrice
                }, replayOnly, (accountId, context, token) => ListEffectAsync(townInstanceId, accountId, characterId,
                    command, context, token), cancellationToken);
            return await BuildOutcomeAsync(MarketOperationKinds.LIST, result, cancellationToken);
        }

        /// <summary> 취소. 판매자 본인의 활성 등록만 닫고 미판매분을 돌려준다. 기능 차단 중에도 호출할 수 있게 기능 키를 보지 않는다 </summary>
        public async Task<MarketOperationOutcome> CancelAsync(Guid townInstanceId, Guid characterId, Guid operationId,
            Guid listingId, CancellationToken cancellationToken = default)
        {
            OperationResult? result = await RunPlayerAsync(characterId, operationId, MarketOperationKinds.CANCEL,
                new { listingId = listingId.ToString("D") }, false, (accountId, context, token) => CancelEffectAsync(
                    townInstanceId, accountId, characterId, operationId, listingId, context, token), cancellationToken);
            return await BuildOutcomeAsync(MarketOperationKinds.CANCEL, result, cancellationToken);
        }

        /// <summary> 판매자가 종료된 등록의 결과를 확인했다고 표시한다(seen_at). 경제 효과와 원장이 없는 단순 갱신이라 operations 행을 만들지 않는다.
        /// 같은 요청을 다시 보내도 이미 표시된 행은 건너뛰므로 결과가 같다. listingIds가 비면 확인하지 않은 종료 등록 전체다.
        /// 성공이면 확정 결과, 이 마을에 접속해 있지 않거나 캐릭터가 없으면 거절 결과다 </summary>
        public async Task<OperationResult> AckAsync(Guid townInstanceId, Guid characterId, Guid operationId,
            IReadOnlyCollection<Guid> listingIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(listingIds);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            AckAccessRow access = await connection.QuerySingleAsync<AckAccessRow>(new CommandDefinition(
                """
                SELECT character.account_id AS "AccountId",
                    EXISTS (SELECT 1 FROM account_leases AS lease
                        WHERE lease.account_id = character.account_id AND lease.instance_id = @Town
                            AND lease.role = @TownRole AND lease.expires_at > clock_timestamp()) AS "InTown",
                    (extract(epoch FROM clock_timestamp()) * 1000)::bigint AS "ServerTimeUnixMs"
                FROM (SELECT 1) AS anchor
                LEFT JOIN characters AS character ON character.character_id = @CharacterId AND character.deleted_at IS NULL;
                """, new { CharacterId = characterId, Town = townInstanceId, TownRole = ContractCodes.ServerRole.TOWN },
                cancellationToken: cancellationToken));
            string id = operationId.ToString("D");
            if (!access.AccountId.HasValue)
            {
                return new OperationResult(id, ContractCodes.OperationStatus.REJECTED,
                    ContractCodes.RejectReason.INVALID_TARGET, NO_REVISION, access.ServerTimeUnixMs, string.Empty);
            }
            if (!access.InTown)
            {
                return new OperationResult(id, ContractCodes.OperationStatus.REJECTED,
                    ContractCodes.RejectReason.WRONG_LOCATION, NO_REVISION, access.ServerTimeUnixMs, string.Empty);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE market_listings SET seen_at = clock_timestamp()
                WHERE seller_account_id = @AccountId AND state <> 'Active' AND seen_at IS NULL
                    AND (cardinality(@ListingIds) = 0 OR listing_id = ANY(@ListingIds));
                """, new { AccountId = access.AccountId.Value, ListingIds = listingIds.ToArray() },
                cancellationToken: cancellationToken));
            return new OperationResult(id, ContractCodes.OperationStatus.COMMITTED, ContractCodes.RejectReason.NONE,
                NO_REVISION, access.ServerTimeUnixMs, id);
        }

        // 멱등 actor는 캐릭터의 계정이라 재접속이나 다른 마을에서 같은 operationId를 다시 보내도 처음 결과를 받는다.
        // 없는 캐릭터는 저장할 actor가 없어 저장 없이 거절한다(replayOnly면 null)
        private async Task<OperationResult?> RunPlayerAsync(Guid characterId, Guid operationId, string kind, object body,
            bool replayOnly, Func<Guid, OperationExecutionContext, CancellationToken, Task<OperationDecision>> effect,
            CancellationToken cancellationToken)
        {
            OwnerRow owner;
            await using (NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken))
            {
                owner = await connection.QuerySingleAsync<OwnerRow>(new CommandDefinition(
                    """
                    SELECT (SELECT account_id FROM characters WHERE character_id = @CharacterId) AS "AccountId",
                        (extract(epoch FROM clock_timestamp()) * 1000)::bigint AS "ServerTimeUnixMs";
                    """, new { CharacterId = characterId }, cancellationToken: cancellationToken));
            }

            if (!owner.AccountId.HasValue)
            {
                return replayOnly ? null : new OperationResult(operationId.ToString("D"),
                    ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.INVALID_TARGET, NO_REVISION,
                    owner.ServerTimeUnixMs, string.Empty);
            }

            Guid accountId = owner.AccountId.Value;
            OperationRequest request = new OperationRequest(accountId, operationId, kind,
                SessionDb.ToRequestJson(new { characterId = characterId.ToString("D"), body }));
            return replayOnly
                ? await _executor.FindStoredAsync(request, cancellationToken)
                : await _executor.ExecuteAsync(request, (context, token) => effect(accountId, context, token),
                    cancellationToken);
        }

        // 확정된 작업의 영수증 ID(등록 ID, 성사 ID)로 응답 필드를 채운다. 재전송도 같은 값을 받는다
        private async Task<MarketOperationOutcome> BuildOutcomeAsync(string kind, OperationResult? result,
            CancellationToken cancellationToken)
        {
            if ((result == null) || (result.Status != ContractCodes.OperationStatus.COMMITTED)
                || !Guid.TryParse(result.ReceiptId, out Guid receiptId))
            {
                return MarketOperationOutcome.Of(result);
            }

            if (kind != MarketOperationKinds.BUY)
            {
                return new MarketOperationOutcome(result, receiptId.ToString("D"), string.Empty, 0, 0, 0);
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            TradeRow? trade = await connection.QuerySingleOrDefaultAsync<TradeRow>(new CommandDefinition(
                """
                SELECT listing_id AS "ListingId", gross AS "Gross", fee AS "Fee", net AS "Net"
                FROM market_trades WHERE trade_id = @TradeId;
                """, new { TradeId = receiptId }, cancellationToken: cancellationToken));
            return trade == null
                ? MarketOperationOutcome.Of(result)
                : new MarketOperationOutcome(result, trade.ListingId.ToString("D"), receiptId.ToString("D"), trade.Gross,
                    trade.Fee, trade.Net);
        }

        private async Task<OperationDecision> ListEffectAsync(Guid townInstanceId, Guid accountId, Guid characterId,
            MarketListCommand command, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            bool equipment = command.AssetKind == MarketCodes.ASSET_EQUIPMENT;
            if (!equipment && (command.AssetKind != MarketCodes.ASSET_ACCOUNT_ITEM))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_NOT_TRADABLE);
            }
            if (!MarketTradeRules.IsUnitPriceInRange(command.UnitPrice)
                || !MarketTradeRules.IsQuantityInRange(command.Quantity) || (equipment && (command.Quantity != 1)))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_PRICE_OUT_OF_RANGE);
            }
            if ((equipment && !command.ItemId.HasValue)
                || (!equipment && !MarketTradeRules.IsTradableAccountItem(command.DefinitionId)))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_NOT_TRADABLE);
            }

            // 계정 행 잠금이 같은 계정의 동시 등록을 직렬화하므로 활성 수 검사가 경합하지 않는다
            await LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken);
            int access = await LockCharacterAsync(connection, transaction, accountId, characterId, townInstanceId,
                cancellationToken);
            if (access != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(access);
            }

            long active = await connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT count(*) FROM market_listings WHERE seller_account_id = @AccountId AND state = 'Active';",
                new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            if (active >= MarketLimits.MAX_ACTIVE_LISTINGS)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_LISTING_LIMIT);
            }

            Guid listingId = Guid.NewGuid();
            string definitionId = command.DefinitionId;
            int grade = 0;
            int? classId = null;
            EquipmentRecord? item = null;
            if (equipment)
            {
                Guid itemId = command.ItemId!.Value;
                Dictionary<Guid, EquipmentRecord> items = await EquipmentRows.LockAsync(connection, transaction,
                    new[] { itemId }, cancellationToken);
                if (!items.TryGetValue(itemId, out item) || (item.AccountId != accountId) || (item.CharacterId != characterId))
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_NOT_OWNER);
                }
                if (item.EscrowKind != null)
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_ESCROWED);
                }
                if (!MarketTradeRules.IsTradableEquipment(item.Grade, item.Enhancement, item.Equipped, item.Bound,
                    item.Locked, item.IsTraining, item.Origin, false))
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_NOT_TRADABLE);
                }

                definitionId = item.DefinitionId;
                grade = item.Grade;

                // TODO(민성): 장비 정의(growth-content.json equipment.definitions)에 직업 필드가 없어 class_id를 NULL로 둔다.
                // 장비 직업 분류가 확정되면 여기서 채운다. 임의로 매핑하지 않는다

            }
            else
            {
                // 부적 카드는 효과 정의의 직업을 쓴다. 약초 등 직업이 없는 품목은 NULL이다
                if (_content.TryGetEffect(definitionId, out GrowthContent.TalismanEffect effect))
                {
                    classId = effect.PlayerClass;
                }

                EconomyMover mover = CreateMover(context, MarketOperationKinds.ACTOR_PLAYER, accountId,
                    command.OperationId, listingId);
                EconomyAsset asset = EconomyAsset.AccountItem(accountId, definitionId, GrowthAssets.ACCOUNT_BINDING);
                if (!await mover.TryDebitAsync(asset, command.Quantity, ContractCodes.EconomyReason.MARKET_ESCROW,
                    ESCROW_SCOPE, cancellationToken))
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO market_listings (listing_id, seller_account_id, seller_character_id, asset_kind, definition_id,
                    item_id, grade, class_id, category, unit_price, listed_qty, remaining_qty, state, created_at, expires_at)
                VALUES (@ListingId, @AccountId, @CharacterId, @AssetKind, @DefinitionId, @ItemId, @Grade, @ClassId, @Category,
                    @UnitPrice, @Quantity, @Quantity, 'Active', clock_timestamp(), clock_timestamp() + @Duration);
                """, new
                {
                    ListingId = listingId,
                    AccountId = accountId,
                    CharacterId = characterId,
                    command.AssetKind,
                    DefinitionId = definitionId,
                    ItemId = command.ItemId,
                    Grade = (short)grade,
                    ClassId = (short?)classId,
                    Category = (short)MarketTradeRules.CategoryOf(command.AssetKind, definitionId),
                    command.UnitPrice,
                    command.Quantity,
                    Duration = TimeSpan.FromHours(MarketLimits.LISTING_HOURS)
                }, transaction, cancellationToken: cancellationToken));

            if (item != null)
            {
                EquipmentRecord after = item.Clone();
                after.EscrowKind = MARKET_ESCROW_KIND;
                after.EscrowRef  = listingId;
                await EquipmentRows.UpdateOwnershipAsync(connection, transaction, after, cancellationToken);
                await EquipmentRows.AppendHistoryAsync(connection, transaction, context, _history,
                    GrowthAssets.HISTORY_MARKET_ESCROWED, item, after, accountId, accountId, command.OperationId,
                    cancellationToken);
            }

            return OperationDecision.Committed(NO_REVISION, listingId.ToString("D"));
        }

        private async Task<OperationDecision> CancelEffectAsync(Guid townInstanceId, Guid accountId, Guid characterId,
            Guid operationId, Guid listingId, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            MarketListingRow? listing = await LockListingAsync(connection, transaction, listingId, false, cancellationToken);
            if (listing == null)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (listing.SellerAccountId != accountId)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.MARKET_NOT_OWNER);
            }
            if (listing.State != MarketCodes.ListingState.ACTIVE)
            {
                return OperationDecision.Rejected(ClosedReason(listing.State), listing.Revision);
            }

            await LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken);
            int access = await LockCharacterAsync(connection, transaction, accountId, characterId, townInstanceId,
                cancellationToken);
            if (access != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(access);
            }

            // 본인이 닫은 등록은 확인한 것으로 본다
            long revision = await CloseListingAsync(connection, transaction, listingId, MarketCodes.ListingState.CANCELLED,
                true, cancellationToken);
            EconomyMover mover = CreateMover(context, MarketOperationKinds.ACTOR_PLAYER, accountId, operationId, listingId);
            await MarketEscrow.ReturnUnsoldAsync(context, connection, transaction, mover, _history, listing, accountId,
                operationId, cancellationToken);
            return OperationDecision.Committed(revision, listingId.ToString("D"));
        }

        private EconomyMover CreateMover(OperationExecutionContext context, string actorKind, Guid actorId,
            Guid operationId, Guid sourceId)
        {
            return new EconomyMover(context, _ledger, actorKind, actorId, operationId,
                MarketOperationKinds.LEDGER_SOURCE_TYPE, sourceId.ToString("D"));
        }

        // 닫힌 등록에 온 요청의 거절 사유. 만료로 닫힌 것만 MarketExpired고 나머지(매진, 취소)는 MarketSoldOut이다
        private static int ClosedReason(string state)
        {
            return state == MarketCodes.ListingState.EXPIRED
                ? ContractCodes.RejectReason.MARKET_EXPIRED
                : ContractCodes.RejectReason.MARKET_SOLD_OUT;
        }

        private static async Task LockAccountsAsync(DbConnection connection, DbTransaction transaction,
            IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
        {
            int expected = accountIds.Distinct().Count();
            if (await SessionDb.LockAccountsAsync(connection, transaction, accountIds, cancellationToken) != expected)
            {
                throw new InvalidOperationException("A market account is missing.");
            }
        }

        // 캐릭터 행을 잠그고 계정 접속 임대가 호출 마을에 있는지 본다. 판정은 계정 잠금 뒤의 값이 최종이다
        private static async Task<int> LockCharacterAsync(DbConnection connection, DbTransaction transaction,
            Guid accountId, Guid characterId, Guid townInstanceId, CancellationToken cancellationToken)
        {
            bool? inTown = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM account_leases AS lease WHERE lease.account_id = character.account_id
                    AND lease.instance_id = @Town AND lease.role = @TownRole AND lease.expires_at > clock_timestamp())
                FROM characters AS character
                WHERE character.character_id = @CharacterId AND character.account_id = @AccountId
                    AND character.deleted_at IS NULL
                FOR UPDATE OF character;
                """, new
                {
                    CharacterId = characterId,
                    AccountId = accountId,
                    Town = townInstanceId,
                    TownRole = ContractCodes.ServerRole.TOWN
                }, transaction, cancellationToken: cancellationToken));
            if (!inTown.HasValue)
            {
                return ContractCodes.RejectReason.INVALID_TARGET;
            }

            return inTown.Value ? ContractCodes.RejectReason.NONE : ContractCodes.RejectReason.WRONG_LOCATION;
        }

        // 등록 행을 잠가 읽는다. skipLocked면 다른 작업이 잡고 있는 행은 건너뛰어 null이다
        private static Task<MarketListingRow?> LockListingAsync(DbConnection connection, DbTransaction transaction,
            Guid listingId, bool skipLocked, CancellationToken cancellationToken)
        {
            string sql = "SELECT " + MarketListingRow.COLUMNS
                + " FROM market_listings WHERE listing_id = @ListingId FOR UPDATE" + (skipLocked ? " SKIP LOCKED;" : ";");
            return connection.QuerySingleOrDefaultAsync<MarketListingRow?>(new CommandDefinition(sql,
                new { ListingId = listingId }, transaction, cancellationToken: cancellationToken));
        }

        // 잠근 뒤 별도 문장으로 비교하므로 잠금 대기 중에 지난 만료도 잡는다
        private static Task<bool> IsExpiredAsync(DbConnection connection, DbTransaction transaction, Guid listingId,
            CancellationToken cancellationToken)
        {
            return connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT expires_at <= clock_timestamp() FROM market_listings WHERE listing_id = @ListingId;",
                new { ListingId = listingId }, transaction, cancellationToken: cancellationToken));
        }

        // 상태 전이는 Active 조건부 갱신이다. 호출자가 등록 행을 잠갔으므로 영향 행은 항상 1이다
        private static async Task<long> CloseListingAsync(DbConnection connection, DbTransaction transaction,
            Guid listingId, string state, bool markSeen, CancellationToken cancellationToken)
        {
            long? revision = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                UPDATE market_listings
                SET state = @State, closed_at = clock_timestamp(), revision = revision + 1,
                    seen_at = CASE WHEN @MarkSeen THEN clock_timestamp() ELSE seen_at END
                WHERE listing_id = @ListingId AND state = 'Active'
                RETURNING revision;
                """, new { ListingId = listingId, State = state, MarkSeen = markSeen }, transaction,
                cancellationToken: cancellationToken));
            return revision ?? throw new InvalidOperationException("The listing was not active when closing.");
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class OwnerRow
        {
            public Guid? AccountId        { get; set; }
            public long  ServerTimeUnixMs { get; set; }
        }

        private sealed class AckAccessRow
        {
            public Guid? AccountId        { get; set; }
            public bool  InTown           { get; set; }
            public long  ServerTimeUnixMs { get; set; }
        }

        private sealed class TradeRow
        {
            public Guid ListingId { get; set; }
            public long Gross     { get; set; }
            public long Fee       { get; set; }
            public long Net       { get; set; }
        }
    }
}
