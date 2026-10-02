// System
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests.Admin.Grants
{
    /// <summary> 실제 PostgreSQL과 Program 파이프라인에서 지급 역할 검사, 어드민과 플레이어 토큰 분리, 지급부터 수령까지를 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminGrantHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string VIEWER_NAME = "viewer.one";
        private const long   CURRENCY    = 50;
        private const long   OVER_DEBIT  = 1000;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task GrantFlow_OverHttp_EnforcesRolesSeparatesTokensAndDeliversToInbox()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AuthTokenOptions authOptions = AuthTokenHasherTests.CreateOptions();
            AdminOptions adminOptions = AdminTestSupport.CreateOptions();
            byte[] ownerSecret = AdminTotp.CreateSecret();
            Assert.True(await new PostgresAdminUserRepository(database.DataSource,
                new PostgresAdminAuditWriter(database.DataSource)).BootstrapOwnerAsync(AdminTestSupport.CreateOperator(
                    adminOptions.CreateSecretProtector(), GrantTestSupport.OWNER_NAME, EAdminRole.Owner, ownerSecret, null)));
            using ApiFactory factory = new ApiFactory(database.DataSource, authOptions, adminOptions);
            using HttpClient client = factory.CreateClient();
            string ownerToken = await LoginAsync(client, GrantTestSupport.OWNER_NAME, AdminTestSupport.TEST_PASSWORD,
                ownerSecret);
            string viewerToken = await CreateViewerAsync(client, ownerToken);
            (Guid accountId, string playerToken) = await IssuePlayerAsync(database.DataSource, authOptions);
            AdminGrantCreateRequest request = GrantTestSupport.CreateRequest(GrantTestSupport.NewId(), new[] { accountId },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY));

            using HttpResponseMessage viewerCreate = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.GRANTS,
                viewerToken, request);
            using HttpResponseMessage playerCreate = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.GRANTS,
                playerToken, request);
            using HttpResponseMessage adminInbox = await SendAsync(client, HttpMethod.Get, ApiRoutes.INBOX, ownerToken);
            using HttpResponseMessage adminClaim = await SendAsync(client, HttpMethod.Post, ApiRoutes.INBOX_CLAIM,
                ownerToken, new InboxClaimRequest(GrantTestSupport.NewId(), Guid.NewGuid().ToString("D"), null));
            using HttpResponseMessage malformed = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.GRANTS,
                ownerToken, GrantTestSupport.CreateRequest(GrantTestSupport.NewId(), new[] { accountId, accountId },
                    GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY)));
            Assert.Equal(HttpStatusCode.Forbidden, viewerCreate.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, playerCreate.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, adminInbox.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, adminClaim.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_REQUEST,
                (await AdminTestSupport.ReadAsync<ApiRejection>(malformed)).ReasonCode);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource, "SELECT count(*) FROM admin_grants;"));

            using HttpResponseMessage created = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.GRANTS, ownerToken,
                request);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            string grantId = (await AdminTestSupport.ReadAsync<AdminGrantCreateResponse>(created)).GrantId;
            using HttpResponseMessage executed = await SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.GRANT_EXECUTE_FORMAT, grantId), ownerToken,
                new AdminGrantActionRequest(GrantTestSupport.NewId(), GrantTestSupport.REASON));
            using HttpResponseMessage viewerRead = await SendAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.GRANT_FORMAT, grantId), viewerToken);
            using HttpResponseMessage missing = await SendAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.GRANT_FORMAT, Guid.NewGuid().ToString("D")), ownerToken);
            Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
            AdminGrantActionResponse execution = await AdminTestSupport.ReadAsync<AdminGrantActionResponse>(executed);
            Assert.Equal(AdminGrantCodes.STATE_EXECUTED, execution.Grant.State);
            Assert.Equal(1, execution.Grant.OpenCount);
            Assert.Equal(HttpStatusCode.Forbidden, viewerRead.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            using HttpResponseMessage listed = await SendAsync(client, HttpMethod.Get, ApiRoutes.INBOX, playerToken);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            InboxEntry entry = Assert.Single((await AdminTestSupport.ReadAsync<InboxListResponse>(listed)).Entries);
            Assert.False(entry.RequiresCharacter);
            Assert.Equal(CURRENCY, Assert.Single(entry.Rewards).Quantity);
            using HttpResponseMessage claimed = await SendAsync(client, HttpMethod.Post, ApiRoutes.INBOX_CLAIM,
                playerToken, new InboxClaimRequest(GrantTestSupport.NewId(), entry.InboxId, null));
            using HttpResponseMessage claimedAgain = await SendAsync(client, HttpMethod.Post, ApiRoutes.INBOX_CLAIM,
                playerToken, new InboxClaimRequest(GrantTestSupport.NewId(), entry.InboxId, null));
            using HttpResponseMessage badClaim = await SendAsync(client, HttpMethod.Post, ApiRoutes.INBOX_CLAIM,
                playerToken, new InboxClaimRequest("not-a-guid", entry.InboxId, null));
            using HttpResponseMessage afterClaim = await SendAsync(client, HttpMethod.Get, ApiRoutes.INBOX, playerToken);
            Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
            Assert.Equal(entry.InboxId, (await AdminTestSupport.ReadAsync<OperationResult>(claimed)).ReceiptId);
            Assert.Equal(HttpStatusCode.Conflict, claimedAgain.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INBOX_ALREADY_CLAIMED,
                (await AdminTestSupport.ReadAsync<OperationResult>(claimedAgain)).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, badClaim.StatusCode);
            Assert.Empty((await AdminTestSupport.ReadAsync<InboxListResponse>(afterClaim)).Entries);

            AdminAdjustmentRequest debit = new AdminAdjustmentRequest(GrantTestSupport.NewId(), GrantTestSupport.REASON,
                AdminGrantCodes.ASSET_CURRENCY, null, null, null, -OVER_DEBIT);
            string adjustRoute = string.Format(AdminApiRoutes.ACCOUNT_ADJUSTMENTS_FORMAT, accountId.ToString("D"));
            using HttpResponseMessage adjusted = await SendAsync(client, HttpMethod.Post, adjustRoute, ownerToken, debit);
            using HttpResponseMessage replayed = await SendAsync(client, HttpMethod.Post, adjustRoute, ownerToken, debit);
            using HttpResponseMessage viewerAdjust = await SendAsync(client, HttpMethod.Post, adjustRoute, viewerToken,
                debit);
            Assert.Equal(HttpStatusCode.OK, adjusted.StatusCode);
            AdminAdjustmentResponse first = await AdminTestSupport.ReadAsync<AdminAdjustmentResponse>(adjusted);
            AdminAdjustmentResponse second = await AdminTestSupport.ReadAsync<AdminAdjustmentResponse>(replayed);
            Assert.Equal(-CURRENCY, first.AppliedDelta);
            Assert.Equal(OVER_DEBIT - CURRENCY, first.Shortfall);
            Assert.Equal(0L, first.BalanceAfter);
            Assert.Equal(first.AppliedDelta, second.AppliedDelta);
            Assert.Equal(first.Shortfall, second.Shortfall);
            Assert.Equal(first.Operation.ServerTimeUnixMs, second.Operation.ServerTimeUnixMs);
            Assert.Equal(HttpStatusCode.Forbidden, viewerAdjust.StatusCode);
            Assert.Empty(await new EconomyLedgerReconciler(database.DataSource).FindMismatchesAsync(
                GrantTestSupport.RECONCILE_PAGE));
        }

        private static async Task<string> CreateViewerAsync(HttpClient client, string ownerToken)
        {
            using HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.OPERATORS,
                ownerToken, new AdminOperatorCreateRequest(GrantTestSupport.NewId(), GrantTestSupport.REASON, VIEWER_NAME,
                    AdminContractCodes.Role.VIEWER));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AdminOperatorCreateResponse viewer = await AdminTestSupport.ReadAsync<AdminOperatorCreateResponse>(response);
            return await LoginAsync(client, VIEWER_NAME, viewer.InitialPassword,
                AdminTestSupport.FromBase32(viewer.TotpSecretBase32));
        }

        private static async Task<string> LoginAsync(HttpClient client, string loginName, string password, byte[] secret)
        {
            using HttpResponseMessage response = await client.PostAsync(AdminApiRoutes.AUTH_LOGIN, AdminTestSupport.Json(
                new AdminLoginRequest(loginName, password, AdminTestSupport.CodeAt(secret, DateTimeOffset.UtcNow))));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await AdminTestSupport.ReadAsync<AdminLoginResponse>(response)).AccessToken;
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

        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string route,
            string token, object? body = null)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, route)
            {
                Content = body == null ? null : AdminTestSupport.Json(body)
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token);
            return await client.SendAsync(request);
        }

        private sealed class ApiFactory : WebApplicationFactory<Program>
        {
            private readonly NpgsqlDataSource _dataSource;
            private readonly AuthTokenOptions _authOptions;
            private readonly AdminOptions _adminOptions;

            public ApiFactory(NpgsqlDataSource dataSource, AuthTokenOptions authOptions, AdminOptions adminOptions)
            {
                _dataSource   = dataSource;
                _authOptions  = authOptions;
                _adminOptions = adminOptions;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<NpgsqlDataSource>();
                    services.AddSingleton(_dataSource);
                    services.PostConfigure<AuthTokenOptions>(options => options.HashKeyBase64 = _authOptions.HashKeyBase64);
                    services.PostConfigure<ServiceStateOptions>(options => options.Maintenance = false);
                    services.PostConfigure<AdminOptions>(options =>
                    {
                        options.TotpKeyBase64      = _adminOptions.TotpKeyBase64;
                        options.PasswordIterations = _adminOptions.PasswordIterations;
                    });
                    services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
                });
            }
        }
    }
}
