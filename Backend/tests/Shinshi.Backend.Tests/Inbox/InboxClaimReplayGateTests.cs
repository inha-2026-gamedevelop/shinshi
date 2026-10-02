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
    /// <summary> inbox_claim 차단 중에도 이미 수령한 operationId 재전송은 저장된 처음 결과를 받고, 새 수령은 작업 행 없이
    /// 거절되는지 실제 PostgreSQL과 Program 파이프라인에서 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class InboxClaimReplayGateTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ADMIN_NAME = "replay.gate.admin";
        private const string REASON     = "inbox replay check";
        private const long   CURRENCY   = 40;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Claim_StoredClaimWhileDisabled_ReplaysFirstResultAndRejectsNewClaim()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            (Guid accountId, string player) = await IssuePlayerAsync(database.DataSource, host.AuthOptions);
            await UpdateStateAsync(client, admin, Array.Empty<string>(), 0);
            string firstInbox = await DeliverCurrencyAsync(client, database.DataSource, admin, accountId);
            string secondInbox = await DeliverCurrencyAsync(client, database.DataSource, admin, accountId);
            InboxClaimRequest first = new InboxClaimRequest(AdminApiTestHost.NewOperationId(), firstInbox, null);
            using HttpResponseMessage claimed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                ApiRoutes.INBOX_CLAIM, player, first);
            Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
            OperationResult original = await AdminTestSupport.ReadAsync<OperationResult>(claimed);

            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.INBOX_CLAIM }, 1);
            using HttpResponseMessage replayed = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                ApiRoutes.INBOX_CLAIM, player, first);
            InboxClaimRequest second = new InboxClaimRequest(AdminApiTestHost.NewOperationId(), secondInbox, null);
            using HttpResponseMessage blocked = await AdminApiTestHost.SendAsync(client, HttpMethod.Post,
                ApiRoutes.INBOX_CLAIM, player, second);

            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            OperationResult replay = await AdminTestSupport.ReadAsync<OperationResult>(replayed);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replay.Status);
            Assert.Equal(firstInbox, replay.ReceiptId);
            Assert.Equal(original.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(blocked)).ReasonCode);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @AccountId;", new { AccountId = accountId }));
            Assert.Equal(CURRENCY, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT currency FROM account_wallets WHERE account_id = @AccountId;", new { AccountId = accountId }));
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
    }
}
