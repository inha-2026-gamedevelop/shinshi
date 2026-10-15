// System
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Growth;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 성장 경로의 서버 역할 가드, 경로 상수, 기능 차단 재전송을 Program 파이프라인으로 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class GrowthHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CHARACTER     = "7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a001";
        private const string ITEM          = "7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1a002";
        private const string EMPTY_BODY    = "{}";
        private const string JSON_MEDIA    = "application/json";
        private const string ADMIN_NAME    = "growth.admin";
        private const string REASON        = "growth gate check";
        private const string EFFECT        = "T01-1A";
        private const int    TOWN_PORT     = 7777;
        private const int    LEASE_MINUTES = 15;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Templates_MatchSharedFormats()
        {
            Assert.Equal(GrowthRouteTemplates.GROWTH, Format(GrowthRoutes.GROWTH_FORMAT));
            Assert.Equal(GrowthRouteTemplates.TALISMAN_REGISTER, Format(GrowthRoutes.TALISMAN_REGISTER_FORMAT));
            Assert.Equal(GrowthRouteTemplates.TALISMAN_UPGRADE, Format(GrowthRoutes.TALISMAN_UPGRADE_FORMAT));
            Assert.Equal(GrowthRouteTemplates.TALISMAN_LOADOUT, Format(GrowthRoutes.TALISMAN_LOADOUT_FORMAT));
            Assert.Equal(GrowthRouteTemplates.TALISMAN_DISASSEMBLE, Format(GrowthRoutes.TALISMAN_DISASSEMBLE_FORMAT));
            Assert.Equal(GrowthRouteTemplates.TRAINING, Format(GrowthRoutes.TRAINING_FORMAT));
            Assert.Equal(GrowthRouteTemplates.TRAINING_RESPEC, Format(GrowthRoutes.TRAINING_RESPEC_FORMAT));
            Assert.Equal(GrowthRouteTemplates.EQUIPMENT_CRAFT, Format(GrowthRoutes.EQUIPMENT_CRAFT_FORMAT));
            Assert.Equal(GrowthRouteTemplates.EQUIPMENT_INHERIT, Format(GrowthRoutes.EQUIPMENT_INHERIT_FORMAT));
            Assert.Equal(GrowthRouteTemplates.EQUIPMENT_ENHANCE, Format(GrowthRoutes.EQUIPMENT_ENHANCE_FORMAT));
            Assert.Equal(GrowthRouteTemplates.EQUIPMENT_EQUIP, Format(GrowthRoutes.EQUIPMENT_EQUIP_FORMAT));
            Assert.Equal(GrowthRouteTemplates.EQUIPMENT_UNEQUIP, Format(GrowthRoutes.EQUIPMENT_UNEQUIP_FORMAT));
            Assert.Equal(GrowthRouteTemplates.EQUIPMENT_LOCK, Format(GrowthRoutes.EQUIPMENT_LOCK_FORMAT));
            Assert.Equal(GrowthRouteTemplates.EQUIPMENT_DISMANTLE, Format(GrowthRoutes.EQUIPMENT_DISMANTLE_FORMAT));
            Assert.Equal(GrowthRouteTemplates.EQUIPMENT_REROLL_OPTIONS, Format(GrowthRoutes.EQUIPMENT_REROLL_OPTIONS_FORMAT));
            Assert.Equal(GrowthRouteTemplates.INTRO_COMPLETE, string.Format(GrowthRoutes.INTRO_COMPLETE_FORMAT, "{accountId}"));
        }

        [Theory]
        [InlineData(GrowthRoutes.TALISMAN_REGISTER_FORMAT)]
        [InlineData(GrowthRoutes.TALISMAN_LOADOUT_FORMAT)]
        [InlineData(GrowthRoutes.TRAINING_FORMAT)]
        [InlineData(GrowthRoutes.TRAINING_RESPEC_FORMAT)]
        [InlineData(GrowthRoutes.EQUIPMENT_CRAFT_FORMAT)]
        [InlineData(GrowthRoutes.EQUIPMENT_INHERIT_FORMAT)]
        [InlineData(GrowthRoutes.EQUIPMENT_ENHANCE_FORMAT)]
        [InlineData(GrowthRoutes.EQUIPMENT_DISMANTLE_FORMAT)]
        [InlineData(GrowthRoutes.EQUIPMENT_REROLL_OPTIONS_FORMAT)]
        [InlineData(GrowthRoutes.INTRO_COMPLETE_FORMAT)]
        public async Task Post_WithoutServerCredential_Returns401(string format)
        {
            using GrowthGuardFactory factory = new GrowthGuardFactory(ContractCodes.ServerRole.TOWN);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(string.Format(format, CHARACTER, ITEM),
                new StringContent(EMPTY_BODY, Encoding.UTF8, JSON_MEDIA));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(GrowthRoutes.TALISMAN_REGISTER_FORMAT)]
        [InlineData(GrowthRoutes.TRAINING_FORMAT)]
        [InlineData(GrowthRoutes.EQUIPMENT_ENHANCE_FORMAT)]
        [InlineData(GrowthRoutes.EQUIPMENT_EQUIP_FORMAT)]
        [InlineData(GrowthRoutes.EQUIPMENT_REROLL_OPTIONS_FORMAT)]
        [InlineData(GrowthRoutes.INTRO_COMPLETE_FORMAT)]
        public async Task Post_DungeonServer_Returns403(string format)
        {
            using GrowthGuardFactory factory = new GrowthGuardFactory(ContractCodes.ServerRole.DUNGEON);
            using HttpClient client = factory.CreateClient();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, string.Format(format, CHARACTER, ITEM))
            {
                Content = new StringContent(EMPTY_BODY, Encoding.UTF8, JSON_MEDIA)
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        /// <summary> 형식이 틀린 옵션 재설정은 마을 서버 임대가 맞아도 DB에 닿기 전에 400이다 </summary>
        [Theory]
        [InlineData(ITEM, "[0,0,0]")]
        [InlineData(ITEM, "[0,0,0,0,0]")]
        [InlineData(ITEM, "[0,0,0,10000]")]
        [InlineData(ITEM, "[-1,0,0,0]")]
        [InlineData(ITEM, "null")]
        [InlineData("not-an-item", "[0,0,0,0]")]
        [InlineData("00000000-0000-0000-0000-000000000000", "[0,0,0,0]")]
        public async Task RerollOptions_MalformedItemOrRolls_Returns400(string itemId, string rolls)
        {
            using GrowthGuardFactory factory = new GrowthGuardFactory(ContractCodes.ServerRole.TOWN);
            using HttpClient client = factory.CreateClient();
            string body = "{\"operationId\":\"" + Guid.NewGuid().ToString("D") + "\",\"expectedRevision\":0,\"rolls\":" + rolls + "}";
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post,
                string.Format(GrowthRoutes.EQUIPMENT_REROLL_OPTIONS_FORMAT, CHARACTER, itemId))
            {
                Content = new StringContent(body, Encoding.UTF8, JSON_MEDIA)
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetProgress_WithoutPlayerToken_Returns401()
        {
            using GrowthGuardFactory factory = new GrowthGuardFactory(ContractCodes.ServerRole.TOWN);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(GrowthRoutes.ACCOUNT_PROGRESS);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [PostgresFact]
        public async Task Register_FeatureDisabled_ReplaysStoredAndRefusesNewWithoutSaving()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            Seed seed = await SeedAsync(database.DataSource, host.AuthOptions);
            await UpdateStateAsync(client, admin, Array.Empty<string>(), 0);

            TalismanRegisterRequest first = new TalismanRegisterRequest(Guid.NewGuid().ToString("D"), 0, EFFECT, 0);
            (HttpStatusCode firstStatus, GrowthOperationResponse registered) = await PostAsync(client, seed, first);
            Assert.Equal(HttpStatusCode.OK, firstStatus);
            Assert.Equal(GrowthCodes.Outcome.SUCCEEDED, registered.Outcome);
            Assert.Equal(1, registered.Growth.LoadoutRevision);

            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.TALISMAN }, 1);
            (HttpStatusCode replayStatus, GrowthOperationResponse replayed) = await PostAsync(client, seed,
                new TalismanRegisterRequest(first.OperationId, 0, EFFECT, 9999));
            TalismanRegisterRequest second = new TalismanRegisterRequest(Guid.NewGuid().ToString("D"), 1, EFFECT, 0);
            (HttpStatusCode blockedStatus, GrowthOperationResponse blocked) = await PostAsync(client, seed, second);

            Assert.Equal(HttpStatusCode.OK, replayStatus);
            Assert.Equal(registered.Result.ReceiptId, replayed.Result.ReceiptId);
            Assert.Equal(GrowthCodes.Outcome.SUCCEEDED, replayed.Outcome);
            Assert.Equal(HttpStatusCode.Conflict, blockedStatus);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED, blocked.Result.ReasonCode);
            Assert.Null(blocked.Growth);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @TownId AND operation_id = @OperationId;",
                new { seed.TownId, OperationId = Guid.Parse(second.OperationId) }));

            // 조회는 기능 차단과 무관하다
            using HttpRequestMessage get = new HttpRequestMessage(HttpMethod.Get,
                string.Format(GrowthRoutes.GROWTH_FORMAT, seed.CharacterId));
            Authorize(get, seed);
            using HttpResponseMessage growth = await client.SendAsync(get);
            Assert.Equal(HttpStatusCode.OK, growth.StatusCode);
        }

        [PostgresFact]
        public async Task RerollOptions_FeatureDisabled_ReplaysStoredAndRefusesNewWithoutSaving()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            Seed seed = await SeedAsync(database.DataSource, host.AuthOptions);
            Guid item = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE characters SET shards = 10 WHERE character_id = @CharacterId;
                    INSERT INTO equipment_instances (item_id, account_id, character_id, definition_id, slot, grade,
                        is_training, origin)
                    VALUES (@ItemId, @AccountId, @CharacterId, 'equip_weapon_common', 0, 0, false, 'Craft');
                    """, new { seed.AccountId, seed.CharacterId, ItemId = item });
            }
            await UpdateStateAsync(client, admin, Array.Empty<string>(), 0);
            string route = string.Format(GrowthRoutes.EQUIPMENT_REROLL_OPTIONS_FORMAT, seed.CharacterId, item);

            // 형식 오류는 400이고 작업으로 저장하지 않는다
            EquipmentRerollOptionsRequest malformed = new EquipmentRerollOptionsRequest(Guid.NewGuid().ToString("D"), 0,
                new[] { 0, 0, 0, EquipmentOptionRules.ROLL_RANGE });
            (HttpStatusCode malformedStatus, GrowthOperationResponse? _) = await PostAsync(client, seed, route, malformed);
            Assert.Equal(HttpStatusCode.BadRequest, malformedStatus);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE operation_id = @OperationId;",
                new { OperationId = Guid.Parse(malformed.OperationId) }));

            // 줄1 공격 100, 줄2 치명 20
            EquipmentRerollOptionsRequest first = new EquipmentRerollOptionsRequest(Guid.NewGuid().ToString("D"), 0,
                new[] { 0, 9999, 5000, 0 });
            (HttpStatusCode firstStatus, GrowthOperationResponse? rerolled) = await PostAsync(client, seed, route, first);
            Assert.Equal(HttpStatusCode.OK, firstStatus);
            Assert.NotNull(rerolled);
            Assert.Equal(GrowthCodes.Outcome.SUCCEEDED, rerolled.Outcome);
            Assert.Equal(item.ToString("D"), rerolled.ItemId);
            Assert.Equal((1L, 700L, 7L), (rerolled.Growth.LoadoutRevision, rerolled.Growth.Essence, rerolled.Growth.Shards));
            EquipmentView view = Assert.Single(rerolled.Growth.Equipment);
            Assert.Equal((EquipmentOptionRules.KIND_ATTACK_RATE, 100, EquipmentOptionRules.KIND_CRIT_CHANCE, 20, true),
                (view.Option1Kind, view.Option1Value, view.Option2Kind, view.Option2Value, view.Bound));
            Assert.Equal(new int[6], rerolled.Growth.OptionRerollPity);

            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.REROLL_OPTIONS }, 1);
            (HttpStatusCode replayStatus, GrowthOperationResponse? replayed) = await PostAsync(client, seed, route,
                new EquipmentRerollOptionsRequest(first.OperationId, 1, new[] { 0, 0, 0, 0 }));
            EquipmentRerollOptionsRequest second = new EquipmentRerollOptionsRequest(Guid.NewGuid().ToString("D"), 1,
                new[] { 0, 0, 0, 0 });
            (HttpStatusCode blockedStatus, GrowthOperationResponse? blocked) = await PostAsync(client, seed, route, second);

            Assert.Equal(HttpStatusCode.OK, replayStatus);
            Assert.NotNull(replayed);
            Assert.Equal(rerolled.Result.ReceiptId, replayed.Result.ReceiptId);
            Assert.Equal((700L, 100), (replayed.Growth.Essence, Assert.Single(replayed.Growth.Equipment).Option1Value));
            Assert.Equal(HttpStatusCode.Conflict, blockedStatus);
            Assert.NotNull(blocked);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED, blocked.Result.ReasonCode);
            Assert.Null(blocked.Growth);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE operation_id = @OperationId;",
                new { OperationId = Guid.Parse(second.OperationId) }));

            // 다른 기능 키(강화)는 옵션 재설정 차단과 무관하다
            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.ENHANCE }, 2);
            (HttpStatusCode reopenedStatus, GrowthOperationResponse? reopened) = await PostAsync(client, seed, route, second);
            Assert.Equal(HttpStatusCode.OK, reopenedStatus);
            Assert.NotNull(reopened);
            Assert.Equal((2L, 400L, 1), (reopened.Growth.LoadoutRevision, reopened.Growth.Essence,
                reopened.Growth.OptionRerollPity[0]));
        }

        private static async Task<(HttpStatusCode Status, GrowthOperationResponse? Body)> PostAsync(HttpClient client,
            Seed seed, string route, EquipmentRerollOptionsRequest body)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()),
                    Encoding.UTF8, JSON_MEDIA)
            };
            Authorize(request, seed);
            using HttpResponseMessage response = await client.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();
            GrowthOperationResponse? parsed = response.StatusCode == HttpStatusCode.BadRequest
                ? null : JsonConvert.DeserializeObject<GrowthOperationResponse>(text, ContractJson.CreateSettings());
            return (response.StatusCode, parsed);
        }

        private static string Format(string format)
        {
            return string.Format(format, "{characterId}", "{itemId}");
        }

        private static async Task<(HttpStatusCode Status, GrowthOperationResponse Body)> PostAsync(HttpClient client,
            Seed seed, TalismanRegisterRequest body)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post,
                string.Format(GrowthRoutes.TALISMAN_REGISTER_FORMAT, seed.CharacterId))
            {
                Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()),
                    Encoding.UTF8, JSON_MEDIA)
            };
            Authorize(request, seed);
            using HttpResponseMessage response = await client.SendAsync(request);
            GrowthOperationResponse? parsed = JsonConvert.DeserializeObject<GrowthOperationResponse>(
                await response.Content.ReadAsStringAsync(), ContractJson.CreateSettings());
            Assert.NotNull(parsed);
            return (response.StatusCode, parsed);
        }

        private static void Authorize(HttpRequestMessage request, Seed seed)
        {
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + seed.TownToken);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, seed.TownId.ToString("D"));
        }

        private static async Task UpdateStateAsync(HttpClient client, string adminToken, string[] disabled,
            long expectedRevision)
        {
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, adminToken, new AdminServiceStateUpdateRequest(
                    AdminApiTestHost.NewOperationId(), REASON, false, disabled, Array.Empty<string>(), expectedRevision));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static async Task<Seed> SeedAsync(NpgsqlDataSource dataSource, AuthTokenOptions options)
        {
            Seed seed = new Seed();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                // 시작 잔액은 대사 대상이 아니므로 이 HTTP 검사에서는 원장 없이 넣는다
                await connection.ExecuteAsync(
                    """
                    INSERT INTO accounts(account_id) VALUES (@AccountId);
                    INSERT INTO characters(character_id, account_id, class_id, essence) VALUES (@CharacterId, @AccountId, 0, 1000);
                    INSERT INTO account_items(account_id, definition_id, binding_kind, quantity)
                        VALUES (@AccountId, @Effect, 'Account', 3);
                    INSERT INTO server_instances(instance_id, role, address, port, build_id, content_version, state, boot_id)
                        VALUES (@TownId, @Town, '127.0.0.1', @TownPort, 'growth', 'growth', 'Idle', @TownBoot);
                    INSERT INTO account_leases(account_id, lease_id, instance_id, role, expires_at)
                        VALUES (@AccountId, @AccountLease, @TownId, @Town, clock_timestamp() + interval '15 minutes');
                    """, new
                    {
                        seed.AccountId, seed.CharacterId, seed.TownId, Town = ContractCodes.ServerRole.TOWN,
                        TownPort = TOWN_PORT, TownBoot = Guid.NewGuid(), AccountLease = Guid.NewGuid(), Effect = EFFECT
                    });
            }

            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            IssuedAuthToken lease = await auth.IssueServerLeaseAsync(Guid.NewGuid(), seed.TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(LEASE_MINUTES));
            seed.TownToken = lease.Token;
            return seed;
        }

        private sealed class Seed
        {
            public Guid   AccountId   { get; } = Guid.NewGuid();
            public Guid   CharacterId { get; } = Guid.NewGuid();
            public Guid   TownId      { get; } = Guid.NewGuid();
            public string TownToken   { get; set; } = string.Empty;
        }

        private sealed class GrowthGuardFactory : WebApplicationFactory<Program>
        {
            public IntegrationAuthRepository Repository { get; } = new IntegrationAuthRepository();

            public GrowthGuardFactory(int role)
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
