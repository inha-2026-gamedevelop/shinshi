// System
using System;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Admin.Grants;
using Shinshi.Data.Inbox;

namespace Shinshi.Backend.Tests.Admin.Grants
{
    /// <summary> 실제 PostgreSQL에서 보정 지급의 실행 역할과 중복 발행 방지, 작성 정책을 벗어난 저장 보상의 읽기,
    /// 우편 목록의 만료 순 정렬을 검사한다 </summary>
    public sealed class AdminGrantPolicyPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   CURRENCY    = 25;
        private const int    LONG_DAYS   = 30;
        private const int    SHORT_DAYS  = 1;
        private const string SESSION_REF = "session-recovery-ref";
        private const string LEGACY_ITEM = "legacy_removed_item";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 잠근 지급 행 기준으로 Operator의 보정 지급(대상 1명) 실행은 FORBIDDEN_ROLE이고 우편을 넣지 않는다 </summary>
        [PostgresFact]
        public async Task Execute_RecoveryCompensationByOperator_IsForbiddenOnLockedRow()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            Guid grantId = await CreateCompensationAsync(support, account, SESSION_REF);
            AdminIdentity operatorIdentity = new AdminIdentity(support.Admin.AdminId, EAdminRole.Operator, Guid.NewGuid());

            OperationResult refused = await support.Executor.ExecuteAsync(operatorIdentity, null,
                new AdminGrantActionRequest(GrantTestSupport.NewId(), GrantTestSupport.REASON),
                AdminGrantAuditActions.GRANT_EXECUTE, grantId.ToString("D"),
                (context, token) => support.Grants.ExecuteAsync(context, grantId, token));
            OperationResult executed = await support.ExecuteGrantAsync(grantId, GrantTestSupport.NewId());

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, refused.Status);
            Assert.Equal(AdminContractCodes.RejectReason.FORBIDDEN_ROLE, refused.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, executed.Status);
            Assert.True(AdminGrantPolicy.RequiresAdminToExecute(1, AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION));
            Assert.False(AdminGrantPolicy.RequiresAdminToExecute(1, AdminGrantCodes.SOURCE_ADMIN_GRANT));
            Assert.Equal(1L, await support.CountAsync(
                "SELECT count(*) FROM reward_inbox WHERE grant_id = @GrantId;", new { GrantId = grantId }));
        }

        /// <summary> 같은 sourceRef의 두 번째 보정 지급 작성은 INVALID_STATE이고, 첫 지급을 취소한 뒤에는 다시 작성된다.
        /// 일반 지급은 같은 sourceRef를 막지 않는다 </summary>
        [PostgresFact]
        public async Task Create_DuplicateRecoveryCompensation_RejectedUntilCancelled()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            Guid first = await CreateCompensationAsync(support, account, SESSION_REF);

            OperationResult duplicate = await support.CreateGrantAsync(CompensationRequest(account, SESSION_REF,
                AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION));
            OperationResult plain = await support.CreateGrantAsync(CompensationRequest(account, SESSION_REF,
                AdminGrantCodes.SOURCE_ADMIN_GRANT));
            OperationResult cancelled = await support.CancelGrantAsync(first, GrantTestSupport.NewId());
            OperationResult reissued = await support.CreateGrantAsync(CompensationRequest(account, SESSION_REF,
                AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION));

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, duplicate.Status);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE, duplicate.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, plain.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, cancelled.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reissued.Status);
            Assert.Equal(2L, await support.CountAsync(
                "SELECT count(*) FROM admin_grants WHERE source_kind = @Kind AND source_ref = @Ref;",
                new { Kind = AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION, Ref = SESSION_REF }));
            // 거절된 작성은 감사 행 없이 결과만 저장된다
            Assert.Equal(3L, await support.CountAsync(
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;",
                new { Action = AdminGrantAuditActions.GRANT_CREATE }));
        }

        /// <summary> 작성 정책(수량 상한, 허용 아이템)을 벗어난 저장 보상도 목록, 지급 조회, 수령이 실패하지 않는다.
        /// 구조가 깨진 행만 목록에서 빠지고 ID로 알려진다 </summary>
        [PostgresFact]
        public async Task StoredRewardsOutsidePolicy_ListReadAndClaimStillWork()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            Guid grantId = await support.GrantAndExecuteAsync(new[] { account },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY));
            Guid corruptGrant = await support.GrantAndExecuteAsync(new[] { account },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY));
            Guid inboxId = await support.SingleInboxIdAsync(account, grantId);
            Guid corruptInbox = await support.SingleInboxIdAsync(account, corruptGrant);
            long overLimit = AdminGrantCodes.MAX_REWARD_QUANTITY + 1;
            string outsidePolicy = RewardLines.Serialize(new[]
            {
                new RewardLine(AdminGrantCodes.ASSET_CURRENCY, null, null, overLimit),
                new RewardLine(AdminGrantCodes.ASSET_ACCOUNT_ITEM, LEGACY_ITEM, GrantTestSupport.ACCOUNT_BIND, 1)
            });
            string corrupt = "[{\"assetKind\":\"Currency\",\"assetId\":\"\",\"bindingKind\":\"\",\"quantity\":0}]";
            await support.CountAsync(
                """
                WITH grant_update AS (
                    UPDATE admin_grants SET rewards = CAST(@Rewards AS jsonb) WHERE grant_id = @GrantId RETURNING 1
                ), inbox_update AS (
                    UPDATE reward_inbox SET rewards = CAST(@Rewards AS jsonb) WHERE inbox_id = @InboxId RETURNING 1
                ), corrupt_update AS (
                    UPDATE reward_inbox SET rewards = CAST(@Corrupt AS jsonb) WHERE inbox_id = @CorruptInbox RETURNING 1
                )
                SELECT (SELECT count(*) FROM grant_update) + (SELECT count(*) FROM inbox_update)
                    + (SELECT count(*) FROM corrupt_update);
                """, new
                {
                    Rewards = outsidePolicy, GrantId = grantId, InboxId = inboxId, Corrupt = corrupt,
                    CorruptInbox = corruptInbox
                });
            Assert.False(RewardLines.IsValid(RewardLines.Deserialize(outsidePolicy)));

            InboxListing listing = await support.Inbox.ListAsync(account);
            AdminGrantRecord? record = await support.Grants.ReadAsync(grantId);
            OperationResult claimed = await support.ClaimAsync(account, GrantTestSupport.NewId(), inboxId, null);

            InboxRecord entry = Assert.Single(listing.Entries);
            Assert.Equal(inboxId, entry.InboxId);
            Assert.Equal(2, entry.Rewards.Count);
            Assert.Equal(corruptInbox, Assert.Single(listing.CorruptInboxIds));
            Assert.NotNull(record);
            Assert.Equal(2, record.Rewards.Count);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, claimed.Status);
            Assert.Equal(overLimit, await support.CountAsync(
                "SELECT currency FROM account_wallets WHERE account_id = @AccountId;", new { AccountId = account }));
            Assert.Equal(1L, await support.CountAsync(
                "SELECT quantity FROM account_items WHERE account_id = @AccountId AND definition_id = @Item;",
                new { AccountId = account, Item = LEGACY_ITEM }));
            Assert.Throws<InvalidOperationException>(() => RewardLines.Deserialize(corrupt));
        }

        /// <summary> 열린 우편은 곧 만료되는 순서다. 나중에 받은 짧은 기한 우편이 앞에 온다 </summary>
        [PostgresFact]
        public async Task List_OpenEntries_OrderedByExpiry()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            Guid account = await support.CreateAccountAsync();
            Guid longGrant = await CreateAndExecuteAsync(support, account, LONG_DAYS);
            Guid shortGrant = await CreateAndExecuteAsync(support, account, SHORT_DAYS);

            InboxListing listing = await support.Inbox.ListAsync(account);

            Assert.Equal(2, listing.Entries.Count);
            Assert.Equal(await support.SingleInboxIdAsync(account, shortGrant), listing.Entries[0].InboxId);
            Assert.Equal(await support.SingleInboxIdAsync(account, longGrant), listing.Entries[1].InboxId);
            Assert.True(listing.Entries[0].ExpiresAt < listing.Entries[1].ExpiresAt);
        }

        private static async Task<Guid> CreateCompensationAsync(GrantTestSupport support, Guid account, string sourceRef)
        {
            OperationResult created = await support.CreateGrantAsync(CompensationRequest(account, sourceRef,
                AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, created.Status);
            return Guid.Parse(created.ReceiptId);
        }

        private static async Task<Guid> CreateAndExecuteAsync(GrantTestSupport support, Guid account, int claimDays)
        {
            OperationResult created = await support.CreateGrantAsync(new AdminGrantCreateRequest(GrantTestSupport.NewId(),
                GrantTestSupport.REASON, new[] { account.ToString("D") },
                new[] { GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY) }, claimDays, null!, null!));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, created.Status);
            Guid grantId = Guid.Parse(created.ReceiptId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await support.ExecuteGrantAsync(grantId, GrantTestSupport.NewId())).Status);
            return grantId;
        }

        private static AdminGrantCreateRequest CompensationRequest(Guid account, string sourceRef, string sourceKind)
        {
            return new AdminGrantCreateRequest(GrantTestSupport.NewId(), GrantTestSupport.REASON,
                new[] { account.ToString("D") }, new[] { GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY) },
                null, sourceKind, sourceRef);
        }
    }
}
