// System
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Sessions;
using Shinshi.Data.Auth;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 게임 서버 전용 경로가 서버 자격 없이는 401, 다른 역할이면 403으로 효과 전에 막히는지 검사한다 </summary>
    public sealed class GameServerHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string EMPTY_BODY    = "{}";
        private const string JSON_MEDIA    = "application/json";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 플레이어 토큰처럼 서버 자격으로 인정되지 않는 호출은 모든 서버 경로에서 401이다 </summary>
        [Theory]
        [InlineData(GameServerRoutes.RESERVATIONS)]
        [InlineData("/v1/game-sessions/7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a001/start")]
        [InlineData("/v1/game-sessions/7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a001/events")]
        [InlineData("/v1/game-sessions/7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a001/settlement")]
        [InlineData("/v1/game-sessions/7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a001/cancel")]
        [InlineData(LifeRoutes.SNAPSHOT)]
        [InlineData(LifeRoutes.GATHERS)]
        [InlineData(LifeRoutes.POTION_CRAFTS)]
        public async Task Post_WithoutServerCredential_Returns401(string route)
        {
            using GameServerApplicationFactory factory = new GameServerApplicationFactory();
            using HttpClient client = CreateClient(factory);

            using HttpResponseMessage response = await client.PostAsync(route, Json(EMPTY_BODY));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary> 마을 서버는 던전 경로를, 던전 서버는 마을 경로를 호출할 수 없다 </summary>
        [Theory]
        [InlineData(GameServerRoutes.RESERVATIONS, 1)]
        [InlineData("/v1/game-sessions/7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a001/start", 0)]
        [InlineData("/v1/game-sessions/7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a001/settlement", 0)]
        [InlineData(LifeRoutes.GATHERS, 1)]
        [InlineData(LifeRoutes.POTION_CRAFTS, 1)]
        public async Task Post_WrongServerRole_Returns403(string route, int role)
        {
            using GameServerApplicationFactory factory = new GameServerApplicationFactory(role);
            using HttpClient client = CreateClient(factory);
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, route) { Content = Json(EMPTY_BODY) };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        /// <summary> 정적 정기 상한 초과는 DB 연결 전에 공통 거절 DTO의 904로 응답한다 </summary>
        [Fact]
        public async Task Post_EssenceAboveLimit_Returns904BeforeDatabase()
        {
            using GameServerApplicationFactory factory = new GameServerApplicationFactory(ContractCodes.ServerRole.DUNGEON);
            using HttpClient client = CreateClient(factory);
            SessionEventEntry entry = new SessionEventEntry(1, Guid.NewGuid().ToString("D"),
                GameSessionCodes.EventKind.ESSENCE_ACCRUED, Guid.NewGuid().ToString("D"), string.Empty, string.Empty,
                SettlementSanityLimits.MAX_SESSION_ESSENCE + 1, 0, string.Empty, Array.Empty<SettlementGrant>(), string.Empty);

            using HttpResponseMessage response = await PostEventsAsync(client, factory, entry);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            ApiRejection rejection = JsonConvert.DeserializeObject<ApiRejection>(
                await response.Content.ReadAsStringAsync(), ContractJson.CreateSettings())!;
            Assert.Equal(ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED, rejection.ReasonCode);
        }

        /// <summary> 정산 아이템의 정적 수량 상한 초과도 같은 904를 반환한다 </summary>
        [Fact]
        public async Task Post_ItemGrantAboveLimit_Returns904BeforeDatabase()
        {
            using GameServerApplicationFactory factory = new GameServerApplicationFactory(ContractCodes.ServerRole.DUNGEON);
            using HttpClient client = CreateClient(factory);
            SessionEventEntry entry = EndedGrant("item_boss_material_common", SettlementSanityLimits.MAX_ITEM_QUANTITY + 1);

            using HttpResponseMessage response = await PostEventsAsync(client, factory, entry);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            ApiRejection rejection = JsonConvert.DeserializeObject<ApiRejection>(
                await response.Content.ReadAsStringAsync(), ContractJson.CreateSettings())!;
            Assert.Equal(ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED, rejection.ReasonCode);
        }

        /// <summary> 티켓을 일반 아이템 정산으로 지급하는 서버 입력은 DB에 닿기 전에 거절한다 </summary>
        [Fact]
        public async Task Post_TicketGrant_ReturnsBadRequestBeforeDatabase()
        {
            using GameServerApplicationFactory factory = new GameServerApplicationFactory(ContractCodes.ServerRole.DUNGEON);
            using HttpClient client = CreateClient(factory);
            SessionEventEntry entry = EndedGrant(SettlementSanityLimits.TICKET_DEFINITION_ID, 1);

            using HttpResponseMessage response = await PostEventsAsync(client, factory, entry);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary> 정산안 지갑 칸 상한 초과는 컨트롤러 변환을 거쳐 DB 전에 904로 거절한다 </summary>
        [Fact]
        public async Task Post_WalletGrantAboveLimit_Returns904BeforeDatabase()
        {
            using GameServerApplicationFactory factory = new GameServerApplicationFactory(ContractCodes.ServerRole.DUNGEON);
            using HttpClient client = CreateClient(factory);
            SettlementGrant grant = new SettlementGrant(Guid.NewGuid().ToString("D"), 0,
                Array.Empty<SettlementItemGrant>(), 0, SettlementSanityLimits.MAX_SESSION_TICKETS + 1, 0,
                Array.Empty<SettlementItemGrant>());

            using HttpResponseMessage response = await PostEventsAsync(client, factory, Ended(grant));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            ApiRejection rejection = JsonConvert.DeserializeObject<ApiRejection>(
                await response.Content.ReadAsStringAsync(), ContractJson.CreateSettings())!;
            Assert.Equal(ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED, rejection.ReasonCode);
        }

        /// <summary> 계정 귀속이 아닌 부적 줄은 형식 오류로 DB 전에 거절한다(티켓 아이템 줄과 같은 400) </summary>
        [Fact]
        public async Task Post_TalismanWithCharacterBinding_ReturnsBadRequestBeforeDatabase()
        {
            using GameServerApplicationFactory factory = new GameServerApplicationFactory(ContractCodes.ServerRole.DUNGEON);
            using HttpClient client = CreateClient(factory);
            SettlementGrant grant = new SettlementGrant(Guid.NewGuid().ToString("D"), 0,
                Array.Empty<SettlementItemGrant>(), 0, 0, 0, new[] { new SettlementItemGrant("T01-1A", "Character", 1) });

            using HttpResponseMessage response = await PostEventsAsync(client, factory, Ended(grant));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary> 공유 경로 상수와 서버 라우트 템플릿이 같은 URL을 만든다 </summary>
        [Fact]
        public void RouteTemplates_MatchSharedFormats()
        {
            string id = Guid.NewGuid().ToString("D");

            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_HEARTBEAT_FORMAT, id),
                GameServerRouteTemplates.SERVER_HEARTBEAT.Replace("{instanceId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_DRAIN_FORMAT, id),
                GameServerRouteTemplates.SERVER_DRAIN.Replace("{instanceId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.RESERVATION_CANCEL_FORMAT, id),
                GameServerRouteTemplates.RESERVATION_CANCEL.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.JOIN_TICKETS_FORMAT, id),
                GameServerRouteTemplates.JOIN_TICKETS.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.LEASE_TRANSFER_FORMAT, id),
                GameServerRouteTemplates.LEASE_TRANSFER.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.START_FORMAT, id),
                GameServerRouteTemplates.START.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.EVENTS_FORMAT, id),
                GameServerRouteTemplates.EVENTS.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.CHECKPOINTS_FORMAT, id),
                GameServerRouteTemplates.CHECKPOINTS.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SETTLEMENT_FORMAT, id),
                GameServerRouteTemplates.SETTLEMENT.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTIONS_FORMAT, id),
                GameServerRouteTemplates.AUCTIONS.Replace("{sessionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTION_BIDS_FORMAT, id, id),
                GameServerRouteTemplates.AUCTION_BIDS.Replace("{sessionId}", id).Replace("{auctionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTION_CLOSE_FORMAT, id, id),
                GameServerRouteTemplates.AUCTION_CLOSE.Replace("{sessionId}", id).Replace("{auctionId}", id));
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTION_FINISH_FORMAT, id),
                GameServerRouteTemplates.AUCTION_FINISH.Replace("{sessionId}", id));
        }

        private static async Task<HttpResponseMessage> PostEventsAsync(HttpClient client, GameServerApplicationFactory factory,
            SessionEventEntry entry)
        {
            string route = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.EVENTS_FORMAT,
                Guid.NewGuid().ToString("D"));
            SessionEventBatchRequest batch = new SessionEventBatchRequest(Guid.NewGuid().ToString("D"), new[] { entry });
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = Json(JsonConvert.SerializeObject(batch, ContractJson.CreateSettings()))
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));
            return await client.SendAsync(request);
        }

        private static SessionEventEntry EndedGrant(string definitionId, long quantity)
        {
            SettlementGrant grant = new SettlementGrant(Guid.NewGuid().ToString("D"), 0,
                new[] { new SettlementItemGrant(definitionId, SessionTestEnvironment.BINDING, quantity) });
            return Ended(grant);
        }

        private static SessionEventEntry Ended(SettlementGrant grant)
        {
            return new SessionEventEntry(1, Guid.NewGuid().ToString("D"), GameSessionCodes.EventKind.SESSION_ENDED,
                string.Empty, string.Empty, string.Empty, 0, GameSessionCodes.EndReason.SUCCESS,
                Guid.NewGuid().ToString("D"), new[] { grant }, string.Empty);
        }

        private static HttpClient CreateClient(GameServerApplicationFactory factory)
        {
            return factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        }

        private static StringContent Json(string body)
        {
            return new StringContent(body, Encoding.UTF8, JSON_MEDIA);
        }

        private sealed class GameServerApplicationFactory : WebApplicationFactory<Program>
        {
            public IntegrationAuthRepository Repository { get; } = new IntegrationAuthRepository();

            public GameServerApplicationFactory(int role = ContractCodes.ServerRole.TOWN)
            {
                Repository.Server = new ServerAuthIdentity(Repository.Server.InstanceId, Repository.Server.LeaseId,
                    Repository.Server.ServiceId, role, long.MaxValue);
            }
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureTestServices(services =>
                {
                    // 연결하지 않는 데이터 소스. 인증 가드가 효과 전에 막으므로 DB에 닿지 않는다
                    services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=test;Database=test"));
                    services.AddSingleton<IAuthRepository>(Repository);
                    services.AddSingleton<IJoinTokenIssuer, FakeJoinTokenIssuer>();
                    services.AddSingleton<IEconomyLedgerWriter, TestLedgerWriter>();

                });
            }
        }

    }
}
