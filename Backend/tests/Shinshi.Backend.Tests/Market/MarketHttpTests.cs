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
using Shinshi.Backend.Market;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Backend.Trade;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Market
{
    /// <summary> 경매장, 거래 경로의 서버 역할 가드, 경로 상수, 기능 차단 재전송, 전체 흐름을 Program 파이프라인으로 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class MarketHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CHARACTER   = "7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1b001";
        private const string LISTING     = "7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1b002";
        private const string EXECUTION   = "7b4c1c0e-8f69-4d0a-9d55-1f3c8fd1b003";
        private const string EMPTY_BODY  = "{}";
        private const string JSON_MEDIA  = "application/json";
        private const string ADMIN_NAME  = "market.admin";
        private const string REASON      = "market gate check";
        private const string HERB        = MarketTradeRules.HERB_ID;
        private const int    TOWN_PORT   = 7777;
        private const int    LEASE_MINUTES = 15;
        private const long   UNIT_PRICE  = 40;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Templates_MatchSharedFormats()
        {
            Assert.Equal(MarketRouteTemplates.SEARCH, MarketRoutes.SEARCH);
            Assert.Equal(MarketRouteTemplates.LISTINGS_QUERY, MarketRoutes.LISTINGS_QUERY);
            Assert.Equal(MarketRouteTemplates.LIST, Format(MarketRoutes.LIST_FORMAT));
            Assert.Equal(MarketRouteTemplates.PURCHASE, Format(MarketRoutes.PURCHASE_FORMAT));
            Assert.Equal(MarketRouteTemplates.CANCEL, Format(MarketRoutes.CANCEL_FORMAT));
            Assert.Equal(MarketRouteTemplates.MINE, Format(MarketRoutes.MINE_FORMAT));
            Assert.Equal(MarketRouteTemplates.HISTORY, Format(MarketRoutes.HISTORY_FORMAT));
            Assert.Equal(MarketRouteTemplates.ACK, Format(MarketRoutes.ACK_FORMAT));
            Assert.Equal(TradeRouteTemplates.PREPARE, TradeRoutes.PREPARE);
            Assert.Equal(TradeRouteTemplates.COMMIT, string.Format(TradeRoutes.COMMIT_FORMAT, "{executionId}"));
            Assert.Equal(TradeRouteTemplates.RESOLVE, string.Format(TradeRoutes.RESOLVE_FORMAT, "{executionId}"));
        }

        [Theory]
        [InlineData(MarketRoutes.SEARCH)]
        [InlineData(MarketRoutes.LISTINGS_QUERY)]
        [InlineData(MarketRoutes.LIST_FORMAT)]
        [InlineData(MarketRoutes.PURCHASE_FORMAT)]
        [InlineData(MarketRoutes.CANCEL_FORMAT)]
        [InlineData(MarketRoutes.ACK_FORMAT)]
        [InlineData(TradeRoutes.PREPARE)]
        [InlineData(TradeRoutes.COMMIT_FORMAT)]
        [InlineData(TradeRoutes.RESOLVE_FORMAT)]
        public async Task Post_WithoutServerCredential_Returns401(string format)
        {
            using GuardFactory factory = new GuardFactory(ContractCodes.ServerRole.TOWN);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(string.Format(format, CHARACTER, LISTING),
                new StringContent(EMPTY_BODY, Encoding.UTF8, JSON_MEDIA));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(MarketRoutes.SEARCH)]
        [InlineData(MarketRoutes.LIST_FORMAT)]
        [InlineData(MarketRoutes.PURCHASE_FORMAT)]
        [InlineData(MarketRoutes.CANCEL_FORMAT)]
        [InlineData(TradeRoutes.PREPARE)]
        [InlineData(TradeRoutes.COMMIT_FORMAT)]
        [InlineData(TradeRoutes.RESOLVE_FORMAT)]
        public async Task Post_DungeonServer_Returns403(string format)
        {
            using GuardFactory factory = new GuardFactory(ContractCodes.ServerRole.DUNGEON);
            using HttpClient client = factory.CreateClient();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, string.Format(format, CHARACTER, LISTING))
            {
                Content = new StringContent(EMPTY_BODY, Encoding.UTF8, JSON_MEDIA)
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + IntegrationAuthRepository.TOKEN);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, factory.Repository.Server.InstanceId.ToString("D"));

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Theory]
        [InlineData(MarketRoutes.MINE_FORMAT)]
        [InlineData(MarketRoutes.HISTORY_FORMAT)]
        public async Task Get_WithoutServerCredential_Returns401(string format)
        {
            using GuardFactory factory = new GuardFactory(ContractCodes.ServerRole.TOWN);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(string.Format(format, CHARACTER));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [PostgresFact]
        public async Task Market_EndToEnd_ListBuyCancelAndFeatureGateReplay()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            Seed seed = await SeedAsync(database.DataSource, host.AuthOptions);

            Guid firstOperation = Guid.NewGuid();
            (HttpStatusCode listStatus, MarketOperationResponse listed) = await SendAsync<MarketOperationResponse>(client, seed,
                HttpMethod.Post, string.Format(MarketRoutes.LIST_FORMAT, seed.Seller.CharacterId),
                new MarketListRequest(firstOperation.ToString("D"), MarketCodes.ASSET_ACCOUNT_ITEM, HERB, null, 6, UNIT_PRICE));
            Assert.Equal(HttpStatusCode.OK, listStatus);
            Assert.False(string.IsNullOrEmpty(listed.ListingId));
            (_, MarketOperationResponse again) = await SendAsync<MarketOperationResponse>(client, seed, HttpMethod.Post,
                string.Format(MarketRoutes.LIST_FORMAT, seed.Seller.CharacterId), new MarketListRequest(
                    firstOperation.ToString("D"), MarketCodes.ASSET_ACCOUNT_ITEM, HERB, null, 6, UNIT_PRICE));
            Assert.Equal(listed.ListingId, again.ListingId);

            (_, MarketSearchResponse search) = await SendAsync<MarketSearchResponse>(client, seed, HttpMethod.Post,
                MarketRoutes.SEARCH, new MarketSearchRequest(null, null, null, null, MarketCodes.Sort.UNIT_PRICE,
                    MarketCodes.Order.ASCENDING, 0));
            Assert.Equal(6L, Assert.Single(search.Groups).TotalQty);
            (_, MarketListingsResponse query) = await SendAsync<MarketListingsResponse>(client, seed, HttpMethod.Post,
                MarketRoutes.LISTINGS_QUERY, new MarketListingsQueryRequest(HERB, 0));
            Assert.Equal(listed.ListingId, Assert.Single(query.Listings).ListingId);

            (HttpStatusCode buyStatus, MarketOperationResponse bought) = await SendAsync<MarketOperationResponse>(client, seed,
                HttpMethod.Post, string.Format(MarketRoutes.PURCHASE_FORMAT, seed.Buyer.CharacterId), new MarketBuyRequest(
                    Guid.NewGuid().ToString("D"), listed.ListingId, 2, UNIT_PRICE));
            Assert.Equal(HttpStatusCode.OK, buyStatus);
            Assert.Equal((80L, 4L, 76L), (bought.Gross, bought.Fee, bought.Net));
            (HttpStatusCode selfStatus, MarketOperationResponse self) = await SendAsync<MarketOperationResponse>(client, seed,
                HttpMethod.Post, string.Format(MarketRoutes.PURCHASE_FORMAT, seed.Seller.CharacterId), new MarketBuyRequest(
                    Guid.NewGuid().ToString("D"), listed.ListingId, 1, UNIT_PRICE));
            Assert.Equal(HttpStatusCode.Conflict, selfStatus);
            Assert.Equal(ContractCodes.RejectReason.MARKET_SELF_PURCHASE, self.Result.ReasonCode);

            (_, MarketMineResponse mine) = await SendAsync<MarketMineResponse>(client, seed, HttpMethod.Get,
                string.Format(MarketRoutes.MINE_FORMAT, seed.Seller.CharacterId), null);
            Assert.Equal(listed.ListingId, Assert.Single(mine.Listings).ListingId);
            Assert.Equal(76L, mine.AvailableYeopjeon);
            (_, MarketHistoryResponse history) = await SendAsync<MarketHistoryResponse>(client, seed, HttpMethod.Get,
                string.Format(MarketRoutes.HISTORY_FORMAT, seed.Buyer.CharacterId) + "?" + MarketRoutes.PAGE_PARAMETER + "=0", null);
            Assert.Equal(bought.TradeId, Assert.Single(history.Trades).TradeId);
            (HttpStatusCode wrongStatus, _) = await SendAsync<ApiRejection>(client, seed, HttpMethod.Get,
                string.Format(MarketRoutes.MINE_FORMAT, Guid.NewGuid()), null);
            Assert.Equal(HttpStatusCode.NotFound, wrongStatus);

            // 기능 차단: 저장된 처음 결과는 재생하고, 새 등록과 구매는 저장 없이 거절하며, 취소는 계속 허용한다
            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.MARKET }, 0);
            long operationsBefore = await CountOperationsAsync(database.DataSource);
            (HttpStatusCode replayStatus, MarketOperationResponse replayed) = await SendAsync<MarketOperationResponse>(client,
                seed, HttpMethod.Post, string.Format(MarketRoutes.LIST_FORMAT, seed.Seller.CharacterId), new MarketListRequest(
                    firstOperation.ToString("D"), MarketCodes.ASSET_ACCOUNT_ITEM, HERB, null, 6, UNIT_PRICE));
            Assert.Equal((HttpStatusCode.OK, listed.ListingId), (replayStatus, replayed.ListingId));
            (HttpStatusCode blockedStatus, MarketOperationResponse blocked) = await SendAsync<MarketOperationResponse>(client,
                seed, HttpMethod.Post, string.Format(MarketRoutes.LIST_FORMAT, seed.Seller.CharacterId), new MarketListRequest(
                    Guid.NewGuid().ToString("D"), MarketCodes.ASSET_ACCOUNT_ITEM, HERB, null, 1, UNIT_PRICE));
            (HttpStatusCode blockedBuyStatus, MarketOperationResponse blockedBuy) = await SendAsync<MarketOperationResponse>(
                client, seed, HttpMethod.Post, string.Format(MarketRoutes.PURCHASE_FORMAT, seed.Buyer.CharacterId),
                new MarketBuyRequest(Guid.NewGuid().ToString("D"), listed.ListingId, 1, UNIT_PRICE));
            Assert.Equal(HttpStatusCode.Conflict, blockedStatus);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED, blocked.Result.ReasonCode);
            Assert.Equal((HttpStatusCode.Conflict, ContractCodes.RejectReason.FEATURE_DISABLED),
                (blockedBuyStatus, blockedBuy.Result.ReasonCode));
            Assert.Equal(operationsBefore, await CountOperationsAsync(database.DataSource));

            (HttpStatusCode cancelStatus, MarketOperationResponse cancelled) = await SendAsync<MarketOperationResponse>(client,
                seed, HttpMethod.Post, string.Format(MarketRoutes.CANCEL_FORMAT, seed.Seller.CharacterId, listed.ListingId),
                new MarketCancelRequest(Guid.NewGuid().ToString("D")));
            Assert.Equal(HttpStatusCode.OK, cancelStatus);
            Assert.Equal(listed.ListingId, cancelled.ListingId);
            long herbs = await HerbsAsync(database.DataSource, seed.Seller.AccountId);
            Assert.Equal(4L + 4L, herbs);

            (HttpStatusCode badStatus, _) = await SendAsync<ApiRejection>(client, seed, HttpMethod.Post, MarketRoutes.SEARCH,
                new MarketSearchRequest(null, null, null, null, 9, 0, 0));
            Assert.Equal(HttpStatusCode.BadRequest, badStatus);
        }

        [PostgresFact]
        public async Task Trade_EndToEnd_PrepareCommitResolveAndFeatureGateReplay()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, true);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync(ADMIN_NAME, EAdminRole.Admin);
            string admin = await AdminApiTestHost.LoginAsync(client, ADMIN_NAME, secret);
            Seed seed = await SeedAsync(database.DataSource, host.AuthOptions);
            Guid execution = Guid.NewGuid();
            Guid commitOperation = Guid.NewGuid();
            TradePrepareRequest prepare = new TradePrepareRequest(execution.ToString("D"), execution.ToString("D"),
                Guid.NewGuid().ToString("D"), seed.TownId.ToString("D"),
                new TradeSideOffer(seed.Seller.AccountId.ToString("D"), seed.Seller.CharacterId.ToString("D"),
                    new[] { new TradeItemLine(MarketCodes.ASSET_ACCOUNT_ITEM, HERB, null, 3) }, 0),
                new TradeSideOffer(seed.Buyer.AccountId.ToString("D"), seed.Buyer.CharacterId.ToString("D"),
                    Array.Empty<TradeItemLine>(), 200), commitOperation.ToString("D"));

            (HttpStatusCode prepareStatus, TradeExecutionResponse prepared) = await SendAsync<TradeExecutionResponse>(client,
                seed, HttpMethod.Post, TradeRoutes.PREPARE, prepare);
            Assert.Equal(HttpStatusCode.OK, prepareStatus);
            Assert.Equal(TradeCodes.ExecutionState.PREPARED, prepared.Execution.State);

            // 본문의 townInstanceId가 호출 인스턴스와 다르면 400이다
            (HttpStatusCode foreignStatus, _) = await SendAsync<ApiRejection>(client, seed, HttpMethod.Post, TradeRoutes.PREPARE,
                new TradePrepareRequest(Guid.NewGuid().ToString("D"), prepare.ExecutionId, prepare.TradeId,
                    Guid.NewGuid().ToString("D"), prepare.SideA, prepare.SideB, prepare.CommitOperationId));
            Assert.Equal(HttpStatusCode.BadRequest, foreignStatus);

            (HttpStatusCode mismatchStatus, TradeExecutionResponse mismatch) = await SendAsync<TradeExecutionResponse>(client,
                seed, HttpMethod.Post, string.Format(TradeRoutes.COMMIT_FORMAT, execution),
                new TradeCommitRequest(Guid.NewGuid().ToString("D")));
            Assert.Equal(HttpStatusCode.Conflict, mismatchStatus);
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET, mismatch.Result.ReasonCode);
            (_, TradeExecutionResponse pending) = await SendAsync<TradeExecutionResponse>(client, seed, HttpMethod.Post,
                string.Format(TradeRoutes.RESOLVE_FORMAT, execution), null);
            Assert.Equal((ContractCodes.OperationStatus.PENDING, TradeCodes.ExecutionState.PREPARED),
                (pending.Result.Status, pending.Execution.State));

            (HttpStatusCode commitStatus, TradeExecutionResponse committed) = await SendAsync<TradeExecutionResponse>(client,
                seed, HttpMethod.Post, string.Format(TradeRoutes.COMMIT_FORMAT, execution),
                new TradeCommitRequest(commitOperation.ToString("D")));
            Assert.Equal(HttpStatusCode.OK, commitStatus);
            Assert.Equal(TradeCodes.ExecutionState.COMMITTED, committed.Execution.State);

            // 기능 차단: 저장된 prepare, commit 결과는 재생하고 새 prepare는 저장 없이 거절하며 resolve는 계속 허용한다
            await UpdateStateAsync(client, admin, new[] { ServiceFeatureKeys.TRADE }, 0);
            long operationsBefore = await CountOperationsAsync(database.DataSource);
            (HttpStatusCode replayPrepare, TradeExecutionResponse replayedPrepare) = await SendAsync<TradeExecutionResponse>(
                client, seed, HttpMethod.Post, TradeRoutes.PREPARE, prepare);
            (HttpStatusCode replayCommit, TradeExecutionResponse replayedCommit) = await SendAsync<TradeExecutionResponse>(
                client, seed, HttpMethod.Post, string.Format(TradeRoutes.COMMIT_FORMAT, execution),
                new TradeCommitRequest(commitOperation.ToString("D")));
            Guid other = Guid.NewGuid();
            (HttpStatusCode blockedStatus, TradeExecutionResponse blocked) = await SendAsync<TradeExecutionResponse>(client,
                seed, HttpMethod.Post, TradeRoutes.PREPARE, new TradePrepareRequest(other.ToString("D"), other.ToString("D"),
                    Guid.NewGuid().ToString("D"), seed.TownId.ToString("D"), prepare.SideA, prepare.SideB,
                    Guid.NewGuid().ToString("D")));
            (HttpStatusCode resolveStatus, TradeExecutionResponse resolved) = await SendAsync<TradeExecutionResponse>(client,
                seed, HttpMethod.Post, string.Format(TradeRoutes.RESOLVE_FORMAT, execution), null);
            Assert.Equal((HttpStatusCode.OK, prepared.Result.ReceiptId), (replayPrepare, replayedPrepare.Result.ReceiptId));
            Assert.Equal((HttpStatusCode.OK, committed.Result.ReceiptId), (replayCommit, replayedCommit.Result.ReceiptId));
            Assert.Equal(HttpStatusCode.Conflict, blockedStatus);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED, blocked.Result.ReasonCode);
            Assert.Null(blocked.Execution);
            Assert.Equal((HttpStatusCode.OK, ContractCodes.OperationStatus.COMMITTED), (resolveStatus, resolved.Result.Status));
            Assert.Equal(operationsBefore, await CountOperationsAsync(database.DataSource));

            (HttpStatusCode missing, _) = await SendAsync<ApiRejection>(client, seed, HttpMethod.Post,
                string.Format(TradeRoutes.RESOLVE_FORMAT, Guid.NewGuid()), null);
            Assert.Equal(HttpStatusCode.NotFound, missing);
        }

        private static string Format(string format)
        {
            return string.Format(format, "{characterId}", "{listingId}");
        }

        private static async Task<(HttpStatusCode Status, T Body)> SendAsync<T>(HttpClient client, Seed seed,
            HttpMethod method, string route, object? body)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, route);
            if (body != null)
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()),
                    Encoding.UTF8, JSON_MEDIA);
            }
            else if (method == HttpMethod.Post)
            {
                request.Content = new StringContent(string.Empty, Encoding.UTF8, JSON_MEDIA);
            }
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + seed.TownToken);
            request.Headers.Add(JoinApiRoutes.SERVER_INSTANCE_HEADER, seed.TownId.ToString("D"));
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

        private static async Task<long> HerbsAsync(NpgsqlDataSource dataSource, Guid accountId)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>(
                "SELECT quantity FROM account_items WHERE account_id = @AccountId AND definition_id = @Herb;",
                new { AccountId = accountId, Herb = HERB });
        }

        private static async Task<long> CountOperationsAsync(NpgsqlDataSource dataSource)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>("SELECT count(*) FROM operations;");
        }

        private static async Task<Seed> SeedAsync(NpgsqlDataSource dataSource, AuthTokenOptions options)
        {
            Seed seed = new Seed();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                // 시작 잔액은 대사 대상이 아니므로 이 HTTP 검사에서는 원장 없이 넣는다
                await connection.ExecuteAsync(
                    """
                    INSERT INTO accounts(account_id) VALUES (@SellerAccount), (@BuyerAccount);
                    INSERT INTO characters(character_id, account_id, class_id)
                        VALUES (@SellerCharacter, @SellerAccount, 0), (@BuyerCharacter, @BuyerAccount, 0);
                    INSERT INTO account_items(account_id, definition_id, binding_kind, quantity)
                        VALUES (@SellerAccount, @Herb, 'Account', 10);
                    INSERT INTO account_wallets(account_id, currency) VALUES (@BuyerAccount, 1000), (@SellerAccount, 0);
                    INSERT INTO server_instances(instance_id, role, address, port, build_id, content_version, state, boot_id)
                        VALUES (@TownId, @Town, '127.0.0.1', @TownPort, 'market', 'market', 'Idle', @TownBoot);
                    INSERT INTO account_leases(account_id, lease_id, instance_id, role, expires_at)
                        VALUES (@SellerAccount, @SellerLease, @TownId, @Town, clock_timestamp() + interval '15 minutes'),
                            (@BuyerAccount, @BuyerLease, @TownId, @Town, clock_timestamp() + interval '15 minutes');
                    """, new
                    {
                        SellerAccount = seed.Seller.AccountId,
                        BuyerAccount = seed.Buyer.AccountId,
                        SellerCharacter = seed.Seller.CharacterId,
                        BuyerCharacter = seed.Buyer.CharacterId,
                        seed.TownId,
                        Town = ContractCodes.ServerRole.TOWN,
                        TownPort = TOWN_PORT,
                        TownBoot = Guid.NewGuid(),
                        SellerLease = Guid.NewGuid(),
                        BuyerLease = Guid.NewGuid(),
                        Herb = HERB
                    });
            }

            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            IssuedAuthToken lease = await auth.IssueServerLeaseAsync(Guid.NewGuid(), seed.TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(LEASE_MINUTES));
            seed.TownToken = lease.Token;
            return seed;
        }

        private sealed class Party
        {
            public Guid AccountId   { get; } = Guid.NewGuid();
            public Guid CharacterId { get; } = Guid.NewGuid();
        }

        private sealed class Seed
        {
            public Party  Seller    { get; } = new Party();
            public Party  Buyer     { get; } = new Party();
            public Guid   TownId    { get; } = Guid.NewGuid();
            public string TownToken { get; set; } = string.Empty;
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
