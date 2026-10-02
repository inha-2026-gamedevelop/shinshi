// System
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Inbox
{
    /// <summary> DB 모드 서비스 상태에서 inbox_claim 차단이 우편 수령을 효과 전에 막고, 차단을 풀면 같은 operationId로 수령되는지
    /// 실제 PostgreSQL과 Program 파이프라인에서 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class InboxFeatureGateTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ADMIN_NAME = "gate.admin";
        private const string REASON     = "inbox gate check";
        private const long   CURRENCY   = 75;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Claim_InboxClaimDisabledThenEnabled_RejectsWithoutEffectThenClaims()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            (Guid accountId, string player) = await IssuePlayerAsync(database.DataSource, host.AuthOptions);

            // 0008 초기 행(revision 0)에서 점검을 풀고 우편 수령만 끈다
            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.INBOX_CLAIM }, 0);
            using (HttpResponseMessage state = await client.GetAsync(ApiRoutes.SERVICE_STATE))
            {
                Assert.Equal(new[] { ServiceFeatureKeys.INBOX_CLAIM },
                    (await AdminTestSupport.ReadAsync<ServiceStateResponse>(state)).DisabledFeatures);
            }

            string inboxId = await DeliverCurrencyAsync(client, database.DataSource, admin, accountId);
            string operationId = AdminApiTestHost.NewOperationId();
            InboxClaimRequest claim = new InboxClaimRequest(operationId, inboxId, null);

            using HttpResponseMessage blocked = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                ApiRoutes.INBOX_CLAIM, player, claim);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(blocked)).ReasonCode);

            // 차단은 수령만 막는다. 우편은 목록에 남고 작업 행, 원장, 잔액은 생기지 않는다
            using (HttpResponseMessage listed = await AdminApiTestHost.SendAsync(client, HttpMethod.Get, ApiRoutes.INBOX,
                player))
            {
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                Assert.Equal(inboxId,
                    Assert.Single((await AdminTestSupport.ReadAsync<InboxListResponse>(listed)).Entries).InboxId);
            }
            Assert.Equal(0L, await CountForAccountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @AccountId;", accountId));
            Assert.Equal(0L, await CountForAccountAsync(database.DataSource,
                "SELECT count(*) FROM economy_ledger WHERE account_id = @AccountId;", accountId));
            Assert.Equal(0L, await CountForAccountAsync(database.DataSource,
                "SELECT count(*) FROM reward_inbox WHERE account_id = @AccountId AND claimed_at IS NOT NULL;", accountId));

            // 이 인스턴스가 쓴 상태는 캐시 주기를 기다리지 않고 바로 적용된다
            await UpdateStateAsync(client, admin, Array.Empty<string>(), 1);
            using HttpResponseMessage claimed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                ApiRoutes.INBOX_CLAIM, player, claim);
            Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
            OperationResult result = await AdminTestSupport.ReadAsync<OperationResult>(claimed);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
            Assert.Equal(operationId, result.OperationId);
            Assert.Equal(inboxId, result.ReceiptId);
            Assert.Equal(CURRENCY, await CountForAccountAsync(database.DataSource,
                "SELECT currency FROM account_wallets WHERE account_id = @AccountId;", accountId));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM economy_ledger
                WHERE account_id = @AccountId AND reason = @Reason AND actor_kind = 'Player' AND operation_id = @OperationId;
                """, new
                {
                    AccountId = accountId, Reason = ContractCodes.EconomyReason.INBOX_CLAIM,
                    OperationId = Guid.Parse(operationId)
                }));
        }

        private static async Task UpdateStateAsync(HttpClient client, string adminToken, string[] disabledFeatures,
            long expectedRevision)
        {
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, adminToken, new AdminServiceStateUpdateRequest(
                    AdminApiTestHost.NewOperationId(), REASON, false, disabledFeatures, Array.Empty<string>(),
                    expectedRevision));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // 어드민 API로 엽전 지급을 만들고 실행해 그 계정의 우편 ID를 돌려준다
        private static async Task<string> DeliverCurrencyAsync(HttpClient client, NpgsqlDataSource dataSource,
            string adminToken, Guid accountId)
        {
            using HttpResponseMessage created = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                AdminApiRoutes.GRANTS, adminToken, new AdminGrantCreateRequest(AdminApiTestHost.NewOperationId(), REASON,
                    new[] { accountId.ToString("D") },
                    new[] { new AdminGrantReward(AdminGrantCodes.ASSET_CURRENCY, string.Empty, string.Empty, CURRENCY) },
                    null, null, null));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            string grantId = (await AdminTestSupport.ReadAsync<AdminGrantCreateResponse>(created)).GrantId;

            using HttpResponseMessage executed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.GRANT_EXECUTE_FORMAT, grantId), adminToken,
                new AdminGrantActionRequest(AdminApiTestHost.NewOperationId(), REASON));
            Assert.Equal(HttpStatusCode.OK, executed.StatusCode);

            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            Guid inboxId = await connection.QuerySingleAsync<Guid>(
                "SELECT inbox_id FROM reward_inbox WHERE account_id = @AccountId AND grant_id = @GrantId;",
                new { AccountId = accountId, GrantId = Guid.Parse(grantId) });
            return inboxId.ToString("D");
        }

        private static async Task<(Guid AccountId, string Token)> IssuePlayerAsync(NpgsqlDataSource dataSource,
            AuthTokenOptions options)
        {
            Guid accountId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                    new { AccountId = accountId });
            }

            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            IssuedAuthToken invite = await auth.IssueInviteAsync(accountId);
            return (accountId, (await auth.ExchangeInviteAsync(invite.Token)).Response!.AccessToken);
        }

        private static Task<long> CountForAccountAsync(NpgsqlDataSource dataSource, string sql, Guid accountId)
        {
            return AdminTestSupport.CountAsync(dataSource, sql, new { AccountId = accountId });
        }
    }
}
