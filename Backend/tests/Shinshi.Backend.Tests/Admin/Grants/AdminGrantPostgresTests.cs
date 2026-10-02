// System
using System;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin.Grants;

namespace Shinshi.Backend.Tests.Admin.Grants
{
    /// <summary> 실제 PostgreSQL에서 지급 작성, 실행 1회 적용, 미수령 취소, 개인 조정의 클램프와 감사, 원장 대사를 검사한다 </summary>
    public sealed class AdminGrantPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long CURRENCY        = 100;
        private const long RESERVED        = 30;
        private const long ITEM_QUANTITY   = 5;
        private const long ITEM_RESERVED   = 2;
        private const long OVER_DEBIT      = 1000;
        private const int  TARGET_ACCOUNTS = 3;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Create_ReplayConflictAndMissingAccount_FollowOperationRules()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            string operationId = GrantTestSupport.NewId();
            AdminGrantCreateRequest request = GrantTestSupport.CreateRequest(operationId, new[] { account },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY));

            OperationResult created = await support.CreateGrantAsync(request);
            OperationResult replay = await support.CreateGrantAsync(request);
            OperationResult conflict = await support.CreateGrantAsync(GrantTestSupport.CreateRequest(operationId,
                new[] { account }, GrantTestSupport.Wallet(AdminGrantCodes.ASSET_TICKETS, CURRENCY)));
            OperationResult missing = await support.CreateGrantAsync(GrantTestSupport.CreateRequest(
                GrantTestSupport.NewId(), new[] { account, Guid.NewGuid() },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY)));

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, created.Status);
            Assert.Equal(created.ReceiptId, replay.ReceiptId);
            Assert.Equal(created.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND, missing.ReasonCode);
            Assert.Equal(1L, await support.CountAsync("SELECT count(*) FROM admin_grants;"));
            Assert.Equal(1L, await support.CountAsync(
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action AND target_id = @Target;",
                new { Action = AdminGrantAuditActions.GRANT_CREATE, Target = created.ReceiptId }));
            AdminGrantRecord? record = await support.Grants.ReadAsync(Guid.Parse(created.ReceiptId));
            Assert.NotNull(record);
            Assert.Equal(AdminGrantCodes.STATE_DRAFT, record.State);
            Assert.Equal(AdminGrantCodes.DEFAULT_CLAIM_DAYS, record.ClaimDays);
            Assert.Equal(0, record.DeliveredCount);
        }

        [PostgresFact]
        public async Task Execute_SameAndDifferentOperation_DeliversOneInboxPerTargetOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid[] accounts = new Guid[TARGET_ACCOUNTS];
            for (int index = 0; index < TARGET_ACCOUNTS; ++index)
            {
                accounts[index] = await support.CreateAccountAsync();
            }
            OperationResult created = await support.CreateGrantAsync(GrantTestSupport.CreateRequest(
                GrantTestSupport.NewId(), accounts, GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY)));
            Guid grantId = Guid.Parse(created.ReceiptId);
            string executeId = GrantTestSupport.NewId();

            OperationResult first = await support.ExecuteGrantAsync(grantId, executeId);
            OperationResult replay = await support.ExecuteGrantAsync(grantId, executeId);
            OperationResult second = await support.ExecuteGrantAsync(grantId, GrantTestSupport.NewId());
            OperationResult unknown = await support.ExecuteGrantAsync(Guid.NewGuid(), GrantTestSupport.NewId());

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(first.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE, second.ReasonCode);
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND, unknown.ReasonCode);
            Assert.Equal((long)TARGET_ACCOUNTS, await support.CountAsync(
                "SELECT count(DISTINCT account_id) FROM reward_inbox WHERE grant_id = @GrantId;", new { GrantId = grantId }));
            Assert.Equal((long)TARGET_ACCOUNTS, await support.CountAsync(
                """
                SELECT count(*) FROM reward_inbox i JOIN admin_grants g ON g.grant_id = i.grant_id
                WHERE i.grant_id = @GrantId AND i.expires_at = i.created_at + make_interval(days => g.claim_days)
                    AND i.source_kind = 'AdminGrant' AND i.source_ref = @GrantText;
                """, new { GrantId = grantId, GrantText = grantId.ToString("D") }));
            Assert.Equal(1L, await support.CountAsync(
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND target_id = @Target AND self_approved
                    AND (after_state ->> 'delivered')::int = @Delivered;
                """, new { Action = AdminGrantAuditActions.GRANT_EXECUTE, Target = grantId.ToString("D"),
                    Delivered = TARGET_ACCOUNTS }));
            AdminGrantRecord? record = await support.Grants.ReadAsync(grantId);
            Assert.Equal(AdminGrantCodes.STATE_EXECUTED, record!.State);
            Assert.Equal(TARGET_ACCOUNTS, record.OpenCount);
            Assert.NotNull(record.ExecutedAt);
        }

        [PostgresFact]
        public async Task Cancel_AfterOneClaim_CancelsOnlyOpenInboxAndKeepsClaimed()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid claimer = await support.CreateAccountAsync();
            Guid waiting = await support.CreateAccountAsync();
            Guid grantId = await support.GrantAndExecuteAsync(new[] { claimer, waiting },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY));
            Guid claimedInbox = await support.SingleInboxIdAsync(claimer, grantId);
            Guid waitingInbox = await support.SingleInboxIdAsync(waiting, grantId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await support.ClaimAsync(claimer, GrantTestSupport.NewId(), claimedInbox, null)).Status);

            OperationResult cancelled = await support.CancelGrantAsync(grantId, GrantTestSupport.NewId());
            OperationResult again = await support.CancelGrantAsync(grantId, GrantTestSupport.NewId());
            OperationResult lateClaim = await support.ClaimAsync(waiting, GrantTestSupport.NewId(), waitingInbox, null);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, cancelled.Status);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE, again.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INBOX_CANCELLED, lateClaim.ReasonCode);
            AdminGrantRecord? record = await support.Grants.ReadAsync(grantId);
            Assert.Equal(AdminGrantCodes.STATE_CANCELLED, record!.State);
            Assert.Equal(1, record.ClaimedCount);
            Assert.Equal(1, record.CancelledCount);
            Assert.Equal(0, record.OpenCount);
            Assert.Equal(1L, await support.CountAsync(
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND (after_state ->> 'claimed')::int = 1
                    AND (after_state ->> 'cancelledNow')::int = 1 AND before_state ->> 'state' = 'Executed';
                """, new { Action = AdminGrantAuditActions.GRANT_CANCEL }));
            Assert.Equal(CURRENCY, await support.CountAsync(
                "SELECT currency FROM account_wallets WHERE account_id = @AccountId;", new { AccountId = claimer }));
            await support.AssertLedgerReconciledAsync();
        }

        [PostgresFact]
        public async Task Cancel_Draft_BecomesCancelledAndCannotExecute()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            OperationResult created = await support.CreateGrantAsync(GrantTestSupport.CreateRequest(
                GrantTestSupport.NewId(), new[] { account }, GrantTestSupport.Wallet(AdminGrantCodes.ASSET_TICKETS, 1)));
            Guid grantId = Guid.Parse(created.ReceiptId);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await support.CancelGrantAsync(grantId, GrantTestSupport.NewId())).Status);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE,
                (await support.ExecuteGrantAsync(grantId, GrantTestSupport.NewId())).ReasonCode);
            Assert.Equal(0L, await support.CountAsync("SELECT count(*) FROM reward_inbox;"));
        }

        [PostgresFact]
        public async Task Adjust_NegativeBeyondAvailable_ClampsKeepsReservedAndRecordsShortfall()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await support.AdjustAsync(account,
                Adjustment(AdminGrantCodes.ASSET_CURRENCY, string.Empty, string.Empty, null, CURRENCY))).Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await support.AdjustAsync(account,
                Adjustment(AdminGrantCodes.ASSET_ACCOUNT_ITEM, GrantTestSupport.ITEM_ID, GrantTestSupport.ACCOUNT_BIND,
                    null, ITEM_QUANTITY))).Status);
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                // 예약은 잔액을 바꾸지 않으므로 원장 없이 예약 열만 올린다
                await connection.ExecuteAsync(
                    """
                    UPDATE account_wallets SET currency_reserved = @Reserved WHERE account_id = @AccountId;
                    UPDATE account_items SET reserved = @ItemReserved WHERE account_id = @AccountId;
                    """, new { Reserved = RESERVED, ItemReserved = ITEM_RESERVED, AccountId = account });
            }

            AdminAdjustmentRequest debit = Adjustment(AdminGrantCodes.ASSET_CURRENCY, string.Empty, string.Empty, null,
                -OVER_DEBIT);
            OperationResult currency = await support.AdjustAsync(account, debit);
            OperationResult replay = await support.AdjustAsync(account, debit);
            OperationResult item = await support.AdjustAsync(account, Adjustment(AdminGrantCodes.ASSET_ACCOUNT_ITEM,
                GrantTestSupport.ITEM_ID, GrantTestSupport.ACCOUNT_BIND, null, -OVER_DEBIT));
            OperationResult empty = await support.AdjustAsync(account, Adjustment(AdminGrantCodes.ASSET_TICKETS,
                string.Empty, string.Empty, null, -OVER_DEBIT));

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, currency.Status);
            Assert.Equal(currency.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            AdminAdjustmentResult? currencyResult = await support.Adjustments.ReadResultAsync(support.Admin.AdminId,
                Guid.Parse(debit.OperationId));
            Assert.Equal(RESERVED - CURRENCY, currencyResult!.AppliedDelta);
            Assert.Equal(OVER_DEBIT - (CURRENCY - RESERVED), currencyResult.Shortfall);
            Assert.Equal(RESERVED, currencyResult.BalanceAfter);
            Assert.Equal(RESERVED, currencyResult.Reserved);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, item.Status);
            Assert.Equal(ITEM_RESERVED, await support.CountAsync(
                "SELECT quantity FROM account_items WHERE account_id = @AccountId;", new { AccountId = account }));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, empty.Status);
            Assert.Equal(2L, await support.CountAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason AND actor_kind = 'Admin' AND source_type = @Source;",
                new { Reason = ContractCodes.EconomyReason.ADMIN_REVOKE, Source = AdminGrantAuditActions.ADJUSTMENT_SOURCE_TYPE }));
            Assert.Equal(2L, await support.CountAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason;",
                new { Reason = ContractCodes.EconomyReason.ADMIN_GRANT }));
            Assert.Equal(5L, await support.CountAsync(
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;",
                new { Action = AdminGrantAuditActions.ACCOUNT_ADJUST }));
            Assert.Equal(1L, await support.CountAsync(
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND (after_state ->> 'appliedDelta')::bigint = 0
                    AND (after_state ->> 'shortfall')::bigint = @Shortfall AND after_state ->> 'ledgerId' IS NULL;
                """, new { Action = AdminGrantAuditActions.ACCOUNT_ADJUST, Shortfall = OVER_DEBIT }));
            await support.AssertLedgerReconciledAsync();
        }

        [PostgresFact]
        public async Task Adjust_CharacterAssetOfOtherAccountOrMissingAccount_IsNotFound()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid owner = await support.CreateAccountAsync();
            Guid other = await support.CreateAccountAsync();
            Guid otherCharacter = await support.CreateCharacterAsync(other);
            Guid ownCharacter = await support.CreateCharacterAsync(owner);

            OperationResult foreign = await support.AdjustAsync(owner, Adjustment(AdminGrantCodes.ASSET_ESSENCE,
                string.Empty, string.Empty, otherCharacter.ToString("D"), CURRENCY));
            OperationResult missing = await support.AdjustAsync(Guid.NewGuid(), Adjustment(AdminGrantCodes.ASSET_CURRENCY,
                string.Empty, string.Empty, null, CURRENCY));
            OperationResult own = await support.AdjustAsync(owner, Adjustment(AdminGrantCodes.ASSET_SHARDS,
                string.Empty, string.Empty, ownCharacter.ToString("D"), CURRENCY));

            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND, foreign.ReasonCode);
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND, missing.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, own.Status);
            Assert.Equal(CURRENCY, await support.CountAsync(
                "SELECT shards FROM characters WHERE character_id = @CharacterId;", new { CharacterId = ownCharacter }));
            Assert.Equal(1L, await support.CountAsync("SELECT count(*) FROM admin_audit_log WHERE action = @Action;",
                new { Action = AdminGrantAuditActions.ACCOUNT_ADJUST }));
            await support.AssertLedgerReconciledAsync();
        }

        [Fact]
        public void Validation_RejectsMalformedRewardsTargetsAndAdjustments()
        {
            Guid account = Guid.NewGuid();
            AdminGrantReward currency = GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY);
            Assert.NotNull(AdminGrantMapper.ToDraft(Guid.NewGuid(),
                GrantTestSupport.CreateRequest(GrantTestSupport.NewId(), new[] { account }, currency,
                    GrantTestSupport.Item(GrantTestSupport.TALISMAN_ID, 1))));
            Assert.Null(AdminGrantMapper.ToDraft(Guid.NewGuid(),
                GrantTestSupport.CreateRequest(GrantTestSupport.NewId(), new[] { account, account }, currency)));
            Assert.Null(AdminGrantMapper.ToDraft(Guid.NewGuid(),
                GrantTestSupport.CreateRequest(GrantTestSupport.NewId(), new[] { account }, currency, currency)));
            Assert.Null(AdminGrantMapper.ToDraft(Guid.NewGuid(),
                GrantTestSupport.CreateRequest(GrantTestSupport.NewId(), new[] { account },
                    GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, AdminGrantCodes.MAX_REWARD_QUANTITY + 1))));
            Assert.Null(AdminGrantMapper.ToDraft(Guid.NewGuid(),
                GrantTestSupport.CreateRequest(GrantTestSupport.NewId(), new[] { account },
                    GrantTestSupport.Item("item_yeopjeon", 1))));
            Assert.Null(AdminGrantMapper.ToDraft(Guid.NewGuid(),
                GrantTestSupport.CreateRequest(GrantTestSupport.NewId(), new[] { account },
                    new AdminGrantReward(AdminGrantCodes.ASSET_CURRENCY, GrantTestSupport.ITEM_ID, string.Empty, 1))));
            Assert.Null(AdminGrantMapper.ToDraft(Guid.NewGuid(), new AdminGrantCreateRequest(
                GrantTestSupport.NewId(), GrantTestSupport.REASON, new[] { account.ToString("D") },
                new[] { currency }, null, AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION, null)));
            Assert.Null(AdminGrantMapper.ToDraft(Guid.NewGuid(), new AdminGrantCreateRequest(
                GrantTestSupport.NewId(), GrantTestSupport.REASON, new[] { account.ToString("D") },
                new[] { currency }, AdminGrantCodes.MAX_CLAIM_DAYS + 1, null, null)));
            Assert.Null(AdminAdjustment.TryCreate(account, AdminGrantCodes.ASSET_CURRENCY, null, null, null, 0));
            Assert.Null(AdminAdjustment.TryCreate(account, AdminGrantCodes.ASSET_ESSENCE, null, null, null, 1));
            Assert.Null(AdminAdjustment.TryCreate(account, AdminGrantCodes.ASSET_CURRENCY, null, null, Guid.NewGuid(), 1));
            Assert.Null(AdminAdjustment.TryCreate(account, AdminGrantCodes.ASSET_CURRENCY, null, null, null, long.MinValue));
        }

        private static AdminAdjustmentRequest Adjustment(string assetKind, string assetId, string bindingKind,
            string? characterId, long delta)
        {
            return new AdminAdjustmentRequest(GrantTestSupport.NewId(), GrantTestSupport.REASON, assetKind, assetId,
                bindingKind, characterId, delta);
        }
    }
}
