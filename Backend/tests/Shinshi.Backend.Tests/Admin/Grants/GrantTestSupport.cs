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
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Admin.Grants;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests.Admin.Grants
{
    /// <summary> 지급, 우편함 테스트 공용 도구. 운영자 하나와 저장소, 실행기를 실제 PostgreSQL에 묶는다 </summary>
    internal sealed class GrantTestSupport
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OWNER_NAME     = "owner.one";
        public const string REASON         = "compensate outage";
        public const string ITEM_ID        = "item_herb";
        public const string TALISMAN_ID    = "T01-1A";
        public const string ACCOUNT_BIND   = "Account";
        public const int    WARRIOR_CLASS  = 0;
        public const int    RECONCILE_PAGE = 1000;

        /****************************************
        *              Properties
        ****************************************/

        public NpgsqlDataSource DataSource { get; }
        public AdminIdentity Admin { get; private set; } = null!;
        public AdminOperationExecutor Executor { get; }
        public PostgresAdminGrantRepository Grants { get; }
        public PostgresAdminAdjustmentRepository Adjustments { get; }
        public PostgresInboxRepository Inbox { get; }

        /****************************************
        *                Methods
        ****************************************/

        private GrantTestSupport(NpgsqlDataSource dataSource)
        {
            DataSource = dataSource;
            PostgresAdminAuditWriter audit = new PostgresAdminAuditWriter(dataSource);
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(new PostgresOperationStore(dataSource));
            PostgresEconomyLedgerWriter ledger = new PostgresEconomyLedgerWriter();
            Executor    = new AdminOperationExecutor(executor, audit, new PostgresAdminOperationClaimRepository(dataSource));
            Grants      = new PostgresAdminGrantRepository(dataSource);
            Adjustments = new PostgresAdminAdjustmentRepository(dataSource, ledger);
            Inbox       = new PostgresInboxRepository(dataSource, executor, ledger);
        }

        /// <summary> Owner 하나를 부트스트랩하고 그 운영자로 실행하는 도구를 만든다 </summary>
        public static async Task<GrantTestSupport> CreateAsync(NpgsqlDataSource dataSource)
        {
            GrantTestSupport support = new GrantTestSupport(dataSource);
            AdminSecretProtector protector = AdminSecretProtector.FromBase64(AdminTestSupport.CreateKeyBase64());
            AdminNewOperator owner = AdminTestSupport.CreateOperator(protector, OWNER_NAME, EAdminRole.Owner,
                AdminTotp.CreateSecret(), null);
            Assert.True(await new PostgresAdminUserRepository(dataSource, new PostgresAdminAuditWriter(dataSource))
                .BootstrapOwnerAsync(owner));
            support.Admin = new AdminIdentity(owner.AdminId, EAdminRole.Owner, Guid.NewGuid());
            return support;
        }

        public async Task<Guid> CreateAccountAsync()
        {
            Guid accountId = Guid.NewGuid();
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                new { AccountId = accountId });
            return accountId;
        }

        public async Task<Guid> CreateCharacterAsync(Guid accountId)
        {
            Guid characterId = Guid.NewGuid();
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                "INSERT INTO characters (character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, @Class);",
                new { CharacterId = characterId, AccountId = accountId, Class = WARRIOR_CLASS });
            return characterId;
        }

        public Task<OperationResult> CreateGrantAsync(AdminGrantCreateRequest request)
        {
            AdminGrantDraft draft = AdminGrantMapper.ToDraft(Guid.NewGuid(), request)
                ?? throw new InvalidOperationException("Test grant request is invalid.");
            return Executor.ExecuteAsync(Admin, null, request, AdminGrantAuditActions.GRANT_CREATE, string.Empty,
                (context, token) => Grants.CreateAsync(context, draft, token));
        }

        public Task<OperationResult> ExecuteGrantAsync(Guid grantId, string operationId)
        {
            return Executor.ExecuteAsync(Admin, null, new AdminGrantActionRequest(operationId, REASON),
                AdminGrantAuditActions.GRANT_EXECUTE, grantId.ToString("D"),
                (context, token) => Grants.ExecuteAsync(context, grantId, token));
        }

        public Task<OperationResult> CancelGrantAsync(Guid grantId, string operationId)
        {
            return Executor.ExecuteAsync(Admin, null, new AdminGrantActionRequest(operationId, REASON),
                AdminGrantAuditActions.GRANT_CANCEL, grantId.ToString("D"),
                (context, token) => Grants.CancelAsync(context, grantId, token));
        }

        public Task<OperationResult> AdjustAsync(Guid accountId, AdminAdjustmentRequest request)
        {
            Guid? characterId = string.IsNullOrEmpty(request.CharacterId) ? null : Guid.Parse(request.CharacterId);
            AdminAdjustment adjustment = AdminAdjustment.TryCreate(accountId, request.AssetKind, request.AssetId,
                request.BindingKind, characterId, request.Delta)
                ?? throw new InvalidOperationException("Test adjustment request is invalid.");
            return Executor.ExecuteAsync(Admin, null, request, AdminGrantAuditActions.ACCOUNT_ADJUST,
                accountId.ToString("D"), (context, token) => Adjustments.AdjustAsync(context, adjustment, token));
        }

        /// <summary> 지급을 만들고 실행해 지급 ID를 돌려준다 </summary>
        public async Task<Guid> GrantAndExecuteAsync(Guid[] accounts, params AdminGrantReward[] rewards)
        {
            OperationResult created = await CreateGrantAsync(CreateRequest(NewId(), accounts, rewards));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, created.Status);
            Guid grantId = Guid.Parse(created.ReceiptId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await ExecuteGrantAsync(grantId, NewId())).Status);
            return grantId;
        }

        public async Task<Guid> SingleInboxIdAsync(Guid accountId, Guid grantId)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<Guid>(
                "SELECT inbox_id FROM reward_inbox WHERE account_id = @AccountId AND grant_id = @GrantId;",
                new { AccountId = accountId, GrantId = grantId });
        }

        public Task<OperationResult> ClaimAsync(Guid accountId, string operationId, Guid inboxId, Guid? characterId)
        {
            return Inbox.ClaimAsync(accountId, Guid.Parse(operationId), inboxId, characterId);
        }

        public async Task<long> CountAsync(string sql, object? parameters = null)
        {
            return await AdminTestSupport.CountAsync(DataSource, sql, parameters);
        }

        public async Task AssertLedgerReconciledAsync()
        {
            Assert.Empty(await new EconomyLedgerReconciler(DataSource).FindMismatchesAsync(RECONCILE_PAGE));
        }

        public static AdminGrantCreateRequest CreateRequest(string operationId, Guid[] accounts,
            params AdminGrantReward[] rewards)
        {
            return new AdminGrantCreateRequest(operationId, REASON, Array.ConvertAll(accounts, id => id.ToString("D")),
                rewards, null, null, null);
        }

        public static AdminGrantReward Wallet(string assetKind, long quantity)
        {
            return new AdminGrantReward(assetKind, string.Empty, string.Empty, quantity);
        }

        public static AdminGrantReward Item(string definitionId, long quantity)
        {
            return new AdminGrantReward(AdminGrantCodes.ASSET_ACCOUNT_ITEM, definitionId, ACCOUNT_BIND, quantity);
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("D");
        }
    }
}
