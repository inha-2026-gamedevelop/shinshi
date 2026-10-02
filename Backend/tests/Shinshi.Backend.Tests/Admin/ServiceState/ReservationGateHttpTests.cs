// System
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 점검과 기능 차단이 새 출정 예약을 DB 효과 전에 거절하는지 Program 파이프라인에서 검사한다(닫힌 포트 DB) </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class ReservationGateHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CLOSED_DATABASE = "Host=127.0.0.1;Port=1;Username=test;Database=test;Timeout=2";

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData(true, null, GameSessionCodes.Mode.NORMAL, ContractCodes.RejectReason.MAINTENANCE)]
        [InlineData(false, ServiceFeatureKeys.DUNGEON_ENTRY, GameSessionCodes.Mode.NORMAL,
            ContractCodes.RejectReason.FEATURE_DISABLED)]
        [InlineData(false, ServiceFeatureKeys.BOSS_ENTRY, GameSessionCodes.Mode.BOSS,
            ContractCodes.RejectReason.FEATURE_DISABLED)]
        public async Task Reserve_MaintenanceOrDisabledFeature_RejectsBeforeDatabase(bool maintenance,
            string? disabledFeature, int mode, int expected)
        {
            using GateFactory factory = new GateFactory(maintenance, disabledFeature);
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            string operationId = Guid.NewGuid().ToString("D");
            ReservationRequest body = new ReservationRequest(operationId, mode, "forest", "content-1", new[]
            {
                new ReservationParticipant(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), 0,
                    Array.Empty<ReservationPotion>())
            });
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GameServerRoutes.RESERVATIONS)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()),
                    Encoding.UTF8, "application/json")
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));

            using HttpResponseMessage response = await client.SendAsync(request);
            ReservationResponse? reservation = JsonConvert.DeserializeObject<ReservationResponse>(
                await response.Content.ReadAsStringAsync(), ContractJson.CreateSettings());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.NotNull(reservation);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, reservation.Result.Status);
            Assert.Equal(expected, reservation.Result.ReasonCode);
            Assert.Equal(operationId, reservation.Result.OperationId);
            Assert.Equal(string.Empty, reservation.SessionId);
            Assert.Empty(reservation.Participants);
        }

        private sealed class GateFactory : WebApplicationFactory<Program>
        {
            private readonly bool _maintenance;
            private readonly string? _disabledFeature;

            public IntegrationAuthRepository Repository { get; } = new IntegrationAuthRepository();

            public GateFactory(bool maintenance, string? disabledFeature)
            {
                _maintenance     = maintenance;
                _disabledFeature = disabledFeature;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureTestServices(services =>
                {
                    // 연결하지 않는 데이터 소스. 게이트가 막지 못하면 예약 저장소가 이 DB에 닿아 실패한다
                    services.AddSingleton(NpgsqlDataSource.Create(CLOSED_DATABASE));
                    services.AddSingleton<IAuthRepository>(Repository);
                    services.PostConfigure<ServiceStateOptions>(options =>
                    {
                        options.Maintenance      = _maintenance;
                        options.DisabledFeatures = _disabledFeature == null
                            ? Array.Empty<string>() : new[] { _disabledFeature };
                    });
                });
            }
        }
    }
}
