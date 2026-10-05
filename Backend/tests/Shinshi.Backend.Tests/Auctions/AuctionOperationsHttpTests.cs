// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Admin;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auctions;
using Shinshi.Data.Auth;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Auctions
{
    /// <summary> 멈춘 경매 운영 목록(Viewer 조회, 게임 토큰 401, 정상 경매 미노출)과 auction 기능 차단의 입찰 게이트를
    /// 실제 PostgreSQL과 Program 파이프라인에서 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AuctionOperationsHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string VIEWER_NAME    = "auction.viewer";
        private const string ADMIN_NAME     = "auction.admin";
        private const string REASON         = "auction operations check";
        private const string MATERIAL_ID    = "item_boss_material_rare";
        private const long   TICKETS        = 3;
        private const long   POTIONS        = 2;
        private const long   ESSENCE_GRANT  = 10;
        private const long   CURRENCY_GRANT = 500;
        private const long   FIRST_BID      = 100;
        private const long   SECOND_BID     = 110;
        private const int    LEASE_MINUTES  = 15;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task List_OverdueAndHealthyAuctions_ShowsOnlyStuckToViewer()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(2);
            AuctionSetup healthy = await CreateAuctionSessionAsync(environment);
            AuctionSetup stuck = await CreateAuctionSessionAsync(environment);
            Guid auction = Guid.NewGuid();
            await OpenAsync(environment, healthy, auction);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await environment.Auctions.BidAsync(healthy.Session.InstanceId, healthy.BootId,
                    Bid(healthy, auction, Guid.NewGuid(), FIRST_BID, 0))).Status);
            await environment.ExecuteAsync(
                "UPDATE game_sessions SET auction_ends_at = clock_timestamp() - make_interval(secs => @Past) WHERE session_id = @Id;",
                new { Id = stuck.Session.SessionId, Past = PartyAuctionRules.PHASE_RECOVERY_GRACE_SECONDS + 1 });

            using AdminApiTestHost host = new AdminApiTestHost(environment.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(VIEWER_NAME, EAdminRole.Viewer);
            string viewer = await AdminApiTestHost.LoginAsync(client, VIEWER_NAME, secret);

            AdminAuctionPage stuckPage = await GetAsync<AdminAuctionPage>(client, AdminApiRoutes.AUCTIONS, viewer);
            AdminAuctionSession listed = Assert.Single(stuckPage.Sessions);
            Assert.Equal(stuck.Session.SessionId.ToString("D"), listed.SessionId);
            Assert.True(listed.Overdue);
            Assert.False(listed.InstanceLost);
            Assert.Null(listed.AuctionId);
            Assert.Null(stuckPage.NextCursor);

            AdminAuctionPage openPage = await GetAsync<AdminAuctionPage>(client,
                AdminApiRoutes.AUCTIONS + "?" + AdminApiRoutes.STATE_PARAMETER + "=" + AdminAuctionPage.STATE_OPEN, viewer);
            AdminAuctionSession open = Assert.Single(openPage.Sessions);
            Assert.Equal(healthy.Session.SessionId.ToString("D"), open.SessionId);
            Assert.False(open.Overdue);
            Assert.Equal(auction.ToString("D"), open.AuctionId);
            Assert.Equal(PartyAuctionRules.STATE_OPEN, open.ItemState);
            Assert.Equal(1L, open.BidSeq);
            Assert.Equal(FIRST_BID, open.ReservedCurrency);
            Assert.True(open.HardEndsAtUnixMs > 0);

            // 복구 대상 목록에도 멈춘 경매만 auctionOverdue로 오른다
            AdminRecoverySessionPage recovery = await GetAsync<AdminRecoverySessionPage>(client, AdminApiRoutes.SESSIONS,
                viewer);
            AdminRecoverySession candidate = Assert.Single(recovery.Sessions);
            Assert.Equal(stuck.Session.SessionId.ToString("D"), candidate.Session.SessionId);
            Assert.True(candidate.AuctionOverdue);

            using HttpResponseMessage bogus = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.AUCTIONS + "?" + AdminApiRoutes.STATE_PARAMETER + "=Sold", viewer);
            Assert.Equal(HttpStatusCode.BadRequest, bogus.StatusCode);
            string gameToken = await IssueGameAccessTokenAsync(environment.DataSource, host.AuthOptions);
            using HttpResponseMessage game = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                AdminApiRoutes.AUCTIONS, gameToken);
            Assert.Equal(HttpStatusCode.Unauthorized, game.StatusCode);
        }

        /// <summary> auction이 꺼지면 새 입찰은 저장 없이 FEATURE_DISABLED, 확정된 입찰 재전송은 처음 결과, 마감은 그대로 진행된다 </summary>
        [PostgresFact]
        public async Task Bid_AuctionDisabled_RejectsNewBidReplaysStoredAndStillCloses()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            AuctionSetup setup = await CreateAuctionSessionAsync(environment);
            Guid auction = Guid.NewGuid();
            await OpenAsync(environment, setup, auction);

            using AdminApiTestHost host = new AdminApiTestHost(environment.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            IssuedAuthToken dungeon = await new PostgresAuthRepository(environment.DataSource, host.AuthOptions)
                .IssueServerLeaseAsync(Guid.NewGuid(), setup.Session.InstanceId, ContractCodes.ServerRole.DUNGEON,
                    TimeSpan.FromMinutes(LEASE_MINUTES));
            Guid bidder = setup.ParticipantIds[0];
            AuctionBidRequest first = new AuctionBidRequest(Guid.NewGuid().ToString("D"), bidder.ToString("D"),
                FIRST_BID, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            AuctionBidRequest second = new AuctionBidRequest(Guid.NewGuid().ToString("D"), bidder.ToString("D"),
                SECOND_BID, 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            string bids = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTION_BIDS_FORMAT,
                setup.Session.SessionId, auction);

            (HttpStatusCode acceptedStatus, OperationResult accepted) = await PostAsync(client, dungeon, setup, bids, first);
            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.AUCTION }, 0);
            (HttpStatusCode replayStatus, OperationResult replayed) = await PostAsync(client, dungeon, setup, bids, first);
            using HttpResponseMessage blocked = await SendAsync(client, dungeon, setup, bids, second);
            string close = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTION_CLOSE_FORMAT,
                setup.Session.SessionId, auction);
            (HttpStatusCode closeStatus, OperationResult closed) = await PostAsync(client, dungeon, setup, close,
                new AuctionCloseRequest(Guid.NewGuid().ToString("D"),
                    setup.ParticipantIds.Select(id => id.ToString("D")).ToArray(),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

            Assert.Equal(HttpStatusCode.OK, acceptedStatus);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, accepted.Status);
            Assert.Equal(HttpStatusCode.OK, replayStatus);
            Assert.Equal(accepted.ReceiptId, replayed.ReceiptId);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(blocked)).ReasonCode);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(environment.DataSource,
                "SELECT count(*) FROM operations WHERE operation_id = @Id;", new { Id = Guid.Parse(second.OperationId) }));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(environment.DataSource,
                "SELECT count(*) FROM auction_bids WHERE auction_id = @Id;", new { Id = auction }));
            Assert.Equal(HttpStatusCode.OK, closeStatus);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, closed.Status);
            Assert.Equal(PartyAuctionRules.STATE_SOLD, await environment.TextAsync(
                "SELECT state FROM auctions WHERE auction_id = @Id;", new { Id = auction }));
        }

        private static async Task<AuctionSetup> CreateAuctionSessionAsync(SessionTestEnvironment environment)
        {
            List<TestPlayer> players = new List<TestPlayer>
            {
                await environment.CreatePlayerAsync(TICKETS, POTIONS),
                await environment.CreatePlayerAsync(TICKETS, POTIONS)
            };
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, players, 0);
            Guid settlementId = Guid.NewGuid();
            List<SettlementGrantInput> grants = session.Participants.Select(row =>
            {
                SettlementGrantInput grant = SessionEventTests.Grant(row.ParticipantId, ESSENCE_GRANT);
                grant.Currency = CURRENCY_GRANT;
                return grant;
            }).ToList();
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await SessionEventTests.AppendAsync(environment,
                session, new[] { SessionEventTests.Ended(1, settlementId, grants) })).Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await environment.Settlement.SettleAsync(
                session.InstanceId, session.SessionId, settlementId)).Status);

            Guid? boot;
            await using (NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync())
            {
                boot = await connection.QuerySingleOrDefaultAsync<Guid?>(
                    "SELECT instance_boot_id FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId });
            }

            return new AuctionSetup(session, boot);
        }

        private static async Task OpenAsync(SessionTestEnvironment environment, AuctionSetup setup, Guid auction)
        {
            OperationResult opened = await environment.Auctions.OpenAsync(setup.Session.InstanceId, setup.BootId,
                new AuctionOpenInput
                {
                    SessionId = setup.Session.SessionId, OperationId = Guid.NewGuid(), AuctionId = auction,
                    DefinitionId = MATERIAL_ID, EligibleParticipantIds = setup.ParticipantIds
                });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, opened.Status);
        }

        private static AuctionBidInput Bid(AuctionSetup setup, Guid auction, Guid operationId, long amount, long seq)
        {
            return new AuctionBidInput
            {
                SessionId = setup.Session.SessionId, AuctionId = auction, OperationId = operationId,
                ParticipantId = setup.ParticipantIds[0], Amount = amount, ExpectedBidSeq = seq,
                AcceptedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private static async Task<T> GetAsync<T>(HttpClient client, string route, string token)
        {
            using HttpResponseMessage response = await AdminApiTestHost.SendAsync(client, HttpMethod.Get, route, token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await AdminTestSupport.ReadAsync<T>(response);
        }

        private static async Task<(HttpStatusCode Status, OperationResult Result)> PostAsync(HttpClient client,
            IssuedAuthToken token, AuctionSetup setup, string route, object body)
        {
            using HttpResponseMessage response = await SendAsync(client, token, setup, route, body);
            return (response.StatusCode, await AdminTestSupport.ReadAsync<OperationResult>(response));
        }

        private static Task<HttpResponseMessage> SendAsync(HttpClient client, IssuedAuthToken token, AuctionSetup setup,
            string route, object body)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = AdminTestSupport.Json(body)
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token.Token);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, setup.Session.InstanceId.ToString("D"));
            return client.SendAsync(request);
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

        private static async Task<string> IssueGameAccessTokenAsync(NpgsqlDataSource dataSource, AuthTokenOptions options)
        {
            Guid accountId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                    new { AccountId = accountId });
            }
            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            IssuedAuthToken invite = await auth.IssueInviteAsync(accountId);
            return (await auth.ExchangeInviteAsync(invite.Token)).Response!.AccessToken;
        }

        private sealed class AuctionSetup
        {
            public ReservedSessionView Session        { get; }
            public Guid?               BootId         { get; }
            public Guid[]              ParticipantIds { get; }

            public AuctionSetup(ReservedSessionView session, Guid? bootId)
            {
                Session        = session;
                BootId         = bootId;
                ParticipantIds = session.Participants.Select(row => row.ParticipantId).ToArray();
            }
        }
    }
}
