// System
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;
using Shinshi.Data.Life;

namespace Shinshi.Backend.Tests.Life
{
    /// <summary> 마을 서버 임대로 부르는 생활 경로의 조회, 형식 오류, gathering 기능 차단(저장 없음, 해제 뒤 같은 ID 실행)을
    /// 실제 PostgreSQL과 Program 파이프라인에서 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class LifeHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ADMIN_NAME    = "life.admin";
        private const string REASON        = "life gate check";
        private const string CONTENT       = "life-http";
        private const int    TOWN_PORT     = 7777;
        private const int    LEASE_MINUTES = 15;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Gather_GatheringDisabledThenEnabled_RejectsWithoutEffectThenRunsSameOperation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            Seed seed = await SeedAsync(database.DataSource, host.AuthOptions);
            LifeGatherRequest gather = new LifeGatherRequest(Guid.NewGuid().ToString("D"), seed.AccountId.ToString("D"),
                seed.LeaseId.ToString("D"), "zone_a", "node_1");

            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.GATHERING }, 0);
            using HttpResponseMessage blocked = await SendAsync(client, seed, LifeRoutes.GATHERS, gather);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(blocked)).ReasonCode);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @TownId;", new { seed.TownId }));
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM account_items WHERE account_id = @AccountId;", new { seed.AccountId }));

            await UpdateStateAsync(client, admin, Array.Empty<string>(), 1);
            using HttpResponseMessage committed = await SendAsync(client, seed, LifeRoutes.GATHERS, gather);
            Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
            OperationResult result = await AdminTestSupport.ReadAsync<OperationResult>(committed);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
            Assert.Equal(gather.OperationId, result.OperationId);

            using HttpResponseMessage snapshotResponse = await SendAsync(client, seed, LifeRoutes.SNAPSHOT,
                new LifeSnapshotRequest(seed.AccountId.ToString("D"), seed.LeaseId.ToString("D")));
            Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
            LifeSnapshotResponse snapshot = await AdminTestSupport.ReadAsync<LifeSnapshotResponse>(snapshotResponse);
            Assert.Equal(LifeContent.ACTION_MAX - LifeContent.ACTION_COST_PER_GATHER, snapshot.ActionValue);
            Assert.Equal(LifeContent.HERBS_PER_GATHER, snapshot.Herb.Quantity);
            Assert.True(snapshot.NextRecoverAtUnixMs > snapshot.ServerTimeUnixMs);

            // crafting이 꺼지면 제작도 같은 방식으로 막힌다. 형식이 틀린 정의와 0개는 400이다
            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.CRAFTING }, 2);
            using HttpResponseMessage craftBlocked = await SendAsync(client, seed, LifeRoutes.POTION_CRAFTS,
                new LifePotionCraftRequest(Guid.NewGuid().ToString("D"), seed.AccountId.ToString("D"),
                    seed.LeaseId.ToString("D"), LifeContent.POTION_SMALL_ID, 1));
            using HttpResponseMessage unknown = await SendAsync(client, seed, LifeRoutes.POTION_CRAFTS,
                new LifePotionCraftRequest(Guid.NewGuid().ToString("D"), seed.AccountId.ToString("D"),
                    seed.LeaseId.ToString("D"), "item_potion_huge", 1));
            using HttpResponseMessage zero = await SendAsync(client, seed, LifeRoutes.POTION_CRAFTS,
                new LifePotionCraftRequest(Guid.NewGuid().ToString("D"), seed.AccountId.ToString("D"),
                    seed.LeaseId.ToString("D"), LifeContent.POTION_SMALL_ID, 0));
            Assert.Equal(HttpStatusCode.Conflict, craftBlocked.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(craftBlocked)).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Seed seed, string route, object body)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = AdminTestSupport.Json(body)
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + seed.TownToken);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, seed.TownId.ToString("D"));
            return await client.SendAsync(request);
        }

        private static async Task UpdateStateAsync(HttpClient client, string adminToken, string[] disabledFeatures,
            long expectedRevision)
        {
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, adminToken, new AdminServiceStateUpdateRequest(
                    AdminApiTestHost.NewOperationId(), REASON, false, disabledFeatures, Array.Empty<string>(),
                    expectedRevision));
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
                    INSERT INTO server_instances(instance_id, role, address, port, build_id, content_version, state, boot_id)
                        VALUES (@TownId, @Town, '127.0.0.1', @Port, @Content, @Content, 'Idle', @Boot);
                    INSERT INTO account_leases(account_id, lease_id, instance_id, role, expires_at)
                        VALUES (@AccountId, @LeaseId, @TownId, @Town, clock_timestamp() + make_interval(mins => @Minutes));
                    """, new
                    {
                        seed.AccountId, seed.TownId, seed.LeaseId, Town = ContractCodes.ServerRole.TOWN, Port = TOWN_PORT,
                        Content = CONTENT, Boot = Guid.NewGuid(), Minutes = LEASE_MINUTES
                    });
            }

            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            seed.TownToken = (await auth.IssueServerLeaseAsync(Guid.NewGuid(), seed.TownId, ContractCodes.ServerRole.TOWN,
                TimeSpan.FromMinutes(LEASE_MINUTES))).Token;
            return seed;
        }

        private sealed class Seed
        {
            public Guid   AccountId { get; } = Guid.NewGuid();
            public Guid   TownId    { get; } = Guid.NewGuid();
            public Guid   LeaseId   { get; } = Guid.NewGuid();
            public string TownToken { get; set; } = string.Empty;
        }
    }
}
