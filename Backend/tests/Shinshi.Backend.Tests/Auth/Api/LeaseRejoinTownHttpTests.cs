// System
using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
    /// <summary> 실제 PostgreSQL과 Program으로 서버 임대 첫 등록, 진행 중 출정 복귀 입장권, 마을 입장 토큰 교환을 검사한다 </summary>
    public sealed class LeaseRejoinTownHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    SERVER_MINUTES = 15;
        private const int    TOWN_PORT      = 7777;
        private const int    DUNGEON_PORT   = 7780;
        private const string CONTENT        = "0.1.4";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 행이 없는 인스턴스도 서비스 자격으로 임대를 받아 등록하고, 임대는 발급 boot에만 쓰이며 Offline boot는 다시 받지 못한다 </summary>
        [PostgresFact]
        public async Task Lease_FirstRegistration_IssuesLeaseUsableForRegisterOnly()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AuthTokenOptions options = AuthTokenHasherTests.CreateOptions();
            string credential = new AuthTokenHasher(AuthTokenHasherTests.CreateOptions()).CreateToken();
            ServiceCredentialRegistration registration = new ServiceCredentialRegistration
            {
                ServiceId = Guid.NewGuid(), InstanceId = Guid.NewGuid(), Role = ContractCodes.ServerRole.DUNGEON,
                CredentialHashBase64 = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(credential)))
            };
            Guid instanceId = registration.InstanceId;
            Guid boot = Guid.NewGuid();
            using ApiFactory factory = new ApiFactory(database.DataSource, options, registration);
            using HttpClient service = factory.CreateClient();
            Authorize(service, credential, instanceId);

            using HttpResponseMessage wrongRole = await service.PostAsync(GameServerRoutes.SERVER_LEASE,
                Body(new ServerLeaseRequest(instanceId.ToString("D"), ContractCodes.ServerRole.TOWN, boot.ToString("D"))));
            using HttpResponseMessage leased = await service.PostAsync(GameServerRoutes.SERVER_LEASE,
                Body(new ServerLeaseRequest(instanceId.ToString("D"), ContractCodes.ServerRole.DUNGEON, boot.ToString("D"))));
            Assert.Equal(HttpStatusCode.OK, leased.StatusCode);
            ServerLeaseResponse lease = Read<ServerLeaseResponse>(await leased.Content.ReadAsStringAsync());
            using HttpClient server = factory.CreateClient();
            Authorize(server, lease.LeaseToken, instanceId);
            using HttpResponseMessage registered = await server.PostAsync(GameServerRoutes.SERVER_REGISTER,
                Body(new ServerRegisterRequest(instanceId.ToString("D"), boot.ToString("D"), ContractCodes.ServerRole.DUNGEON,
                    "127.0.0.1", DUNGEON_PORT, CONTENT, CONTENT)));
            using HttpResponseMessage otherBoot = await server.PostAsync(
                string.Format(GameServerRoutes.SERVER_HEARTBEAT_FORMAT, instanceId.ToString("D")),
                Body(new ServerHeartbeatRequest(Guid.NewGuid().ToString("D"))));
            using HttpResponseMessage heartbeat = await server.PostAsync(
                string.Format(GameServerRoutes.SERVER_HEARTBEAT_FORMAT, instanceId.ToString("D")),
                Body(new ServerHeartbeatRequest(boot.ToString("D"))));

            using HttpClient intruder = factory.CreateClient();
            Authorize(intruder, new AuthTokenHasher(options).CreateToken(), instanceId);
            using HttpResponseMessage wrongCredential = await intruder.PostAsync(GameServerRoutes.SERVER_LEASE,
                Body(new ServerLeaseRequest(instanceId.ToString("D"), ContractCodes.ServerRole.DUNGEON, boot.ToString("D"))));
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("UPDATE server_instances SET state = 'Offline' WHERE instance_id = @Id;",
                    new { Id = instanceId });
            }
            using HttpResponseMessage deadBoot = await service.PostAsync(GameServerRoutes.SERVER_LEASE,
                Body(new ServerLeaseRequest(instanceId.ToString("D"), ContractCodes.ServerRole.DUNGEON, boot.ToString("D"))));

            Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
            Assert.True(lease.ExpiresAtUnixMs > lease.ServerTimeUnixMs);
            Assert.Equal("no-store", leased.Headers.CacheControl!.ToString());
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
            Assert.Equal(GameSessionCodes.InstanceState.IDLE,
                Read<ServerInstanceResponse>(await registered.Content.ReadAsStringAsync()).State);
            Assert.Equal(HttpStatusCode.Conflict, otherBoot.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                Read<ApiRejection>(await otherBoot.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongCredential.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, deadBoot.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_TERMINAL,
                Read<ApiRejection>(await deadBoot.Content.ReadAsStringAsync()).ReasonCode);
        }

        /// <summary> Running 출정의 Active 참가자 본인만 복귀 입장권을 받고, 다시 받으면 이전 토큰은 폐기되며 새 토큰은 한 번 교환된다 </summary>
        [PostgresFact]
        public async Task Rejoin_RunningActiveParticipant_IssuesSingleUseTicketForOwnerOnly()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options, null);
            using HttpClient town = factory.CreateClient();
            Authorize(town, fixture.TownLease.Token, fixture.TownId);
            using HttpClient dungeon = factory.CreateClient();
            Authorize(dungeon, fixture.DungeonLease.Token, fixture.DungeonId);
            using HttpClient player = factory.CreateClient();
            Authorize(player, await GameTokenAsync(fixture, fixture.AccountId), fixture.DungeonId);
            using HttpClient stranger = factory.CreateClient();
            Authorize(stranger, await GameTokenAsync(fixture, fixture.OtherAccountId), fixture.DungeonId);

            ReservationResponse reservation = await ReserveAsync(town, fixture);
            string rejoinPath = string.Format(ApiRoutes.REJOIN_TICKET_FORMAT, reservation.SessionId);
            using HttpResponseMessage beforeStart = await player.PostAsync(rejoinPath, null);
            using HttpResponseMessage started = await dungeon.PostAsync(string.Format(GameServerRoutes.START_FORMAT,
                reservation.SessionId), Body(new SessionStartRequest(Guid.NewGuid().ToString("D"),
                new[] { reservation.Participants[0].ParticipantId })));
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            using HttpResponseMessage first = await player.PostAsync(rejoinPath, null);
            using HttpResponseMessage second = await player.PostAsync(rejoinPath, null);
            using HttpResponseMessage other = await stranger.PostAsync(rejoinPath, null);
            JoinTicket revoked = Read<JoinTicket>(await first.Content.ReadAsStringAsync());
            JoinTicket ticket = Read<JoinTicket>(await second.Content.ReadAsStringAsync());
            using HttpResponseMessage oldExchange = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(revoked.JoinToken, fixture.DungeonId.ToString("D"), ContractCodes.ServerRole.DUNGEON)));
            using HttpResponseMessage exchange = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(ticket.JoinToken, fixture.DungeonId.ToString("D"), ContractCodes.ServerRole.DUNGEON)));
            using HttpResponseMessage replay = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(ticket.JoinToken, fixture.DungeonId.ToString("D"), ContractCodes.ServerRole.DUNGEON)));

            Assert.Equal(HttpStatusCode.Conflict, beforeStart.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.WRONG_PHASE,
                Read<ApiRejection>(await beforeStart.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(ContractCodes.ServerRole.DUNGEON, ticket.ServerRole);
            Assert.Equal(fixture.DungeonId.ToString("D"), ticket.InstanceId);
            Assert.Equal(DUNGEON_PORT, ticket.Port);
            Assert.Equal(fixture.CharacterId.ToString("D"), ticket.CharacterId);
            Assert.Equal(reservation.Participants[0].ParticipantId, ticket.ParticipantId);
            Assert.True(ticket.ExpiresAtUnixMs > ticket.ServerTimeUnixMs);
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, oldExchange.StatusCode);
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            JoinTokenExchangeResponse joined = Read<JoinTokenExchangeResponse>(await exchange.Content.ReadAsStringAsync());
            Assert.Equal(ticket.ParticipantId, joined.ParticipantId);
            Assert.Equal(reservation.SessionId, joined.SortieId);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        }

        /// <summary> 마을 입장 토큰은 마을 역할로만 한 번 교환되고 출정 필드는 값 없음이며, 다른 살아 있는 서버에 접속 중이면 거절한다 </summary>
        [PostgresFact]
        public async Task ExchangeTown_TownToken_ReturnsCharacterWithoutSortieExactlyOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            AuthExchangeResponse login = await LoginAsync(fixture, fixture.AccountId);
            PlayerAuthIdentity identity = (await fixture.Auth.ValidateAccessAsync(login.AccessToken)).Identity!;
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options, null);
            using HttpClient town = factory.CreateClient();
            Authorize(town, fixture.TownLease.Token, fixture.TownId);
            using HttpClient dungeon = factory.CreateClient();
            Authorize(dungeon, fixture.DungeonLease.Token, fixture.DungeonId);

            await ClearAccountLeaseAsync(database, fixture.AccountId);
            IssuedAuthToken first = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;
            using HttpResponseMessage asDungeon = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(first.Token, fixture.DungeonId.ToString("D"), ContractCodes.ServerRole.DUNGEON)));
            using HttpResponseMessage exchange = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(first.Token, fixture.TownId.ToString("D"), ContractCodes.ServerRole.TOWN)));
            string body = await exchange.Content.ReadAsStringAsync();
            using HttpResponseMessage replay = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(first.Token, fixture.TownId.ToString("D"), ContractCodes.ServerRole.TOWN)));
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    "UPDATE account_leases SET instance_id = @DungeonId, role = @Dungeon WHERE account_id = @AccountId;",
                    new { fixture.DungeonId, Dungeon = ContractCodes.ServerRole.DUNGEON, fixture.AccountId });
            }
            IssuedAuthToken second = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;
            using HttpResponseMessage connected = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(second.Token, fixture.TownId.ToString("D"), ContractCodes.ServerRole.TOWN)));

            Assert.Equal(HttpStatusCode.Forbidden, asDungeon.StatusCode);
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            JObject json = JObject.Parse(body);
            Assert.Equal(fixture.CharacterId.ToString("D"), json.Value<string>("characterId"));
            Assert.Equal(fixture.TownId.ToString("D"), json.Value<string>("instanceId"));
            Assert.Equal(ContractCodes.ServerRole.TOWN, json.Value<int>("serverRole"));
            Assert.Equal(JTokenType.Null, json["participantId"]!.Type);
            Assert.Equal(JTokenType.Null, json["sortieId"]!.Type);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, connected.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.ALREADY_CONNECTED,
                Read<ApiRejection>(await connected.Content.ReadAsStringAsync()).ReasonCode);
        }

        /// <summary> 마을 입장권 HTTP는 서버가 선택한 주소를 반환하고 재발급 때 이전 토큰을 폐기하며 새 토큰은 한 번만 교환된다 </summary>
        [PostgresFact]
        public async Task IssueTownJoinToken_OwnedCharacter_ReturnsSelectedTownAndSingleUseTicket()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options, null);
            using HttpClient player = factory.CreateClient();
            player.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER,
                ApiRoutes.BEARER_PREFIX + await GameTokenAsync(fixture, fixture.AccountId));
            using HttpClient town = factory.CreateClient();
            Authorize(town, fixture.TownLease.Token, fixture.TownId);

            await ClearAccountLeaseAsync(database, fixture.AccountId);
            using HttpResponseMessage first = await player.PostAsync(ApiRoutes.TOWN_JOIN_TICKET,
                Body(new TownJoinTicketRequest(fixture.CharacterId.ToString("D"))));
            using HttpResponseMessage second = await player.PostAsync(ApiRoutes.TOWN_JOIN_TICKET,
                Body(new TownJoinTicketRequest(fixture.CharacterId.ToString("D"))));
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            JoinTicket revoked = Read<JoinTicket>(await first.Content.ReadAsStringAsync());
            JoinTicket ticket = Read<JoinTicket>(await second.Content.ReadAsStringAsync());

            Assert.Equal("no-store", second.Headers.CacheControl!.ToString());
            Assert.Equal(ContractCodes.ServerRole.TOWN, ticket.ServerRole);
            Assert.Equal(fixture.TownId.ToString("D"), ticket.InstanceId);
            Assert.Equal("127.0.0.1", ticket.Address);
            Assert.Equal(TOWN_PORT, ticket.Port);
            Assert.Equal(fixture.CharacterId.ToString("D"), ticket.CharacterId);
            Assert.Equal(string.Empty, ticket.ParticipantId);
            Assert.True(ticket.ExpiresAtUnixMs > ticket.ServerTimeUnixMs);
            Assert.NotEqual(revoked.JoinToken, ticket.JoinToken);

            using HttpResponseMessage oldExchange = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(revoked.JoinToken, ticket.InstanceId, ContractCodes.ServerRole.TOWN)));
            using HttpResponseMessage exchange = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(ticket.JoinToken, ticket.InstanceId, ContractCodes.ServerRole.TOWN)));
            using HttpResponseMessage replay = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(ticket.JoinToken, ticket.InstanceId, ContractCodes.ServerRole.TOWN)));
            Assert.Equal(HttpStatusCode.Unauthorized, oldExchange.StatusCode);
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            JoinTokenExchangeResponse joined = Read<JoinTokenExchangeResponse>(await exchange.Content.ReadAsStringAsync());
            Assert.Equal(ticket.CharacterId, joined.CharacterId);
            Assert.Equal(ticket.InstanceId, joined.InstanceId);
            Assert.Null(joined.ParticipantId);
            Assert.Null(joined.SortieId);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        }

        /// <summary> 접근 토큰의 계정과 다른 캐릭터는 입장 토큰을 저장하지 않고 거절한다 </summary>
        [PostgresFact]
        public async Task IssueTownJoinToken_OtherAccountCharacter_ReturnsForbiddenWithoutToken()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options, null);
            using HttpClient player = factory.CreateClient();
            player.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER,
                ApiRoutes.BEARER_PREFIX + await GameTokenAsync(fixture, fixture.AccountId));

            using HttpResponseMessage response = await player.PostAsync(ApiRoutes.TOWN_JOIN_TICKET,
                Body(new TownJoinTicketRequest(fixture.OtherCharacterId.ToString("D"))));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM auth_join_tokens;"));
        }

        /// <summary> 드레인, 소실, heartbeat 부재, 임대 만료나 폐기, 등록 boot 불일치인 마을은 입장을 받지 않는다 </summary>
        [PostgresFact]
        public async Task IssueTownJoinToken_NoEligibleTown_ReturnsNoCapacityWithoutToken()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options, null);
            using HttpClient player = factory.CreateClient();
            player.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER,
                ApiRoutes.BEARER_PREFIX + await GameTokenAsync(fixture, fixture.AccountId));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Guid boot = await connection.QuerySingleAsync<Guid>(
                "SELECT boot_id FROM server_instances WHERE instance_id = @TownId;", new { fixture.TownId });
            string[] unavailableStates =
            {
                "UPDATE server_instances SET state = 'Draining' WHERE instance_id = @TownId;",
                "UPDATE server_instances SET state = 'Offline' WHERE instance_id = @TownId;",
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '1 minute' WHERE instance_id = @TownId;",
                "UPDATE auth_server_leases SET expires_at = clock_timestamp() - interval '1 second' WHERE instance_id = @TownId;",
                "UPDATE auth_server_leases SET revoked_at = clock_timestamp() WHERE instance_id = @TownId;",
                "UPDATE server_instances SET boot_id = @OtherBoot WHERE instance_id = @TownId;",
                "UPDATE server_instances SET boot_id = NULL WHERE instance_id = @TownId;"
            };
            foreach (string unavailableState in unavailableStates)
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE server_instances SET state = 'Idle', heartbeat_at = clock_timestamp(), boot_id = @Boot
                        WHERE instance_id = @TownId;
                    UPDATE auth_server_leases SET revoked_at = NULL, expires_at = clock_timestamp() + interval '15 minutes'
                        WHERE instance_id = @TownId;
                    """, new { fixture.TownId, Boot = boot });
                await connection.ExecuteAsync(unavailableState, new { fixture.TownId, OtherBoot = Guid.NewGuid() });

                using HttpResponseMessage response = await player.PostAsync(ApiRoutes.TOWN_JOIN_TICKET,
                    Body(new TownJoinTicketRequest(fixture.CharacterId.ToString("D"))));
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY,
                    Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            }

            Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM auth_join_tokens;"));
        }

        private static async Task<ReservationResponse> ReserveAsync(HttpClient town, Fixture fixture)
        {
            ReservationRequest request = new ReservationRequest(Guid.NewGuid().ToString("D"), GameSessionCodes.Mode.NORMAL,
                "region_forest", CONTENT, new[] { new ReservationParticipant(fixture.AccountId.ToString("D"),
                    fixture.CharacterId.ToString("D"), 0, Array.Empty<ReservationPotion>()) });
            using HttpResponseMessage response = await town.PostAsync(GameServerRoutes.RESERVATIONS, Body(request));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Read<ReservationResponse>(await response.Content.ReadAsStringAsync());
        }

        private static async Task<AuthExchangeResponse> LoginAsync(Fixture fixture, Guid accountId)
        {
            IssuedAuthToken invite = await fixture.Auth.IssueInviteAsync(accountId);
            return (await fixture.Auth.ExchangeInviteAsync(invite.Token)).Response!;
        }

        // 마을, 복귀 입장권은 빌드 검사를 거친 게임 하위 세션 토큰만 받는다
        private static async Task<string> GameTokenAsync(Fixture fixture, Guid accountId)
        {
            AuthExchangeResponse login = await LoginAsync(fixture, accountId);
            PlayerAuthIdentity identity = (await fixture.Auth.ValidateAccessAsync(login.AccessToken)).Identity!;
            IssuedAuthToken game = await fixture.Auth.IssueGameSessionAsync(identity,
                TimeSpan.FromMinutes(GameSessionController.GAME_SESSION_MINUTES));
            return game.Token;
        }

        private static async Task<Fixture> SeedAsync(PostgresTestDatabase database)
        {
            Fixture fixture = new Fixture(database.DataSource);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts(account_id) VALUES (@AccountId), (@OtherAccountId);
                INSERT INTO characters(character_id,account_id,class_id) VALUES (@CharacterId,@AccountId,@Class),
                    (@OtherCharacterId,@OtherAccountId,@Class);
                INSERT INTO account_wallets(account_id) VALUES (@AccountId), (@OtherAccountId);
                INSERT INTO server_instances(instance_id,role,address,port,build_id,content_version,state,boot_id)
                    VALUES (@TownId,@Town,'127.0.0.1',@TownPort,@Content,@Content,'Idle',@TownBoot),
                        (@DungeonId,@Dungeon,'127.0.0.1',@DungeonPort,@Content,@Content,'Idle',@DungeonBoot);
                INSERT INTO account_leases(account_id,lease_id,instance_id,role,expires_at)
                    VALUES (@AccountId,@AccountLease,@TownId,@Town,clock_timestamp() + interval '15 minutes');
                """, new { fixture.AccountId, fixture.OtherAccountId, fixture.CharacterId, fixture.OtherCharacterId,
                    fixture.TownId, fixture.DungeonId, Class = ContractCodes.PlayerClass.WARRIOR,
                    Town = ContractCodes.ServerRole.TOWN, Dungeon = ContractCodes.ServerRole.DUNGEON,
                    TownPort = TOWN_PORT, DungeonPort = DUNGEON_PORT, Content = CONTENT, TownBoot = Guid.NewGuid(),
                    DungeonBoot = Guid.NewGuid(), AccountLease = Guid.NewGuid() });
            fixture.TownLease = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(SERVER_MINUTES));
            fixture.DungeonLease = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.DungeonId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(SERVER_MINUTES));
            return fixture;
        }

        // 시드가 만든 마을 접속 임대를 지운다. 마을 입장 교환은 살아 있는 임대가 있으면 같은 마을이어도 거절한다
        private static async Task ClearAccountLeaseAsync(PostgresTestDatabase database, Guid accountId)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;",
                new { AccountId = accountId });
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
            private readonly ServiceCredentialRegistration? _registration;

            public ApiFactory(NpgsqlDataSource dataSource, AuthTokenOptions options,
                ServiceCredentialRegistration? registration)
            {
                _dataSource   = dataSource;
                _options      = options;
                _registration = registration;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<NpgsqlDataSource>();
                    services.AddSingleton(_dataSource);
                    services.PostConfigure<AuthTokenOptions>(options => options.HashKeyBase64 = _options.HashKeyBase64);
                    services.PostConfigure<ServiceStateOptions>(options => options.Maintenance = false);
                    services.PostConfigure<ServiceCredentialOptions>(options =>
                    {
                        options.Registrations = _registration == null
                            ? Array.Empty<ServiceCredentialRegistration>()
                            : new[] { _registration };
                    });
                });
            }
        }

        private sealed class Fixture
        {
            public AuthTokenOptions Options { get; } = AuthTokenHasherTests.CreateOptions();
            public PostgresAuthRepository Auth { get; }
            public Guid AccountId { get; } = Guid.NewGuid();
            public Guid OtherAccountId { get; } = Guid.NewGuid();
            public Guid CharacterId { get; } = Guid.NewGuid();
            public Guid OtherCharacterId { get; } = Guid.NewGuid();
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
