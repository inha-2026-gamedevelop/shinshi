// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> 무작위 bearer가 DB에 닿지 않는지, IP별 인증 실패 한도, 정산안 상한을 DB 없이 검사한다 </summary>
    public sealed class AuthHardeningTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 연결 거부가 즉시 나는 닫힌 포트. 저장소가 DB를 열면 NpgsqlException이 난다
        private const string UNREACHABLE = "Host=127.0.0.1;Port=1;Username=test;Database=test;Timeout=2";
        private const int    TOKEN_BYTES = 32;
        private const int    EXTRA_ATTEMPTS = 5;
        private const long   SPLIT_QUANTITY = SettlementSanityLimits.MAX_ITEM_QUANTITY / 2 + 1;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void IsAuthentic_OwnForeignTamperedAndRandomTokens_OnlyAcceptsOwnTag()
        {
            AuthTokenHasher hasher = new AuthTokenHasher(AuthTokenHasherTests.CreateOptions());
            string own = hasher.CreateToken();
            char last = own[own.Length - 1];
            string tampered = own.Substring(0, own.Length - 2) + (own[own.Length - 2] == 'A' ? 'B' : 'A') + last;

            Assert.True(hasher.IsAuthentic(own));
            Assert.False(hasher.IsAuthentic(new AuthTokenHasher(AuthTokenHasherTests.CreateOptions()).CreateToken()));
            Assert.False(hasher.IsAuthentic(tampered));
            Assert.False(hasher.IsAuthentic(RandomToken()));
            Assert.False(hasher.IsAuthentic(null));
        }

        /// <summary> 형식은 맞지만 이 키로 만들지 않은 토큰은 모든 검증, 교환 경로에서 DB를 열기 전에 거절한다 </summary>
        [Fact]
        public async Task Repository_RandomWellFormedBearer_RejectsWithoutOpeningDatabase()
        {
            AuthTokenOptions options = AuthTokenHasherTests.CreateOptions();
            await using NpgsqlDataSource unreachable = NpgsqlDataSource.Create(UNREACHABLE);
            PostgresAuthRepository repository = new PostgresAuthRepository(unreachable, options);
            List<string> tokens = new List<string>
            {
                RandomToken(), new AuthTokenHasher(AuthTokenHasherTests.CreateOptions()).CreateToken()
            };

            foreach (string token in tokens)
            {
                Assert.True(AuthTokenHasher.IsWellFormed(token));
                Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, (await repository.ValidateAccessAsync(token)).ReasonCode);
                Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                    (await repository.ValidateServerLeaseAsync(token)).ReasonCode);
                Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                    (await repository.ValidateGameSessionAsync(token)).ReasonCode);
                Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, (await repository.ConsumeJoinTokenAsync(token,
                    Guid.NewGuid(), ContractCodes.ServerRole.DUNGEON, Guid.NewGuid())).ReasonCode);
                Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, (await repository.RotateRefreshAsync(token)).ReasonCode);
                Assert.Equal(ContractCodes.RejectReason.INVALID_INVITE_CODE,
                    (await repository.ExchangeInviteAsync(token)).ReasonCode);
            }

            // 같은 키로 만든 토큰은 DB 조회까지 간다. 위 거절이 연결 실패가 아니라 태그 검사였다는 대조군이다
            await Assert.ThrowsAnyAsync<NpgsqlException>(() =>
                repository.ValidateAccessAsync(new AuthTokenHasher(options).CreateToken()));
        }

        /// <summary> 한 IP의 인증 실패가 한도를 넘으면 이후 요청은 저장소를 부르지 않고 429로 거절한다 </summary>
        [Fact]
        public async Task Middleware_RepeatedAuthFailures_BlocksBeforeRepository()
        {
            IntegrationAuthRepository repository = new IntegrationAuthRepository();
            using IHost host = await StartHostAsync(repository);
            using HttpClient client = host.GetTestClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);

            List<HttpStatusCode> statuses = new List<HttpStatusCode>();
            for (int attempt = 0; attempt < AuthFailureLimiter.MAX_FAILURES_PER_WINDOW + EXTRA_ATTEMPTS; ++attempt)
            {
                using HttpResponseMessage response = await client.GetAsync(AuthActorProbeController.ROUTE);
                statuses.Add(response.StatusCode);
            }
            using HttpResponseMessage blocked = await client.GetAsync(AuthActorProbeController.ROUTE);
            ApiRejection rejection = JsonConvert.DeserializeObject<ApiRejection>(await blocked.Content.ReadAsStringAsync(),
                ContractJson.CreateSettings())!;

            Assert.All(statuses.GetRange(0, AuthFailureLimiter.MAX_FAILURES_PER_WINDOW),
                status => Assert.Equal(HttpStatusCode.Unauthorized, status));
            Assert.All(statuses.GetRange(AuthFailureLimiter.MAX_FAILURES_PER_WINDOW, EXTRA_ATTEMPTS),
                status => Assert.Equal(HttpStatusCode.TooManyRequests, status));
            Assert.Equal(AuthFailureLimiter.MAX_FAILURES_PER_WINDOW, repository.AccessCalls);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED, rejection.ReasonCode);
            Assert.NotNull(blocked.Headers.RetryAfter);
        }

        [Fact]
        public void Validator_SettlementOverSanityCaps_RejectsBatch()
        {
            Guid participant = Guid.NewGuid();
            SettlementItemInput allowed = Item(SettlementSanityLimits.ITEM_DEFINITION_PREFIX + "boss_material_common", 1);

            Assert.True(SessionEventValidator.IsValid(new[] { Ended(Grant(participant, 1, allowed)) }));
            Assert.False(SessionEventValidator.IsValid(new[]
            {
                Ended(Grant(participant, SettlementSanityLimits.MAX_SESSION_ESSENCE + 1))
            }));
            Assert.False(SessionEventValidator.IsValid(new[]
            {
                Ended(Grant(participant, 1, Item(allowed.DefinitionId, SettlementSanityLimits.MAX_ITEM_QUANTITY + 1)))
            }));
            Assert.False(SessionEventValidator.IsValid(new[]
            {
                Ended(Grant(participant, 1, Item(allowed.DefinitionId, SPLIT_QUANTITY), Item(allowed.DefinitionId, SPLIT_QUANTITY)))
            }));
            foreach (string definitionId in new[] { "boss-material", "item_", "Item_herb", "item_herb;drop", "currency" })
            {
                Assert.False(SessionEventValidator.IsValid(new[] { Ended(Grant(participant, 1, Item(definitionId, 1))) }));
            }
            Assert.False(SessionEventValidator.IsValid(new[]
            {
                new SessionEventInput
                {
                    EventSeq = 1, EventId = Guid.NewGuid(), EventKind = GameSessionCodes.EventKind.ESSENCE_ACCRUED,
                    ParticipantId = participant, Amount = SettlementSanityLimits.MAX_SESSION_ESSENCE + 1
                }
            }));
        }

        private static async Task<IHost> StartHostAsync(IntegrationAuthRepository repository)
        {
            IHost host = new HostBuilder().ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.AddShinshiAuth(new ConfigurationBuilder().Build());
                    services.AddSingleton<IAuthRepository>(repository);
                    services.AddControllers().AddApplicationPart(typeof(AuthActorProbeController).Assembly);
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

        private static string RandomToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(TOKEN_BYTES))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static SessionEventInput Ended(SettlementGrantInput grant)
        {
            return SessionEventTests.Ended(1, Guid.NewGuid(), new[] { grant });
        }

        private static SettlementGrantInput Grant(Guid participantId, long essence, params SettlementItemInput[] items)
        {
            return SessionEventTests.Grant(participantId, essence, items);
        }

        private static SettlementItemInput Item(string definitionId, long quantity)
        {
            return new SettlementItemInput
            {
                DefinitionId = definitionId, BindingKind = SessionTestEnvironment.BINDING, Quantity = quantity
            };
        }
    }
}
