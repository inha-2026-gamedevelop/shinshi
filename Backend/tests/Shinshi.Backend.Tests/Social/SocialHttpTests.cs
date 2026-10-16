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
using Shinshi.Backend.Social;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Social
{
    /// <summary> 소셜 경로의 서버 역할 가드, 경로 상수, 형식 오류, 접속 확인, 기능 차단, 한도, 꺼내기를 Program 파이프라인으로 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class SocialHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CHARACTER     = "7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1c001";
        private const string OTHER         = "7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1c002";
        private const string EMPTY_BODY    = "{}";
        private const string JSON_MEDIA    = "application/json";
        private const string ADMIN_NAME    = "social.admin";
        private const string REASON        = "social gate check";
        private const int    TOWN_PORT     = 7821;
        private const int    DUNGEON_PORT  = 7822;
        private const int    LEASE_MINUTES = 15;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Templates_RouteTemplates_MatchSharedFormats()
        {
            Assert.Equal(SocialRouteTemplates.SNAPSHOT, Format(SocialRoutes.SNAPSHOT_FORMAT));
            Assert.Equal(SocialRouteTemplates.FRIEND_REQUEST, Format(SocialRoutes.FRIEND_REQUEST_FORMAT));
            Assert.Equal(SocialRouteTemplates.FRIEND_ACCEPT, Format(SocialRoutes.FRIEND_ACCEPT_FORMAT));
            Assert.Equal(SocialRouteTemplates.FRIEND_DECLINE, Format(SocialRoutes.FRIEND_DECLINE_FORMAT));
            Assert.Equal(SocialRouteTemplates.FRIEND_CANCEL, Format(SocialRoutes.FRIEND_CANCEL_FORMAT));
            Assert.Equal(SocialRouteTemplates.FRIEND_REMOVE, Format(SocialRoutes.FRIEND_REMOVE_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_CREATE, Format(SocialRoutes.GUILD_CREATE_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_APPLY, Format(SocialRoutes.GUILD_APPLY_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_APPLICATION_ACCEPT, Format(SocialRoutes.GUILD_APPLICATION_ACCEPT_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_APPLICATION_DECLINE, Format(SocialRoutes.GUILD_APPLICATION_DECLINE_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_INVITE, Format(SocialRoutes.GUILD_INVITE_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_INVITE_ACCEPT, Format(SocialRoutes.GUILD_INVITE_ACCEPT_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_INVITE_DECLINE, Format(SocialRoutes.GUILD_INVITE_DECLINE_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_LEAVE, Format(SocialRoutes.GUILD_LEAVE_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_KICK, Format(SocialRoutes.GUILD_KICK_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_DELEGATE, Format(SocialRoutes.GUILD_DELEGATE_FORMAT));
            Assert.Equal(SocialRouteTemplates.WHISPER, Format(SocialRoutes.WHISPER_FORMAT));
            Assert.Equal(SocialRouteTemplates.GUILD_CHAT, Format(SocialRoutes.GUILD_CHAT_FORMAT));
            Assert.Equal(SocialRouteTemplates.DELIVERY_PULL, string.Format(SocialRoutes.DELIVERY_PULL_FORMAT, "{instanceId}"));
        }

        [Theory]
        [InlineData(SocialRoutes.SNAPSHOT_FORMAT)]
        [InlineData(SocialRoutes.FRIEND_REQUEST_FORMAT)]
        [InlineData(SocialRoutes.FRIEND_ACCEPT_FORMAT)]
        [InlineData(SocialRoutes.GUILD_CREATE_FORMAT)]
        [InlineData(SocialRoutes.GUILD_LEAVE_FORMAT)]
        [InlineData(SocialRoutes.GUILD_DELEGATE_FORMAT)]
        [InlineData(SocialRoutes.WHISPER_FORMAT)]
        [InlineData(SocialRoutes.GUILD_CHAT_FORMAT)]
        [InlineData(SocialRoutes.DELIVERY_PULL_FORMAT)]
        public async Task Post_WithoutServerCredential_Returns401(string format)
        {
            using GuardFactory factory = new GuardFactory(ContractCodes.ServerRole.TOWN);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(string.Format(format, CHARACTER, OTHER),
                new StringContent(EMPTY_BODY, Encoding.UTF8, JSON_MEDIA));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary> 관리 경로는 마을만 받는다. 형식 오류 본문이어도 역할 판정이 먼저다 </summary>
        [Theory]
        [InlineData(SocialRoutes.SNAPSHOT_FORMAT)]
        [InlineData(SocialRoutes.FRIEND_REQUEST_FORMAT)]
        [InlineData(SocialRoutes.FRIEND_REMOVE_FORMAT)]
        [InlineData(SocialRoutes.GUILD_CREATE_FORMAT)]
        [InlineData(SocialRoutes.GUILD_KICK_FORMAT)]
        [InlineData(SocialRoutes.GUILD_DELEGATE_FORMAT)]
        public async Task Post_DungeonServerOnManagement_Returns403(string format)
        {
            using GuardFactory factory = new GuardFactory(ContractCodes.ServerRole.DUNGEON);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.SendAsync(Request(factory, string.Format(format, CHARACTER, OTHER),
                EMPTY_BODY));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        /// <summary> 채팅 경로는 던전도 받는다. 형식 오류는 DB 전에 400이다 </summary>
        [Theory]
        [InlineData(ContractCodes.ServerRole.TOWN)]
        [InlineData(ContractCodes.ServerRole.DUNGEON)]
        public async Task Chat_BothRoles_MalformedBodyIs400(int role)
        {
            using GuardFactory factory = new GuardFactory(role);
            using HttpClient client = factory.CreateClient();
            string tooLong = new string('a', SocialLimits.MAX_CHAT_LENGTH + 1);

            using HttpResponseMessage empty = await client.SendAsync(Request(factory,
                string.Format(SocialRoutes.WHISPER_FORMAT, CHARACTER), Json(new SocialWhisperRequest("Bob", string.Empty))));
            using HttpResponseMessage longText = await client.SendAsync(Request(factory,
                string.Format(SocialRoutes.GUILD_CHAT_FORMAT, CHARACTER), Json(new SocialGuildChatRequest(tooLong))));
            using HttpResponseMessage badId = await client.SendAsync(Request(factory,
                string.Format(SocialRoutes.GUILD_CHAT_FORMAT, "not-a-guid"), Json(new SocialGuildChatRequest("hi"))));

            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, longText.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        }

        [Fact]
        public async Task Management_MalformedBodyOrPath_Is400()
        {
            using GuardFactory factory = new GuardFactory(ContractCodes.ServerRole.TOWN);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage leave = await client.SendAsync(Request(factory,
                string.Format(SocialRoutes.GUILD_LEAVE_FORMAT, CHARACTER), Json(new SocialGuildMembershipRequest("x", OTHER))));
            using HttpResponseMessage create = await client.SendAsync(Request(factory,
                string.Format(SocialRoutes.GUILD_CREATE_FORMAT, CHARACTER), Json(new SocialGuildCreateRequest("청룡", "bad"))));
            using HttpResponseMessage accept = await client.SendAsync(Request(factory,
                string.Format(SocialRoutes.FRIEND_ACCEPT_FORMAT, CHARACTER, "bad"), EMPTY_BODY));
            using HttpResponseMessage pull = await client.SendAsync(Request(factory,
                string.Format(SocialRoutes.DELIVERY_PULL_FORMAT, factory.Repository.Server.InstanceId),
                Json(new SocialDeliveryPullRequest(Guid.NewGuid().ToString("D"), 1000, Array.Empty<SocialDeliveryAck>()))));
            using HttpResponseMessage foreign = await client.SendAsync(Request(factory,
                string.Format(SocialRoutes.DELIVERY_PULL_FORMAT, Guid.NewGuid()),
                Json(new SocialDeliveryPullRequest(Guid.NewGuid().ToString("D"), 0, Array.Empty<SocialDeliveryAck>()))));

            Assert.Equal(HttpStatusCode.BadRequest, leave.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, accept.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, pull.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        }

        [PostgresFact]
        public async Task Social_EndToEnd_GuardsGateLimitAndPull()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            Seed seed = await SeedAsync(database.DataSource, host.AuthOptions);

            // 친구 요청 200, 상대에게 알림 행
            (HttpStatusCode requestStatus, SocialActionResponse requested) = await SendAsync<SocialActionResponse>(client,
                seed.TownToken, seed.TownId, string.Format(SocialRoutes.FRIEND_REQUEST_FORMAT, seed.Alice),
                new SocialTargetNameRequest("ＢＯＢ"));
            Assert.Equal(HttpStatusCode.OK, requestStatus);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, requested.Status);
            Assert.Equal(new[] { seed.Bob.ToString("D") }, requested.ChangedCharacterIds);

            // 던전에 있는 캐릭터를 마을이 대신 움직이면 409 SOCIAL_NOT_CONNECTED
            (HttpStatusCode foreignStatus, SocialActionResponse foreign) = await SendAsync<SocialActionResponse>(client,
                seed.TownToken, seed.TownId, string.Format(SocialRoutes.FRIEND_REQUEST_FORMAT, seed.Dan),
                new SocialTargetNameRequest("Alice"));
            Assert.Equal((HttpStatusCode.Conflict, ContractCodes.RejectReason.SOCIAL_NOT_CONNECTED),
                (foreignStatus, foreign.ReasonCode));
            (HttpStatusCode snapshotForeign, ApiRejection snapshotRejection) = await SendAsync<ApiRejection>(client,
                seed.TownToken, seed.TownId, string.Format(SocialRoutes.SNAPSHOT_FORMAT, seed.Dan), null);
            Assert.Equal((HttpStatusCode.Conflict, ContractCodes.RejectReason.SOCIAL_NOT_CONNECTED),
                (snapshotForeign, snapshotRejection.ReasonCode));

            // 스냅샷
            (HttpStatusCode snapshotStatus, SocialSnapshotResponse snapshot) = await SendAsync<SocialSnapshotResponse>(client,
                seed.TownToken, seed.TownId, string.Format(SocialRoutes.SNAPSHOT_FORMAT, seed.Bob), null);
            Assert.Equal(HttpStatusCode.OK, snapshotStatus);
            Assert.Equal(seed.Alice.ToString("D"), Assert.Single(snapshot.Incoming).CharacterId);
            Assert.False(snapshot.Guild.HasGuild);

            // 던전 서버의 귓속말(마을 대상): 로컬 행 없음, 마을 꺼내기로 받는다
            (HttpStatusCode whisperStatus, SocialWhisperResponse whisper) = await SendAsync<SocialWhisperResponse>(client,
                seed.DungeonToken, seed.DungeonId, string.Format(SocialRoutes.WHISPER_FORMAT, seed.Dan),
                new SocialWhisperRequest("alice", "안녕"));
            Assert.Equal(HttpStatusCode.OK, whisperStatus);
            Assert.Equal(("Alice", 0), (whisper.TargetName, whisper.LocalDeliveries.Length));
            (HttpStatusCode pullStatus, SocialDeliveryPullResponse pulled) = await SendAsync<SocialDeliveryPullResponse>(client,
                seed.TownToken, seed.TownId, string.Format(SocialRoutes.DELIVERY_PULL_FORMAT, seed.TownId),
                new SocialDeliveryPullRequest(seed.TownBoot.ToString("D"), 0, Array.Empty<SocialDeliveryAck>()));
            Assert.Equal(HttpStatusCode.OK, pullStatus);
            Assert.Equal(2, pulled.Deliveries.Length);
            SocialDeliveryRow line = Array.Find(pulled.Deliveries, row => row.Kind == SocialCodes.DeliveryKind.WHISPER)!;
            Assert.Equal((seed.Alice.ToString("D"), "Dan", "안녕", seed.Dan.ToString("D")),
                (line.TargetCharacterId, line.SenderName, line.Payload, line.SenderCharacterId));
            (HttpStatusCode ackStatus, _) = await SendAsync<SocialDeliveryPullResponse>(client, seed.TownToken, seed.TownId,
                string.Format(SocialRoutes.DELIVERY_PULL_FORMAT, seed.TownId), new SocialDeliveryPullRequest(
                    seed.TownBoot.ToString("D"), 0, new[] { new SocialDeliveryAck(line.DeliveryId, line.TargetCharacterId) }));
            Assert.Equal(HttpStatusCode.OK, ackStatus);
            Assert.Equal(0L, await ScalarAsync(database.DataSource, "SELECT count(*) FROM social_deliveries WHERE delivery_id = @Id;",
                new { Id = line.DeliveryId }));
            (HttpStatusCode bootStatus, ApiRejection boot) = await SendAsync<ApiRejection>(client, seed.TownToken, seed.TownId,
                string.Format(SocialRoutes.DELIVERY_PULL_FORMAT, seed.TownId),
                new SocialDeliveryPullRequest(Guid.NewGuid().ToString("D"), 0, Array.Empty<SocialDeliveryAck>()));
            Assert.Equal((HttpStatusCode.Conflict, ContractCodes.RejectReason.SESSION_MISMATCH), (bootStatus, boot.ReasonCode));

            // 플레이어 접근 토큰, 어드민 토큰으로는 서버 경로를 부를 수 없다
            (HttpStatusCode adminStatus, _) = await SendAsync<ApiRejection>(client, admin, seed.TownId,
                string.Format(SocialRoutes.SNAPSHOT_FORMAT, seed.Bob), null);
            (HttpStatusCode playerStatus, _) = await SendAsync<ApiRejection>(client, seed.PlayerToken, seed.TownId,
                string.Format(SocialRoutes.SNAPSHOT_FORMAT, seed.Bob), null);
            Assert.Equal(HttpStatusCode.Unauthorized, adminStatus);
            Assert.Equal(HttpStatusCode.Unauthorized, playerStatus);

            // 기능 차단: 관리 쓰기와 채팅은 409 FEATURE_DISABLED, 스냅샷, 탈퇴, 꺼내기는 허용
            await SendAsync<SocialActionResponse>(client, seed.TownToken, seed.TownId,
                string.Format(SocialRoutes.GUILD_CREATE_FORMAT, seed.Bob), new SocialGuildCreateRequest("청룡", Guid.NewGuid().ToString("D")));
            Guid guildId = await SingleAsync<Guid>(database.DataSource, "SELECT guild_id FROM guilds;");
            Guid membership = await SingleAsync<Guid>(database.DataSource, "SELECT membership_id FROM guild_members;");
            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.SOCIAL }, 0);
            (HttpStatusCode blockedStatus, SocialActionResponse blocked) = await SendAsync<SocialActionResponse>(client,
                seed.TownToken, seed.TownId, string.Format(SocialRoutes.FRIEND_ACCEPT_FORMAT, seed.Bob, seed.Alice), null);
            (HttpStatusCode blockedChat, SocialGuildChatResponse chat) = await SendAsync<SocialGuildChatResponse>(client,
                seed.TownToken, seed.TownId, string.Format(SocialRoutes.GUILD_CHAT_FORMAT, seed.Bob),
                new SocialGuildChatRequest("막힘"));
            (HttpStatusCode openSnapshot, _) = await SendAsync<SocialSnapshotResponse>(client, seed.TownToken, seed.TownId,
                string.Format(SocialRoutes.SNAPSHOT_FORMAT, seed.Bob), null);
            (HttpStatusCode leaveStatus, SocialActionResponse left) = await SendAsync<SocialActionResponse>(client,
                seed.TownToken, seed.TownId, string.Format(SocialRoutes.GUILD_LEAVE_FORMAT, seed.Bob),
                new SocialGuildMembershipRequest(guildId.ToString("D"), membership.ToString("D")));
            Assert.Equal((HttpStatusCode.Conflict, ContractCodes.RejectReason.FEATURE_DISABLED),
                (blockedStatus, blocked.ReasonCode));
            Assert.Equal((HttpStatusCode.Conflict, ContractCodes.RejectReason.FEATURE_DISABLED),
                (blockedChat, chat.Result.ReasonCode));
            Assert.Equal(HttpStatusCode.OK, openSnapshot);
            Assert.Equal((HttpStatusCode.OK, ContractCodes.OperationStatus.COMMITTED), (leaveStatus, left.Status));
            Assert.Equal(1L, await ScalarAsync(database.DataSource, "SELECT count(*) FROM friend_requests;", null));
            await UpdateStateAsync(client, admin, Array.Empty<string>(), 1);

            // 캐릭터별 관리 쓰기 한도(1분 30회)를 넘으면 429와 Retry-After
            HttpStatusCode last = HttpStatusCode.OK;
            for (int attempt = 0; attempt <= SocialRequestLimiter.MAX_WRITES_PER_MINUTE; ++attempt)
            {
                (last, _) = await SendAsync<ApiRejection>(client, seed.TownToken, seed.TownId,
                    string.Format(SocialRoutes.FRIEND_CANCEL_FORMAT, seed.Eve, seed.Alice), null);
            }
            Assert.Equal(HttpStatusCode.TooManyRequests, last);
        }

        private static string Format(string format)
        {
            return string.Format(format, "{characterId}", "{otherId}");
        }

        private static HttpRequestMessage Request(GuardFactory factory, string path, string body)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, JSON_MEDIA)
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));
            return request;
        }

        private static string Json(object body)
        {
            return JsonConvert.SerializeObject(body, ContractJson.CreateSettings());
        }

        private static async Task<(HttpStatusCode Status, T Body)> SendAsync<T>(HttpClient client, string token, Guid instance,
            string route, object? body)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = new StringContent(body == null ? string.Empty : Json(body), Encoding.UTF8, JSON_MEDIA)
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, instance.ToString("D"));
            using HttpResponseMessage response = await client.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();
            T? parsed = string.IsNullOrEmpty(text) ? default : JsonConvert.DeserializeObject<T>(text, ContractJson.CreateSettings());
            return (response.StatusCode, parsed!);
        }

        private static async Task UpdateStateAsync(HttpClient client, string adminToken, string[] disabled, long expectedRevision)
        {
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Put,
                AdminApiRoutes.SERVICE_STATE, adminToken, new AdminServiceStateUpdateRequest(
                    AdminApiTestHost.NewOperationId(), REASON, false, disabled, Array.Empty<string>(), expectedRevision));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static async Task<long> ScalarAsync(NpgsqlDataSource dataSource, string sql, object? parameters)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>(sql, parameters);
        }

        private static async Task<T> SingleAsync<T>(NpgsqlDataSource dataSource, string sql)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<T>(sql);
        }

        private static async Task<Seed> SeedAsync(NpgsqlDataSource dataSource, AuthTokenOptions options)
        {
            Seed seed = new Seed();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO accounts (account_id) VALUES (@AliceAccount), (@BobAccount), (@DanAccount), (@EveAccount);
                    INSERT INTO characters (character_id, account_id, class_id, name, name_key) VALUES
                        (@Alice, @AliceAccount, 5, 'Alice', 'alice'), (@Bob, @BobAccount, 4, 'Bob', 'bob'),
                        (@Dan, @DanAccount, 3, 'Dan', 'dan'), (@Eve, @EveAccount, 3, 'Eve', 'eve');
                    INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state, boot_id)
                    VALUES (@TownId, 0, '127.0.0.1', @TownPort, 'social', 'social', 'Idle', @TownBoot),
                        (@DungeonId, 1, '127.0.0.1', @DungeonPort, 'social', 'social', 'Idle', @DungeonBoot);
                    INSERT INTO account_leases (account_id, lease_id, instance_id, role, expires_at, character_id) VALUES
                        (@AliceAccount, gen_random_uuid(), @TownId, 0, clock_timestamp() + interval '15 minutes', @Alice),
                        (@BobAccount, gen_random_uuid(), @TownId, 0, clock_timestamp() + interval '15 minutes', @Bob),
                        (@DanAccount, gen_random_uuid(), @DungeonId, 1, clock_timestamp() + interval '15 minutes', @Dan),
                        (@EveAccount, gen_random_uuid(), @TownId, 0, clock_timestamp() + interval '15 minutes', @Eve);
                    """, new
                    {
                        seed.AliceAccount, seed.BobAccount, seed.DanAccount, seed.EveAccount, seed.Alice, seed.Bob, seed.Dan,
                        seed.Eve, seed.TownId, seed.DungeonId, seed.TownBoot, seed.DungeonBoot, TownPort = TOWN_PORT,
                        DungeonPort = DUNGEON_PORT
                    });
            }

            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            seed.TownToken = (await auth.IssueServerLeaseAsync(Guid.NewGuid(), seed.TownId, ContractCodes.ServerRole.TOWN,
                seed.TownBoot, TimeSpan.FromMinutes(LEASE_MINUTES))).Identity!.Token;
            seed.DungeonToken = (await auth.IssueServerLeaseAsync(Guid.NewGuid(), seed.DungeonId,
                ContractCodes.ServerRole.DUNGEON, seed.DungeonBoot, TimeSpan.FromMinutes(LEASE_MINUTES))).Identity!.Token;
            IssuedAuthToken invite = await auth.IssueInviteAsync(seed.EveAccount);
            seed.PlayerToken = (await auth.ExchangeInviteAsync(invite.Token)).Response!.AccessToken;
            return seed;
        }

        private sealed class Seed
        {
            public Guid   AliceAccount { get; } = Guid.NewGuid();
            public Guid   BobAccount   { get; } = Guid.NewGuid();
            public Guid   DanAccount   { get; } = Guid.NewGuid();
            public Guid   EveAccount   { get; } = Guid.NewGuid();
            public Guid   Alice        { get; } = Guid.NewGuid();
            public Guid   Bob          { get; } = Guid.NewGuid();
            public Guid   Dan          { get; } = Guid.NewGuid();
            public Guid   Eve          { get; } = Guid.NewGuid();
            public Guid   TownId       { get; } = Guid.NewGuid();
            public Guid   DungeonId    { get; } = Guid.NewGuid();
            public Guid   TownBoot     { get; } = Guid.NewGuid();
            public Guid   DungeonBoot  { get; } = Guid.NewGuid();
            public string TownToken    { get; set; } = string.Empty;
            public string DungeonToken { get; set; } = string.Empty;
            public string PlayerToken  { get; set; } = string.Empty;
        }

        private sealed class GuardFactory : WebApplicationFactory<Program>
        {
            public IntegrationAuthRepository Repository { get; } = new IntegrationAuthRepository();

            public GuardFactory(int role)
            {
                Repository.Server = new ServerAuthIdentity(Repository.Server.InstanceId, Repository.Server.LeaseId,
                    Repository.Server.ServiceId, role, long.MaxValue);
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureTestServices(services =>
                {
                    // 연결하지 않는 데이터 소스. 가드와 형식 검사가 DB 전에 막는다
                    services.AddSingleton(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=test;Database=test"));
                    services.AddSingleton<IAuthRepository>(Repository);
                    services.AddSingleton<IJoinTokenIssuer, FakeJoinTokenIssuer>();
                    services.AddSingleton<IEconomyLedgerWriter, TestLedgerWriter>();
                });
            }
        }
    }
}
