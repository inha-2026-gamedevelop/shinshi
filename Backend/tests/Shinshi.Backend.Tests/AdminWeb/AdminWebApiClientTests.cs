// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    public sealed class AdminWebApiClientTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string REASON = "운영 확인 사유";
        private const string ONE_TIME_SECRET = "initial-password-for-first-response-only";
        private const int LIFETIME_MINUTES = 30;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task GetAsync_ActiveSession_SendsServerTokenWithoutBrowserCookiesOrForwardingHeaders()
        {
            using ApiFixture fixture = new ApiFixture();
            fixture.Handler.Respond = _ => AdminWebTestHandler.Json(new { statusText = "ok" });

            AdminWebApiResult<JToken> result = await fixture.Api.GetAsync<JToken>(AdminApiRoutes.SERVICE_STATE);
            AdminWebTestRequest request = Assert.Single(fixture.Handler.Requests);

            Assert.True(result.IsSuccess);
            Assert.Equal("Bearer " + AdminWebTestHandler.TOKEN, request.Authorization);
            Assert.Null(request.ForwardedFor);
            Assert.Null(request.Cookie);
            Assert.DoesNotContain(AdminWebTestHandler.TOKEN, JsonConvert.SerializeObject(fixture.Context));
        }

        [Fact]
        public async Task GetAsync_ApiReturns401_RevokesSessionAndNotifiesOnlyOnce()
        {
            using ApiFixture fixture = new ApiFixture();
            fixture.Handler.Respond = _ => AdminWebTestHandler.Json(
                new { diagnostic = AdminWebTestHandler.TOKEN }, HttpStatusCode.Unauthorized);
            int notified = 0;
            fixture.Context.SessionExpired += () => ++notified;

            AdminWebApiResult<JToken> first = await fixture.Api.GetAsync<JToken>(AdminApiRoutes.SERVICE_STATE);
            AdminWebApiResult<JToken> second = await fixture.Api.GetAsync<JToken>(AdminApiRoutes.SERVICE_STATE);

            Assert.True(first.RequiresLogin);
            Assert.True(second.RequiresLogin);
            Assert.False(fixture.Store.IsActive(fixture.SessionId));
            Assert.False(fixture.Context.IsAuthenticated);
            Assert.Null(first.Value);
            Assert.DoesNotContain(AdminWebTestHandler.TOKEN, first.ErrorMessage);
            Assert.Equal(1, notified);
            Assert.Single(fixture.Handler.Requests);
        }

        [Fact]
        public async Task GetAsync_SessionExpired_DoesNotSendOldTokenToApi()
        {
            using ApiFixture fixture = new ApiFixture();
            fixture.Clock.Now = fixture.Clock.Now.AddMinutes(LIFETIME_MINUTES);

            AdminWebApiResult<JToken> result = await fixture.Api.GetAsync<JToken>(AdminApiRoutes.SERVICE_STATE);

            Assert.True(result.RequiresLogin);
            Assert.False(fixture.Store.IsActive(fixture.SessionId));
            Assert.Empty(fixture.Handler.Requests);
        }

        [Theory]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        public async Task SendAsync_RetryableResult_ResendsSameIdAndExactFrozenBody(HttpStatusCode initialStatus)
        {
            using ApiFixture fixture = new ApiFixture();
            string reason = REASON;
            List<string> targets = new List<string> { Guid.NewGuid().ToString("D") };
            int factoryCalls = 0;
            PendingAdminWrite pending = PendingAdminWrite.Create(HttpMethod.Post, AdminApiRoutes.GRANTS, operationId =>
            {
                ++factoryCalls;
                return new { operationId, reason, targetAccountIds = targets };
            });
            fixture.Handler.Respond = _ => AdminWebTestHandler.Json(new { reasonCode = 0 }, initialStatus);

            AdminWebApiResult<JToken> first = await fixture.Api.SendAsync<JToken>(pending);
            reason = "재시도 때 바꾸려는 사유";
            targets.Add(Guid.NewGuid().ToString("D"));
            fixture.Handler.Respond = _ => AdminWebTestHandler.Json(new { confirmed = true });
            AdminWebApiResult<JToken> second = await fixture.Api.SendAsync<JToken>(pending);

            Assert.False(first.IsSuccess);
            Assert.True(first.CanRetry);
            Assert.Equal(initialStatus == HttpStatusCode.Accepted, first.IsPending);
            Assert.Equal(pending.OperationId, first.OperationId);
            Assert.True(second.IsSuccess);
            Assert.Equal(1, factoryCalls);
            Assert.Equal(2, fixture.Handler.Requests.Count);
            Assert.Equal(fixture.Handler.Requests[0].Body, fixture.Handler.Requests[1].Body);
            JObject body = JObject.Parse(fixture.Handler.Requests[1].Body);
            Assert.Equal(pending.OperationId, body.Value<string>("operationId"));
            Assert.Equal(REASON, body.Value<string>("reason"));
            Assert.Single(body["targetAccountIds"]!);
        }

        [Fact]
        public async Task SendAsync_ConnectionFailure_IsUncertainAndRetryPreservesRequestWithoutLeakingException()
        {
            using ApiFixture fixture = new ApiFixture();
            PendingAdminWrite pending = PendingAdminWrite.Create(HttpMethod.Post, AdminApiRoutes.OPERATORS,
                operationId => new { operationId, reason = REASON, loginName = "new-operator" });
            fixture.Handler.Respond = _ => throw new HttpRequestException(ONE_TIME_SECRET + AdminWebTestHandler.TOKEN);

            AdminWebApiResult<JToken> lost = await fixture.Api.SendAsync<JToken>(pending);
            fixture.Handler.Respond = _ => AdminWebTestHandler.Json(new { adminId = Guid.NewGuid().ToString("D") });
            AdminWebApiResult<JToken> retry = await fixture.Api.SendAsync<JToken>(pending);

            Assert.False(lost.IsSuccess);
            Assert.True(lost.CanRetry);
            Assert.True(lost.ResponseLost);
            Assert.Null(lost.Value);
            Assert.DoesNotContain(ONE_TIME_SECRET, lost.ErrorMessage);
            Assert.DoesNotContain(AdminWebTestHandler.TOKEN, lost.ErrorMessage);
            Assert.True(retry.IsSuccess);
            Assert.Equal(fixture.Handler.Requests[0].Body, fixture.Handler.Requests[1].Body);
        }

        [Fact]
        public async Task SendAsync_OneTimeSecretsInFirstResponse_DoesNotAddThemToSessionOrPendingRequest()
        {
            using ApiFixture fixture = new ApiFixture();
            PendingAdminWrite pending = PendingAdminWrite.Create(HttpMethod.Post, AdminApiRoutes.OPERATORS,
                operationId => new { operationId, reason = REASON, loginName = "new-operator" });
            fixture.Handler.Respond = _ => AdminWebTestHandler.Json(new
            {
                adminId = Guid.NewGuid().ToString("D"), initialPassword = ONE_TIME_SECRET,
                totpSecretBase32 = "BASE32-ONE-TIME", totpUri = "otpauth://totp/one-time"
            });

            AdminWebApiResult<JToken> first = await fixture.Api.SendAsync<JToken>(pending);
            Assert.Equal(ONE_TIME_SECRET, first.Value!.Value<string>("initialPassword"));
            fixture.Handler.Respond = _ => AdminWebTestHandler.Json(new { adminId = Guid.NewGuid().ToString("D") });
            AdminWebApiResult<JToken> replay = await fixture.Api.SendAsync<JToken>(pending);

            Assert.True(replay.IsSuccess);
            Assert.Null(replay.Value!["initialPassword"]);
            Assert.DoesNotContain(ONE_TIME_SECRET, JsonConvert.SerializeObject(pending));
            Assert.DoesNotContain(ONE_TIME_SECRET, JsonConvert.SerializeObject(fixture.Context));
            Assert.DoesNotContain(ONE_TIME_SECRET, JsonConvert.SerializeObject(fixture.Store.GetIdentity(fixture.SessionId)));
            Assert.DoesNotContain(ONE_TIME_SECRET, fixture.Handler.Requests[1].Body);
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.Conflict)]
        public async Task SendAsync_PermissionOrStateConflict_RequiresNewDecisionAndDoesNotExposeRawDiagnostics(
            HttpStatusCode status)
        {
            using ApiFixture fixture = new ApiFixture();
            fixture.Handler.Respond = _ => AdminWebTestHandler.Json(
                new { reasonCode = AdminContractCodes.RejectReason.INVALID_STATE, detail = ONE_TIME_SECRET }, status);
            PendingAdminWrite pending = PendingAdminWrite.Create(HttpMethod.Post, AdminApiRoutes.OPERATORS,
                operationId => new { operationId, reason = REASON, loginName = "new-operator" });

            AdminWebApiResult<JToken> result = await fixture.Api.SendAsync<JToken>(pending);

            Assert.False(result.IsSuccess);
            Assert.False(result.CanRetry);
            Assert.Null(result.Value);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE, result.ReasonCode);
            Assert.DoesNotContain(ONE_TIME_SECRET, JsonConvert.SerializeObject(result));
        }

        [Theory]
        [InlineData("http://api.test/admin")]
        [InlineData("https://api.test/admin/")]
        public void NormalizeBaseUrl_HttpOrHttpsWithPath_PreservesPrefixAndRemovesTrailingSlash(string value)
        {
            Assert.Equal(value.TrimEnd('/'), AdminWebApiClient.NormalizeBaseUrl(value));
        }

        [Theory]
        [InlineData("ftp://api.test")]
        [InlineData("https://user:password@api.test")]
        [InlineData("https://api.test?token=secret")]
        [InlineData("https://api.test#fragment")]
        public void NormalizeBaseUrl_UnsupportedOrSecretBearingUrl_Throws(string value)
        {
            Assert.Throws<InvalidOperationException>(() => AdminWebApiClient.NormalizeBaseUrl(value));
        }

        [Theory]
        [InlineData("/v1/service-state")]
        [InlineData("https://evil.test/admin/v1/operators")]
        [InlineData("/admin/v1/../healthz")]
        [InlineData("/admin/v1/%2e%2e/healthz")]
        [InlineData("/admin/v1/operators#fragment")]
        public async Task GetAsync_NonAdminOrTraversalPath_ThrowsBeforeHttpRequest(string path)
        {
            using ApiFixture fixture = new ApiFixture();

            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Api.GetAsync<JToken>(path));
            Assert.Empty(fixture.Handler.Requests);
        }

        private sealed class ApiFixture : IDisposable
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly HttpClient _http;

            /****************************************
            *              Properties
            ****************************************/

            public ManualTimeProvider Clock { get; } = new ManualTimeProvider();
            public AdminWebSessionStore Store { get; }
            public string SessionId { get; }
            public AdminWebContext Context { get; }
            public AdminWebTestHandler Handler { get; }
            public AdminWebApiClient Api { get; }

            /****************************************
            *                Methods
            ****************************************/

            public ApiFixture()
            {
                Store = new AdminWebSessionStore(Clock);
                SessionId = Store.Create(new AdminLoginResponse(AdminWebTestHandler.TOKEN,
                    Clock.Now.AddMinutes(LIFETIME_MINUTES).ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("D"),
                    AdminContractCodes.Role.OWNER), "test-owner");
                DefaultHttpContext httpContext = new DefaultHttpContext();
                httpContext.Request.Headers.Cookie = AdminWebSessionStore.COOKIE_NAME + "=" + SessionId;
                httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.5";
                Context = new AdminWebContext(Store, new HttpContextAccessor { HttpContext = httpContext });
                Handler = new AdminWebTestHandler(Clock);
                _http = new HttpClient(Handler);
                IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { [AdminWebApiClient.API_BASE_KEY] = "http://fake-api.test" }).Build();
                Api = new AdminWebApiClient(_http, Context, configuration);
            }

            public void Dispose() { _http.Dispose(); }
        }
    }
}