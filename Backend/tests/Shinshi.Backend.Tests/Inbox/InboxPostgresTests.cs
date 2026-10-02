// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Admin.Grants;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Inbox;

namespace Shinshi.Backend.Tests.Inbox
{
    /// <summary> 실제 PostgreSQL에서 우편 목록, 수령의 잔액, 원장, 멱등, 동시 수령, 만료, 남의 우편, 캐릭터 요구를 검사한다 </summary>
    public sealed class InboxPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long CURRENCY     = 120;
        private const long TICKETS      = 2;
        private const long ESSENCE      = 40;
        private const long SHARDS       = 7;
        private const long HERBS        = 3;
        private const long CHARMS       = 1;
        private const int  REWARD_LINES = 6;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Claim_AllAssetKinds_IncreasesBalancesWritesLedgerAndIsIdempotent()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            Guid character = await support.CreateCharacterAsync(account);
            Guid otherCharacter = await support.CreateCharacterAsync(await support.CreateAccountAsync());
            Guid grantId = await support.GrantAndExecuteAsync(new[] { account },
                GrantTestSupport.Item(GrantTestSupport.ITEM_ID, HERBS),
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_SHARDS, SHARDS),
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY),
                GrantTestSupport.Item(GrantTestSupport.TALISMAN_ID, CHARMS),
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_ESSENCE, ESSENCE),
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_TICKETS, TICKETS));
            Guid inboxId = await support.SingleInboxIdAsync(account, grantId);

            InboxListing listing = await support.Inbox.ListAsync(account);
            InboxRecord entry = Assert.Single(listing.Entries);
            Assert.Equal(inboxId, entry.InboxId);
            Assert.Equal(REWARD_LINES, entry.Rewards.Count);
            Assert.True(RewardLines.RequiresCharacter(entry.Rewards));
            Assert.True(entry.ExpiresAt > listing.ServerTime);

            OperationResult noCharacter = await support.ClaimAsync(account, GrantTestSupport.NewId(), inboxId, null);
            OperationResult foreignCharacter = await support.ClaimAsync(account, GrantTestSupport.NewId(), inboxId,
                otherCharacter);
            string operationId = GrantTestSupport.NewId();
            OperationResult claimed = await support.ClaimAsync(account, operationId, inboxId, character);
            OperationResult replay = await support.ClaimAsync(account, operationId, inboxId, character);
            OperationResult conflict = await support.ClaimAsync(account, operationId, inboxId, null);
            OperationResult again = await support.ClaimAsync(account, GrantTestSupport.NewId(), inboxId, character);

            Assert.Equal(ContractCodes.RejectReason.INBOX_CHARACTER_REQUIRED, noCharacter.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INBOX_CHARACTER_REQUIRED, foreignCharacter.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, claimed.Status);
            Assert.Equal(inboxId.ToString("D"), claimed.ReceiptId);
            Assert.Equal(claimed.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replay.Status);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INBOX_ALREADY_CLAIMED, again.ReasonCode);
            Assert.Empty((await support.Inbox.ListAsync(account)).Entries);

            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            (long currency, long tickets) = await connection.QuerySingleAsync<(long, long)>(
                "SELECT currency, tickets FROM account_wallets WHERE account_id = @AccountId;", new { AccountId = account });
            (long essence, long shards) = await connection.QuerySingleAsync<(long, long)>(
                "SELECT essence, shards FROM characters WHERE character_id = @CharacterId;", new { CharacterId = character });
            Assert.Equal(CURRENCY, currency);
            Assert.Equal(TICKETS, tickets);
            Assert.Equal(ESSENCE, essence);
            Assert.Equal(SHARDS, shards);
            Assert.Equal(HERBS + CHARMS, await support.CountAsync(
                "SELECT sum(quantity)::bigint FROM account_items WHERE account_id = @AccountId;", new { AccountId = account }));
            Assert.Equal((long)REWARD_LINES, await support.CountAsync(
                """
                SELECT count(*) FROM economy_ledger
                WHERE account_id = @AccountId AND reason = @Reason AND actor_kind = 'Player' AND actor_id = @AccountId
                    AND source_type = @Source AND source_id = @InboxId AND operation_id = @OperationId;
                """, new
                {
                    AccountId = account, Reason = ContractCodes.EconomyReason.INBOX_CLAIM,
                    Source = PostgresInboxRepository.LEDGER_SOURCE_TYPE, InboxId = inboxId.ToString("D"),
                    OperationId = Guid.Parse(operationId)
                }));
            Assert.Equal(1L, await support.CountAsync(
                """
                SELECT count(*) FROM reward_inbox
                WHERE inbox_id = @InboxId AND claimed_at IS NOT NULL AND claimed_character_id = @CharacterId
                    AND claim_operation_id = @OperationId;
                """, new { InboxId = inboxId, CharacterId = character, OperationId = Guid.Parse(operationId) }));
            await support.AssertLedgerReconciledAsync();
        }

        [PostgresFact]
        public async Task Claim_ConcurrentDifferentOperations_OnlyOneSucceeds()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            Guid grantId = await support.GrantAndExecuteAsync(new[] { account },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY));
            Guid inboxId = await support.SingleInboxIdAsync(account, grantId);

            OperationResult[] results = await Task.WhenAll(
                support.ClaimAsync(account, GrantTestSupport.NewId(), inboxId, null),
                support.ClaimAsync(account, GrantTestSupport.NewId(), inboxId, null));

            Assert.Equal(1, results.Count(result => result.Status == ContractCodes.OperationStatus.COMMITTED));
            Assert.Equal(1, results.Count(result => result.ReasonCode == ContractCodes.RejectReason.INBOX_ALREADY_CLAIMED));
            Assert.Equal(CURRENCY, await support.CountAsync(
                "SELECT currency FROM account_wallets WHERE account_id = @AccountId;", new { AccountId = account }));
            Assert.Equal(1L, await support.CountAsync("SELECT count(*) FROM economy_ledger;"));
            await support.AssertLedgerReconciledAsync();
        }

        [PostgresFact]
        public async Task Claim_ExpiredOrOtherAccountsInbox_IsRejectedAndHiddenFromList()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            Guid stranger = await support.CreateAccountAsync();
            Guid expiredGrant = await support.GrantAndExecuteAsync(new[] { account },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY));
            Guid openGrant = await support.GrantAndExecuteAsync(new[] { account },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_TICKETS, TICKETS));
            Guid expiredInbox = await support.SingleInboxIdAsync(account, expiredGrant);
            Guid openInbox = await support.SingleInboxIdAsync(account, openGrant);
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                // 기한 CHECK(expires_at > created_at)를 지키며 이미 지난 우편으로 만든다
                await connection.ExecuteAsync(
                    """
                    UPDATE reward_inbox
                    SET created_at = clock_timestamp() - interval '2 days', expires_at = clock_timestamp() - interval '1 day'
                    WHERE inbox_id = @InboxId;
                    """, new { InboxId = expiredInbox });
            }

            OperationResult expired = await support.ClaimAsync(account, GrantTestSupport.NewId(), expiredInbox, null);
            OperationResult foreign = await support.ClaimAsync(stranger, GrantTestSupport.NewId(), openInbox, null);
            OperationResult unknown = await support.ClaimAsync(account, GrantTestSupport.NewId(), Guid.NewGuid(), null);

            Assert.Equal(ContractCodes.RejectReason.INBOX_EXPIRED, expired.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INBOX_NOT_FOUND, foreign.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INBOX_NOT_FOUND, unknown.ReasonCode);
            Assert.Equal(openInbox, Assert.Single((await support.Inbox.ListAsync(account)).Entries).InboxId);
            Assert.Empty((await support.Inbox.ListAsync(stranger)).Entries);
            Assert.Equal(0L, await support.CountAsync("SELECT count(*) FROM economy_ledger;"));
            Assert.Equal(0L, await support.CountAsync("SELECT count(*) FROM account_wallets;"));
        }
    }
}
