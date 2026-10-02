// System
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> 인증 실패 한도가 서버 임대 경로를 막지 않는지, 우리 키로 만든 만료 토큰을 실패로 세지 않는지 DB 없이 검사한다 </summary>
    public sealed class BearerFailureScopeTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN          = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const int    EXTRA_ATTEMPTS = 5;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 IP의 플레이어 경로 실패로 IP가 막혀도 서버 임대 경로는 계속 검증받고, 서버 경로의 실패는 IP 한도에 세지 않는다 </summary>
        [Fact]
        public async Task Middleware_BlockedPlayerIp_StillServesServerLeaseRoutes()
        {
            ScopeRepository repository = new ScopeRepository();
            using IHost host = await StartHostAsync(repository);
            using HttpClient client = host.GetTestClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + TOKEN);

            repository.ServerReason = ContractCodes.RejectReason.TOKEN_EXPIRED;
            for (int attempt = 0; attempt < AuthFailureLimiter.MAX_FAILURES_PER_WINDOW + EXTRA_ATTEMPTS; ++attempt)
            {
                using HttpResponseMessage serverFailure = await client.GetAsync(ServerProbeController.ROUTE);
                Assert.Equal(HttpStatusCode.Unauthorized, serverFailure.StatusCode);
            }
            using HttpResponseMessage playerAfterServerFailures = await client.GetAsync(PlayerProbeController.ROUTE);
            for (int attempt = 0; attempt < AuthFailureLimiter.MAX_FAILURES_PER_WINDOW; ++attempt)
            {
                using HttpResponseMessage playerFailure = await client.GetAsync(PlayerProbeController.ROUTE);
            }
            using HttpResponseMessage playerBlocked = await client.GetAsync(PlayerProbeController.ROUTE);
            repository.ServerReason = ContractCodes.RejectReason.NONE;
            using HttpResponseMessage server = await client.GetAsync(ServerProbeController.ROUTE);

            Assert.Equal(HttpStatusCode.Unauthorized, playerAfterServerFailures.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, playerBlocked.StatusCode);
            Assert.Equal(HttpStatusCode.OK, server.StatusCode);
        }

        /// <summary> 우리 키로 만든(태그가 맞는) 접근 토큰의 만료, 폐기는 한도를 넘게 반복돼도 IP를 막지 않는다 </summary>
        [Fact]
        public async Task Middleware_OwnExpiredTokens_AreNotCountedAsFailures()
        {
            ScopeRepository repository = new ScopeRepository { Authentic = true };
            using IHost host = await StartHostAsync(repository);
            using HttpClient client = host.GetTestClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + TOKEN);

            for (int attempt = 0; attempt < AuthFailureLimiter.MAX_FAILURES_PER_WINDOW + EXTRA_ATTEMPTS; ++attempt)
            {
                using HttpResponseMessage expired = await client.GetAsync(PlayerProbeController.ROUTE);
                Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
            }

            Assert.Equal(AuthFailureLimiter.MAX_FAILURES_PER_WINDOW + EXTRA_ATTEMPTS, repository.AccessCalls);
        }

        /// <summary> 신뢰 프록시는 IP 주소만 받고 설정이 없으면 비어 있다(ForwardedHeaders를 켜지 않는다) </summary>
        [Fact]
        public void ForwardedHeaders_KnownProxies_ParsedOnlyWhenConfigured()
        {
            IConfiguration empty = new ConfigurationBuilder().Build();
            IConfiguration configured = new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>(
                    ForwardedHeadersConfiguration.KNOWN_PROXIES_SECTION + ":0", "10.0.0.5")
            }).Build();
            IConfiguration invalid = new ConfigurationBuilder().AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>(
                    ForwardedHeadersConfiguration.KNOWN_PROXIES_SECTION + ":0", "load-balancer")
            }).Build();

            Assert.Empty(ForwardedHeadersConfiguration.ReadKnownProxies(empty));
            Assert.Equal(IPAddress.Parse("10.0.0.5"), Assert.Single(ForwardedHeadersConfiguration.ReadKnownProxies(configured)));
            Assert.Throws<InvalidOperationException>(() => ForwardedHeadersConfiguration.ReadKnownProxies(invalid));
        }

        private static async Task<IHost> StartHostAsync(ScopeRepository repository)
        {
            IHost host = new HostBuilder().ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.AddShinshiAuth(new ConfigurationBuilder().Build());
                    services.AddSingleton<IAuthRepository>(repository);
                    services.AddControllers().AddApplicationPart(typeof(PlayerProbeController).Assembly);
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseShinshiAuthentication();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            }).Build();
            await host.StartAsync();
            return host;
        }

        /// <summary> 검증 결과와 태그 판정을 시험이 정하는 저장소 </summary>
        private sealed class ScopeRepository : IAuthRepository
        {
            public bool Authentic    { get; set; }
            public int  ServerReason { get; set; }
            public int  AccessCalls  { get; private set; }

            public bool IsAuthenticToken(string token)
            {
                return Authentic;
            }

            public Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateAccessAsync(string token,
                CancellationToken cancellationToken = default)
            {
                ++AccessCalls;
                return Task.FromResult(AuthValidationOutcome<PlayerAuthIdentity>.Rejected(
                    ContractCodes.RejectReason.TOKEN_EXPIRED));
            }

            public Task<AuthValidationOutcome<ServerAuthIdentity>> ValidateServerLeaseAsync(string token,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(ServerReason == ContractCodes.RejectReason.NONE
                    ? AuthValidationOutcome<ServerAuthIdentity>.Success(new ServerAuthIdentity(Guid.NewGuid(), Guid.NewGuid(),
                        Guid.NewGuid(), ContractCodes.ServerRole.TOWN, long.MaxValue))
                    : AuthValidationOutcome<ServerAuthIdentity>.Rejected(ServerReason));
            }

            public Task<AuthExchangeOutcome> ExchangeInviteAsync(string token, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Unexpected invite exchange.");
            }

            public Task<AuthExchangeOutcome> RotateRefreshAsync(string token, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Unexpected refresh rotation.");
            }

            public Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeJoinTokenAsync(string token, Guid instanceId,
                int serverRole, Guid serverLeaseId, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Unexpected token consumption.");
            }

            public Task<AuthValidationOutcome<IssuedAuthToken>> IssueDungeonJoinTokenAsync(ServerAuthIdentity server,
                DungeonJoinGrant grant, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Unexpected token issuance.");
            }
        }
    }

    /// <summary> 플레이어 접근 토큰 경로 시험용 </summary>
    public sealed class PlayerProbeController : ControllerBase
    {
        public const string ROUTE = "/__tests/scope/player";

        [HttpGet(ROUTE)]
        public IActionResult Get()
        {
            return Ok();
        }
    }

    /// <summary> 서버 임대 경로 시험용 </summary>
    [RequireServerLease]
    public sealed class ServerProbeController : ControllerBase
    {
        public const string ROUTE = "/__tests/scope/server";

        [HttpGet(ROUTE)]
        public IActionResult Get()
        {
            return Ok();
        }
    }
}
