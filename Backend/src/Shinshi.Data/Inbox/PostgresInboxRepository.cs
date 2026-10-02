// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Inbox
{
    /// <summary> 플레이어 우편함 조회와 수령. 수령은 플레이어 작업(actor = 계정)이고 잔액 증가, 원장, 수령 표시를 한 트랜잭션에 쓴다 </summary>
    public sealed class PostgresInboxRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CLAIM_OPERATION_KIND = "inbox.claim";
        public const string LEDGER_SOURCE_TYPE   = "inbox";
        public const int    MAX_LIST_ENTRIES     = 100;

        private const string PLAYER_ACTOR     = "Player";
        private const string ENTRY_KEY_PREFIX = "inbox:";

        // 우편 수령은 계정 revision 계약이 없어 0으로 확정한다
        private const long NO_REVISION = 0;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly IEconomyLedgerEntryWriter _ledger;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresInboxRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            IEconomyLedgerEntryWriter ledger)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(ledger);
            _dataSource = dataSource;
            _executor   = executor;
            _ledger     = ledger;
        }

        /// <summary> 미수령, 미취소, 미만료 우편을 곧 만료되는 순서로 최대 100개 읽는다. 만료는 DB 시각으로 가른다.
        /// 보상 JSON이 손상된 행은 목록에서 빼고 CorruptInboxIds로 알린다 </summary>
        public async Task<InboxListing> ListAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            DateTime serverTime = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", cancellationToken: cancellationToken));

            // 열린 우편이 100개를 넘으면 오래 남는 우편보다 기한이 짧은 우편을 먼저 보여 줘야 받기 전에 만료되지 않는다
            IEnumerable<InboxRow> rows = await connection.QueryAsync<InboxRow>(new CommandDefinition(
                """
                SELECT inbox_id AS "InboxId", source_kind AS "SourceKind", created_at AS "CreatedAt",
                    expires_at AS "ExpiresAt", rewards::text AS "Rewards"
                FROM reward_inbox
                WHERE account_id = @AccountId AND claimed_at IS NULL AND cancelled_at IS NULL AND expires_at > @Now
                ORDER BY expires_at, inbox_id
                LIMIT @Limit;
                """, new { AccountId = accountId, Now = serverTime, Limit = MAX_LIST_ENTRIES },
                cancellationToken: cancellationToken));
            List<InboxRecord> entries = new List<InboxRecord>();
            List<Guid> corrupt = new List<Guid>();
            foreach (InboxRow row in rows)
            {
                if (!RewardLines.TryDeserialize(row.Rewards, out List<RewardLine>? rewards))
                {
                    corrupt.Add(row.InboxId);
                    continue;
                }

                entries.Add(new InboxRecord(row.InboxId, row.SourceKind, ToUtc(row.CreatedAt), ToUtc(row.ExpiresAt),
                    rewards!));
            }

            return new InboxListing(ToUtc(serverTime), entries, corrupt);
        }

        /// <summary> 영수증 ID는 우편 ID다. 이 계정의 우편이 아니면 작업 행을 남기지 않고 INBOX_NOT_FOUND로 거절한다
        /// (우편 ID는 서버가 무작위로 만들므로 같은 ID가 나중에 생기지 않아 결과가 바뀌지 않는다) </summary>
        public async Task<OperationResult> ClaimAsync(Guid accountId, Guid operationId, Guid inboxId, Guid? characterId,
            CancellationToken cancellationToken = default)
        {
            DateTime? missingAt = await FindMissingInboxTimeAsync(accountId, inboxId, cancellationToken);
            if (missingAt.HasValue)
            {
                return new OperationResult(operationId.ToString("D"), ContractCodes.OperationStatus.REJECTED,
                    ContractCodes.RejectReason.INBOX_NOT_FOUND, NO_REVISION, ToUtc(missingAt.Value).ToUnixTimeMilliseconds(),
                    string.Empty);
            }

            OperationRequest request = new OperationRequest(accountId, operationId, CLAIM_OPERATION_KIND,
                ClaimRequestJson(operationId, inboxId, characterId));
            return await _executor.ExecuteAsync(request, (context, token) => ClaimEffectAsync(context, accountId,
                operationId, inboxId, characterId, token), cancellationToken);
        }

        /// <summary> 같은 수령 요청의 저장된 결과만 읽고 아무것도 수령하지 않는다. 처음 보는 operationId면 null이다 </summary>
        public Task<OperationResult?> FindStoredClaimAsync(Guid accountId, Guid operationId, Guid inboxId,
            Guid? characterId, CancellationToken cancellationToken = default)
        {
            OperationRequest request = new OperationRequest(accountId, operationId, CLAIM_OPERATION_KIND,
                ClaimRequestJson(operationId, inboxId, characterId));
            return _executor.FindStoredAsync(request, cancellationToken);
        }

        /// <summary> 수령 요청 해시 입력. 원문 대신 파싱한 값을 정해진 형식("D", 캐릭터 없음은 null)으로 쓴다.
        /// 원문을 쓰면 대소문자나 GUID 표기만 다른 재전송이 IdempotencyConflict가 된다 </summary>
        public static string ClaimRequestJson(Guid operationId, Guid inboxId, Guid? characterId)
        {
            return JsonConvert.SerializeObject(new
            {
                operationId = operationId.ToString("D"),
                inboxId = inboxId.ToString("D"),
                characterId = characterId?.ToString("D")
            }, Formatting.None);
        }

        // 없으면 그 판정의 DB 시각, 있으면 null이다. 응답 시각을 서버 시각으로 맞추려고 같은 문장에서 읽는다
        private async Task<DateTime?> FindMissingInboxTimeAsync(Guid accountId, Guid inboxId,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleAsync<DateTime?>(new CommandDefinition(
                """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM reward_inbox WHERE inbox_id = @InboxId AND account_id = @AccountId)
                    THEN NULL ELSE clock_timestamp() END;
                """, new { InboxId = inboxId, AccountId = accountId }, cancellationToken: cancellationToken));
        }

        private async Task<OperationDecision> ClaimEffectAsync(OperationExecutionContext context, Guid accountId,
            Guid operationId, Guid inboxId, Guid? characterId, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 잠금 순서: 우편(출처 행) -> 계정 -> 지갑 -> 캐릭터 -> 아이템. 정산의 세션 -> 계정 순서와 같은 방향이다.
            // 지급 취소는 우편 행만 잠그고 계정을 잠그지 않으므로 수령과 교착하지 않는다
            ClaimRow? inbox = await connection.QuerySingleOrDefaultAsync<ClaimRow>(new CommandDefinition(
                """
                SELECT rewards::text AS "Rewards", claimed_at IS NOT NULL AS "Claimed",
                    cancelled_at IS NOT NULL AS "Cancelled", expires_at <= clock_timestamp() AS "Expired"
                FROM reward_inbox
                WHERE inbox_id = @InboxId AND account_id = @AccountId
                FOR UPDATE;
                """, new { InboxId = inboxId, AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            if (inbox == null)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INBOX_NOT_FOUND);
            }
            if (inbox.Claimed)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INBOX_ALREADY_CLAIMED);
            }
            if (inbox.Cancelled)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INBOX_CANCELLED);
            }
            if (inbox.Expired)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INBOX_EXPIRED);
            }

            List<RewardLine> lines = RewardLines.Deserialize(inbox.Rewards);
            bool needsCharacter = RewardLines.RequiresCharacter(lines);
            if (needsCharacter && (characterId == null))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INBOX_CHARACTER_REQUIRED);
            }

            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken) != 1)
            {
                throw new InvalidOperationException("Inbox owner account is missing.");
            }

            // 캐릭터 소유는 바뀌지 않으므로 여기서는 잠그지 않고, 잔액 갱신이 지갑 다음 순서로 행을 잠근다
            Guid? claimCharacter = needsCharacter ? characterId : null;
            if ((claimCharacter != null) && !await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM characters WHERE character_id = @CharacterId AND account_id = @AccountId);",
                new { CharacterId = claimCharacter, AccountId = accountId }, transaction,
                cancellationToken: cancellationToken)))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INBOX_CHARACTER_REQUIRED);
            }

            string sourceId = inboxId.ToString("D");
            foreach (RewardLine line in RewardLines.OrderForUpdate(lines))
            {
                EconomyAsset asset = RewardLines.ToAsset(accountId, line.IsCharacterAsset ? claimCharacter : null,
                    line.AssetKind, line.AssetId, line.BindingKind);
                long balance = await RewardBalances.CreditAsync(connection, transaction, asset, line.Quantity,
                    cancellationToken);
                await _ledger.AppendAsync(context, new EconomyLedgerEntry(Guid.NewGuid(), ENTRY_KEY_PREFIX + line.Key,
                    asset, line.Quantity, balance, ContractCodes.EconomyReason.INBOX_CLAIM, LEDGER_SOURCE_TYPE, sourceId,
                    PLAYER_ACTOR, accountId, operationId), cancellationToken);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE reward_inbox
                SET claimed_at = clock_timestamp(), claimed_character_id = @CharacterId, claim_operation_id = @OperationId
                WHERE inbox_id = @InboxId;
                """, new { InboxId = inboxId, CharacterId = claimCharacter, OperationId = operationId }, transaction,
                cancellationToken: cancellationToken));
            return OperationDecision.Committed(NO_REVISION, sourceId);
        }

        private static DateTimeOffset ToUtc(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private sealed class InboxRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid     InboxId    { get; set; }
            public string   SourceKind { get; set; } = string.Empty;
            public DateTime CreatedAt  { get; set; }
            public DateTime ExpiresAt  { get; set; }
            public string   Rewards    { get; set; } = string.Empty;
        }

        private sealed class ClaimRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string Rewards   { get; set; } = string.Empty;
            public bool   Claimed   { get; set; }
            public bool   Cancelled { get; set; }
            public bool   Expired   { get; set; }
        }
    }
}
