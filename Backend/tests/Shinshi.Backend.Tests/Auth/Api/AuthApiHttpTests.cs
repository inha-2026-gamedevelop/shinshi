// System
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests
{
    public sealed class AuthApiHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BUILD = "api-test-build";
        private const string CONTENT = "api-test-content";
        private const int PROTOCOL = 7;
        private static readonly string _token = new string('A', 43);

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(ContractCodes.RejectReason.INVALID_INVITE_CODE)]
        [InlineData(ContractCodes.RejectReason.INVITE_CODE_EXPIRED)]
        [InlineData(ContractCodes.RejectReason.INVITE_CODE_USED)]
        public async Task Exchange_InviteRejection_PreservesSharedCode(int reason)
        {
            using AuthFactory factory = new AuthFactory();
            factory.Repository.ExchangeReason = reason;
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                JsonBody(new AuthExchangeRequest(_token, string.Empty, Build())));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            ApiRejection rejection = Read<ApiRejection>(await response.Content.ReadAsStringAsync());
            Assert.Equal(reason, rejection.ReasonCode);
            Assert.Equal(BUILD, rejection.BuildId);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        }

        [Fact]
        public async Task Exchange_InvalidBuildOrTwoCredentials_DoesNotConsumeCredential()
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage version = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                JsonBody(new AuthExchangeRequest(_token, string.Empty, Build(protocol: PROTOCOL + 1))));
            using HttpResponseMessage build = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                JsonBody(new AuthExchangeRequest(_token, string.Empty, Build(build: "unknown"))));
            using HttpResponseMessage both = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                JsonBody(new AuthExchangeRequest(_token, _token, Build())));
            Assert.Equal(ContractCodes.RejectReason.VERSION_MISMATCH,
                Read<ApiRejection>(await version.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.BUILD_NOT_ALLOWED,
                Read<ApiRejection>(await build.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
            Assert.Equal(0, factory.Repository.ExchangeCalls);
        }

        [Fact]
        public async Task Exchange_RefreshRequest_UsesRotationAndReturnsPrivateFieldContract()
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                JsonBody(new AuthExchangeRequest(string.Empty, _token, Build())));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, factory.Repository.RotateCalls);
            AuthExchangeResponse result = Read<AuthExchangeResponse>(await response.Content.ReadAsStringAsync());
            Assert.Equal(factory.Repository.Player.AccountId.ToString("D"), result.AccountId);
            Assert.Equal(_token, result.AccessToken);
        }

        [Fact]
        public async Task Middleware_BearerOnly_UsesTokenActorAndSeparatesServerCredential()
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage missing = await client.GetAsync(AuthActorProbeController.ROUTE);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.Equal(0, factory.Repository.AccessCalls);
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));
            using HttpResponseMessage player = await client.GetAsync(AuthActorProbeController.ROUTE + "?accountId=" + Guid.NewGuid());
            Assert.Equal(HttpStatusCode.OK, player.StatusCode);
            Assert.Equal(factory.Repository.Player.AccountId.ToString("D"),
                JsonConvert.DeserializeObject<string>(await player.Content.ReadAsStringAsync()));
            factory.Repository.ServerReason = ContractCodes.RejectReason.TOKEN_REVOKED;
            using HttpResponseMessage serverDenied = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                JsonBody(new JoinTokenExchangeRequest(_token, factory.Repository.Server.InstanceId.ToString("D"), factory.Repository.Server.Role)));
            Assert.Equal(HttpStatusCode.Unauthorized, serverDenied.StatusCode);
            Assert.Equal(0, factory.Repository.ConsumeCalls);
        }

        [Fact]
        public async Task Consume_WrongInstanceDeniedAndSuccessUsesStoredIdentity()
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));
            using HttpResponseMessage wrong = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                JsonBody(new JoinTokenExchangeRequest(_token, Guid.NewGuid().ToString("D"), factory.Repository.Server.Role)));
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            Assert.Equal(0, factory.Repository.ConsumeCalls);
            using HttpResponseMessage response = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                JsonBody(new JoinTokenExchangeRequest(_token, factory.Repository.Server.InstanceId.ToString("D"), factory.Repository.Server.Role)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            JoinTokenExchangeResponse result = Read<JoinTokenExchangeResponse>(await response.Content.ReadAsStringAsync());
            Assert.Equal(factory.Repository.Join.CharacterId.ToString("D"), result.CharacterId);
            Assert.Equal(factory.Repository.Join.SessionId.ToString("D"), result.SortieId);
            Assert.Equal(ContractCodes.PlayerClass.SUMMONER, result.ClassId);
            Assert.Equal(factory.Repository.Server.Role, result.ServerRole);
            Assert.Equal(factory.Repository.Server.LeaseId, factory.Repository.ConsumedLease);
            Assert.Equal(1, factory.Repository.ConsumeCalls);
        }

        [Fact]
        public async Task Middleware_InvalidHeaders_DoNotQueryRepository()
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            foreach (string header in new[] { "Basic anything", "Bearer malformed", "Bearer " + _token + ",another-token" })
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, AuthActorProbeController.ROUTE);
                request.Headers.TryAddWithoutValidation(ApiRoutes.AUTHORIZATION_HEADER, header);
                using HttpResponseMessage response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
            Assert.Equal(0, factory.Repository.AccessCalls);
        }

        [Fact]
        public async Task Middleware_AnonymousServiceState_DoesNotValidateBearerOrOpenAuthenticationStore()
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation(ApiRoutes.AUTHORIZATION_HEADER, "Bearer invalid");
            using HttpResponseMessage response = await client.GetAsync(ApiRoutes.SERVICE_STATE);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, factory.Repository.AccessCalls);
        }
        [Theory]
        [InlineData("{")]
        [InlineData("{\"inviteCode\":\"secret-sentinel\",\"build\":{\"protocolVersion\":\"not-an-integer\"}}")]
        public async Task Exchange_MalformedJsonOrFieldType_ReturnsSharedRejectionWithoutEchoingToken(string json)
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                new StringContent(json, Encoding.UTF8, "application/json"));
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_INVITE_CODE, Read<ApiRejection>(body).ReasonCode);
            Assert.DoesNotContain("secret-sentinel", body);
            Assert.DoesNotContain("errors", body);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal(0, factory.Repository.ExchangeCalls);
        }
        [Fact]
        public async Task Consume_MalformedJson_ReturnsSharedRejectionBeforeConsuming()
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));
            using HttpResponseMessage response = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                new StringContent("{", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, factory.Repository.ConsumeCalls);
        }
        [Theory]
        [InlineData(ContractCodes.RejectReason.TOKEN_EXPIRED, HttpStatusCode.Unauthorized)]
        [InlineData(ContractCodes.RejectReason.TOKEN_REVOKED, HttpStatusCode.Unauthorized)]
        [InlineData(ContractCodes.RejectReason.SESSION_MISMATCH, HttpStatusCode.Forbidden)]
        [InlineData(ContractCodes.RejectReason.ALREADY_CONNECTED, HttpStatusCode.Conflict)]
        [InlineData(ContractCodes.RejectReason.ALREADY_SETTLED, HttpStatusCode.Conflict)]
        [InlineData(ContractCodes.RejectReason.MAINTENANCE, HttpStatusCode.ServiceUnavailable)]
        [InlineData(ContractCodes.RejectReason.SANCTIONED, HttpStatusCode.Forbidden)]
        public async Task ExchangeJoin_RepositoryRejection_PreservesSharedReason(int reason, HttpStatusCode expectedStatus)
        {
            using AuthFactory factory = new AuthFactory();
            factory.Repository.ConsumeReason = reason;
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));
            using HttpResponseMessage response = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                JsonBody(new JoinTokenExchangeRequest(_token, factory.Repository.Server.InstanceId.ToString("D"),
                    factory.Repository.Server.Role)));
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(reason, Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        }

        [Fact]
        public async Task ExchangeJoin_MissingOrWrongHeaderAndRole_DoesNotConsume()
        {
            using AuthFactory factory = new AuthFactory();
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            using HttpResponseMessage missing = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                JsonBody(new JoinTokenExchangeRequest(_token, factory.Repository.Server.InstanceId.ToString("D"),
                    factory.Repository.Server.Role)));
            Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, Guid.NewGuid().ToString("D"));
            using HttpResponseMessage wrongHeader = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                JsonBody(new JoinTokenExchangeRequest(_token, factory.Repository.Server.InstanceId.ToString("D"),
                    factory.Repository.Server.Role)));
            Assert.Equal(HttpStatusCode.Forbidden, wrongHeader.StatusCode);
            client.DefaultRequestHeaders.Remove(JoinApiRoutes.SERVER_INSTANCE_HEADER);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));
            using HttpResponseMessage wrongRole = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                JsonBody(new JoinTokenExchangeRequest(_token, factory.Repository.Server.InstanceId.ToString("D"),
                    ContractCodes.ServerRole.TOWN)));
            Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
            Assert.Equal(0, factory.Repository.ConsumeCalls);
        }

        [Fact]
        public async Task ExchangeJoin_Maintenance_DoesNotConsumeAndLegacyRouteIsAbsent()
        {
            using AuthFactory factory = new AuthFactory(maintenance: true);
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));
            using HttpResponseMessage maintenance = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                JsonBody(new JoinTokenExchangeRequest(_token, factory.Repository.Server.InstanceId.ToString("D"),
                    factory.Repository.Server.Role)));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, maintenance.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.MAINTENANCE,
                Read<ApiRejection>(await maintenance.Content.ReadAsStringAsync()).ReasonCode);
            using HttpResponseMessage legacy = await client.PostAsync("/v1/auth/join/consume", JsonBody(new { }));
            Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
            Assert.Equal(0, factory.Repository.ConsumeCalls);
        }
        [Theory]
        [InlineData(ApiRoutes.AUTH_EXCHANGE)]
        [InlineData(ApiRoutes.TOWN_JOIN_TICKET)]
        public async Task Post_CredentialRequestLimit_ReturnsRateLimitedWithRetryAfter(string route)
        {
            using AuthFactory factory = new AuthFactory(provideDatabaseRepository: true);
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            for (int attempt = 0; attempt < AuthFailureLimiter.MAX_CREDENTIAL_REQUESTS_PER_WINDOW; ++attempt)
            {
                using HttpResponseMessage allowed = await client.PostAsync(route, JsonBody(new { }));
                Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
            }

            using HttpResponseMessage response = await client.PostAsync(route, JsonBody(new { }));

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(TimeSpan.FromSeconds(AuthFailureLimiter.WINDOW_SECONDS), response.Headers.RetryAfter!.Delta);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal(0, factory.Repository.ExchangeCalls);
        }

        /// <summary> 서버 임대 경로는 IP+인스턴스별 전용 한도를 쓴다. 같은 IP의 다른 인스턴스는 한도를 나눠 쓰지 않는다 </summary>
        [Fact]
        public async Task Lease_RequestLimit_IsPerInstanceAndHigherThanCredentialLimit()
        {
            using AuthFactory factory = new AuthFactory(provideDatabaseRepository: true);
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            string instance = Guid.NewGuid().ToString("D");
            for (int attempt = 0; attempt < AuthFailureLimiter.MAX_SERVER_LEASE_REQUESTS_PER_WINDOW; ++attempt)
            {
                using HttpRequestMessage allowed = LeaseRequest(instance);
                using HttpResponseMessage response = await client.SendAsync(allowed);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            using HttpRequestMessage over = LeaseRequest(instance);
            using HttpResponseMessage limited = await client.SendAsync(over);
            using HttpRequestMessage other = LeaseRequest(Guid.NewGuid().ToString("D"));
            using HttpResponseMessage otherInstance = await client.SendAsync(other);

            Assert.True(AuthFailureLimiter.MAX_SERVER_LEASE_REQUESTS_PER_WINDOW
                > AuthFailureLimiter.MAX_CREDENTIAL_REQUESTS_PER_WINDOW);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                Read<ApiRejection>(await limited.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, otherInstance.StatusCode);
        }

        private static HttpRequestMessage LeaseRequest(string instance)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GameServerRoutes.SERVER_LEASE)
            {
                Content = JsonBody(new { })
            };
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, instance);
            return request;
        }

        [Fact]
        public async Task Exchange_AuthFailureLimit_ReturnsRateLimitedBeforeConsumingCredential()
        {
            using AuthFactory factory = new AuthFactory();
            factory.Repository.ExchangeReason = ContractCodes.RejectReason.INVALID_INVITE_CODE;
            using HttpClient client = factory.CreateClient();
            for (int attempt = 0; attempt < AuthFailureLimiter.MAX_FAILURES_PER_WINDOW; ++attempt)
            {
                using HttpResponseMessage failed = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                    JsonBody(new AuthExchangeRequest(_token, string.Empty, Build())));
                Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            }

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                JsonBody(new AuthExchangeRequest(_token, string.Empty, Build())));

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(TimeSpan.FromSeconds(AuthFailureLimiter.WINDOW_SECONDS), response.Headers.RetryAfter!.Delta);
            Assert.Equal(AuthFailureLimiter.MAX_FAILURES_PER_WINDOW, factory.Repository.ExchangeCalls);
        }
        [Fact]
        public async Task Lease_AuthFailureLimit_ReturnsRateLimitedWithoutDatabaseAccess()
        {
            using AuthFactory factory = new AuthFactory(provideDatabaseRepository: true);
            using HttpClient client = factory.CreateClient();
            Guid instance = Guid.NewGuid();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, instance.ToString("D"));
            ServerLeaseRequest request = new ServerLeaseRequest(instance.ToString("D"),
                ContractCodes.ServerRole.TOWN, Guid.NewGuid().ToString("D"));
            for (int attempt = 0; attempt < AuthFailureLimiter.MAX_FAILURES_PER_WINDOW; ++attempt)
            {
                using HttpResponseMessage failed = await client.PostAsync(GameServerRoutes.SERVER_LEASE, JsonBody(request));
                Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            }

            using HttpResponseMessage response = await client.PostAsync(GameServerRoutes.SERVER_LEASE, JsonBody(request));

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(TimeSpan.FromSeconds(AuthFailureLimiter.WINDOW_SECONDS), response.Headers.RetryAfter!.Delta);
        }

        /// <summary> 서비스 자격 실패로 막힌 IP라도 플레이어 자격 교환은 막히지 않는다. 두 실패는 다른 한도 인스턴스에서 센다 </summary>
        [Fact]
        public async Task Lease_ServiceCredentialFailures_DoNotBlockPlayerExchange()
        {
            using AuthFactory factory = new AuthFactory(provideDatabaseRepository: true);
            factory.Repository.ExchangeReason = ContractCodes.RejectReason.INVALID_INVITE_CODE;
            using HttpClient server = factory.CreateClient();
            Guid instance = Guid.NewGuid();
            server.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            server.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, instance.ToString("D"));
            ServerLeaseRequest request = new ServerLeaseRequest(instance.ToString("D"),
                ContractCodes.ServerRole.TOWN, Guid.NewGuid().ToString("D"));
            for (int attempt = 0; attempt <= AuthFailureLimiter.MAX_FAILURES_PER_WINDOW; ++attempt)
            {
                using HttpResponseMessage failed = await server.PostAsync(GameServerRoutes.SERVER_LEASE, JsonBody(request));
            }
            using HttpResponseMessage leaseBlocked = await server.PostAsync(GameServerRoutes.SERVER_LEASE, JsonBody(request));
            using HttpClient player = factory.CreateClient();

            using HttpResponseMessage exchange = await player.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                JsonBody(new AuthExchangeRequest(_token, string.Empty, Build())));

            Assert.Equal(HttpStatusCode.TooManyRequests, leaseBlocked.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, exchange.StatusCode);
            Assert.Equal(1, factory.Repository.ExchangeCalls);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{}")]
        [InlineData("{\"characterId\":\"invalid\"}")]
        [InlineData("{\"characterId\":\"00000000-0000-0000-0000-000000000000\"}")]
        public async Task IssueTownJoinTicket_InvalidBody_ReturnsSharedRejectionBeforeDatabase(string json)
        {
            using AuthFactory factory = new AuthFactory(provideDatabaseRepository: true);
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.TOWN_JOIN_TICKET,
                new StringContent(json, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
        }

        private static ClientBuildInfo Build(string build = BUILD, int protocol = PROTOCOL)
        {
            return new ClientBuildInfo(build, protocol, CONTENT);
        }

        private static StringContent JsonBody(object value)
        {
            return new StringContent(JsonConvert.SerializeObject(value, ContractJson.CreateSettings()),
                Encoding.UTF8, "application/json");
        }

        private static T Read<T>(string body)
        {
            return JsonConvert.DeserializeObject<T>(body, ContractJson.CreateSettings())!;
        }

        private sealed class AuthFactory : IDisposable
        {
            private readonly IHost _host;
            public FakeAuthRepository Repository { get; } = new FakeAuthRepository();

            public AuthFactory(bool maintenance = false, bool provideDatabaseRepository = false)
            {
                _host = new HostBuilder().ConfigureWebHost(builder =>
                {
                    builder.UseTestServer();
                    builder.ConfigureLogging(logging => logging.ClearProviders());
                    builder.ConfigureServices(services =>
                    {
                        services.AddShinshiAuth(new ConfigurationBuilder().Build());
                        services.AddSingleton<IAuthRepository>(Repository);
                        if (provideDatabaseRepository)
                        {
                            NpgsqlDataSource dataSource = NpgsqlDataSource.Create(
                                "Host=127.0.0.1;Port=1;Username=test;Database=test;Timeout=2");
                            services.AddSingleton(dataSource);
                            services.AddSingleton(new PostgresAuthRepository(dataSource,
                                Auth.AuthTokenHasherTests.CreateOptions()));
                        }
                        services.PostConfigure<AuthBuildOptions>(options =>
                        {
                            options.ProtocolVersion = PROTOCOL;
                            options.ContentHash     = CONTENT;
                        });
                        services.PostConfigure<ServiceStateOptions>(options =>
                        {
                            options.Maintenance     = maintenance;
                            options.AllowedBuildIds = new[] { BUILD };
                        });
                        services.AddControllers().AddApplicationPart(typeof(AuthExchangeController).Assembly)
                            .AddApplicationPart(typeof(AuthActorProbeController).Assembly)
                            .AddNewtonsoftJson(options => ContractJson.Configure(options.SerializerSettings));
                    });
                    builder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseShinshiAuthentication();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    });
                }).Start();
            }

            public HttpClient CreateClient() { return _host.GetTestClient(); }
            public void Dispose() { _host.Dispose(); }
        }
        private sealed class FakeAuthRepository : IAuthRepository
        {
            public PlayerAuthIdentity Player { get; } = new PlayerAuthIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            public ServerAuthIdentity Server { get; } = new ServerAuthIdentity(Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), ContractCodes.ServerRole.DUNGEON, long.MaxValue);
            public JoinAuthIdentity Join { get; } = new JoinAuthIdentity(Guid.NewGuid(), Guid.NewGuid(),
                ContractCodes.PlayerClass.SUMMONER, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            public int ExchangeReason { get; set; }
            public int ServerReason { get; set; }
            public int ConsumeReason { get; set; }
            public int ExchangeCalls { get; private set; }
            public int RotateCalls { get; private set; }
            public int AccessCalls { get; private set; }
            public int ConsumeCalls { get; private set; }
            public Guid ConsumedLease { get; private set; }

            public Task<AuthExchangeOutcome> ExchangeInviteAsync(string inviteCode, CancellationToken cancellationToken = default)
            {
                ++ExchangeCalls;
                return Task.FromResult(ExchangeReason == ContractCodes.RejectReason.NONE ? Success() : AuthExchangeOutcome.Rejected(ExchangeReason));
            }

            public Task<AuthExchangeOutcome> RotateRefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
            {
                ++RotateCalls;
                return Task.FromResult(Success());
            }

            public Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateAccessAsync(string accessToken,
                CancellationToken cancellationToken = default)
            {
                ++AccessCalls;
                return Task.FromResult(AuthValidationOutcome<PlayerAuthIdentity>.Success(Player));
            }

            public Task<AuthValidationOutcome<ServerAuthIdentity>> ValidateServerLeaseAsync(string serverLeaseToken,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(ServerReason == ContractCodes.RejectReason.NONE
                    ? AuthValidationOutcome<ServerAuthIdentity>.Success(Server)
                    : AuthValidationOutcome<ServerAuthIdentity>.Rejected(ServerReason));
            }

            public Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeJoinTokenAsync(string joinToken, Guid instanceId,
                int serverRole, Guid serverLeaseId, CancellationToken cancellationToken = default)
            {
                ++ConsumeCalls;
                ConsumedLease = serverLeaseId;
                return Task.FromResult(ConsumeReason == ContractCodes.RejectReason.NONE
                    ? AuthValidationOutcome<JoinAuthIdentity>.Success(Join)
                    : AuthValidationOutcome<JoinAuthIdentity>.Rejected(ConsumeReason));
            }

            public Task<AuthValidationOutcome<IssuedAuthToken>> IssueDungeonJoinTokenAsync(ServerAuthIdentity server,
                DungeonJoinGrant grant, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
            private AuthExchangeOutcome Success()
            {
                return AuthExchangeOutcome.Success(new AuthExchangeResponse(Player.AccountId.ToString("D"),
                    _token, long.MaxValue, _token, long.MaxValue, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            }
        }
    }

    [ApiController]
    public sealed class AuthActorProbeController : ControllerBase
    {
        public const string ROUTE = "/__tests/auth/player";

        [HttpGet(ROUTE)]
        public ActionResult<string> Get()
        {
            return new JsonResult(AuthActors.GetPlayer(HttpContext)!.AccountId.ToString("D"));
        }
    }
}





