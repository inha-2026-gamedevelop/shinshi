// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> 게임 하위 세션 발급, 회전, 폐기의 HTTP 계약과 토큰 종류 구분을 대역 저장소로 검사한다 </summary>
    public sealed class GameSessionHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BUILD = "game-session-build";
        private const string CONTENT = "game-session-content";
        private const int PROTOCOL = 3;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task Issue_AccessToken_ReturnsGameSessionForAccount()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            using HttpClient client = factory.CreateClient(factory.Repository.AccessToken);

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION,
                JsonBody(new GameSessionIssueRequest(Build())));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            GameSessionIssueResponse result = Read<GameSessionIssueResponse>(await response.Content.ReadAsStringAsync());
            Assert.Equal(factory.Repository.Player.AccountId.ToString("D"), result.AccountId);
            Assert.True(factory.Repository.IsLiveGameSession(result.GameSessionToken));
            Assert.Equal(TimeSpan.FromMinutes(GameSessionController.GAME_SESSION_MINUTES), factory.Repository.LastDuration);
            Assert.Equal(1, factory.Repository.IssueCalls);
        }

        [Fact]
        public async Task Issue_GameSessionToken_IsForbiddenWithoutIssuing()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            string gameSession = factory.Repository.AddGameSession();
            using HttpClient client = factory.CreateClient(gameSession);

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION,
                JsonBody(new GameSessionIssueRequest(Build())));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, factory.Repository.IssueCalls);
        }

        [Fact]
        public async Task Issue_DisallowedBuildVersionOrMaintenance_RejectsWithoutIssuing()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            using HttpClient client = factory.CreateClient(factory.Repository.AccessToken);

            using HttpResponseMessage build = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION,
                JsonBody(new GameSessionIssueRequest(Build(build: "unknown-build"))));
            using HttpResponseMessage version = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION,
                JsonBody(new GameSessionIssueRequest(Build(protocol: PROTOCOL + 1))));
            using HttpResponseMessage malformed = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION,
                new StringContent("{", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.Forbidden, build.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.BUILD_NOT_ALLOWED,
                Read<ApiRejection>(await build.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.Forbidden, version.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.VERSION_MISMATCH,
                Read<ApiRejection>(await version.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.VERSION_MISMATCH,
                Read<ApiRejection>(await malformed.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, factory.Repository.IssueCalls);

            using GameSessionFactory maintenanceFactory = new GameSessionFactory(maintenance: true);
            using HttpClient maintenanceClient = maintenanceFactory.CreateClient(maintenanceFactory.Repository.AccessToken);
            using HttpResponseMessage maintenance = await maintenanceClient.PostAsync(ApiRoutes.AUTH_GAME_SESSION,
                JsonBody(new GameSessionIssueRequest(Build())));
            Assert.Equal(ContractCodes.RejectReason.MAINTENANCE,
                Read<ApiRejection>(await maintenance.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, maintenanceFactory.Repository.IssueCalls);
        }

        [Fact]
        public async Task Issue_ParentRevokedAfterValidation_ReturnsUnauthorized()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            factory.Repository.IssueReason = ContractCodes.RejectReason.TOKEN_REVOKED;
            using HttpClient client = factory.CreateClient(factory.Repository.AccessToken);

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION,
                JsonBody(new GameSessionIssueRequest(Build())));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
        }

        [Fact]
        public async Task Refresh_GameSessionToken_RotatesAndOldTokenIsRejected()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            string original = factory.Repository.AddGameSession();
            using HttpClient client = factory.CreateClient(original);

            using HttpResponseMessage rotated = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REFRESH, null);
            using HttpResponseMessage replay = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REFRESH, null);
            using HttpResponseMessage oldProbe = await client.GetAsync(AuthActorProbeController.ROUTE);

            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            GameSessionIssueResponse result = Read<GameSessionIssueResponse>(await rotated.Content.ReadAsStringAsync());
            Assert.NotEqual(original, result.GameSessionToken);
            Assert.Equal(factory.Repository.Player.AccountId.ToString("D"), result.AccountId);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, oldProbe.StatusCode);
            using HttpClient next = factory.CreateClient(result.GameSessionToken);
            using HttpResponseMessage newProbe = await next.GetAsync(AuthActorProbeController.ROUTE);
            Assert.Equal(HttpStatusCode.OK, newProbe.StatusCode);
        }

        [Fact]
        public async Task RefreshAndRevoke_AccessToken_AreRejectedAndAccessStaysValid()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            using HttpClient client = factory.CreateClient(factory.Repository.AccessToken);

            using HttpResponseMessage refresh = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REFRESH, null);
            using HttpResponseMessage revoke = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REVOKE, null);
            using HttpResponseMessage probe = await client.GetAsync(AuthActorProbeController.ROUTE);

            Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                Read<ApiRejection>(await refresh.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.Unauthorized, revoke.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                Read<ApiRejection>(await revoke.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, factory.Repository.RotateCalls);
            Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        }

        [Fact]
        public async Task Revoke_GameSessionToken_RejectsTokenAndRepeatIsNoContent()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            string gameSession = factory.Repository.AddGameSession();
            using HttpClient client = factory.CreateClient(gameSession);

            using HttpResponseMessage first = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REVOKE, null);
            using HttpResponseMessage probe = await client.GetAsync(AuthActorProbeController.ROUTE);
            using HttpResponseMessage repeat = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REVOKE, null);
            using HttpResponseMessage refresh = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REFRESH, null);

            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            Assert.Equal("no-store", first.Headers.CacheControl!.ToString());
            Assert.Equal(HttpStatusCode.Unauthorized, probe.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        [Fact]
        public async Task Revoke_ForgedOrMissingBearer_CountsFailureWithoutRepository()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            using HttpClient client = factory.CreateClient(new string('Z', factory.Repository.AccessToken.Length));
            using HttpClient anonymous = factory.CreateClient(null);

            using HttpResponseMessage forged = await client.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REVOKE, null);
            using HttpResponseMessage missing = await anonymous.PostAsync(ApiRoutes.AUTH_GAME_SESSION_REVOKE, null);

            Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.Equal(0, factory.Repository.RevokeCalls);
        }

        [Fact]
        public async Task GameSessionToken_IsAcceptedAsPlayerOnNormalRoute()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            string gameSession = factory.Repository.AddGameSession();
            using HttpClient client = factory.CreateClient(gameSession);

            using HttpResponseMessage response = await client.GetAsync(AuthActorProbeController.ROUTE);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(factory.Repository.Player.AccountId.ToString("D"),
                JsonConvert.DeserializeObject<string>(await response.Content.ReadAsStringAsync()));
            Assert.Equal(1, factory.Repository.GameValidateCalls);
        }

        [Fact]
        public async Task ExpiredAccessToken_DoesNotFallBackToGameSessionLookup()
        {
            using GameSessionFactory factory = new GameSessionFactory();
            factory.Repository.AccessReason = ContractCodes.RejectReason.TOKEN_EXPIRED;
            using HttpClient client = factory.CreateClient(factory.Repository.AccessToken);

            using HttpResponseMessage response = await client.GetAsync(AuthActorProbeController.ROUTE);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_EXPIRED,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, factory.Repository.GameValidateCalls);
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

        /****************************************
        *               Test Host
        ****************************************/

        private sealed class GameSessionFactory : IDisposable
        {
            private readonly IHost _host;
            public GameSessionRepository Repository { get; } = new GameSessionRepository();

            public GameSessionFactory(bool maintenance = false)
            {
                _host = new HostBuilder().ConfigureWebHost(builder =>
                {
                    builder.UseTestServer();
                    builder.ConfigureLogging(logging => logging.ClearProviders());
                    builder.ConfigureServices(services =>
                    {
                        services.AddShinshiAuth(new ConfigurationBuilder().Build());
                        services.AddSingleton<IAuthRepository>(Repository);
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
                        services.AddControllers().AddApplicationPart(typeof(GameSessionController).Assembly)
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

            public HttpClient CreateClient(string? bearer)
            {
                HttpClient client = _host.GetTestClient();
                if (bearer != null)
                {
                    client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + bearer);
                }

                return client;
            }

            public void Dispose() { _host.Dispose(); }
        }

        /// <summary> 접근 토큰 하나와 게임 하위 세션 표를 메모리에 둔다. 토큰은 실제 해셔로 만들어 태그 검사를 통과한다 </summary>
        private sealed class GameSessionRepository : IAuthRepository
        {
            private readonly AuthTokenHasher _hasher = new AuthTokenHasher(AuthTokenHasherTests.CreateOptions());
            private readonly HashSet<string> _liveGameSessions = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _knownGameSessions = new HashSet<string>(StringComparer.Ordinal);
            private readonly object _gate = new object();

            public PlayerAuthIdentity Player { get; } = new PlayerAuthIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            public string AccessToken { get; }
            public int AccessReason { get; set; }
            public int IssueReason { get; set; }
            public int IssueCalls { get; private set; }
            public int RotateCalls { get; private set; }
            public int RevokeCalls { get; private set; }
            public int GameValidateCalls { get; private set; }
            public TimeSpan LastDuration { get; private set; }

            public GameSessionRepository()
            {
                AccessToken = _hasher.CreateToken();
            }

            public string AddGameSession()
            {
                lock (_gate)
                {
                    string token = _hasher.CreateToken();
                    _liveGameSessions.Add(token);
                    _knownGameSessions.Add(token);
                    return token;
                }
            }

            public bool IsLiveGameSession(string token)
            {
                lock (_gate)
                {
                    return _liveGameSessions.Contains(token);
                }
            }

            public bool IsAuthenticToken(string token)
            {
                return _hasher.IsAuthentic(token);
            }

            public Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateAccessAsync(string accessToken,
                CancellationToken cancellationToken = default)
            {
                if (!string.Equals(accessToken, AccessToken, StringComparison.Ordinal))
                {
                    return Task.FromResult(AuthValidationOutcome<PlayerAuthIdentity>.Rejected(
                        ContractCodes.RejectReason.TOKEN_REVOKED));
                }

                return Task.FromResult(AccessReason == ContractCodes.RejectReason.NONE
                    ? AuthValidationOutcome<PlayerAuthIdentity>.Success(Player)
                    : AuthValidationOutcome<PlayerAuthIdentity>.Rejected(AccessReason));
            }

            public Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateGameSessionAsync(string gameSessionToken,
                CancellationToken cancellationToken = default)
            {
                ++GameValidateCalls;
                return Task.FromResult(IsLiveGameSession(gameSessionToken)
                    ? AuthValidationOutcome<PlayerAuthIdentity>.Success(Player)
                    : AuthValidationOutcome<PlayerAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED));
            }

            public Task<AuthValidationOutcome<IssuedAuthToken>> IssueGameSessionForPlayerAsync(
                PlayerAuthIdentity identity, TimeSpan duration, CancellationToken cancellationToken = default)
            {
                ++IssueCalls;
                LastDuration = duration;
                if (IssueReason != ContractCodes.RejectReason.NONE)
                {
                    return Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Rejected(IssueReason));
                }

                return Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Success(Issue(duration)));
            }

            public Task<AuthValidationOutcome<IssuedAuthToken>> RotateGameSessionAsync(string gameSessionToken,
                TimeSpan duration, CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    ++RotateCalls;
                    if (!_liveGameSessions.Remove(gameSessionToken))
                    {
                        return Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Rejected(
                            ContractCodes.RejectReason.TOKEN_REVOKED));
                    }
                }

                return Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Success(Issue(duration)));
            }

            public Task<bool> RevokeGameSessionAsync(string gameSessionToken, CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    ++RevokeCalls;
                    _liveGameSessions.Remove(gameSessionToken);
                    return Task.FromResult(_knownGameSessions.Contains(gameSessionToken));
                }
            }

            public Task<AuthExchangeOutcome> ExchangeInviteAsync(string inviteCode, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<AuthExchangeOutcome> RotateRefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<AuthValidationOutcome<ServerAuthIdentity>> ValidateServerLeaseAsync(string serverLeaseToken,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(AuthValidationOutcome<ServerAuthIdentity>.Rejected(
                    ContractCodes.RejectReason.TOKEN_REVOKED));
            }

            public Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeJoinTokenAsync(string joinToken, Guid instanceId,
                int serverRole, Guid serverLeaseId, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<AuthValidationOutcome<IssuedAuthToken>> IssueDungeonJoinTokenAsync(ServerAuthIdentity server,
                DungeonJoinGrant grant, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            private IssuedAuthToken Issue(TimeSpan duration)
            {
                string token = AddGameSession();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                return new IssuedAuthToken(token, Guid.NewGuid(), now.Add(duration).ToUnixTimeMilliseconds(),
                    now.ToUnixTimeMilliseconds());
            }
        }
    }
}
