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
using Shinshi.Data.Life;

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
        private const string CONTENT = "0.1.2";
        private const string BATTLE_ID = "battle_forest_1";
        private const long POTIONS_OWNED = 5;
        private const long POTIONS_CARRIED = 3;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Exchange_ReservationIssuerAndRenewedDungeonLease_ReturnsSharedContractExactlyOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            // 헤어/의상은 직업과 독립이다. 소환사(SUMMONER) 캐릭터에 자기 직업 기본 의상이 아닌 조합을 심어
            // 교환 응답이 직업 기본값이 아니라 DB에 저장된 그대로의 닉네임/외형을 돌려주는지 가린다
            const string OWNED_NAME = "서버검증";
            CharacterAppearance ownedAppearance = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_SUMMONER, CharacterAppearanceCatalog.OUTFIT_SORCERER);
            Fixture fixture = await SeedAsync(database, ContractCodes.PlayerClass.SUMMONER, OWNED_NAME,
                JsonConvert.SerializeObject(ownedAppearance, ContractJson.CreateSettings()));
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
            Assert.Equal(new[] { "accountId", "accountLeaseId", "appearance", "battleId", "bossId", "characterId", "characterName",
                    "classId", "combatLoadout", "difficulty", "experience", "instanceId", "mapId", "mode", "participantId", "potions",
                    "serverRole", "serverTimeUnixMs", "sortieId" },
                JObject.Parse(responseBody).Properties().Select(property => property.Name).OrderBy(name => name).ToArray());
            Assert.Null(result.AccountLeaseId);

            // 출정 계약은 예약 때 저장한 값 그대로다. 일반전이라 bossId는 값 없음, 난이도는 NONE이다
            Assert.Equal(GameSessionCodes.Mode.NORMAL, result.Mode);
            Assert.Equal("region_forest", result.MapId);
            Assert.Equal(BATTLE_ID, result.BattleId);
            Assert.Null(result.BossId);
            Assert.Equal(GameSessionCodes.Difficulty.NONE, result.Difficulty);

            // 던전 입장도 참가자가 묶인 캐릭터의 직업을 API가 준다. 무사(기본값)가 아닌 직업으로 심어 기본값 누락을 가린다
            Assert.Equal(ContractCodes.PlayerClass.SUMMONER, result.ClassId);

            // 예약 때 고정한 전투 준비가 함께 온다. 장비 없는 캐릭터면 6칸이 비어 있고 수련은 0이다
            Assert.NotNull(result.CombatLoadout);
            Assert.Equal(ContractCodes.PlayerClass.SUMMONER, result.CombatLoadout.ClassId);
            Assert.Equal(6, result.CombatLoadout.Equipped.Length);
            Assert.Equal(new int[5], result.CombatLoadout.Training);

            // 물약을 지참하지 않았으면 남은 예약분은 null이 아니라 빈 배열이다. 던전은 null을 모르는 API로 본다
            Assert.NotNull(result.Potions);
            Assert.Empty(result.Potions);

            // 던전 입장도 DB 소유 캐릭터의 닉네임과 외형 원문을 그대로 전달한다. 직업 기본값으로 대체하지 않는다
            Assert.Equal(OWNED_NAME, result.CharacterName);
            CharacterAppearance returnedAppearance = JsonConvert.DeserializeObject<CharacterAppearance>(
                result.AppearanceJson!, ContractJson.CreateSettings())!;
            Assert.True(ownedAppearance.ContentEquals(returnedAppearance));

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
        public async Task Exchange_TownJoinToken_ReturnsCharacterClass()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database, ContractCodes.PlayerClass.SORCERER);

            // 시험 시드는 예약 검사용 계정 임대를 미리 넣어 두므로, 마을 입장 교환이 임대를 새로 얻도록 지운다
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;",
                new { fixture.AccountId });
            IssuedAuthToken invite = await fixture.Auth.IssueInviteAsync(fixture.AccountId);
            AuthExchangeResponse login = (await fixture.Auth.ExchangeInviteAsync(invite.Token)).Response!;
            PlayerAuthIdentity identity = (await fixture.Auth.ValidateAccessAsync(login.AccessToken)).Identity!;
            IssuedAuthToken joinToken = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = factory.CreateClient();
            Authorize(town, fixture.TownLease.Token, fixture.TownId);

            using HttpResponseMessage exchange = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(joinToken.Token, fixture.TownId.ToString("D"), ContractCodes.ServerRole.TOWN)));

            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            JoinTokenExchangeResponse result = Read<JoinTokenExchangeResponse>(await exchange.Content.ReadAsStringAsync());
            Assert.Equal(fixture.CharacterId.ToString("D"), result.CharacterId);
            Assert.Equal(ContractCodes.PlayerClass.SORCERER, result.ClassId);
            Assert.Null(result.ParticipantId);
            Assert.NotNull(result.AccountLeaseId);
        }

        // 마을 입장은 세 저장 형태(명시 레거시 null, v1, v2) 모두 DB 소유 캐릭터의 닉네임/외형 원문을 그대로 전달하고,
        // 다른 계정의 값과 섞이지 않는다. consumed once 조건은 위 던전 테스트의 재전송 단언이 같은 경로로 이미 지킨다
        [PostgresFact]
        public async Task Exchange_TownJoinToken_PassesThroughOwnedNameAndAppearanceAcrossStorageVariantsWithoutCrossAccountLeak()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture legacy = await SeedAsync(database, ContractCodes.PlayerClass.WARRIOR);
            CharacterAppearance v1Appearance = new CharacterAppearance(CharacterAppearance.VERSION_1,
                CharacterAppearance.BODY_TYPE_FEMALE,
                Enumerable.Repeat(CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.FACE_SLIDER_COUNT).ToArray(),
                Enumerable.Repeat(CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.BODY_SLIDER_COUNT).ToArray(),
                10, 1, 2, 3, 4, 5);
            const string V1_NAME = "구닉네임";
            Fixture v1 = await SeedAsync(database, ContractCodes.PlayerClass.SORCERER, V1_NAME,
                JsonConvert.SerializeObject(v1Appearance, ContractJson.CreateSettings()));
            const string OTHER_NAME = "다른계정";
            CharacterAppearance otherAppearance = CharacterAppearanceCatalog.CreateDefault(ContractCodes.PlayerClass.WARRIOR);
            Fixture other = await SeedAsync(database, ContractCodes.PlayerClass.WARRIOR, OTHER_NAME,
                JsonConvert.SerializeObject(otherAppearance, ContractJson.CreateSettings()));

            // 시험 시드는 예약 검사용 계정 임대를 미리 넣어 두므로, 마을 입장 교환이 임대를 새로 얻도록 지운다
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM account_leases WHERE account_id IN (@A,@B,@C);",
                new { A = legacy.AccountId, B = v1.AccountId, C = other.AccountId });

            JoinTokenExchangeResponse legacyResult = await ExchangeTownAsync(database, legacy);
            JoinTokenExchangeResponse v1Result = await ExchangeTownAsync(database, v1);
            JoinTokenExchangeResponse otherResult = await ExchangeTownAsync(database, other);

            Assert.Null(legacyResult.CharacterName);
            Assert.Null(legacyResult.AppearanceJson);

            Assert.Equal(V1_NAME, v1Result.CharacterName);
            CharacterAppearance storedV1 = JsonConvert.DeserializeObject<CharacterAppearance>(v1Result.AppearanceJson!,
                ContractJson.CreateSettings())!;
            Assert.True(v1Appearance.ContentEquals(storedV1));

            Assert.Equal(OTHER_NAME, otherResult.CharacterName);
            CharacterAppearance storedOther = JsonConvert.DeserializeObject<CharacterAppearance>(
                otherResult.AppearanceJson!, ContractJson.CreateSettings())!;
            Assert.True(otherAppearance.ContentEquals(storedOther));

            // 다른 계정 소유 캐릭터의 교환 응답이 섞이지 않았다
            Assert.NotEqual(v1Result.CharacterId, otherResult.CharacterId);
            Assert.NotEqual(v1Result.CharacterName, otherResult.CharacterName);
            Assert.NotEqual(v1Result.AppearanceJson, otherResult.AppearanceJson);
        }

        // v3(P09) 외형은 성별과 수염, 가슴 크기, 장비 비트까지 가공 없이 입장 교환 응답에 실려야 한다
        [PostgresFact]
        public async Task Exchange_TownJoinToken_PassesThroughStoredV3AppearanceUnchanged()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            CharacterAppearance male = CharacterAppearanceCatalog.CreateP09(CharacterAppearance.BODY_TYPE_MALE, 2, 3, 7, 4, 3, 6, 0, 21);
            CharacterAppearance female = CharacterAppearanceCatalog.CreateP09(CharacterAppearance.BODY_TYPE_FEMALE, 3, 2, 8, 3, 1, 0, 3, 5);
            Assert.True(CharacterAppearanceCatalog.IsAllowed(ContractCodes.PlayerClass.SUMMONER, male));
            Assert.True(CharacterAppearanceCatalog.IsAllowed(ContractCodes.PlayerClass.SORCERER, female));
            Fixture maleFixture = await SeedAsync(database, ContractCodes.PlayerClass.SUMMONER, "남성외형",
                JsonConvert.SerializeObject(male, ContractJson.CreateSettings()));
            Fixture femaleFixture = await SeedAsync(database, ContractCodes.PlayerClass.SORCERER, "여성외형",
                JsonConvert.SerializeObject(female, ContractJson.CreateSettings()));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM account_leases WHERE account_id IN (@A,@B);",
                new { A = maleFixture.AccountId, B = femaleFixture.AccountId });

            JoinTokenExchangeResponse maleResult = await ExchangeTownAsync(database, maleFixture);
            JoinTokenExchangeResponse femaleResult = await ExchangeTownAsync(database, femaleFixture);

            CharacterAppearance maleBack = JsonConvert.DeserializeObject<CharacterAppearance>(maleResult.AppearanceJson!,
                ContractJson.CreateSettings())!;
            CharacterAppearance femaleBack = JsonConvert.DeserializeObject<CharacterAppearance>(femaleResult.AppearanceJson!,
                ContractJson.CreateSettings())!;
            Assert.Equal(CharacterAppearance.VERSION_3, maleBack.Version);
            Assert.True(male.ContentEquals(maleBack));
            Assert.Equal(6, maleBack.FacialHair);
            Assert.True(female.ContentEquals(femaleBack));
            Assert.Equal(3, femaleBack.BustSize);
        }

        private static async Task<JoinTokenExchangeResponse> ExchangeTownAsync(PostgresTestDatabase database, Fixture fixture)
        {
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = factory.CreateClient();
            Authorize(town, fixture.TownLease.Token, fixture.TownId);
            IssuedAuthToken invite = await fixture.Auth.IssueInviteAsync(fixture.AccountId);
            AuthExchangeResponse login = (await fixture.Auth.ExchangeInviteAsync(invite.Token)).Response!;
            PlayerAuthIdentity identity = (await fixture.Auth.ValidateAccessAsync(login.AccessToken)).Identity!;
            IssuedAuthToken joinToken = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;

            using HttpResponseMessage exchange = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(joinToken.Token, fixture.TownId.ToString("D"), ContractCodes.ServerRole.TOWN)));
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            return Read<JoinTokenExchangeResponse>(await exchange.Content.ReadAsStringAsync());
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

        [PostgresFact]
        public async Task Exchange_DungeonJoinWithReservedPotions_ReturnsRemainingReservation()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database, ContractCodes.PlayerClass.WARRIOR);

            // 입장 토큰은 로그인한 계정 세션 계보에 묶여 발급된다
            IssuedAuthToken invite = await fixture.Auth.IssueInviteAsync(fixture.AccountId);
            Assert.NotNull((await fixture.Auth.ExchangeInviteAsync(invite.Token)).Response);
            await using (NpgsqlConnection seed = await database.DataSource.OpenConnectionAsync())
            {
                await seed.ExecuteAsync(
                    "INSERT INTO account_items(account_id, definition_id, binding_kind, quantity) VALUES (@AccountId, @Potion, @Binding, @Owned);",
                    new { fixture.AccountId, Potion = LifeContent.POTION_SMALL_ID, Binding = LifeContent.BINDING_KIND,
                        Owned = POTIONS_OWNED });
            }

            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = factory.CreateClient();
            Authorize(town, fixture.TownLease.Token, fixture.TownId);
            ReservationResponse reservation = await ReserveAsync(town, fixture,
                new[] { new ReservationPotion(LifeContent.POTION_SMALL_ID, LifeContent.BINDING_KIND, POTIONS_CARRIED) });

            // 확정된 사용 하나를 흉내 낸다. 교환 응답은 예약 수량에서 확정 사용을 뺀 값이다
            await using (NpgsqlConnection used = await database.DataSource.OpenConnectionAsync())
            {
                await used.ExecuteAsync("UPDATE reservations SET consumed = 1 WHERE asset_kind = 'Item';");
            }

            using HttpResponseMessage issue = await town.PostAsync(string.Format(GameServerRoutes.JOIN_TICKETS_FORMAT, reservation.SessionId),
                new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
            JoinTicket ticket = Assert.Single(Read<JoinTicketsResponse>(await issue.Content.ReadAsStringAsync()).Tickets);

            using HttpClient dungeon = factory.CreateClient();
            Authorize(dungeon, fixture.DungeonLease.Token, fixture.DungeonId);
            using HttpResponseMessage exchange = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(ticket.JoinToken, fixture.DungeonId.ToString("D"), ContractCodes.ServerRole.DUNGEON)));
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            JoinTokenExchangeResponse result = Read<JoinTokenExchangeResponse>(await exchange.Content.ReadAsStringAsync());
            ReservationPotion potion = Assert.Single(result.Potions);
            Assert.Equal(LifeContent.POTION_SMALL_ID, potion.DefinitionId);
            Assert.Equal(LifeContent.BINDING_KIND, potion.BindingKind);
            Assert.Equal(POTIONS_CARRIED - 1, potion.Quantity);
        }

        private static Task<ReservationResponse> ReserveAsync(HttpClient town, Fixture fixture)
        {
            return ReserveAsync(town, fixture, Array.Empty<ReservationPotion>());
        }

        private static async Task<ReservationResponse> ReserveAsync(HttpClient town, Fixture fixture, ReservationPotion[] potions)
        {
            ReservationRequest request = new ReservationRequest(Guid.NewGuid().ToString("D"), GameSessionCodes.Mode.NORMAL,
                "region_forest", CONTENT, new[] { new ReservationParticipant(fixture.AccountId.ToString("D"),
                    fixture.CharacterId.ToString("D"), 0, potions) }, BATTLE_ID, null,
                GameSessionCodes.Difficulty.NONE);
            using HttpResponseMessage response = await town.PostAsync(GameServerRoutes.RESERVATIONS, Body(request));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ReservationResponse reservation = Read<ReservationResponse>(await response.Content.ReadAsStringAsync());
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reservation.Result.Status);
            return reservation;
        }

        // name/appearanceJson을 비워 두면 과거 레거시 행처럼 둘 다 NULL이고, 채우면 그 값으로 실제 저장된 캐릭터를 심는다.
        // name_key는 CharacterNameRules와 같은 규칙(소문자)으로 맞춰야 characters 테이블의 CHECK를 통과한다
        private static async Task<Fixture> SeedAsync(PostgresTestDatabase database,
            int classId = ContractCodes.PlayerClass.WARRIOR, string? name = null, string? appearanceJson = null)
        {
            Fixture fixture = new Fixture(database.DataSource);
            string? nameKey = (name == null) ? null : name.ToLowerInvariant();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts(account_id) VALUES (@AccountId);
                INSERT INTO characters(character_id,account_id,class_id,name,name_key,appearance)
                    VALUES (@CharacterId,@AccountId,@Class,@Name,@NameKey,CAST(@Appearance AS jsonb));
                INSERT INTO account_wallets(account_id) VALUES (@AccountId);
                INSERT INTO server_instances(instance_id,role,address,port,build_id,content_version,state,boot_id)
                    VALUES (@TownId,@Town,'127.0.0.1',@TownPort,@Content,@Content,'Idle',@TownBoot),
                        (@DungeonId,@Dungeon,'127.0.0.1',@DungeonPort,@Content,@Content,'Idle',@DungeonBoot);
                INSERT INTO account_leases(account_id,lease_id,instance_id,role,expires_at)
                    VALUES (@AccountId,@AccountLease,@TownId,@Town,clock_timestamp() + interval '15 minutes');
                """, new { fixture.AccountId, fixture.CharacterId, fixture.TownId, fixture.DungeonId,
                    Class = classId, Name = name, NameKey = nameKey, Appearance = appearanceJson,
                    Town = ContractCodes.ServerRole.TOWN, Dungeon = ContractCodes.ServerRole.DUNGEON,
                    TownPort = TOWN_PORT, DungeonPort = DUNGEON_PORT, Content = CONTENT, TownBoot = Guid.NewGuid(),
                    DungeonBoot = Guid.NewGuid(), AccountLease = Guid.NewGuid() });
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
