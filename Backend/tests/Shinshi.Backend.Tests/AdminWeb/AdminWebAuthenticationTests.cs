// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

// Project
using Shinshi.Admin.Web;
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminWebAuthenticationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LOGIN_NAME = "test-owner";
        private const string PASSWORD   = "not-a-production-password";
        private const string TOTP       = "123456";
        private const string OLD_TOKEN  = "previous-server-only-api-token";
        private const string CSRF_NAME  = "__RequestVerificationToken";
        private const int LIFETIME_MINUTES = 30;
        private const int TEST_TIMEOUT_SECONDS = 10;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task Login_FailedIpOrName_PreventsApiCallBeforeRetryBoundary()
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser();
            (string csrf, string cookie) = await ReadFormAsync(client);
            factory.Handler.Respond = _ => AdminWebTestHandler.Json(new { rejected = true }, HttpStatusCode.Unauthorized);
            using HttpResponseMessage first = await LoginAsync(client, csrf, cookie);
            using HttpResponseMessage changedName = await LoginAsync(client, csrf, cookie, "different-owner");
            using HttpResponseMessage changedIp = await LoginAsync(client, csrf, cookie, LOGIN_NAME, "127.0.0.2");

            Assert.Equal("/login?error=credentials", first.Headers.Location!.OriginalString);
            Assert.Equal("/login?error=rate", changedName.Headers.Location!.OriginalString);
            Assert.Equal("/login?error=rate", changedIp.Headers.Location!.OriginalString);
            Assert.Single(factory.Handler.Requests);
            Assert.Equal("1", Assert.Single(changedName.Headers.GetValues("Retry-After")));
            factory.Clock.Now = factory.Clock.Now.AddSeconds(1);
            using HttpResponseMessage retry = await LoginAsync(client, csrf, cookie);
            Assert.Equal("/login?error=credentials", retry.Headers.Location!.OriginalString);
            Assert.Equal(2, factory.Handler.Requests.Count);
        }

        [Fact]
        public async Task Login_SuccessAfterFailure_ResetsWebFailureCounters()
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser();
            (string csrf, string cookie) = await ReadFormAsync(client);
            factory.Handler.Respond = _ => AdminWebTestHandler.Json(new { rejected = true }, HttpStatusCode.Unauthorized);
            using HttpResponseMessage failure = await LoginAsync(client, csrf, cookie);
            factory.Clock.Now = factory.Clock.Now.AddSeconds(1);
            factory.Handler.Respond = null;
            using HttpResponseMessage success = await LoginAsync(client, csrf, cookie);
            using HttpResponseMessage next = await LoginAsync(client, csrf, cookie);

            Assert.Equal("/", success.Headers.Location!.OriginalString);
            Assert.Equal("/", next.Headers.Location!.OriginalString);
            Assert.Equal(0, factory.Services.GetRequiredService<AdminWebLoginLimiter>().TrackedKeyCount);
            Assert.Equal(3, factory.Handler.Requests.Count);
        }

        [Fact]
        public async Task Login_Api429_ShowsSharedAuthenticationLimitWithoutRawDiagnostics()
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser();
            (string csrf, string cookie) = await ReadFormAsync(client);
            factory.Handler.Respond = _ => AdminWebTestHandler.Json(new { diagnostic = OLD_TOKEN }, HttpStatusCode.TooManyRequests);
            using HttpResponseMessage login = await LoginAsync(client, csrf, cookie);
            using HttpResponseMessage page = await client.GetAsync(login.Headers.Location);
            string html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());

            Assert.Equal("/login?error=api-rate", login.Headers.Location!.OriginalString);
            Assert.Contains("공유 인증 서비스의 요청 한도", html);
            Assert.DoesNotContain(OLD_TOKEN, html);
            Assert.Single(factory.Handler.Requests);
        }

        [Fact]
        public async Task Login_ConcurrentSameSource_PreventsSecondApiCallWhileFirstIsPending()
        {
            using AdminWebTestFactory original = new();
            using DelayedLoginHandler handler = new();
            using WebApplicationFactory<AdminWebProgram> factory = original.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddHttpClient<AdminWebApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler)));
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
                BaseAddress = new Uri("http://admin-web.test")
            });
            (string csrf, string cookie) = await ReadFormAsync(client);
            Task<HttpResponseMessage> pending = LoginAsync(client, csrf, cookie);
            try
            {
                await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));
                using HttpResponseMessage blocked = await LoginAsync(client, csrf, cookie, "different-owner");
                Assert.Equal("/login?error=rate", blocked.Headers.Location!.OriginalString);
                Assert.Equal(1, handler.RequestCount);
            }
            finally
            {
                handler.Release.TrySetResult(true);
            }
            using HttpResponseMessage rejected = await pending;
            Assert.Equal("/login?error=credentials", rejected.Headers.Location!.OriginalString);
            Assert.Equal(1, handler.RequestCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Logout_ApiConnectionFailure_RevokesLocalCookieAndShowsWarningWithSafeLog(bool https)
        {
            using AdminWebTestFactory original = new();
            RevocationLogProvider logs = new();
            using WebApplicationFactory<AdminWebProgram> factory = WithLogs(original, logs);
            using HttpClient client = CreateBrowser(factory, https);
            (string csrf, string cookie) = await ReadFormAsync(client);
            AdminWebSessionStore sessions = factory.Services.GetRequiredService<AdminWebSessionStore>();
            string sessionId = SeedSession(sessions, original.Clock);
            string sessionCookie = CookieName(https) + "=" + sessionId;
            original.Handler.Respond = _ => throw new HttpRequestException(OLD_TOKEN);
            using HttpRequestMessage request = FormRequest("/logout", cookie + "; " + sessionCookie,
                new FormUrlEncodedContent(new Dictionary<string, string> { [CSRF_NAME] = csrf }));
            using HttpResponseMessage logout = await client.SendAsync(request);
            using HttpResponseMessage page = await client.GetAsync(logout.Headers.Location);
            string html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            AdminWebTestRequest sent = Assert.Single(original.Handler.Requests);

            Assert.Equal("/login?warning=logout", logout.Headers.Location!.OriginalString);
            Assert.False(sessions.IsActive(sessionId));
            Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value => value.StartsWith(CookieName(https) + "=;", StringComparison.Ordinal));
            Assert.Equal("Bearer " + OLD_TOKEN, sent.Authorization);
            Assert.Equal(AdminApiRoutes.AUTH_LOGOUT, sent.Path);
            Assert.Null(sent.Cookie);
            Assert.Null(sent.ForwardedFor);
            Assert.Contains("서버 세션 폐기를 확인하지 못해", html);
            Assert.Contains("만료 시각에 무효화", html);
            Assert.DoesNotContain(OLD_TOKEN, html);
            string logged = Assert.Single(logs.Messages);
            Assert.Contains("StatusCode: 503", logged);
            Assert.DoesNotContain(OLD_TOKEN, logged);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Login_SuccessWithPreviousSession_LogsOutOldApiTokenBeforeIssuingNewSession(bool https)
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser(https);
            (string csrf, string cookie) = await ReadFormAsync(client);
            AdminWebSessionStore sessions = factory.Services.GetRequiredService<AdminWebSessionStore>();
            string oldId = SeedSession(sessions, factory.Clock);
            AdminWebIdentity oldIdentity = sessions.GetIdentity(oldId)!;
            List<string> otherSessionIds = new();
            for (int index = 1; index < AdminWebSessionStore.MAX_SESSIONS_PER_ADMIN; ++index)
            {
                otherSessionIds.Add(sessions.Create(new AdminLoginResponse(OLD_TOKEN,
                    oldIdentity.ExpiresAtUnixMs, oldIdentity.AdminId, oldIdentity.Role), LOGIN_NAME));
            }
            factory.Handler.Respond = request =>
            {
                if (request.Path == AdminApiRoutes.AUTH_LOGIN)
                {
                    return AdminWebTestHandler.Json(new AdminLoginResponse(AdminWebTestHandler.TOKEN,
                        oldIdentity.ExpiresAtUnixMs, oldIdentity.AdminId, oldIdentity.Role));
                }
                Assert.True(sessions.IsActive(oldId));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            };
            using HttpResponseMessage login = await LoginAsync(client, csrf, cookie + "; " + CookieName(https) + "=" + oldId);
            string newCookie = SessionCookie(login, https);
            string newId = CookieValue(newCookie);
            AdminWebTestRequest[] requests = factory.Handler.Requests.ToArray();

            Assert.Equal("/", login.Headers.Location!.OriginalString);
            Assert.Equal(2, requests.Length);
            Assert.Equal(AdminApiRoutes.AUTH_LOGIN, requests[0].Path);
            Assert.Null(requests[0].Authorization);
            Assert.Equal(AdminApiRoutes.AUTH_LOGOUT, requests[1].Path);
            Assert.Equal("Bearer " + OLD_TOKEN, requests[1].Authorization);
            Assert.Null(requests[1].Cookie);
            Assert.False(sessions.IsActive(oldId));
            Assert.True(sessions.IsActive(newId));
            Assert.NotEqual(oldId, newId);
            Assert.All(otherSessionIds, id => Assert.True(sessions.IsActive(id)));
            Assert.DoesNotContain(OLD_TOKEN, newCookie);
            Assert.DoesNotContain(AdminWebTestHandler.TOKEN, newCookie);
        }

        [Fact]
        public async Task Login_PreviousApiLogoutFails_RevokesOldLocalSessionAndWarnsWhileNewSessionIsActive()
        {
            using AdminWebTestFactory original = new();
            RevocationLogProvider logs = new();
            using WebApplicationFactory<AdminWebProgram> factory = WithLogs(original, logs);
            using HttpClient client = CreateBrowser(factory, true);
            (string csrf, string cookie) = await ReadFormAsync(client);
            AdminWebSessionStore sessions = factory.Services.GetRequiredService<AdminWebSessionStore>();
            string oldId = SeedSession(sessions, original.Clock);
            original.Handler.Respond = request => request.Path == AdminApiRoutes.AUTH_LOGIN
                ? AdminWebTestHandler.Json(new AdminLoginResponse(AdminWebTestHandler.TOKEN,
                    original.Clock.Now.AddMinutes(LIFETIME_MINUTES).ToUnixTimeMilliseconds(),
                    Guid.NewGuid().ToString("D"), AdminContractCodes.Role.OWNER))
                : AdminWebTestHandler.Json(new { diagnostic = OLD_TOKEN }, HttpStatusCode.ServiceUnavailable);
            using HttpResponseMessage login = await LoginAsync(client, csrf, cookie + "; " + CookieName(true) + "=" + oldId);
            string newId = CookieValue(SessionCookie(login, true));
            using HttpResponseMessage page = await client.GetAsync(login.Headers.Location);
            string html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());

            Assert.Equal("/login?warning=previous-session", login.Headers.Location!.OriginalString);
            Assert.False(sessions.IsActive(oldId));
            Assert.True(sessions.IsActive(newId));
            Assert.Contains("이전 서버 세션 폐기를 확인하지 못해", html);
            Assert.Contains("새 세션으로 운영 화면에 들어가기", html);
            Assert.DoesNotContain(OLD_TOKEN, html);
            Assert.DoesNotContain(AdminWebTestHandler.TOKEN, html);
            string logged = Assert.Single(logs.Messages);
            Assert.Contains("relogin", logged);
            Assert.DoesNotContain(OLD_TOKEN, logged);
            Assert.Equal(2, original.Handler.Requests.Count);
        }

        [Fact]
        public async Task Login_InvalidCredentials_DoesNotRevokeExistingWebSession()
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser();
            (string csrf, string cookie) = await ReadFormAsync(client);
            AdminWebSessionStore sessions = factory.Services.GetRequiredService<AdminWebSessionStore>();
            string oldId = SeedSession(sessions, factory.Clock);
            factory.Handler.Respond = _ => AdminWebTestHandler.Json(new { invalid = true }, HttpStatusCode.Unauthorized);
            using HttpResponseMessage login = await LoginAsync(client, csrf, cookie + "; " + CookieName(false) + "=" + oldId);

            Assert.Equal("/login?error=credentials", login.Headers.Location!.OriginalString);
            Assert.True(sessions.IsActive(oldId));
            Assert.Single(factory.Handler.Requests);
        }

        private static WebApplicationFactory<AdminWebProgram> WithLogs(AdminWebTestFactory factory,
            RevocationLogProvider logs)
        {
            return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ILoggerProvider>(logs)));
        }

        private static HttpClient CreateBrowser(WebApplicationFactory<AdminWebProgram> factory, bool https)
        {
            return factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
                BaseAddress = new Uri(https ? "https://admin-web.test" : "http://admin-web.test")
            });
        }

        private static string SeedSession(AdminWebSessionStore sessions, ManualTimeProvider clock)
        {
            return sessions.Create(new AdminLoginResponse(OLD_TOKEN,
                clock.Now.AddMinutes(LIFETIME_MINUTES).ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("D"),
                AdminContractCodes.Role.OWNER), LOGIN_NAME);
        }

        private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string csrf, string cookie,
            string loginName = LOGIN_NAME, string? address = null)
        {
            using HttpRequestMessage request = FormRequest("/login", cookie,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    [CSRF_NAME] = csrf, ["loginName"] = loginName, ["password"] = PASSWORD, ["totpCode"] = TOTP
                }));
            if (address != null)
            {
                request.Headers.Add(AdminTestSupport.REMOTE_IP_HEADER, address);
            }
            return await client.SendAsync(request);
        }

        private static HttpRequestMessage FormRequest(string path, string cookies, HttpContent content)
        {
            HttpRequestMessage request = new(HttpMethod.Post, path) { Content = content };
            request.Headers.Add("Cookie", cookies);
            return request;
        }

        private static async Task<(string Csrf, string Cookie)> ReadFormAsync(HttpClient client)
        {
            using HttpResponseMessage response = await client.GetAsync("/login");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string html = await response.Content.ReadAsStringAsync();
            string input = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase)
                .Select(match => match.Value)
                .Single(value => value.Contains("name=\"" + CSRF_NAME + "\"", StringComparison.Ordinal));
            string csrf = WebUtility.HtmlDecode(Regex.Match(input, "\\bvalue=\"([^\"]+)\"").Groups[1].Value);
            string cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(CookiePair));
            Assert.NotEmpty(csrf);
            return (csrf, cookie);
        }

        private static string CookieName(bool https)
        {
            return https ? AdminWebSessionCookies.HTTPS_COOKIE_NAME : AdminWebSessionStore.COOKIE_NAME;
        }

        private static string SessionCookie(HttpResponseMessage response, bool https)
        {
            return response.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith(CookieName(https) + "=", StringComparison.Ordinal));
        }

        private static string CookiePair(string value) { return value.Split(';')[0]; }
        private static string CookieValue(string value) { return CookiePair(value).Split('=', 2)[1]; }

        private sealed class DelayedLoginHandler : HttpMessageHandler
        {
            /****************************************
            *                Fields
            ****************************************/

            private int _requestCount;

            /****************************************
            *              Properties
            ****************************************/

            public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int RequestCount => Volatile.Read(ref _requestCount);

            /****************************************
            *                Methods
            ****************************************/

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _requestCount);
                Started.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
                return AdminWebTestHandler.Json(new { rejected = true }, HttpStatusCode.Unauthorized);
            }
        }

        private sealed class RevocationLogProvider : ILoggerProvider
        {
            /****************************************
            *              Properties
            ****************************************/

            public ConcurrentQueue<string> Messages { get; } = new();

            /****************************************
            *                Methods
            ****************************************/

            public ILogger CreateLogger(string categoryName)
            {
                return new RevocationLogger(categoryName, Messages);
            }

            public void Dispose() { }

            private sealed class RevocationLogger : ILogger
            {
                /****************************************
                *                Fields
                ****************************************/

                private readonly string _category;
                private readonly ConcurrentQueue<string> _messages;

                /****************************************
                *                Methods
                ****************************************/

                public RevocationLogger(string category, ConcurrentQueue<string> messages)
                {
                    _category = category;
                    _messages = messages;
                }

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull { return null; }
                public bool IsEnabled(LogLevel logLevel) { return true; }

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    if ((_category == typeof(AdminWebAuthenticationEndpoints).FullName) && (logLevel == LogLevel.Warning))
                    {
                        _messages.Enqueue(formatter(state, exception));
                    }
                }
            }
        }
    }
}
