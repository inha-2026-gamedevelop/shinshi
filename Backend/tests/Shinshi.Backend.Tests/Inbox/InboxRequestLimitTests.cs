// System
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Inbox;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Backend.Tests.Admin.Grants;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Inbox
{
    /// <summary> Program 파이프라인에서 우편함의 계정별 요청 한도, 없는 우편의 작업 행 없는 거절, GUID 표기만 다른 재전송을 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class InboxRequestLimitTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long CURRENCY = 40;

        // JSON으로 읽을 수 없는 수령 본문. 바인딩까지 가면 400이다
        private const string MALFORMED_BODY = "{\"operationId\":";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 남의 우편이나 없는 우편은 operations 행 없이 409 INBOX_NOT_FOUND이고, 같은 operationId를 다른 GUID 표기로
        /// 다시 보내도 처음 결과를 받는다 </summary>
        [PostgresFact]
        public async Task Claim_MissingInboxAndGuidSpelling_StoresNothingAndReplays()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrantTestSupport support = await GrantTestSupport.CreateAsync(database.DataSource);
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false,
                options => options.Maintenance = false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid accountId, string player) = await IssuePlayerAsync(database.DataSource, host.AuthOptions);
            (Guid strangerId, string _) = await IssuePlayerAsync(database.DataSource, host.AuthOptions);
            Guid inboxId = await DeliverAsync(support, accountId);
            Guid foreignInbox = await DeliverAsync(support, strangerId);

            using HttpResponseMessage missing = await ClaimAsync(client, player,
                new InboxClaimRequest(AdminApiTestHost.NewOperationId(), Guid.NewGuid().ToString("D"), null));
            using HttpResponseMessage foreign = await ClaimAsync(client, player,
                new InboxClaimRequest(AdminApiTestHost.NewOperationId(), foreignInbox.ToString("D"), null));
            long operationsAfterMissing = await CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @AccountId;", accountId);

            Guid operationId = Guid.NewGuid();
            using HttpResponseMessage claimed = await ClaimAsync(client, player,
                new InboxClaimRequest(operationId.ToString("D"), inboxId.ToString("N").ToUpperInvariant(), null));
            using HttpResponseMessage replayed = await ClaimAsync(client, player,
                new InboxClaimRequest(operationId.ToString("B").ToUpperInvariant(), inboxId.ToString("D"), string.Empty));

            Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INBOX_NOT_FOUND,
                (await AdminTestSupport.ReadAsync<OperationResult>(missing)).ReasonCode);
            Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INBOX_NOT_FOUND,
                (await AdminTestSupport.ReadAsync<OperationResult>(foreign)).ReasonCode);
            Assert.Equal(0L, operationsAfterMissing);
            Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            OperationResult first = await AdminTestSupport.ReadAsync<OperationResult>(claimed);
            OperationResult second = await AdminTestSupport.ReadAsync<OperationResult>(replayed);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, second.Status);
            Assert.Equal(first.ServerTimeUnixMs, second.ServerTimeUnixMs);
            Assert.Equal(inboxId.ToString("D"), second.ReceiptId);
            Assert.Equal(1L, await CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @AccountId;", accountId));
            Assert.Equal(CURRENCY, await CountAsync(database.DataSource,
                "SELECT currency FROM account_wallets WHERE account_id = @AccountId;", accountId));
        }

        /// <summary> 수령 30회, 목록 60회를 넘으면 그 계정만 429 RATE_LIMITED와 Retry-After를 받고 작업 행은 늘지 않는다 </summary>
        [PostgresFact]
        public async Task ClaimAndList_OverAccountLimit_Return429ForThatAccountOnly()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false,
                options => options.Maintenance = false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid accountId, string player) = await IssuePlayerAsync(database.DataSource, host.AuthOptions);
            (Guid _, string other) = await IssuePlayerAsync(database.DataSource, host.AuthOptions);

            for (int index = 0; index < InboxRequestLimiter.MAX_CLAIMS_PER_WINDOW; ++index)
            {
                using HttpResponseMessage allowed = await ClaimAsync(client, player, new InboxClaimRequest(
                    AdminApiTestHost.NewOperationId(), Guid.NewGuid().ToString("D"), null));
                Assert.Equal(HttpStatusCode.Conflict, allowed.StatusCode);
            }
            for (int index = 0; index < InboxRequestLimiter.MAX_LISTS_PER_WINDOW; ++index)
            {
                using HttpResponseMessage listed = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                    ApiRoutes.INBOX, player);
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            }

            using HttpResponseMessage limitedClaim = await ClaimAsync(client, player, new InboxClaimRequest(
                AdminApiTestHost.NewOperationId(), Guid.NewGuid().ToString("D"), null));
            using HttpResponseMessage limitedList = await AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                ApiRoutes.INBOX, player);
            using HttpResponseMessage otherClaim = await ClaimAsync(client, other, new InboxClaimRequest(
                AdminApiTestHost.NewOperationId(), Guid.NewGuid().ToString("D"), null));

            Assert.Equal(HttpStatusCode.TooManyRequests, limitedClaim.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(limitedClaim)).ReasonCode);
            Assert.True(limitedClaim.Headers.RetryAfter!.Delta!.Value.TotalSeconds > 0);
            Assert.Equal(HttpStatusCode.TooManyRequests, limitedList.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(limitedList)).ReasonCode);
            Assert.Equal(HttpStatusCode.Conflict, otherClaim.StatusCode);
            Assert.Equal(0L, await CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @AccountId;", accountId));
        }

        /// <summary> 한도와 크기 상한은 본문 바인딩 전에 적용된다. 형식이 깨진 본문도 한도를 쓰고, 한도를 다 쓴 뒤의 깨진 본문은
        /// 400이 아니라 429다. 상한을 넘는 Content-Length는 본문을 읽지 않고 413이다 </summary>
        [PostgresFact]
        public async Task Claim_LimitAndSize_AreCheckedBeforeReadingBody()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false,
                options => options.Maintenance = false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, string player) = await IssuePlayerAsync(database.DataSource, host.AuthOptions);
            (Guid _, string other) = await IssuePlayerAsync(database.DataSource, host.AuthOptions);

            for (int index = 0; index < InboxRequestLimiter.MAX_CLAIMS_PER_WINDOW; ++index)
            {
                using HttpResponseMessage malformed = await SendRawClaimAsync(client, player, MALFORMED_BODY);
                Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            }
            using HttpResponseMessage limited = await SendRawClaimAsync(client, player, MALFORMED_BODY);
            using HttpResponseMessage oversized = await SendRawClaimAsync(client, other,
                "{\"padding\":\"" + new string('x', InboxRequestGateFilter.MAX_BODY_BYTES) + "\"}");

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                (await AdminTestSupport.ReadAsync<ApiRejection>(limited)).ReasonCode);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        }

        private static async Task<HttpResponseMessage> SendRawClaimAsync(HttpClient client, string token, string body)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.INBOX_CLAIM)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token);
            return await client.SendAsync(request);
        }

        private static Task<HttpResponseMessage> ClaimAsync(HttpClient client, string token, InboxClaimRequest request)
        {
            return AdminApiTestHost.SendAsync(client, HttpMethod.Post, ApiRoutes.INBOX_CLAIM, token, request);
        }

        private static async Task<Guid> DeliverAsync(GrantTestSupport support, Guid accountId)
        {
            Guid grantId = await support.GrantAndExecuteAsync(new[] { accountId },
                GrantTestSupport.Wallet(AdminGrantCodes.ASSET_CURRENCY, CURRENCY));
            return await support.SingleInboxIdAsync(accountId, grantId);
        }

        private static async Task<(Guid AccountId, string Token)> IssuePlayerAsync(NpgsqlDataSource dataSource,
            AuthTokenOptions options)
        {
            Guid accountId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await dataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                    new { AccountId = accountId });
            }

            PostgresAuthRepository auth = new PostgresAuthRepository(dataSource, options);
            IssuedAuthToken invite = await auth.IssueInviteAsync(accountId);
            return (accountId, (await auth.ExchangeInviteAsync(invite.Token)).Response!.AccessToken);
        }

        private static Task<long> CountAsync(NpgsqlDataSource dataSource, string sql, Guid accountId)
        {
            return AdminTestSupport.CountAsync(dataSource, sql, new { AccountId = accountId });
        }
    }
}
