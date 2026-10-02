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
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 PostgreSQL과 Program 파이프라인에서 운영자 로그인, 생성 재전송, 비활성화, 로그아웃과 게임 토큰 분리를 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminPostgresHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OWNER_NAME  = "owner.one";
        private const string VIEWER_NAME = "viewer.one";
        private const string REASON      = "onboard support staff";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task OwnerFlow_LoginCreateReplayDisableLogout_KeepsAdminAndGameTokensApart()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AuthTokenOptions authOptions = AuthTokenHasherTests.CreateOptions();
            AdminOptions adminOptions = AdminTestSupport.CreateOptions();
            byte[] ownerSecret = AdminTotp.CreateSecret();
            PostgresAdminUserRepository users = new PostgresAdminUserRepository(database.DataSource,
                new PostgresAdminAuditWriter(database.DataSource));
            AdminNewOperator owner = AdminTestSupport.CreateOperator(adminOptions.CreateSecretProtector(), OWNER_NAME,
                EAdminRole.Owner, ownerSecret, null);
            Assert.True(await users.BootstrapOwnerAsync(owner));
            using ApiFactory factory = new ApiFactory(database.DataSource, authOptions, adminOptions);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage badLogin = await client.PostAsync(AdminApiRoutes.AUTH_LOGIN, AdminTestSupport.Json(
                new AdminLoginRequest(OWNER_NAME, "wrong password value", AdminTestSupport.CodeAt(ownerSecret,
                    DateTimeOffset.UtcNow))));
            Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_CREDENTIALS,
                (await AdminTestSupport.ReadAsync<ApiRejection>(badLogin)).ReasonCode);

            string ownerToken = await LoginAsync(client, OWNER_NAME, AdminTestSupport.TEST_PASSWORD, ownerSecret);
            using HttpResponseMessage list = await SendAsync(client, HttpMethod.Get, AdminApiRoutes.OPERATORS, ownerToken);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Single((await AdminTestSupport.ReadAsync<AdminOperatorListResponse>(list)).Operators);

            string operationId = Guid.NewGuid().ToString("D");
            AdminOperatorCreateRequest create = new AdminOperatorCreateRequest(operationId, REASON, "Viewer.One",
                AdminContractCodes.Role.VIEWER);
            using HttpResponseMessage created = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.OPERATORS,
                ownerToken, create);
            using HttpResponseMessage replayed = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.OPERATORS,
                ownerToken, create);
            using HttpResponseMessage conflict = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.OPERATORS,
                ownerToken, new AdminOperatorCreateRequest(operationId, REASON, "other.name", AdminContractCodes.Role.VIEWER));
            using HttpResponseMessage taken = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.OPERATORS,
                ownerToken, new AdminOperatorCreateRequest(Guid.NewGuid().ToString("D"), REASON, VIEWER_NAME,
                    AdminContractCodes.Role.OPERATOR));

            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
            AdminOperatorCreateResponse first = await AdminTestSupport.ReadAsync<AdminOperatorCreateResponse>(created);
            AdminOperatorCreateResponse second = await AdminTestSupport.ReadAsync<AdminOperatorCreateResponse>(replayed);
            Assert.Equal(VIEWER_NAME, first.LoginName);
            Assert.True(AdminPasswordHasher.MeetsPolicy(first.InitialPassword));
            Assert.NotNull(first.TotpSecretBase32);
            Assert.StartsWith("otpauth://totp/", first.TotpUri);
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal(first.AdminId, second.AdminId);
            Assert.Equal(first.Operation.ServerTimeUnixMs, second.Operation.ServerTimeUnixMs);
            Assert.Null(second.InitialPassword);
            Assert.Null(second.TotpSecretBase32);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT,
                (await AdminTestSupport.ReadAsync<OperationResult>(conflict)).ReasonCode);
            Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.LOGIN_NAME_TAKEN,
                (await AdminTestSupport.ReadAsync<OperationResult>(taken)).ReasonCode);
            Assert.Equal(2L, await AdminTestSupport.CountAsync(database.DataSource, "SELECT count(*) FROM admin_users;"));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND operation_id = @OperationId AND admin_id = @AdminId AND reason = @Reason
                    AND target_id = @TargetId AND request_ip_hash IS NOT NULL;
                """, new
                {
                    Action = AdminAuditActions.OPERATOR_CREATE, OperationId = Guid.Parse(operationId),
                    owner.AdminId, Reason = REASON, TargetId = first.AdminId
                }));

            string viewerToken = await LoginAsync(client, VIEWER_NAME, first.InitialPassword,
                AdminTestSupport.FromBase32(first.TotpSecretBase32));
            using HttpResponseMessage viewerList = await SendAsync(client, HttpMethod.Get, AdminApiRoutes.OPERATORS,
                viewerToken);
            Assert.Equal(HttpStatusCode.Forbidden, viewerList.StatusCode);

            string gameToken = await IssueGameAccessTokenAsync(database.DataSource, authOptions);
            using HttpResponseMessage gameOnAdmin = await SendAsync(client, HttpMethod.Get, AdminApiRoutes.OPERATORS,
                gameToken);
            using HttpResponseMessage adminOnGame = await SendAsync(client, HttpMethod.Post,
                string.Format(ApiRoutes.REJOIN_TICKET_FORMAT, Guid.NewGuid().ToString("D")), ownerToken);
            using HttpResponseMessage gameOnGame = await SendAsync(client, HttpMethod.Post,
                string.Format(ApiRoutes.REJOIN_TICKET_FORMAT, Guid.NewGuid().ToString("D")), gameToken);
            Assert.Equal(HttpStatusCode.Unauthorized, gameOnAdmin.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, adminOnGame.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, gameOnGame.StatusCode);

            using HttpResponseMessage disabled = await SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.OPERATOR_DISABLE_FORMAT, first.AdminId), ownerToken,
                new AdminOperatorDisableRequest(Guid.NewGuid().ToString("D"), "left the team"));
            using HttpResponseMessage self = await SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.OPERATOR_DISABLE_FORMAT, owner.AdminId.ToString("D")), ownerToken,
                new AdminOperatorDisableRequest(Guid.NewGuid().ToString("D"), "mistake"));
            using HttpResponseMessage viewerAfter = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.AUTH_LOGOUT,
                viewerToken);
            Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.SELF_TARGET,
                (await AdminTestSupport.ReadAsync<OperationResult>(self)).ReasonCode);
            Assert.Equal(HttpStatusCode.Unauthorized, viewerAfter.StatusCode);

            using HttpResponseMessage logout = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.AUTH_LOGOUT,
                ownerToken);
            using HttpResponseMessage afterLogout = await SendAsync(client, HttpMethod.Get, AdminApiRoutes.OPERATORS,
                ownerToken);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
        }

        private static async Task<string> LoginAsync(HttpClient client, string loginName, string password, byte[] secret)
        {
            using HttpResponseMessage response = await client.PostAsync(AdminApiRoutes.AUTH_LOGIN, AdminTestSupport.Json(
                new AdminLoginRequest(loginName, password, AdminTestSupport.CodeAt(secret, DateTimeOffset.UtcNow))));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            AdminLoginResponse login = await AdminTestSupport.ReadAsync<AdminLoginResponse>(response);
            Assert.True(login.ExpiresAtUnixMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return login.AccessToken;
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
