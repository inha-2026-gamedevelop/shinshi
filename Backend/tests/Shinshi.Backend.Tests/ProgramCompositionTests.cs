// System
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Console;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Backend.Growth;
using Shinshi.Backend.Ledger;
using Shinshi.Backend.Observability;
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Characters;
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;
using Shinshi.Data.Life;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests
{
    /// <summary> 실제 Program의 모듈 등록과 인증 미들웨어 순서를 검증한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class ProgramCompositionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 형식은 맞지만 어떤 키로도 만들지 않은 bearer
        private const string RANDOM_BEARER = "q0b9ZqzYlqL1x5Vv7m3n0eFz3Qb2h1sT4cYp8kW6a9E";

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Main_ComposedModules_ResolveAndMaintenanceFollowsRuntimeDatabase(bool runtimeDatabase)
        {
            using CompositionFactory factory = new CompositionFactory(runtimeDatabase);
            using HttpClient client = factory.CreateClient();

            Assert.IsType<PostgresAuthRepository>(factory.Services.GetRequiredService<IAuthRepository>());
            Assert.IsType<ServerCallerAuthenticator>(factory.Services.GetRequiredService<IServerCallerAuthenticator>());
            Assert.IsType<JoinTokenIssuer>(factory.Services.GetRequiredService<IJoinTokenIssuer>());
            Assert.IsType<LedgerWriter>(factory.Services.GetRequiredService<IEconomyLedgerWriter>());
            Assert.Same(factory.Services.GetRequiredService<PostgresEconomyLedgerWriter>(),
                factory.Services.GetRequiredService<IEconomyLedgerEntryWriter>());
            Assert.NotNull(factory.Services.GetRequiredService<PostgresReservationRepository>());
            Assert.NotNull(factory.Services.GetRequiredService<PostgresGameSessionRepository>());
            Assert.NotNull(factory.Services.GetRequiredService<PostgresSettlementRepository>());
            Assert.Equal(runtimeDatabase, factory.Services.GetServices<IHostedService>()
                .Any(service => service is GameSessionMaintenanceService));

            // 성장 모듈과 초기 지급 프로필(기본 Trial). 일일 대사는 DB 연결이 있을 때만 돈다
            Assert.NotNull(factory.Services.GetRequiredService<PostgresTalismanRepository>());
            Assert.NotNull(factory.Services.GetRequiredService<PostgresEquipmentRepository>());
            Assert.NotNull(factory.Services.GetRequiredService<PostgresTrainingRepository>());
            Assert.NotNull(factory.Services.GetRequiredService<PostgresAccountProgressRepository>());
            Assert.Equal(EGrowthContentProfile.Trial, factory.Services.GetRequiredService<InitialGrantService>().Profile);
            Assert.IsType<PostgresCharacterRepository>(factory.Services.GetRequiredService<ICharacterRepository>());
            Assert.Equal(runtimeDatabase, factory.Services.GetServices<IHostedService>()
                .Any(service => service is EconomyReconciliationService));

            using HttpResponseMessage state = await client.GetAsync(ApiRoutes.SERVICE_STATE);
            Assert.Equal(HttpStatusCode.OK, state.StatusCode);
            using HttpResponseMessage denied = await client.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE, null);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            // 닫힌 포트의 DB라 조회하면 500이 난다. 틀린 서비스 자격과 태그 없는 bearer는 DB 전에 401이어야 한다
            Guid instanceId = Guid.NewGuid();
            using HttpRequestMessage lease = new HttpRequestMessage(HttpMethod.Post, GameServerRoutes.SERVER_LEASE)
            {
                Content = new StringContent(JsonConvert.SerializeObject(new ServerLeaseRequest(instanceId.ToString("D"),
                    ContractCodes.ServerRole.DUNGEON, Guid.NewGuid().ToString("D")), ContractJson.CreateSettings()),
                    Encoding.UTF8, "application/json")
            };
            lease.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + RANDOM_BEARER);
            lease.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, instanceId.ToString("D"));
            using HttpResponseMessage leaseDenied = await client.SendAsync(lease);
            Assert.Equal(HttpStatusCode.Unauthorized, leaseDenied.StatusCode);
            using HttpRequestMessage rejoin = new HttpRequestMessage(HttpMethod.Post,
                string.Format(ApiRoutes.REJOIN_TICKET_FORMAT, Guid.NewGuid().ToString("D")));
            rejoin.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + RANDOM_BEARER);
            using HttpResponseMessage rejoinDenied = await client.SendAsync(rejoin);
            Assert.Equal(HttpStatusCode.Unauthorized, rejoinDenied.StatusCode);

            // 요청 로그는 인증 미들웨어보다 앞에 있어 효과 전에 401로 끝난 요청도 시간 지표에 남는다
            Assert.NotNull(factory.Services.GetRequiredService<PostgresLifeRepository>());
            Assert.Contains(factory.Services.GetServices<ConsoleFormatter>(),
                formatter => formatter is ShinshiJsonConsoleFormatter);
            Assert.Contains(factory.Services.GetServices<IHostedService>(),
                service => service is MetricsReporterService);
            Assert.Equal(4, await WaitForSamplesAsync(factory.Services.GetRequiredService<ApiMetrics>(), 4));
        }

        /// <summary> 요청 로그 미들웨어가 전달 헤더 처리 뒤, 라우팅과 인증보다 앞이다. 끝난 요청은 라우팅이 고른 경로 템플릿으로 남는다 </summary>
        [Fact]
        public async Task Main_RequestLogging_WrapsRoutingAndAuthentication()
        {
            using CompositionFactory factory = new CompositionFactory(false);
            using HttpClient client = factory.CreateClient();
            ApiMetrics metrics = factory.Services.GetRequiredService<ApiMetrics>();

            using HttpResponseMessage unmatched = await client.GetAsync("/v1/not-a-route?token=secret");
            using HttpResponseMessage denied = await client.PostAsync(LifeRoutes.GATHERS, null);

            Assert.Equal(HttpStatusCode.NotFound, unmatched.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal(2, await WaitForSamplesAsync(metrics, 2));
        }

        // TestServer는 응답을 돌려준 뒤 미들웨어의 마무리(지표 기록)를 이어 실행할 수 있어 잠시 기다린다
        private static async Task<int> WaitForSamplesAsync(ApiMetrics metrics, int expected)
        {
            const int ATTEMPTS = 100;
            const int DELAY_MS = 20;
            int count = metrics.GetSnapshot().SampleCount;
            for (int attempt = 0; (attempt < ATTEMPTS) && (count < expected); ++attempt)
            {
                await Task.Delay(DELAY_MS);
                count = metrics.GetSnapshot().SampleCount;
            }

            return count;
        }

        private sealed class CompositionFactory : WebApplicationFactory<Program>
        {
            private readonly string? _previousConnection;

            public CompositionFactory(bool runtimeDatabase)
            {
                _previousConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
                // 최소 호스트는 Program 등록 뒤 TestServer 설정을 적용하므로 시작 전에 환경을 주입한다
                Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", runtimeDatabase
                    ? "Host=127.0.0.1;Port=1;Username=test;Database=test" : null);
            }

            protected override void Dispose(bool disposing)
            {
                try
                {
                    base.Dispose(disposing);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _previousConnection);
                }
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=test;Database=test"));
                    services.PostConfigure<AuthTokenOptions>(options =>
                        options.HashKeyBase64 = Convert.ToBase64String(new byte[32]));
                });
            }
        }
    }

    // 프로세스 환경을 쓰는 호스트 시험은 다른 테스트와 동시에 실행하지 않는다
    [CollectionDefinition(NAME, DisableParallelization = true)]
    public sealed class ProgramEnvironmentCollection
    {
        public const string NAME = "Program environment";
    }
}
