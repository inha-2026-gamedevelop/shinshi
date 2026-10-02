// System
using System;
using System.Globalization;
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
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> 실제 PostgreSQL과 Program으로 계정 접속 임대(마을 교환 획득, heartbeat 연장, 해제), 생존 판정 503,
    /// 다른 boot 생존 503, 복귀 입장권 계정 한도, 만료 입장 토큰 정리를 검사한다 </summary>
    public sealed class AccountLeaseHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    SERVER_MINUTES   = 15;
        private const int    TOWN_PORT        = 7777;
        private const int    DUNGEON_PORT     = 7781;
        private const int    STALE_SECONDS    = 20;
        private const int    NEAR_EXPIRY      = 2;
        private const string CONTENT          = "lease-http-test";
        private const int    PURGE_LIMIT      = 100;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 마을 교환이 계정 임대를 얻어 leaseId를 돌려주고, 같은 마을 재입장은 거절하며, heartbeat가 연장하고,
        /// 예약의 마을 임대 검사를 통과하며, 해제는 다른 인스턴스는 403, 소유 마을은 멱등 204다 </summary>
        [PostgresFact]
        public async Task TownExchange_AcquiresLease_HeartbeatExtends_ReleaseIdempotent()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            PlayerAuthIdentity identity = await LoginAsync(fixture);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options, null);
            using HttpClient town = Server(factory, fixture.TownLease.Token, fixture.TownId);
            using HttpClient dungeon = Server(factory, fixture.DungeonLease.Token, fixture.DungeonId);

            JoinTokenExchangeResponse joined = Read<JoinTokenExchangeResponse>(await ExchangeTownAsync(town, fixture,
                identity, HttpStatusCode.OK));
            using HttpResponseMessage sameTown = await PostExchangeAsync(town, fixture, identity);
            await ExecuteAsync(database,
                "UPDATE account_leases SET expires_at = clock_timestamp() + make_interval(secs => @Near) WHERE account_id = @Id;",
                new { Id = fixture.AccountId, Near = NEAR_EXPIRY });
            using HttpResponseMessage beat = await town.PostAsync(
                string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SERVER_HEARTBEAT_FORMAT, fixture.TownId),
                Body(new ServerHeartbeatRequest(fixture.TownBoot.ToString("D"))));
            double remaining = await ScalarAsync<double>(database,
                "SELECT extract(epoch FROM expires_at - clock_timestamp())::float8 FROM account_leases WHERE account_id = @Id;",
                new { Id = fixture.AccountId });
            using HttpResponseMessage reserved = await town.PostAsync(GameServerRoutes.RESERVATIONS, Body(
                new ReservationRequest(Guid.NewGuid().ToString("D"), GameSessionCodes.Mode.NORMAL, CONTENT, CONTENT,
                    new[] { new ReservationParticipant(fixture.AccountId.ToString("D"), fixture.CharacterId.ToString("D"), 0,
                        Array.Empty<ReservationPotion>()) })));
            string releasePath = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.ACCOUNT_LEASE_RELEASE_FORMAT,
                fixture.TownId, joined.AccountLeaseId);
            using HttpResponseMessage foreign = await dungeon.PostAsync(string.Format(CultureInfo.InvariantCulture,
                GameServerRoutes.ACCOUNT_LEASE_RELEASE_FORMAT, fixture.DungeonId, joined.AccountLeaseId), null);
            using HttpResponseMessage released = await town.PostAsync(releasePath, null);
            using HttpResponseMessage releasedAgain = await town.PostAsync(releasePath, null);

            Assert.True(Guid.TryParse(joined.AccountLeaseId, out Guid leaseId));
            Assert.Equal(HttpStatusCode.Conflict, sameTown.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.ALREADY_CONNECTED,
                Read<ApiRejection>(await sameTown.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.OK, beat.StatusCode);
            Assert.True(remaining > ServerPoolTimings.ACCOUNT_LEASE_SECONDS - NEAR_EXPIRY);
            Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, releasedAgain.StatusCode);
            Assert.Equal(0L, await ScalarAsync<long>(database,
                "SELECT count(*) FROM account_leases WHERE lease_id = @Id;", new { Id = leaseId }));
        }

        /// <summary> 0005 이전처럼 boot가 없는 임대로 부른 /events는 heartbeat가 늦으면 저장 없이 503, 810, Retry-After로 거절하고,
        /// boot가 묶인 임대로 부르면 호출 자체가 생존 증거가 되어 같은 operationId가 확정된다 </summary>
        [PostgresFact]
        public async Task Events_StaleHeartbeat_Returns503WithoutStoring_ThenTouchCommits()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            await ExecuteAsync(database, """
                INSERT INTO account_leases(account_id,lease_id,instance_id,role,expires_at)
                VALUES (@AccountId,gen_random_uuid(),@TownId,@Town,clock_timestamp() + interval '15 minutes');
                """, new { fixture.AccountId, fixture.TownId, Town = ContractCodes.ServerRole.TOWN });
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options, null);
            using HttpClient town = Server(factory, fixture.TownLease.Token, fixture.TownId);
            using HttpClient dungeon = Server(factory, fixture.DungeonLease.Token, fixture.DungeonId);
            using HttpResponseMessage reservedResponse = await town.PostAsync(GameServerRoutes.RESERVATIONS, Body(
                new ReservationRequest(Guid.NewGuid().ToString("D"), GameSessionCodes.Mode.NORMAL, CONTENT, CONTENT,
                    new[] { new ReservationParticipant(fixture.AccountId.ToString("D"), fixture.CharacterId.ToString("D"), 0,
                        Array.Empty<ReservationPotion>()) })));
            ReservationResponse reservation = Read<ReservationResponse>(await reservedResponse.Content.ReadAsStringAsync());
            using HttpResponseMessage started = await dungeon.PostAsync(string.Format(CultureInfo.InvariantCulture,
                GameServerRoutes.START_FORMAT, reservation.SessionId), Body(new SessionStartRequest(
                    Guid.NewGuid().ToString("D"), new[] { reservation.Participants[0].ParticipantId })));
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);

            await ExecuteAsync(database, """
                UPDATE auth_server_leases SET boot_id = NULL WHERE instance_id = @DungeonId;
                UPDATE server_instances SET heartbeat_at = clock_timestamp() - make_interval(secs => @Gap)
                    WHERE instance_id = @DungeonId;
                """, new { fixture.DungeonId, Gap = STALE_SECONDS });
            Guid operationId = Guid.NewGuid();
            string eventsPath = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.EVENTS_FORMAT,
                reservation.SessionId);
            SessionEventBatchRequest batch = new SessionEventBatchRequest(operationId.ToString("D"), new[]
            {
                new SessionEventEntry(1, Guid.NewGuid().ToString("D"), GameSessionCodes.EventKind.LOOT, string.Empty,
                    string.Empty, string.Empty, 0, 0, string.Empty, Array.Empty<SettlementGrant>(), "{}")
            });
            using HttpResponseMessage unavailable = await dungeon.PostAsync(eventsPath, Body(batch));
            long stored = await ScalarAsync<long>(database, "SELECT count(*) FROM operations WHERE operation_id = @Id;",
                new { Id = operationId });
            await ExecuteAsync(database, "UPDATE auth_server_leases SET boot_id = @Boot WHERE instance_id = @DungeonId;",
                new { fixture.DungeonId, Boot = fixture.DungeonBoot });
            using HttpResponseMessage committed = await dungeon.PostAsync(eventsPath, Body(batch));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE,
                Read<ApiRejection>(await unavailable.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(TimeSpan.FromSeconds(ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS),
                unavailable.Headers.RetryAfter!.Delta);
            Assert.Equal(0L, stored);
            Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                Read<OperationResult>(await committed.Content.ReadAsStringAsync()).Status);
        }

        /// <summary> 같은 인스턴스의 다른 boot가 살아 있으면 서버 임대 발급은 503, 810, Retry-After 5다 </summary>
        [PostgresFact]
        public async Task Lease_OtherBootAlive_Returns503WithRetryAfter()
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
            using HttpClient service = Server(factory, credential, instanceId);
            using HttpResponseMessage leased = await service.PostAsync(GameServerRoutes.SERVER_LEASE,
                Body(new ServerLeaseRequest(instanceId.ToString("D"), ContractCodes.ServerRole.DUNGEON, boot.ToString("D"))));
            ServerLeaseResponse lease = Read<ServerLeaseResponse>(await leased.Content.ReadAsStringAsync());
            using HttpClient server = Server(factory, lease.LeaseToken, instanceId);
            using HttpResponseMessage registered = await server.PostAsync(GameServerRoutes.SERVER_REGISTER,
                Body(new ServerRegisterRequest(instanceId.ToString("D"), boot.ToString("D"), ContractCodes.ServerRole.DUNGEON,
                    "127.0.0.1", DUNGEON_PORT, CONTENT, CONTENT)));

            using HttpResponseMessage otherBoot = await service.PostAsync(GameServerRoutes.SERVER_LEASE,
                Body(new ServerLeaseRequest(instanceId.ToString("D"), ContractCodes.ServerRole.DUNGEON,
                    Guid.NewGuid().ToString("D"))));

            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, otherBoot.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE,
                Read<ApiRejection>(await otherBoot.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(TimeSpan.FromSeconds(ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS),
                otherBoot.Headers.RetryAfter!.Delta);
        }

        /// <summary> 복귀 입장권은 계정마다 1분에 6회까지 받고, 넘으면 저장소를 부르지 않고 429다 </summary>
        [PostgresFact]
        public async Task Rejoin_PerAccountLimit_Returns429AfterSixRequests()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            AuthExchangeResponse login = await LoginResponseAsync(fixture);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options, null);
            using HttpClient player = factory.CreateClient();
            player.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + login.AccessToken);
            string path = string.Format(CultureInfo.InvariantCulture, ApiRoutes.REJOIN_TICKET_FORMAT, Guid.NewGuid());

            for (int attempt = 0; attempt < AccountRequestLimiter.MAX_REJOIN_TICKETS_PER_WINDOW; ++attempt)
            {
                using HttpResponseMessage allowed = await player.PostAsync(path, null);
                Assert.Equal(HttpStatusCode.Forbidden, allowed.StatusCode);
            }
            using HttpResponseMessage limited = await player.PostAsync(path, null);

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                Read<ApiRejection>(await limited.Content.ReadAsStringAsync()).ReasonCode);
            Assert.NotNull(limited.Headers.RetryAfter);
        }

        /// <summary> 정리 작업은 보존 기간이 지난 만료 입장 토큰만 지운다 </summary>
        [PostgresFact]
        public async Task PurgeExpiredJoinTokens_DeletesOnlyPastRetention()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            PlayerAuthIdentity identity = await LoginAsync(fixture);
            IssuedAuthToken old = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;
            IssuedAuthToken fresh = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;
            await ExecuteAsync(database,
                "UPDATE auth_join_tokens SET expires_at = clock_timestamp() - @Retention - interval '1 minute' WHERE token_id = @Id;",
                new { Id = old.TokenId, Retention = ServerPoolTimings.JoinTokenRetention });

            int purged = await fixture.Auth.PurgeExpiredJoinTokensAsync(PURGE_LIMIT);

            Assert.Equal(1, purged);
            Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM auth_join_tokens WHERE token_id = @Id;",
                new { Id = old.TokenId }));
            Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM auth_join_tokens WHERE token_id = @Id;",
                new { Id = fresh.TokenId }));
        }

        private static async Task<string> ExchangeTownAsync(HttpClient town, Fixture fixture, PlayerAuthIdentity identity,
            HttpStatusCode expected)
        {
            using HttpResponseMessage response = await PostExchangeAsync(town, fixture, identity);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(expected, response.StatusCode);
            return body;
        }

        private static async Task<HttpResponseMessage> PostExchangeAsync(HttpClient town, Fixture fixture,
            PlayerAuthIdentity identity)
        {
            IssuedAuthToken token = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;
            return await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE, Body(new JoinTokenExchangeRequest(token.Token,
                fixture.TownId.ToString("D"), ContractCodes.ServerRole.TOWN)));
        }

        private static async Task<PlayerAuthIdentity> LoginAsync(Fixture fixture)
        {
            AuthExchangeResponse login = await LoginResponseAsync(fixture);
            return (await fixture.Auth.ValidateAccessAsync(login.AccessToken)).Identity!;
        }

        private static async Task<AuthExchangeResponse> LoginResponseAsync(Fixture fixture)
        {
            IssuedAuthToken invite = await fixture.Auth.IssueInviteAsync(fixture.AccountId);
            return (await fixture.Auth.ExchangeInviteAsync(invite.Token)).Response!;
        }

        private static async Task<Fixture> SeedAsync(PostgresTestDatabase database)
        {
            Fixture fixture = new Fixture(database.DataSource);
            await ExecuteAsync(database,
                """
                INSERT INTO accounts(account_id) VALUES (@AccountId);
                INSERT INTO characters(character_id,account_id,class_id) VALUES (@CharacterId,@AccountId,@Class);
                INSERT INTO account_wallets(account_id) VALUES (@AccountId);
                INSERT INTO server_instances(instance_id,role,address,port,build_id,content_version,state,boot_id)
                    VALUES (@TownId,@Town,'127.0.0.1',@TownPort,@Content,@Content,'Idle',@TownBoot),
                        (@DungeonId,@Dungeon,'127.0.0.1',@DungeonPort,@Content,@Content,'Idle',@DungeonBoot);
                """, new { fixture.AccountId, fixture.CharacterId, fixture.TownId, fixture.DungeonId,
                    Class = ContractCodes.PlayerClass.WARRIOR, Town = ContractCodes.ServerRole.TOWN,
                    Dungeon = ContractCodes.ServerRole.DUNGEON, TownPort = TOWN_PORT, DungeonPort = DUNGEON_PORT,
                    Content = CONTENT, fixture.TownBoot, fixture.DungeonBoot });
            fixture.TownLease = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(SERVER_MINUTES));
            fixture.DungeonLease = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.DungeonId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(SERVER_MINUTES));
            return fixture;
        }

        private static async Task ExecuteAsync(PostgresTestDatabase database, string sql, object parameters)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(sql, parameters);
        }

        private static async Task<T> ScalarAsync<T>(PostgresTestDatabase database, string sql, object parameters)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<T>(sql, parameters);
        }

        private static HttpClient Server(ApiFactory factory, string token, Guid instance)
        {
            HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token);
            client.DefaultRequestHeaders.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, instance.ToString("D"));
            return client;
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
            public Guid CharacterId { get; } = Guid.NewGuid();
            public Guid TownId { get; } = Guid.NewGuid();
            public Guid DungeonId { get; } = Guid.NewGuid();
            public Guid TownBoot { get; } = Guid.NewGuid();
            public Guid DungeonBoot { get; } = Guid.NewGuid();
            public IssuedAuthToken TownLease { get; set; } = null!;
            public IssuedAuthToken DungeonLease { get; set; } = null!;

            public Fixture(NpgsqlDataSource dataSource)
            {
                Auth = new PostgresAuthRepository(dataSource, Options);
            }
        }
    }
}
