// System
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Launcher.Account;

namespace Shinshi.Launcher.Tests.Account
{
    /// <summary> 인증 API 클라이언트의 요청 모양과 응답, 거절, 네트워크 실패 분류 </summary>
    public sealed class AuthApiClientTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SHORT_TIMEOUT_MS = 100;
        private const int SLOW_RESPONSE_MS = 2000;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task ExchangeCredentialsAsync_Success_PostsCredentialsAndParsesResponse()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK,
                AccountTestData.Exchange(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS))));
            AuthApiClient client = AccountTestData.Client(handler);

            AuthApiResult<AuthExchangeResponse> result = await client.ExchangeCredentialsAsync(AccountTestData.LOGIN_ID,
                AccountTestData.PASSWORD, AccountTestData.Build(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(AccountTestData.ACCESS_TOKEN, result.Value!.AccessToken);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, result.Value.RefreshToken);

            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(ApiRoutes.AUTH_EXCHANGE, request.Path);
            Assert.Null(request.Authorization);
            AuthExchangeRequest? sent = request.ReadBody<AuthExchangeRequest>();
            Assert.NotNull(sent);
            Assert.Equal(AccountTestData.LOGIN_ID, sent.LoginId);
            Assert.Equal(AccountTestData.PASSWORD, sent.Password);
            Assert.Null(sent.InviteCode);
            Assert.Null(sent.RefreshToken);
            Assert.Equal("build-1", sent.Build.BuildId);
        }

        [Fact]
        public async Task ExchangeRefreshAsync_BaseWithPath_KeepsBasePath()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK,
                AccountTestData.Exchange(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS))));
            AuthApiClient client = new AuthApiClient(new HttpClient(handler), new Uri("https://api.example.test/shinshi"));

            AuthApiResult<AuthExchangeResponse> result = await client.ExchangeRefreshAsync(AccountTestData.REFRESH_TOKEN,
                AccountTestData.Build(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal("/shinshi" + ApiRoutes.AUTH_EXCHANGE, request.Path);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, request.ReadBody<AuthExchangeRequest>()!.RefreshToken);
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden, ContractCodes.RejectReason.VERSION_MISMATCH, EAuthApiError.VersionMismatch)]
        [InlineData(HttpStatusCode.Forbidden, ContractCodes.RejectReason.BUILD_NOT_ALLOWED, EAuthApiError.VersionMismatch)]
        [InlineData(HttpStatusCode.Forbidden, ContractCodes.RejectReason.MAINTENANCE, EAuthApiError.Maintenance)]
        [InlineData(HttpStatusCode.TooManyRequests, ContractCodes.RejectReason.RATE_LIMITED, EAuthApiError.RateLimited)]
        [InlineData(HttpStatusCode.BadRequest, ContractCodes.RejectReason.INVALID_INVITE_CODE, EAuthApiError.Rejected)]
        [InlineData(HttpStatusCode.Forbidden, ContractCodes.RejectReason.SESSION_MISMATCH, EAuthApiError.Rejected)]
        [InlineData(HttpStatusCode.Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED, EAuthApiError.Unauthorized)]
        [InlineData(HttpStatusCode.Unauthorized, AuthRejectCodes.INVALID_CREDENTIALS, EAuthApiError.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError, 0, EAuthApiError.ServerError)]
        [InlineData(HttpStatusCode.GatewayTimeout, 0, EAuthApiError.ResponseLost)]
        public async Task ExchangeCredentialsAsync_Rejection_MapsStatusAndReason(HttpStatusCode status, int reason, EAuthApiError expected)
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Reject(status, reason)));
            AuthApiClient client = AccountTestData.Client(handler);

            AuthApiResult<AuthExchangeResponse> result = await client.ExchangeCredentialsAsync(AccountTestData.LOGIN_ID,
                AccountTestData.PASSWORD, AccountTestData.Build(), CancellationToken.None);

            Assert.Equal(expected, result.Error);
            Assert.Equal(reason, result.ReasonCode);
            Assert.Equal((int)status, result.StatusCode);
            Assert.Null(result.Value);
        }

        [Fact]
        public async Task GetServiceStateAsync_UnauthorizedWithoutBody_IsUnauthorized()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
            AuthApiClient client = AccountTestData.Client(handler);

            AuthApiResult<ServiceStateResponse> result = await client.GetServiceStateAsync(CancellationToken.None);

            Assert.Equal(EAuthApiError.Unauthorized, result.Error);
            Assert.Equal(ContractCodes.RejectReason.NONE, result.ReasonCode);
        }

        [Theory]
        [InlineData(HttpRequestError.ConnectionError, EAuthApiError.Unreachable)]
        [InlineData(HttpRequestError.NameResolutionError, EAuthApiError.Unreachable)]
        [InlineData(HttpRequestError.ResponseEnded, EAuthApiError.ResponseLost)]
        [InlineData(HttpRequestError.Unknown, EAuthApiError.ResponseLost)]
        public async Task ExchangeRefreshAsync_NetworkFailure_ClassifiesWhetherSent(HttpRequestError error, EAuthApiError expected)
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => throw new HttpRequestException(error, "fake failure"));
            AuthApiClient client = AccountTestData.Client(handler);

            AuthApiResult<AuthExchangeResponse> result = await client.ExchangeRefreshAsync(AccountTestData.REFRESH_TOKEN,
                AccountTestData.Build(), CancellationToken.None);

            Assert.Equal(expected, result.Error);
            Assert.Equal(expected == EAuthApiError.ResponseLost, result.IsOutcomeUnknown);
            Assert.Equal(0, result.StatusCode);
        }

        [Fact]
        public async Task ExchangeRefreshAsync_Timeout_IsResponseLost()
        {
            FakeHttpHandler handler = new FakeHttpHandler(async request =>
            {
                await Task.Delay(SLOW_RESPONSE_MS, request.CancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            AuthApiClient client = AccountTestData.Client(handler, TimeSpan.FromMilliseconds(SHORT_TIMEOUT_MS));

            AuthApiResult<AuthExchangeResponse> result = await client.ExchangeRefreshAsync(AccountTestData.REFRESH_TOKEN,
                AccountTestData.Build(), CancellationToken.None);

            Assert.Equal(EAuthApiError.ResponseLost, result.Error);
            Assert.True(result.IsOutcomeUnknown);
        }

        [Fact]
        public async Task ExchangeRefreshAsync_CallerCancels_Throws()
        {
            FakeHttpHandler handler = new FakeHttpHandler(async request =>
            {
                await Task.Delay(SLOW_RESPONSE_MS, request.CancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            AuthApiClient client = AccountTestData.Client(handler);
            using CancellationTokenSource cancel = new CancellationTokenSource(SHORT_TIMEOUT_MS);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExchangeRefreshAsync(AccountTestData.REFRESH_TOKEN,
                AccountTestData.Build(), cancel.Token));
        }

        [Fact]
        public async Task ExchangeRefreshAsync_SuccessWithBrokenBody_IsMalformedAndOutcomeUnknown()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Text(HttpStatusCode.OK, "{not json")));
            AuthApiClient client = AccountTestData.Client(handler);

            AuthApiResult<AuthExchangeResponse> result = await client.ExchangeRefreshAsync(AccountTestData.REFRESH_TOKEN,
                AccountTestData.Build(), CancellationToken.None);

            Assert.Equal(EAuthApiError.Malformed, result.Error);
            Assert.True(result.IsOutcomeUnknown);
        }

        [Fact]
        public async Task IssueGameSessionAsync_SendsAccessTokenAsBearer()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK,
                AccountTestData.GameSession())));
            AuthApiClient client = AccountTestData.Client(handler);

            AuthApiResult<GameSessionIssueResponse> result = await client.IssueGameSessionAsync(AccountTestData.ACCESS_TOKEN,
                AccountTestData.Build(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(AccountTestData.GAME_TOKEN, result.Value!.GameSessionToken);
            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal(ApiRoutes.AUTH_GAME_SESSION, request.Path);
            Assert.Equal(ApiRoutes.BEARER_PREFIX + AccountTestData.ACCESS_TOKEN, request.Authorization);
            Assert.Equal("build-1", request.ReadBody<GameSessionIssueRequest>()!.Build.BuildId);
        }

        [Fact]
        public async Task RevokeGameSessionAsync_NoContent_SucceedsWithGameTokenBearer()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
            AuthApiClient client = AccountTestData.Client(handler);

            AuthApiResult result = await client.RevokeGameSessionAsync(AccountTestData.GAME_TOKEN, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal((int)HttpStatusCode.NoContent, result.StatusCode);
            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(ApiRoutes.AUTH_GAME_SESSION_REVOKE, request.Path);
            Assert.Equal(ApiRoutes.BEARER_PREFIX + AccountTestData.GAME_TOKEN, request.Authorization);
        }

        [Fact]
        public async Task GetServiceStateAsync_Success_ParsesState()
        {
            ServiceStateResponse state = new ServiceStateResponse(true, "점검 안내", null!, new[] { "build-1" },
                AccountTestData.SERVER_TIME_MS);
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, state)));
            AuthApiClient client = AccountTestData.Client(handler);

            AuthApiResult<ServiceStateResponse> result = await client.GetServiceStateAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value!.Maintenance);
            Assert.Equal("점검 안내", result.Value.Notice);
            Assert.Equal(new[] { "build-1" }, result.Value.AllowedBuildIds);
            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(ApiRoutes.SERVICE_STATE, request.Path);
        }
    }
}
