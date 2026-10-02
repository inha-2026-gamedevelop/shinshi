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

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> Program 파이프라인에서 로그 조회와 복구 경로의 역할(Viewer 조회, Admin 복구), 형식 오류 400, 없는 대상 404,
    /// 게임 토큰 401을 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminLogsRecoveryHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OWNER_NAME  = "owner.http";
        private const string VIEWER_NAME = "viewer.http";
        private const string REASON      = "manual recovery check";
        private const string NO_STORE    = "no-store";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task LogsAndRecovery_RolesAndValidation_RejectWithSharedCodes()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AuthTokenOptions authOptions = AuthTokenHasherTests.CreateOptions();
            AdminOptions adminOptions = AdminTestSupport.CreateOptions();
            byte[] ownerSecret = AdminTotp.CreateSecret();
            PostgresAdminUserRepository users = new PostgresAdminUserRepository(database.DataSource,
                new PostgresAdminAuditWriter(database.DataSource));
            Assert.True(await users.BootstrapOwnerAsync(AdminTestSupport.CreateOperator(
                adminOptions.CreateSecretProtector(), OWNER_NAME, EAdminRole.Owner, ownerSecret, null)));
            using ApiFactory factory = new ApiFactory(database.DataSource, authOptions, adminOptions);
            using HttpClient client = factory.CreateClient();
            string ownerToken = await LoginAsync(client, OWNER_NAME, AdminTestSupport.TEST_PASSWORD, ownerSecret);
            using HttpResponseMessage created = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.OPERATORS,
                ownerToken, new AdminOperatorCreateRequest(Guid.NewGuid().ToString("D"), REASON, VIEWER_NAME,
                    AdminContractCodes.Role.VIEWER));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            AdminOperatorCreateResponse viewer = await AdminTestSupport.ReadAsync<AdminOperatorCreateResponse>(created);
            string viewerToken = await LoginAsync(client, VIEWER_NAME, viewer.InitialPassword,
                AdminTestSupport.FromBase32(viewer.TotpSecretBase32));
            string sessionRecovery = string.Format(AdminApiRoutes.SESSION_RECOVERY_FORMAT, Guid.NewGuid().ToString("D"));
            AdminSessionRecoveryRequest retry = new AdminSessionRecoveryRequest(Guid.NewGuid().ToString("D"), REASON,
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT);

            // Viewer 조회. 원장은 사유가 필수인 민감 조회라 계정을 대상으로 감사 행이 남는다
            string viewedAccount = Guid.NewGuid().ToString("D");
            using HttpResponseMessage ledger = await SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.LEDGER + "?" + AdminLogQueryParameters.ACCOUNT_ID + "=" + viewedAccount + "&" + ReasonQuery(),
                viewerToken);
            Assert.Equal(HttpStatusCode.OK, ledger.StatusCode);
            Assert.Equal(NO_STORE, ledger.Headers.CacheControl!.ToString());
            AdminLedgerPage ledgerPage = await AdminTestSupport.ReadAsync<AdminLedgerPage>(ledger);
            Assert.Empty(ledgerPage.Entries);
            Assert.Empty(ledgerPage.Totals);
            Assert.Null(ledgerPage.NextCursor);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND target_kind = @Kind AND target_id = @Target AND reason = @Reason
                    AND operation_id IS NULL;
                """, new
                {
                    Action = AdminLogAuditActions.LEDGER_VIEW, Kind = AdminAccountAuditActions.TARGET_ACCOUNT,
                    Target = viewedAccount, Reason = REASON
                }));
            using HttpResponseMessage sessions = await SendAsync(client, HttpMethod.Get, AdminApiRoutes.SESSIONS,
                viewerToken);
            Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
            Assert.Empty((await AdminTestSupport.ReadAsync<AdminRecoverySessionPage>(sessions)).Sessions);
            using HttpResponseMessage audit = await SendAsync(client, HttpMethod.Get, AdminApiRoutes.AUDIT, viewerToken);
            Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
            Assert.NotEmpty((await AdminTestSupport.ReadAsync<AdminAuditPage>(audit)).Entries);

            // 형식 오류와 범위 조건 누락, 감사 사유 누락은 400 INVALID_REQUEST이고 감사 행을 남기지 않는다
            await AssertRejectedAsync(client, HttpMethod.Get, AdminApiRoutes.LEDGER + "?" + ReasonQuery(), viewerToken,
                null, HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                AdminApiRoutes.LEDGER + "?" + AdminLogQueryParameters.ACCOUNT_ID + "=" + Guid.NewGuid().ToString("D"),
                viewerToken, null, HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                AdminApiRoutes.LEDGER + "?" + AdminLogQueryParameters.SOURCE_TYPE + "=GameSession&" + ReasonQuery(),
                viewerToken, null, HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                AdminApiRoutes.LEDGER + "?" + AdminLogQueryParameters.ACCOUNT_ID + "=" + Guid.NewGuid().ToString("D")
                + "&" + AdminLogQueryParameters.LIMIT + "=501&" + ReasonQuery(), viewerToken, null,
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                AdminApiRoutes.LEDGER + "?" + AdminLogQueryParameters.ACCOUNT_ID + "=" + Guid.NewGuid().ToString("D")
                + "&" + AdminLogQueryParameters.CURSOR + "=not-a-cursor&" + ReasonQuery(), viewerToken, null,
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                AdminApiRoutes.LEDGER + "?" + AdminLogQueryParameters.ACCOUNT_ID + "=not-a-guid&" + ReasonQuery(),
                viewerToken, null, HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                AdminApiRoutes.LEDGER + "?" + AdminLogQueryParameters.ACCOUNT_ID + "=" + Guid.NewGuid().ToString("D")
                + "&" + AdminLogQueryParameters.REASON_CODE + "=not-a-code&" + ReasonQuery(), viewerToken, null,
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.ITEM_HISTORY_FORMAT, Guid.NewGuid().ToString("D")), viewerToken, null,
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.SESSION_EVENTS_FORMAT, Guid.NewGuid().ToString("D")) + "?"
                + AdminLogQueryParameters.REASON + "=%20", viewerToken, null,
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.OPERATION_FORMAT, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D")),
                viewerToken, null, HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action LIKE 'admin.%view' AND action <> @AccountView;",
                new { AccountView = AdminAccountAuditActions.ACCOUNT_VIEW }));
            await AssertRejectedAsync(client, HttpMethod.Get,
                AdminApiRoutes.AUDIT + "?" + AdminLogQueryParameters.TARGET_ID + "=x", viewerToken, null,
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Get,
                AdminApiRoutes.SESSIONS + "?" + AdminApiRoutes.STATE_PARAMETER + "=Bogus", viewerToken, null,
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);

            // 없는 대상은 404 NOT_FOUND. 사유가 있는 조회는 대상이 없어도 시도로 감사된다
            await AssertRejectedAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.ITEM_HISTORY_FORMAT, Guid.NewGuid().ToString("D")) + "?" + ReasonQuery(),
                viewerToken, null, HttpStatusCode.NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            await AssertRejectedAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.SESSION_EVENTS_FORMAT, Guid.NewGuid().ToString("D")) + "?" + ReasonQuery(),
                viewerToken, null, HttpStatusCode.NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            await AssertRejectedAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.OPERATION_FORMAT, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"))
                + "?" + ReasonQuery(), viewerToken, null, HttpStatusCode.NotFound,
                AdminContractCodes.RejectReason.NOT_FOUND);
            foreach (string action in new[]
            {
                AdminLogAuditActions.ITEM_HISTORY_VIEW, AdminLogAuditActions.SESSION_EVENTS_VIEW,
                AdminLogAuditActions.OPERATION_VIEW
            })
            {
                Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                    "SELECT count(*) FROM admin_audit_log WHERE action = @Action AND reason = @Reason;",
                    new { Action = action, Reason = REASON }));
            }

            // 복구는 Admin 이상. Viewer는 403, 게임 토큰은 401
            await AssertRejectedAsync(client, HttpMethod.Post, sessionRecovery, viewerToken, retry,
                HttpStatusCode.Forbidden, AdminContractCodes.RejectReason.FORBIDDEN_ROLE);
            string gameToken = await IssueGameAccessTokenAsync(database.DataSource, authOptions);
            using HttpResponseMessage gameLedger = await SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.LEDGER + "?" + AdminLogQueryParameters.ACCOUNT_ID + "=" + Guid.NewGuid().ToString("D")
                + "&" + ReasonQuery(), gameToken);
            using HttpResponseMessage gameRecovery = await SendAsync(client, HttpMethod.Post, sessionRecovery, gameToken,
                retry);
            Assert.Equal(HttpStatusCode.Unauthorized, gameLedger.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, gameRecovery.StatusCode);

            // Owner: action 누락은 400, 없는 세션은 저장된 거절(409 NOT_FOUND)
            await AssertRejectedAsync(client, HttpMethod.Post, sessionRecovery, ownerToken,
                new AdminSessionRecoveryRequest(Guid.NewGuid().ToString("D"), REASON, null!),
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            await AssertRejectedAsync(client, HttpMethod.Post, sessionRecovery, ownerToken,
                new AdminSessionRecoveryRequest(Guid.NewGuid().ToString("D"), " ", AdminSessionRecoveryCodes.SERVICE_CANCEL),
                HttpStatusCode.BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            using HttpResponseMessage missing = await SendAsync(client, HttpMethod.Post, sessionRecovery, ownerToken,
                retry);
            Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND,
                (await AdminTestSupport.ReadAsync<OperationResult>(missing)).ReasonCode);

            // 막 시작한 API는 서버 소실 판정을 보류하므로 새 장애 취소, 강제 종료는 저장 없이 503과 Retry-After다
            foreach (string action in new[] { AdminSessionRecoveryCodes.SERVICE_CANCEL, AdminSessionRecoveryCodes.FORCE_CLOSE })
            {
                AdminSessionRecoveryRequest deferred = new AdminSessionRecoveryRequest(Guid.NewGuid().ToString("D"),
                    REASON, action);
                using HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, sessionRecovery,
                    ownerToken, deferred);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.NotNull(response.Headers.RetryAfter);
                Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE,
                    (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
                Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                    "SELECT count(*) FROM operations WHERE operation_id = @Id;",
                    new { Id = Guid.Parse(deferred.OperationId) }));
            }
            using HttpResponseMessage deferredList = await SendAsync(client, HttpMethod.Get, AdminApiRoutes.SESSIONS,
                viewerToken);
            Assert.True((await AdminTestSupport.ReadAsync<AdminRecoverySessionPage>(deferredList)).LivenessDeferred);

            // 거절된 복구는 감사 행(의도 행 포함)을 남기지 않는다. 세션 대상 행은 위의 세션 재생 조회 감사뿐이다
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE target_kind = @Kind AND action <> @View;",
                new { Kind = AdminRecoveryActions.TARGET_GAME_SESSION, View = AdminLogAuditActions.SESSION_EVENTS_VIEW }));
            Assert.Equal(4L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action LIKE 'admin.%view' AND action <> @AccountView;",
                new { AccountView = AdminAccountAuditActions.ACCOUNT_VIEW }));
        }

        private static string ReasonQuery()
        {
            return AdminLogQueryParameters.REASON + "=" + Uri.EscapeDataString(REASON);
        }

        private static async Task AssertRejectedAsync(HttpClient client, HttpMethod method, string route, string token,
            object? body, HttpStatusCode status, int reasonCode)
        {
            using HttpResponseMessage response = await SendAsync(client, method, route, token, body);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(reasonCode, (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
        }

        private static async Task<string> LoginAsync(HttpClient client, string loginName, string password, byte[] secret)
        {
            using HttpResponseMessage response = await client.PostAsync(AdminApiRoutes.AUTH_LOGIN, AdminTestSupport.Json(
                new AdminLoginRequest(loginName, password, AdminTestSupport.CodeAt(secret, DateTimeOffset.UtcNow))));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await AdminTestSupport.ReadAsync<AdminLoginResponse>(response)).AccessToken;
        }

        private static async Task<string> IssueGameAccessTokenAsync(NpgsqlDataSource dataSource, AuthTokenOptions options)
        {
            Guid accountId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                    new { AccountId = accountId });
            }
            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            IssuedAuthToken invite = await auth.IssueInviteAsync(accountId);
            return (await auth.ExchangeInviteAsync(invite.Token)).Response!.AccessToken;
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

        /// <summary> 시험 DB와 키를 Program에 주입하고 원격 주소를 루프백으로 채운다 </summary>
        private sealed class ApiFactory : WebApplicationFactory<Program>
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly NpgsqlDataSource _dataSource;
            private readonly AuthTokenOptions _authOptions;
            private readonly AdminOptions _adminOptions;

            /****************************************
            *                Methods
            ****************************************/

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
