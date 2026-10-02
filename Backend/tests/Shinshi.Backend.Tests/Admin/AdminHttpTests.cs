// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
    /// <summary> DB 없이 실제 Program 파이프라인에서 어드민 접근 제한(404), 토큰(401), 역할(403), 쓰기 요청 검증(400)을 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OUTSIDE_IP           = "203.0.113.5";
        private const string INSIDE_IP            = "10.1.2.3";
        private const string VALID_REASON         = "test reason";
        private const string FORWARDED_FOR_HEADER = "X-Forwarded-For";
        private const string FORWARDED_HEADER     = "Forwarded";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task AdminRoute_MissingOrForeignOrGameToken_Returns401()
        {
            using AdminFactory factory = new AdminFactory();
            using HttpClient client = factory.CreateClient();
            string gameStyleToken = new AuthTokenHasher(factory.AuthOptions).CreateToken();

            using HttpResponseMessage missing = await client.GetAsync(AdminProbeController.OPERATOR_ROUTE);
            using HttpResponseMessage foreign = await SendAsync(client, HttpMethod.Get, AdminProbeController.OPERATOR_ROUTE,
                "q0b9ZqzYlqL1x5Vv7m3n0eFz3Qb2h1sT4cYp8kW6a9E");
            using HttpResponseMessage game = await SendAsync(client, HttpMethod.Get, AdminProbeController.OPERATOR_ROUTE,
                gameStyleToken);

            foreach (HttpResponseMessage response in new[] { missing, foreign, game })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                    (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
                Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            }
        }

        [Fact]
        public async Task AdminRoute_OutsideAllowedNetwork_Returns404EvenWithOwnerToken()
        {
            using AdminFactory factory = new AdminFactory();
            using HttpClient client = factory.CreateClient();
            string owner = factory.Issue(EAdminRole.Owner);

            using HttpResponseMessage probe = await SendAsync(client, HttpMethod.Get, AdminProbeController.OPERATOR_ROUTE,
                owner, OUTSIDE_IP);
            using HttpResponseMessage login = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.AUTH_LOGIN, null,
                OUTSIDE_IP, AdminTestSupport.Json(new AdminLoginRequest("owner", "x", "000000")));
            using HttpResponseMessage inside = await SendAsync(client, HttpMethod.Get, AdminProbeController.OPERATOR_ROUTE,
                owner);

            Assert.Equal(HttpStatusCode.NotFound, probe.StatusCode);
            Assert.Equal(string.Empty, await probe.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
        }

        [Fact]
        public async Task AdminRoute_ConfiguredNetworks_ReplaceLoopbackDefault()
        {
            using AdminFactory factory = new AdminFactory(new[] { "10.0.0.0/8" });
            using HttpClient client = factory.CreateClient();
            string viewer = factory.Issue(EAdminRole.Viewer);

            using HttpResponseMessage inside = await SendAsync(client, HttpMethod.Get, AdminProbeController.VIEWER_ROUTE,
                viewer, INSIDE_IP);
            using HttpResponseMessage loopback = await SendAsync(client, HttpMethod.Get, AdminProbeController.VIEWER_ROUTE,
                viewer);

            Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, loopback.StatusCode);
        }

        [Fact]
        public async Task AdminRoute_ForwardingHeaderWithoutKnownProxies_Returns404AndLogsOnce()
        {
            using AdminFactory factory = new AdminFactory();
            using HttpClient client = factory.CreateClient();
            string owner = factory.Issue(EAdminRole.Owner);

            using HttpResponseMessage forwardedFor = await SendAsync(client, HttpMethod.Get,
                AdminProbeController.OPERATOR_ROUTE, owner, null, null, (FORWARDED_FOR_HEADER, INSIDE_IP));
            using HttpResponseMessage forwarded = await SendAsync(client, HttpMethod.Get,
                AdminProbeController.OPERATOR_ROUTE, owner, null, null, (FORWARDED_HEADER, "for=" + INSIDE_IP));
            using HttpResponseMessage direct = await SendAsync(client, HttpMethod.Get, AdminProbeController.OPERATOR_ROUTE,
                owner);

            Assert.Equal(HttpStatusCode.NotFound, forwardedFor.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, forwarded.StatusCode);
            Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
            Assert.Equal(1, factory.Logs.Count(LogLevel.Error, "ForwardedHeaders:KnownProxies"));
        }

        [Theory]
        [InlineData(new[] { "10.0.0.0/8" }, "10.0.0.5")]
        [InlineData(new string[0], "127.0.0.1")]
        [InlineData(new string[0], "::1")]
        [InlineData(new[] { "192.168.0.0/16", "10.1.2.3" }, "::ffff:10.1.2.3")]
        public void Parse_KnownProxyInsideAllowedNetworks_Throws(string[] allowed, string proxy)
        {
            Assert.Throws<InvalidOperationException>(() => AdminNetworkPolicy.Parse(allowed,
                new[] { IPAddress.Parse(proxy) }));
        }

        [Fact]
        public void Parse_KnownProxyOutsideAllowedNetworks_TrustsForwardedHeaders()
        {
            AdminNetworkPolicy trusted = AdminNetworkPolicy.Parse(new[] { "10.0.0.0/8" },
                new[] { IPAddress.Parse("192.168.10.5") });
            AdminNetworkPolicy direct = AdminNetworkPolicy.Parse(Array.Empty<string>());

            Assert.True(trusted.TrustsForwardedHeaders);
            Assert.True(trusted.IsAllowed(IPAddress.Parse(INSIDE_IP)));
            Assert.False(trusted.IsAllowed(IPAddress.Parse("192.168.10.5")));
            Assert.False(direct.TrustsForwardedHeaders);
        }

        [Fact]
        public void Startup_LoopbackProxyWithLoopbackDefault_FailsToStart()
        {
            using AdminFactory factory = new AdminFactory(null, "127.0.0.1");

            // 호스트가 시작 예외를 감쌀 수 있어 형식 대신 설정 키가 담긴 원인을 본다
            Exception? error = Record.Exception(() => factory.CreateClient());
            Assert.NotNull(error);
            Assert.Contains(ForwardedHeadersConfiguration.KNOWN_PROXIES_SECTION, error.ToString());
        }

        [Fact]
        public async Task AdminRoute_InsufficientRole_Returns403AndActionCannotLowerControllerRole()
        {
            using AdminFactory factory = new AdminFactory();
            using HttpClient client = factory.CreateClient();
            string viewer = factory.Issue(EAdminRole.Viewer);
            string operatorToken = factory.Issue(EAdminRole.Operator);

            using HttpResponseMessage operatorRoute = await SendAsync(client, HttpMethod.Get,
                AdminProbeController.OPERATOR_ROUTE, viewer);
            using HttpResponseMessage lowered = await SendAsync(client, HttpMethod.Get,
                AdminProbeController.LOWERED_ROUTE, viewer);
            using HttpResponseMessage operators = await SendAsync(client, HttpMethod.Get, AdminApiRoutes.OPERATORS,
                operatorToken);
            using HttpResponseMessage allowed = await SendAsync(client, HttpMethod.Get,
                AdminProbeController.OPERATOR_ROUTE, operatorToken);

            foreach (HttpResponseMessage response in new[] { operatorRoute, lowered, operators })
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal(AdminContractCodes.RejectReason.FORBIDDEN_ROLE,
                    (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
            }
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        [Fact]
        public async Task OperatorDisable_MissingOperationIdOrReason_Returns400BeforeDatabase()
        {
            using AdminFactory factory = new AdminFactory();
            using HttpClient client = factory.CreateClient();
            string owner = factory.Issue(EAdminRole.Owner);
            string route = string.Format(AdminApiRoutes.OPERATOR_DISABLE_FORMAT, Guid.NewGuid().ToString("D"));
            object[] bodies =
            {
                new AdminOperatorDisableRequest(null!, VALID_REASON),
                new AdminOperatorDisableRequest("not-a-guid", VALID_REASON),
                new AdminOperatorDisableRequest(Guid.Empty.ToString("D"), VALID_REASON),
                new AdminOperatorDisableRequest(Guid.NewGuid().ToString("D"), null!),
                new AdminOperatorDisableRequest(Guid.NewGuid().ToString("D"), "   "),
                new AdminOperatorDisableRequest(Guid.NewGuid().ToString("D"),
                    new string('r', AdminContractCodes.MAX_REASON_LENGTH + 1))
            };

            foreach (object body in bodies)
            {
                using HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, route, owner, null,
                    AdminTestSupport.Json(body));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(AdminContractCodes.RejectReason.INVALID_REQUEST,
                    (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
            }

            using HttpResponseMessage badRoute = await SendAsync(client, HttpMethod.Post,
                string.Format(AdminApiRoutes.OPERATOR_DISABLE_FORMAT, "nope"), owner, null,
                AdminTestSupport.Json(new AdminOperatorDisableRequest(Guid.NewGuid().ToString("D"), VALID_REASON)));
            using HttpResponseMessage noBody = await SendAsync(client, HttpMethod.Post, route, owner);
            using HttpResponseMessage create = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.OPERATORS, owner,
                null, AdminTestSupport.Json(new AdminOperatorCreateRequest(Guid.NewGuid().ToString("D"), string.Empty,
                    "new.operator", AdminContractCodes.Role.VIEWER)));
            Assert.Equal(HttpStatusCode.BadRequest, badRoute.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, noBody.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        }

        [Fact]
        public async Task Login_MissingFields_Returns400WithoutDatabase()
        {
            using AdminFactory factory = new AdminFactory();
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, AdminApiRoutes.AUTH_LOGIN, null,
                null, AdminTestSupport.Json(new AdminLoginRequest("owner", null!, "123456")));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_REQUEST,
                (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        }

        [Fact]
        public async Task AdminPrefix_RouteWithoutAdminMetadata_IsHidden()
        {
            using AdminFactory factory = new AdminFactory();
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(UnmarkedAdminRouteController.ROUTE);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string route,
            string? token, string? remoteIp = null, HttpContent? content = null,
            (string Name, string Value)? extraHeader = null)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, route) { Content = content };
            if (token != null)
            {
                request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token);
            }
            if (remoteIp != null)
            {
                request.Headers.Add(AdminTestSupport.REMOTE_IP_HEADER, remoteIp);
            }
            if (extraHeader != null)
            {
                request.Headers.Add(extraHeader.Value.Name, extraHeader.Value.Value);
            }

            return await client.SendAsync(request);
        }

        private sealed class AdminFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
        {
            private readonly string[] _allowedNetworks;
            private readonly string? _knownProxy;
            private readonly FakeSessionValidator _validator = new FakeSessionValidator();

            public AuthTokenOptions AuthOptions { get; } = AuthTokenHasherTests.CreateOptions();
            public RecordingLoggerProvider Logs { get; } = new RecordingLoggerProvider();

            public AdminFactory(string[]? allowedNetworks = null, string? knownProxy = null)
            {
                _allowedNetworks = allowedNetworks ?? Array.Empty<string>();
                _knownProxy      = knownProxy;
            }

            /// <summary> 이 운영자로 확인되는 어드민 형식 토큰을 만든다 </summary>
            public string Issue(EAdminRole role)
            {
                string token = new AdminTokenHasher(AuthOptions).CreateToken();
                _validator.Sessions[token] = new AdminIdentity(Guid.NewGuid(), role, Guid.NewGuid());
                return token;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                if (_knownProxy != null)
                {
                    builder.UseSetting(ForwardedHeadersConfiguration.KNOWN_PROXIES_SECTION + ":0", _knownProxy);
                }
                builder.ConfigureLogging(logging => logging.AddProvider(Logs));
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=test;Database=test"));
                    services.PostConfigure<AuthTokenOptions>(options => options.HashKeyBase64 = AuthOptions.HashKeyBase64);
                    services.PostConfigure<AdminOptions>(options =>
                    {
                        options.TotpKeyBase64      = AdminTestSupport.CreateKeyBase64();
                        options.PasswordIterations = AdminTestSupport.TEST_ITERATIONS;
                        options.AllowedNetworks    = _allowedNetworks;
                    });
                    services.RemoveAll<IAdminSessionValidator>();
                    services.AddSingleton<IAdminSessionValidator>(_validator);
                    services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
                    services.AddControllers().AddApplicationPart(typeof(AdminProbeController).Assembly);
                });
            }
        }

        private sealed class FakeSessionValidator : IAdminSessionValidator
        {
            public Dictionary<string, AdminIdentity> Sessions { get; } = new Dictionary<string, AdminIdentity>();

            public Task<AdminIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Sessions.TryGetValue(token, out AdminIdentity? identity) ? identity : null);
            }
        }
    }

    /// <summary> 테스트 호스트에서만 역할 판정을 검사하는 어드민 엔드포인트 </summary>
    [RequireAdminRole(EAdminRole.Operator)]
    public sealed class AdminProbeController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OPERATOR_ROUTE = AdminApiRoutes.PREFIX + "/__tests/operator";
        public const string LOWERED_ROUTE  = AdminApiRoutes.PREFIX + "/__tests/lowered";
        public const string VIEWER_ROUTE   = AdminApiRoutes.PREFIX + "/__tests/viewer";

        /****************************************
        *                Methods
        ****************************************/

        [HttpGet(OPERATOR_ROUTE)]
        public ActionResult<string> Operator() { return Ok(CurrentAdmin.AdminId.ToString("D")); }

        // 액션의 낮은 역할 표시가 컨트롤러의 Operator 요구를 낮추지 못하는지 본다
        [RequireAdminRole(EAdminRole.Viewer)]
        [HttpGet(LOWERED_ROUTE)]
        public ActionResult<string> Lowered() { return Ok(CurrentAdmin.AdminId.ToString("D")); }
    }

    /// <summary> 테스트 호스트에서만 Viewer 경로를 검사한다 </summary>
    public sealed class AdminViewerProbeController : AdminControllerBase
    {
        /****************************************
        *                Methods
        ****************************************/

        [HttpGet(AdminProbeController.VIEWER_ROUTE)]
        public ActionResult<string> Viewer() { return Ok(CurrentAdmin.Role.ToString()); }
    }

    /// <summary> 어드민 접두사 아래에 표시 없이 잘못 만든 엔드포인트. 플레이어 토큰으로 열리지 않고 숨겨져야 한다 </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class UnmarkedAdminRouteController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ROUTE = AdminApiRoutes.PREFIX + "/__tests/unmarked";

        /****************************************
        *                Methods
        ****************************************/

        [HttpGet(ROUTE)]
        public ActionResult<string> Get() { return Ok("exposed"); }
    }
}
