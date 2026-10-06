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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Auth
{
    /// <summary> 실제 PostgreSQL과 Program으로 출정 계약(보스 bossId, 난이도), 교환 응답의 계약 필드, 포기 이탈의 임대 해제와 복귀 거절,
    /// 경매 중 재입장, 출정 준비 확인, 캐릭터 삭제 HTTP를 검사한다 </summary>
    public sealed class SessionContractHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    SERVER_MINUTES = 15;
        private const int    TOWN_PORT      = 7777;
        private const int    DUNGEON_PORT   = 7780;
        private const string CONTENT        = "contract-http-test";
        private const string BOSS_ID        = "boss_wolbaek";
        private const string REGION_FOREST  = "region_forest";
        private const long   SEED_TICKETS   = 3;
        private const long   SEED_POTIONS   = 5;
        private const long   RESERVED_POTION = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스전 예약은 bossId 없이 400이고, 저장된 bossId와 난이도가 재입장 교환 응답에 그대로 온다.
        /// 경매 단계는 마감 전까지 복귀 토큰을 마감 이전 만료로 발급하고 교환도 받으며, 마감 뒤에는 SESSION_TERMINAL이다 </summary>
        [PostgresFact]
        public async Task BossSortie_ContractInExchangeAndAuctionRejoinUntilDeadline()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = Server(factory, fixture.TownLease.Token, fixture.TownId);
            using HttpClient dungeon = Server(factory, fixture.DungeonLease.Token, fixture.DungeonId);
            using HttpClient player = Server(factory, await GameTokenAsync(fixture, fixture.AccountId), fixture.DungeonId);

            using HttpResponseMessage noBoss = await town.PostAsync(GameServerRoutes.RESERVATIONS,
                Body(Reservation(fixture, GameSessionCodes.Mode.BOSS, null, null, GameSessionCodes.Difficulty.HARD)));
            using HttpResponseMessage bossInNormal = await town.PostAsync(GameServerRoutes.RESERVATIONS,
                Body(Reservation(fixture, GameSessionCodes.Mode.NORMAL, null, BOSS_ID, GameSessionCodes.Difficulty.NONE)));
            Assert.Equal(HttpStatusCode.BadRequest, noBoss.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, bossInNormal.StatusCode);

            ReservationResponse reservation = await ReserveAsync(town, Reservation(fixture, GameSessionCodes.Mode.BOSS,
                null, BOSS_ID, GameSessionCodes.Difficulty.HARD));
            await StartAsync(dungeon, reservation);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM game_sessions WHERE session_id = @Id AND mode = 'Boss' AND boss_id = @Boss
                    AND difficulty = @Hard AND battle_id IS NULL;
                """, new { Id = Guid.Parse(reservation.SessionId), Boss = BOSS_ID,
                    Hard = GameSessionCodes.Difficulty.HARD }));

            string rejoinPath = string.Format(ApiRoutes.REJOIN_TICKET_FORMAT, reservation.SessionId);
            JoinTokenExchangeResponse running = await RejoinAndExchangeAsync(player, dungeon, rejoinPath, fixture);
            Assert.Equal(GameSessionCodes.Mode.BOSS, running.Mode);
            Assert.Equal(CONTENT, running.MapId);
            Assert.Null(running.BattleId);
            Assert.Equal(BOSS_ID, running.BossId);
            Assert.Equal(GameSessionCodes.Difficulty.HARD, running.Difficulty);

            await connection.ExecuteAsync(
                """
                UPDATE game_sessions SET state = 'Auction', auction_ends_at = clock_timestamp() + interval '30 seconds'
                WHERE session_id = @Id;
                """, new { Id = Guid.Parse(reservation.SessionId) });
            long deadline = await connection.QuerySingleAsync<long>(
                "SELECT (extract(epoch FROM auction_ends_at) * 1000)::bigint FROM game_sessions WHERE session_id = @Id;",
                new { Id = Guid.Parse(reservation.SessionId) });
            using HttpResponseMessage auctionTicket = await player.PostAsync(rejoinPath, null);
            Assert.Equal(HttpStatusCode.OK, auctionTicket.StatusCode);
            JoinTicket ticket = Read<JoinTicket>(await auctionTicket.Content.ReadAsStringAsync());
            Assert.True(ticket.ExpiresAtUnixMs <= deadline);
            using HttpResponseMessage auctionExchange = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(ticket.JoinToken, fixture.DungeonId.ToString("D"),
                    ContractCodes.ServerRole.DUNGEON)));
            Assert.Equal(HttpStatusCode.OK, auctionExchange.StatusCode);

            await connection.ExecuteAsync(
                "UPDATE game_sessions SET auction_ends_at = clock_timestamp() - interval '1 second' WHERE session_id = @Id;",
                new { Id = Guid.Parse(reservation.SessionId) });
            using HttpResponseMessage closed = await player.PostAsync(rejoinPath, null);
            Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_TERMINAL,
                Read<ApiRejection>(await closed.Content.ReadAsStringAsync()).ReasonCode);
        }

        /// <summary> PARTICIPANT_DEPARTED는 같은 트랜잭션에서 이탈을 기록하고 던전 접속 임대를 풀며, 이후 복귀는 ALREADY_SETTLED다.
        /// 같은 operationId 재전송은 처음 결과이고 마을 입장은 다시 된다. 경제 정산은 하지 않는다 </summary>
        [PostgresFact]
        public async Task Departure_ReleasesLeaseBlocksRejoinAndReplaysOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = Server(factory, fixture.TownLease.Token, fixture.TownId);
            using HttpClient dungeon = Server(factory, fixture.DungeonLease.Token, fixture.DungeonId);
            using HttpClient player = Server(factory, await GameTokenAsync(fixture, fixture.AccountId), fixture.DungeonId);
            ReservationResponse reservation = await ReserveAsync(town, Reservation(fixture, GameSessionCodes.Mode.NORMAL,
                "battle_forest_1", null, GameSessionCodes.Difficulty.NONE));
            await StartAsync(dungeon, reservation);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            // 이관을 마친 상태처럼 접속 임대를 던전으로 옮긴다
            await connection.ExecuteAsync(
                "UPDATE account_leases SET instance_id = @DungeonId, role = @Dungeon WHERE account_id = @AccountId;",
                new { fixture.DungeonId, Dungeon = ContractCodes.ServerRole.DUNGEON, fixture.AccountId });
            string participantId = reservation.Participants[0].ParticipantId;
            SessionEventBatchRequest batch = new SessionEventBatchRequest(Guid.NewGuid().ToString("D"), new[]
            {
                new SessionEventEntry(1, Guid.NewGuid().ToString("D"), GameSessionCodes.EventKind.PARTICIPANT_DEPARTED,
                    participantId, null, null, 0, GameSessionCodes.EndReason.ABANDON, null, null, null)
            });
            string eventsPath = string.Format(GameServerRoutes.EVENTS_FORMAT, reservation.SessionId);
            using HttpResponseMessage departed = await dungeon.PostAsync(eventsPath, Body(batch));
            using HttpResponseMessage replay = await dungeon.PostAsync(eventsPath, Body(batch));
            OperationResult first = Read<OperationResult>(await departed.Content.ReadAsStringAsync());
            OperationResult second = Read<OperationResult>(await replay.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, departed.StatusCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(first.ReceiptId, second.ReceiptId);
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM session_participants
                WHERE participant_id = @Id AND state = 'Active' AND departed_at IS NOT NULL AND depart_reason = @Abandon;
                """, new { Id = Guid.Parse(participantId), Abandon = GameSessionCodes.EndReason.ABANDON }));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId;", new { fixture.AccountId }));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM session_events WHERE event_kind = @Kind;",
                new { Kind = GameSessionCodes.EventKind.PARTICIPANT_DEPARTED }));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM economy_ledger;"));

            string rejoinPath = string.Format(ApiRoutes.REJOIN_TICKET_FORMAT, reservation.SessionId);
            using HttpResponseMessage rejoin = await player.PostAsync(rejoinPath, null);
            Assert.Equal(HttpStatusCode.Conflict, rejoin.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.ALREADY_SETTLED,
                Read<ApiRejection>(await rejoin.Content.ReadAsStringAsync()).ReasonCode);

            // 임대가 풀렸으므로 마을로 돌아갈 수 있다
            AuthExchangeResponse login = await LoginAsync(fixture, fixture.AccountId);
            PlayerAuthIdentity identity = (await fixture.Auth.ValidateAccessAsync(login.AccessToken)).Identity!;
            IssuedAuthToken townToken = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;
            using HttpResponseMessage back = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(townToken.Token, fixture.TownId.ToString("D"),
                    ContractCodes.ServerRole.TOWN)));
            Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        }

        /// <summary> 준비 확인은 마을 서버 임대만 부를 수 있고, 이 마을에 접속한 계정의 가용 티켓, 물약, 캐릭터 revision만 알려 준다 </summary>
        [PostgresFact]
        public async Task Readiness_TownOnly_ReportsLeasedParticipantsWithoutEffect()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = Server(factory, fixture.TownLease.Token, fixture.TownId);
            using HttpClient dungeon = Server(factory, fixture.DungeonLease.Token, fixture.DungeonId);
            using HttpClient player = Server(factory, await GameTokenAsync(fixture, fixture.AccountId), fixture.TownId);
            using HttpClient anonymous = factory.CreateClient();
            // characterRevision은 예약이 대조하는 loadout_revision이다. 잔액 변경이 올리는 revision과 갈라 둔다
            await using (NpgsqlConnection seed = await database.DataSource.OpenConnectionAsync())
            {
                await seed.ExecuteAsync("UPDATE characters SET revision = 9, loadout_revision = 4 WHERE character_id = @Id;",
                    new { Id = fixture.CharacterId });
                await seed.ExecuteAsync(
                    """
                    INSERT INTO account_progress (account_id, intro_completed_at) VALUES (@Id, clock_timestamp());
                    INSERT INTO account_unlocks (account_id, unlock_kind, target_id, operation_id)
                        VALUES (@Id, 'RegionCleared', @Forest, gen_random_uuid());
                    """, new { Id = fixture.AccountId, Forest = REGION_FOREST });
            }
            ReadinessRequest request = new ReadinessRequest(new[]
            {
                new ReadinessParticipant(fixture.AccountId.ToString("D"), fixture.CharacterId.ToString("D")),
                new ReadinessParticipant(fixture.OtherAccountId.ToString("D"), fixture.OtherCharacterId.ToString("D"))
            });
            ReadinessRequest duplicate = new ReadinessRequest(new[]
            {
                new ReadinessParticipant(fixture.AccountId.ToString("D"), fixture.CharacterId.ToString("D")),
                new ReadinessParticipant(fixture.AccountId.ToString("D"), fixture.OtherCharacterId.ToString("D"))
            });

            using HttpResponseMessage ready = await town.PostAsync(GameServerRoutes.READINESS, Body(request));
            using HttpResponseMessage asDungeon = await dungeon.PostAsync(GameServerRoutes.READINESS, Body(request));
            using HttpResponseMessage asPlayer = await player.PostAsync(GameServerRoutes.READINESS, Body(request));
            using HttpResponseMessage noAuth = await anonymous.PostAsync(GameServerRoutes.READINESS, Body(request));
            using HttpResponseMessage invalid = await town.PostAsync(GameServerRoutes.READINESS, Body(duplicate));

            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, asDungeon.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, asPlayer.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            ReadinessResponse body = Read<ReadinessResponse>(await ready.Content.ReadAsStringAsync());
            Assert.Equal(2, body.Participants.Length);
            ReadinessParticipantStatus mine = body.Participants[0];
            Assert.Equal(ContractCodes.RejectReason.NONE, mine.ReasonCode);
            Assert.Equal(4L, mine.CharacterRevision);
            Assert.Equal(SEED_TICKETS, mine.AvailableTickets);
            ReservationPotion potion = Assert.Single(mine.Potions);
            Assert.Equal("item_potion_small", potion.DefinitionId);
            Assert.Equal(SEED_POTIONS - RESERVED_POTION, potion.Quantity);
            Assert.True(mine.Progress.IntroCompleted);
            Assert.Equal(new[] { REGION_FOREST }, mine.Progress.ClearedRegionIds);
            Assert.Empty(mine.Progress.ClearedBattleIds);
            ReadinessParticipantStatus other = body.Participants[1];
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, other.ReasonCode);
            Assert.Equal(0L, other.AvailableTickets);
            Assert.Empty(other.Potions);
            Assert.Null(other.Progress);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM operations;"));
        }

        /// <summary> 예약 API가 해금 최종 판정을 한다: 안내를 마치지 않은 계정의 숲 일반전은 409 MISSING_UNLOCK이고
        /// 안내 완료 기록 뒤 같은 요청(새 operationId)은 확정된다 </summary>
        [PostgresFact]
        public async Task Reservation_ForestWithoutIntro_RejectsWithMissingUnlock()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = Server(factory, fixture.TownLease.Token, fixture.TownId);

            using HttpResponseMessage locked = await town.PostAsync(GameServerRoutes.RESERVATIONS,
                Body(Reservation(fixture, GameSessionCodes.Mode.NORMAL, REGION_FOREST, null,
                    GameSessionCodes.Difficulty.NONE)));
            Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
            ReservationResponse rejected = Read<ReservationResponse>(await locked.Content.ReadAsStringAsync());
            Assert.Equal(ContractCodes.RejectReason.MISSING_UNLOCK, rejected.Result.ReasonCode);

            await using (NpgsqlConnection seed = await database.DataSource.OpenConnectionAsync())
            {
                await seed.ExecuteAsync(
                    "INSERT INTO account_progress (account_id, intro_completed_at) VALUES (@Id, clock_timestamp());",
                    new { Id = fixture.AccountId });
            }
            ReservationResponse reserved = await ReserveAsync(town, Reservation(fixture, GameSessionCodes.Mode.NORMAL,
                REGION_FOREST, null, GameSessionCodes.Difficulty.NONE));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reserved.Result.Status);
        }

        /// <summary> 캐릭터 삭제 HTTP: 접속 중이면 409 ALREADY_CONNECTED, 삭제 뒤 같은 ID 재전송은 같은 결과, 다른 본문은 IDEMPOTENCY_CONFLICT다.
        /// 삭제한 캐릭터는 목록에서 빠지고 마을 입장권을 받지 못하며 삭제 전에 받은 마을 토큰도 쓸 수 없다 </summary>
        [PostgresFact]
        public async Task DeleteCharacter_Http_RejectsWhileConnectedThenRemovesFromListAndTown()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = await SeedAsync(database);
            using ApiFactory factory = new ApiFactory(database.DataSource, fixture.Options);
            using HttpClient town = Server(factory, fixture.TownLease.Token, fixture.TownId);
            // 새 로그인은 이전 로그인 묶음을 폐기하므로 한 번 로그인한 주체로 게임 토큰과 마을 토큰을 함께 받는다
            AuthExchangeResponse login = await LoginAsync(fixture, fixture.AccountId);
            PlayerAuthIdentity identity = (await fixture.Auth.ValidateAccessAsync(login.AccessToken)).Identity!;
            IssuedAuthToken game = await fixture.Auth.IssueGameSessionAsync(identity,
                TimeSpan.FromMinutes(GameSessionController.GAME_SESSION_MINUTES));
            using HttpClient player = factory.CreateClient();
            player.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + game.Token);
            string deletePath = string.Format(ApiRoutes.CHARACTER_DELETE_FORMAT, fixture.CharacterId.ToString("D"));
            string operationId = Guid.NewGuid().ToString("D");

            using HttpResponseMessage connected = await player.PostAsync(deletePath,
                Body(new CharacterDeleteRequest(Guid.NewGuid().ToString("D"), 0)));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;",
                new { fixture.AccountId });
            IssuedAuthToken staleTownToken = (await fixture.Auth.IssueTownJoinTokenAsync(identity, fixture.CharacterId,
                fixture.TownId)).Identity!;
            using HttpResponseMessage deleted = await player.PostAsync(deletePath,
                Body(new CharacterDeleteRequest(operationId, 0)));
            using HttpResponseMessage replay = await player.PostAsync(deletePath,
                Body(new CharacterDeleteRequest(operationId, 0)));
            using HttpResponseMessage conflict = await player.PostAsync(deletePath,
                Body(new CharacterDeleteRequest(operationId, 1)));
            using HttpResponseMessage list = await player.GetAsync(ApiRoutes.CHARACTERS);
            using HttpResponseMessage ticket = await player.PostAsync(ApiRoutes.TOWN_JOIN_TICKET,
                Body(new TownJoinTicketRequest(fixture.CharacterId.ToString("D"))));
            using HttpResponseMessage staleExchange = await town.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(staleTownToken.Token, fixture.TownId.ToString("D"),
                    ContractCodes.ServerRole.TOWN)));

            Assert.Equal(HttpStatusCode.Conflict, connected.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.ALREADY_CONNECTED,
                Read<OperationResult>(await connected.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            OperationResult first = Read<OperationResult>(await deleted.Content.ReadAsStringAsync());
            Assert.Equal(fixture.CharacterId.ToString("D"), first.ReceiptId);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(first.ServerTimeUnixMs, Read<OperationResult>(await replay.Content.ReadAsStringAsync())
                .ServerTimeUnixMs);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT,
                Read<OperationResult>(await conflict.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Empty(Read<CharacterListResponse>(await list.Content.ReadAsStringAsync()).Characters);
            Assert.Equal(HttpStatusCode.Forbidden, ticket.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                Read<ApiRejection>(await ticket.Content.ReadAsStringAsync()).ReasonCode);
            // 삭제가 미교환 토큰을 폐기했고, 교환은 소유 캐릭터 확인에서 먼저 막는다
            Assert.Equal(HttpStatusCode.Forbidden, staleExchange.StatusCode);
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM auth_join_tokens WHERE token_id = @Id AND revoked_at IS NOT NULL AND consumed_at IS NULL;",
                new { Id = staleTownToken.TokenId }));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId;", new { fixture.AccountId }));
        }

        private static async Task<JoinTokenExchangeResponse> RejoinAndExchangeAsync(HttpClient player, HttpClient dungeon,
            string rejoinPath, Fixture fixture)
        {
            using HttpResponseMessage issued = await player.PostAsync(rejoinPath, null);
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            JoinTicket ticket = Read<JoinTicket>(await issued.Content.ReadAsStringAsync());
            using HttpResponseMessage exchange = await dungeon.PostAsync(JoinApiRoutes.JOIN_TOKEN_EXCHANGE,
                Body(new JoinTokenExchangeRequest(ticket.JoinToken, fixture.DungeonId.ToString("D"),
                    ContractCodes.ServerRole.DUNGEON)));
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            return Read<JoinTokenExchangeResponse>(await exchange.Content.ReadAsStringAsync());
        }

        private static ReservationRequest Reservation(Fixture fixture, int mode, string? battleId, string? bossId,
            int difficulty)
        {
            return new ReservationRequest(Guid.NewGuid().ToString("D"), mode, CONTENT, CONTENT, new[]
            {
                new ReservationParticipant(fixture.AccountId.ToString("D"), fixture.CharacterId.ToString("D"), 0,
                    Array.Empty<ReservationPotion>())
            }, battleId, bossId, difficulty);
        }

        private static async Task<ReservationResponse> ReserveAsync(HttpClient town, ReservationRequest request)
        {
            using HttpResponseMessage response = await town.PostAsync(GameServerRoutes.RESERVATIONS, Body(request));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Read<ReservationResponse>(await response.Content.ReadAsStringAsync());
        }

        private static async Task StartAsync(HttpClient dungeon, ReservationResponse reservation)
        {
            using HttpResponseMessage started = await dungeon.PostAsync(string.Format(GameServerRoutes.START_FORMAT,
                reservation.SessionId), Body(new SessionStartRequest(Guid.NewGuid().ToString("D"),
                new[] { reservation.Participants[0].ParticipantId })));
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
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
                INSERT INTO characters(character_id,account_id,class_id) VALUES (@CharacterId,@AccountId,0),
                    (@OtherCharacterId,@OtherAccountId,0);
                INSERT INTO account_wallets(account_id, tickets) VALUES (@AccountId, @Tickets), (@OtherAccountId, @Tickets);
                INSERT INTO account_items(account_id, definition_id, binding_kind, quantity, reserved)
                    VALUES (@AccountId, 'item_potion_small', 'Account', @Potions, @Reserved),
                        (@OtherAccountId, 'item_potion_small', 'Account', @Potions, 0);
                INSERT INTO server_instances(instance_id,role,address,port,build_id,content_version,state,boot_id)
                    VALUES (@TownId,@Town,'127.0.0.1',@TownPort,@Content,@Content,'Idle',@TownBoot),
                        (@DungeonId,@Dungeon,'127.0.0.1',@DungeonPort,@Content,@Content,'Idle',@DungeonBoot);
                INSERT INTO account_leases(account_id,lease_id,instance_id,role,expires_at)
                    VALUES (@AccountId,@AccountLease,@TownId,@Town,clock_timestamp() + interval '15 minutes');
                """, new { fixture.AccountId, fixture.OtherAccountId, fixture.CharacterId, fixture.OtherCharacterId,
                    fixture.TownId, fixture.DungeonId, Town = ContractCodes.ServerRole.TOWN,
                    Dungeon = ContractCodes.ServerRole.DUNGEON, TownPort = TOWN_PORT, DungeonPort = DUNGEON_PORT,
                    Content = CONTENT, TownBoot = Guid.NewGuid(), DungeonBoot = Guid.NewGuid(),
                    AccountLease = Guid.NewGuid(), Tickets = SEED_TICKETS, Potions = SEED_POTIONS,
                    Reserved = RESERVED_POTION });
            fixture.TownLease = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(SERVER_MINUTES));
            fixture.DungeonLease = await fixture.Auth.IssueServerLeaseAsync(Guid.NewGuid(), fixture.DungeonId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(SERVER_MINUTES));
            return fixture;
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
