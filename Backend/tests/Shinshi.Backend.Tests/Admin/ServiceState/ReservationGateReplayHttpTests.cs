// System
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 점검 중에도 같은 operationId 예약 재전송은 저장된 처음 결과를 받고, 새 예약은 작업 행 없이 거절되는지
    /// 실제 PostgreSQL과 Program 파이프라인에서 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class ReservationGateReplayHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ADMIN_NAME     = "replay.admin";
        private const string REASON         = "reservation replay check";
        private const string CONTENT        = "replay-content";
        private const int    TOWN_PORT      = 7777;
        private const int    DUNGEON_PORT   = 7780;
        private const int    LEASE_MINUTES  = 15;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Reserve_StoredSuccessDuringMaintenance_ReplaysFirstResultAndRejectsNewOperation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            Seed seed = await SeedAsync(database.DataSource, host.AuthOptions);

            // 0008 초기 행(revision 0)은 점검 중이다. 풀고 예약한 뒤 다시 점검을 켠다
            await UpdateStateAsync(client, admin, false, 0);
            ReservationRequest first = CreateRequest(seed);
            (HttpStatusCode reservedStatus, ReservationResponse reserved) = await ReserveAsync(client, seed, first);
            Assert.Equal(ContractCodes.RejectReason.NONE, reserved.Result.ReasonCode);
            Assert.Equal(HttpStatusCode.OK, reservedStatus);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reserved.Result.Status);
            await UpdateStateAsync(client, admin, true, 1);

            (HttpStatusCode replayStatus, ReservationResponse replayed) = await ReserveAsync(client, seed, first);
            ReservationRequest second = CreateRequest(seed);
            (HttpStatusCode blockedStatus, ReservationResponse blocked) = await ReserveAsync(client, seed, second);

            Assert.Equal(HttpStatusCode.OK, replayStatus);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replayed.Result.Status);
            Assert.Equal(reserved.SessionId, replayed.SessionId);
            Assert.Equal(reserved.Result.ServerTimeUnixMs, replayed.Result.ServerTimeUnixMs);
            Assert.Single(replayed.Participants);
            Assert.Equal(HttpStatusCode.Conflict, blockedStatus);
            Assert.Equal(ContractCodes.RejectReason.MAINTENANCE, blocked.Result.ReasonCode);
            Assert.Equal(string.Empty, blocked.SessionId);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @TownId AND operation_id = @OperationId;",
                new { seed.TownId, OperationId = Guid.Parse(second.OperationId) }));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM game_sessions;"));
        }

        private static ReservationRequest CreateRequest(Seed seed)
        {
            return new ReservationRequest(Guid.NewGuid().ToString("D"), GameSessionCodes.Mode.NORMAL, CONTENT, CONTENT,
                new[]
                {
                    new ReservationParticipant(seed.AccountId.ToString("D"), seed.CharacterId.ToString("D"), 0,
                        Array.Empty<ReservationPotion>())
                });
        }

        private static async Task<(HttpStatusCode Status, ReservationResponse Body)> ReserveAsync(HttpClient client,
            Seed seed, ReservationRequest body)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GameServerRoutes.RESERVATIONS)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()),
                    Encoding.UTF8, "application/json")
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + seed.TownToken);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, seed.TownId.ToString("D"));
            using HttpResponseMessage response = await client.SendAsync(request);
            ReservationResponse? parsed = JsonConvert.DeserializeObject<ReservationResponse>(
                await response.Content.ReadAsStringAsync(), ContractJson.CreateSettings());
            Assert.NotNull(parsed);
            return (response.StatusCode, parsed);
        }

        private static async Task UpdateStateAsync(HttpClient client, string adminToken, bool maintenance,
            long expectedRevision)
        {
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, adminToken, new AdminServiceStateUpdateRequest(
                    AdminApiTestHost.NewOperationId(), REASON, maintenance, Array.Empty<string>(),
                    Array.Empty<string>(), expectedRevision));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static async Task<Seed> SeedAsync(NpgsqlDataSource dataSource, AuthTokenOptions options)
        {
            Seed seed = new Seed();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO accounts(account_id) VALUES (@AccountId);
                    INSERT INTO characters(character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, @Class);
                    INSERT INTO account_wallets(account_id) VALUES (@AccountId);
                    INSERT INTO server_instances(instance_id, role, address, port, build_id, content_version, state, boot_id)
                        VALUES (@TownId, @Town, '127.0.0.1', @TownPort, @Content, @Content, 'Idle', @TownBoot),
                            (@DungeonId, @Dungeon, '127.0.0.1', @DungeonPort, @Content, @Content, 'Idle', @DungeonBoot);
                    INSERT INTO account_leases(account_id, lease_id, instance_id, role, expires_at)
                        VALUES (@AccountId, @AccountLease, @TownId, @Town, clock_timestamp() + interval '15 minutes');
                    """, new
                    {
                        seed.AccountId, seed.CharacterId, seed.TownId, seed.DungeonId,
                        Class = ContractCodes.PlayerClass.WARRIOR, Town = ContractCodes.ServerRole.TOWN,
                        Dungeon = ContractCodes.ServerRole.DUNGEON, TownPort = TOWN_PORT, DungeonPort = DUNGEON_PORT,
                        Content = CONTENT, TownBoot = Guid.NewGuid(), DungeonBoot = Guid.NewGuid(),
                        AccountLease = Guid.NewGuid()
                    });
            }

            // 던전도 살아 있는 서버 임대가 있어야 예약 풀에 잡힌다
            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            IssuedAuthToken lease = await auth.IssueServerLeaseAsync(Guid.NewGuid(), seed.TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(LEASE_MINUTES));
            await auth.IssueServerLeaseAsync(Guid.NewGuid(), seed.DungeonId, ContractCodes.ServerRole.DUNGEON,
                TimeSpan.FromMinutes(LEASE_MINUTES));
            seed.TownToken = lease.Token;
            return seed;
        }

        private sealed class Seed
        {
            public Guid AccountId { get; } = Guid.NewGuid();
            public Guid CharacterId { get; } = Guid.NewGuid();
            public Guid TownId { get; } = Guid.NewGuid();
            public Guid DungeonId { get; } = Guid.NewGuid();
            public string TownToken { get; set; } = string.Empty;
        }
    }
}
