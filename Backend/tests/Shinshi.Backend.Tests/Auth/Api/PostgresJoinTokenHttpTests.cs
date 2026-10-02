// System
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> 실제 PostgreSQL과 Program의 인증, 예약, 입장권 및 Join 교환 경로를 함께 검사한다 </summary>
    public sealed class PostgresJoinTokenHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SERVER_MINUTES = 15;
        private const int TOWN_PORT = 7777;
        private const int DUNGEON_PORT = 7780;
        private const string CONTENT = "join-http-test";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Exchange_ReservationIssuerAndRenewedDungeonLease_ReturnsSharedContractExactlyOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            IssuedAuthToken invite = await fixture.Auth.IssueInviteAsync(fixture.AccountId);
            AuthExchangeResponse login = (await fixture.Auth.ExchangeInviteAsync(invite.Token)).Response!;
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = factory.CreateClient();
            Authorize(town, fixture.TownLease.Token, fixture.TownId);

            ReservationResponse reservation = await ReserveAsync(town, fixture);
            using HttpResponseMessage issue = await town.PostAsync(string.Format(GameServerRoutes.JOIN_TICKETS_FORMAT, reservation.SessionId),
                new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
            JoinTicketsResponse issued = Read<JoinTicketsResponse>(await issue.Content.ReadAsStringAsync());
            JoinTicket ticket = Assert.Single(issued.Tickets);
            Assert.Equal(fixture.CharacterId.ToString("D"), ticket.CharacterId);
            Assert.True(ticket.ExpiresAtUnixMs > ticket.ServerTimeUnixMs);

            IssuedAuthToken renewed = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.DungeonId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(SERVER_MINUTES));
            using HttpClient dungeon = factory.CreateClient();
            Authorize(dungeon, renewed.Token, fixture.DungeonId);
            using HttpClient player = factory.CreateClient();
            Authorize(player, login.AccessToken, fixture.DungeonId);
            JoinTokenExchangeRequest request = new JoinTokenExchangeRequest(ticket.JoinToken,
                fixture.DungeonId.ToString("D"), ContractCodes.ServerRole.DUNGEON);
            using HttpResponseMessage playerDenied = await player.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE, Body(request));
            Assert.Equal(HttpStatusCode.Unauthorized, playerDenied.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                Read<ApiRejection>(await playerDenied.Content.ReadAsStringAsync()).ReasonCode);

            using HttpResponseMessage wrongRole = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(ticket.JoinToken, fixture.DungeonId.ToString("D"), ContractCodes.ServerRole.TOWN)));
            Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
            using HttpResponseMessage exchange = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE, Body(request));
            string responseBody = await exchange.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            JoinTokenExchangeResponse result = Read<JoinTokenExchangeResponse>(responseBody);
            Assert.Equal(fixture.CharacterId.ToString("D"), result.CharacterId);
            Assert.Equal(fixture.DungeonId.ToString("D"), result.InstanceId);
            Assert.Equal(ContractCodes.ServerRole.DUNGEON, result.ServerRole);
            Assert.Equal(ticket.ParticipantId, result.ParticipantId);
            Assert.Equal(reservation.SessionId, result.SortieId);
            Assert.True(result.ServerTimeUnixMs >= ticket.ServerTimeUnixMs);
            Assert.Equal(new[] { "accountLeaseId", "characterId", "instanceId", "participantId", "serverRole",
                    "serverTimeUnixMs", "sortieId" },
                JObject.Parse(responseBody).Properties().Select(property => property.Name).OrderBy(name => name).ToArray());
            Assert.Null(result.AccountLeaseId);

            using HttpResponseMessage replay = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE, Body(request));
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                Read<ApiRejection>(await replay.Content.ReadAsStringAsync()).ReasonCode);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM auth_join_tokens WHERE consumed_at IS NOT NULL;"));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM game_sessions;"));
        }

        [PostgresFact]
        public async Task IssueJoinTickets_ReservedAccountWithoutLogin_ReturnsTokenRevokedAndNoTickets()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = factory.CreateClient();
            Authorize(town, fixture.TownLease.Token, fixture.TownId);
            ReservationResponse reservation = await ReserveAsync(town, fixture);

            using HttpResponseMessage issue = await town.PostAsync(string.Format(GameServerRoutes.JOIN_TICKETS_FORMAT, reservation.SessionId),
                new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.Conflict, issue.StatusCode);
            JoinTicketsResponse body = Read<JoinTicketsResponse>(await issue.Content.ReadAsStringAsync());
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, body.ReasonCode);
            Assert.Empty(body.Tickets);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM auth_join_tokens;"));
        }

        private static async Task<ReservationResponse> ReserveAsync(HttpClient town, Fixture fixture)
        {
            ReservationRequest request = new ReservationRequest(Guid.NewGuid().ToString("D"), GameSessionCodes.Mode.NORMAL,
                CONTENT, CONTENT, new[] { new ReservationParticipant(fixture.AccountId.ToString("D"),
                    fixture.CharacterId.ToString("D"), 0, Array.Empty<ReservationPotion>()) });
            using HttpResponseMessage response = await town.PostAsync(GameServerRoutes.RESERVATIONS, Body(request));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ReservationResponse reservation = Read<ReservationResponse>(await response.Content.ReadAsStringAsync());
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reservation.Result.Status);
            return reservation;
        }

        private static async Task<Fixture> SeedAsync(PostgresTestDatabase database)
        {
            Fixture fixture = new Fixture(database.DataSource);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts(account_id) VALUES (@AccountId);
                INSERT INTO characters(character_id,account_id,class_id) VALUES (@CharacterId,@AccountId,@Class);
                INSERT INTO account_wallets(account_id) VALUES (@AccountId);
                INSERT INTO server_instances(instance_id,role,address,port,build_id,content_version,state,boot_id)
                    VALUES (@TownId,@Town,'127.0.0.1',@TownPort,@Content,@Content,'Idle',@TownBoot),
                        (@DungeonId,@Dungeon,'127.0.0.1',@DungeonPort,@Content,@Content,'Idle',@DungeonBoot);
                INSERT INTO account_leases(account_id,lease_id,instance_id,role,expires_at)
                    VALUES (@AccountId,@AccountLease,@TownId,@Town,clock_timestamp() + interval '15 minutes');
                """, new { fixture.AccountId, fixture.CharacterId, fixture.TownId, fixture.DungeonId,
                    Class = ContractCodes.PlayerClass.WARRIOR, Town = ContractCodes.ServerRole.TOWN,
                    Dungeon = ContractCodes.ServerRole.DUNGEON, TownPort = TOWN_PORT, DungeonPort = DUNGEON_PORT,
                    Content = CONTENT, TownBoot = Guid.NewGuid(), DungeonBoot = Guid.NewGuid(), AccountLease = Guid.NewGuid() });
            fixture.TownLease = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(SERVER_MINUTES));
            fixture.DungeonLease = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.DungeonId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(SERVER_MINUTES));
            return fixture;
        }

        private static void Authorize(HttpClient client, string token, Guid instance)
        {
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, instance.ToString("D"));
        }

        private static StringContent Body(object body)
        {
            return new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()), Encoding.UTF8,
                "application/json");
        }

        private static T Read<T>(string body)
        {
            return JsonConvert.DeserializeObject<T>(body, ContractJson.CreateSettings())!;
        }

        private sealed class ApiFactory : WebApplicationFactory<Program>
        {
            private readonly NpgsqlDataSource _dataSource;
            private readonly AuthTokenOptions _options;

            public ApiFactory(NpgsqlDataSource dataSource, AuthTokenOptions options)
            {
                _dataSource = dataSource;
                _options    = options;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<NpgsqlDataSource>();
                    services.AddSingleton(_dataSource);
                    services.PostConfigure<AuthTokenOptions>(options => options.HashKeyBase64 = _options.HashKeyBase64);
                    services.PostConfigure<ServiceStateOptions>(options => options.Maintenance = false);
                });
            }
        }

        private sealed class Fixture
        {
            public AuthTokenOptions Options { get; } = AuthTokenHasherTests.CreateOptions();
            public PostgresAuthRepository Auth { get; }
            public Guid AccountId { get; } = Guid.NewGuid();
            public Guid CharacterId { get; } = Guid.NewGuid();
            public Guid TownId { get; } = Guid.NewGuid();
            public Guid DungeonId { get; } = Guid.NewGuid();
            public IssuedAuthToken TownLease { get; set; } = null!;
            public IssuedAuthToken DungeonLease { get; set; } = null!;

            public Fixture(NpgsqlDataSource dataSource)
            {
                Auth = new PostgresAuthRepository(dataSource, Options);
            }
        }
    }
}