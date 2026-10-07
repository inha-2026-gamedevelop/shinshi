// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Admin.Web;
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminWebHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LOGIN_NAME    = "test-owner";
        private const string PASSWORD      = "not-a-production-password";
        private const string TOTP          = "123456";
        private const string OUTSIDE_IP    = "203.0.113.5";
        private const string PROXY_IP      = "10.10.0.5";
        private const string FORWARDED_FOR = "X-Forwarded-For";
        private const string CSRF_NAME     = "__RequestVerificationToken";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task Login_AllowedNetwork_ReturnsPageWithAntiforgeryAndSecurityHeaders()
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory();
            using HttpClient client = factory.CreateBrowser();
            using HttpResponseMessage response = await client.GetAsync("/login");
            string html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("name=\"loginName\"", html);
            Assert.Contains("name=\"password\"", html);
            Assert.Contains("name=\"totpCode\"", html);
            Assert.NotEmpty(FindAntiforgery(html));
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            Assert.DoesNotContain(AdminWebTestHandler.TOKEN, html);
            Assert.Empty(factory.Handler.Requests);
        }

        [Theory]
        [InlineData("/login")]
        [InlineData("/healthz")]
        [InlineData("/admin.css")]
        public async Task Request_OutsideAllowedNetwork_Returns404(string path)
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory();
            using HttpClient client = factory.CreateBrowser();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(AdminTestSupport.REMOTE_IP_HEADER, OUTSIDE_IP);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.Empty(factory.Handler.Requests);
        }

        [Theory]
        [InlineData("X-Forwarded-For", "192.0.2.8")]
        [InlineData("Forwarded", "for=192.0.2.8;proto=https")]
        public async Task Request_ForwardingHeaderWithoutTrustedProxy_Returns404(string header, string value)
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory();
            using HttpClient client = factory.CreateBrowser();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/login");
            request.Headers.Add(header, value);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(factory.Handler.Requests);
        }

        [Theory]
        [InlineData("192.0.2.8", HttpStatusCode.OK)]
        [InlineData("203.0.113.5", HttpStatusCode.NotFound)]
        [InlineData(null, HttpStatusCode.NotFound)]
        public async Task Request_TrustedProxy_ChecksRestoredOperatorAddress(string? forwardedIp, HttpStatusCode expected)
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory(new[] { "192.0.2.0/24" }, PROXY_IP);
            using HttpClient client = factory.CreateBrowser();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/login");
            request.Headers.Add(AdminTestSupport.REMOTE_IP_HEADER, PROXY_IP);
            if (forwardedIp != null)
            {
                request.Headers.Add(FORWARDED_FOR, forwardedIp);
                request.Headers.Add("X-Forwarded-Proto", "https");
            }
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(expected, response.StatusCode);
            Assert.Empty(factory.Handler.Requests);
        }

        [Fact]
        public void Startup_TrustedProxyInsideAllowedNetwork_Fails()
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory(new[] { "10.10.0.0/16" }, PROXY_IP);

            Exception? exception = Record.Exception(() => factory.CreateBrowser());

            Assert.NotNull(exception);
            Assert.Contains("KnownProxies", exception.ToString());
        }

        [Fact]
        public async Task Login_MissingAntiforgery_Returns400BeforeApiCall()
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory();
            using HttpClient client = factory.CreateBrowser();
            using HttpResponseMessage response = await client.PostAsync("/login", Credentials());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(factory.Handler.Requests);
        }

        [Fact]
        public async Task Login_ValidAntiforgery_CallsApiAndIssuesOpaqueHttpOnlyCookie()
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory();
            using HttpClient client = factory.CreateBrowser();
            (string csrf, string cookie) = await ReadLoginFormAsync(client);
            using HttpRequestMessage request = FormRequest("/login", cookie, Credentials(csrf));
            using HttpResponseMessage response = await client.SendAsync(request);
            string sessionCookie = SessionCookie(response);
            string sessionId = CookieValue(sessionCookie);
            AdminWebTestRequest apiRequest = Assert.Single(factory.Handler.Requests);
            JObject apiBody = JObject.Parse(apiRequest.Body);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/", response.Headers.Location!.OriginalString);
            Assert.Equal(AdminApiRoutes.AUTH_LOGIN, apiRequest.Path);
            Assert.Equal(HttpMethod.Post, apiRequest.Method);
            Assert.Equal(LOGIN_NAME, apiBody.Value<string>("loginName"));
            Assert.Equal(PASSWORD, apiBody.Value<string>("password"));
            Assert.Equal(TOTP, apiBody.Value<string>("totpCode"));
            Assert.Null(apiRequest.Authorization);
            Assert.Null(apiRequest.ForwardedFor);
            Assert.Null(apiRequest.Cookie);
            Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AdminWebTestHandler.TOKEN, sessionCookie);
            Assert.DoesNotContain(PASSWORD, sessionCookie);
            Assert.True(factory.Services.GetRequiredService<AdminWebSessionStore>().IsActive(sessionId));

            using HttpRequestMessage homeRequest = new HttpRequestMessage(HttpMethod.Get, "/");
            homeRequest.Headers.Add("Cookie", cookie + "; " + CookiePair(sessionCookie));
            using HttpResponseMessage home = await client.SendAsync(homeRequest);
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.DoesNotContain(AdminWebTestHandler.TOKEN, await home.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Login_ProductionHttps_IssuesSecureCookie()
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory(environment: "Production");
            using HttpClient client = factory.CreateBrowser(https: true);
            (string csrf, string cookie) = await ReadLoginFormAsync(client);
            using HttpRequestMessage request = FormRequest("/login", cookie, Credentials(csrf));
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("secure", SessionCookie(response), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(AdminWebSessionCookies.HTTPS_COOKIE_NAME + "=", SessionCookie(response));
            Assert.DoesNotContain("domain=", SessionCookie(response), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", SessionCookie(response), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Logout_ValidAntiforgery_RevokesSessionAndCallsApiWithoutBrowserHeaders()
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory();
            using HttpClient client = factory.CreateBrowser();
            (string csrf, string cookie) = await ReadLoginFormAsync(client);
            using HttpRequestMessage loginRequest = FormRequest("/login", cookie, Credentials(csrf));
            using HttpResponseMessage login = await client.SendAsync(loginRequest);
            string sessionCookie = SessionCookie(login);
            string sessionId = CookieValue(sessionCookie);
            string cookies = cookie + "; " + CookiePair(sessionCookie);
            using HttpRequestMessage logoutRequest = FormRequest("/logout", cookies,
                new FormUrlEncodedContent(new Dictionary<string, string> { [CSRF_NAME] = csrf }));
            using HttpResponseMessage logout = await client.SendAsync(logoutRequest);

            Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
            Assert.False(factory.Services.GetRequiredService<AdminWebSessionStore>().IsActive(sessionId));
            AdminWebTestRequest logoutApi = Assert.Single(factory.Handler.Requests,
                request => request.Path == AdminApiRoutes.AUTH_LOGOUT);
            Assert.Equal("Bearer " + AdminWebTestHandler.TOKEN, logoutApi.Authorization);
            Assert.Null(logoutApi.ForwardedFor);
            Assert.Null(logoutApi.Cookie);
        }

        [Fact]
        public async Task Logout_MissingAntiforgery_DoesNotRevokeSession()
        {
            using AdminWebTestFactory factory = new AdminWebTestFactory();
            using HttpClient client = factory.CreateBrowser();
            (string csrf, string cookie) = await ReadLoginFormAsync(client);
            using HttpRequestMessage loginRequest = FormRequest("/login", cookie, Credentials(csrf));
            using HttpResponseMessage login = await client.SendAsync(loginRequest);
            string sessionCookie = SessionCookie(login);
            string sessionId = CookieValue(sessionCookie);
            using HttpRequestMessage logoutRequest = FormRequest("/logout", cookie + "; " + CookiePair(sessionCookie),
                new FormUrlEncodedContent(new Dictionary<string, string>()));
            using HttpResponseMessage logout = await client.SendAsync(logoutRequest);

            Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
            Assert.True(factory.Services.GetRequiredService<AdminWebSessionStore>().IsActive(sessionId));
            Assert.Single(factory.Handler.Requests);
        }

        private static FormUrlEncodedContent Credentials(string? csrf = null)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>
            {
                ["loginName"] = LOGIN_NAME,
                ["password"] = PASSWORD,
                ["totpCode"] = TOTP
            };
            if (csrf != null)
            {
                fields[CSRF_NAME] = csrf;
            }
            return new FormUrlEncodedContent(fields);
        }

        private static HttpRequestMessage FormRequest(string path, string cookies, HttpContent content)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
            request.Headers.Add("Cookie", cookies);
            return request;
        }

        private static async Task<(string Csrf, string Cookie)> ReadLoginFormAsync(HttpClient client)
        {
            using HttpResponseMessage response = await client.GetAsync("/login");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string html = await response.Content.ReadAsStringAsync();
            string cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(CookiePair));
            return (FindAntiforgery(html), cookie);
        }

        private static string FindAntiforgery(string html)
        {
            string input = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase)
                .Select(match => match.Value)
                .Single(value => value.Contains("name=\"" + CSRF_NAME + "\"", StringComparison.Ordinal));
            Match token = Regex.Match(input, "\\bvalue=\"([^\"]+)\"");
            Assert.True(token.Success);
            return WebUtility.HtmlDecode(token.Groups[1].Value);
        }

        private static string SessionCookie(HttpResponseMessage response)
        {
            return response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(
                AdminWebSessionStore.COOKIE_NAME + "=", StringComparison.Ordinal)
                || cookie.StartsWith(AdminWebSessionCookies.HTTPS_COOKIE_NAME + "=", StringComparison.Ordinal));
        }

        private static string CookiePair(string cookie) { return cookie.Split(';')[0]; }
        private static string CookieValue(string cookie) { return CookiePair(cookie).Split('=', 2)[1]; }
    }

    internal sealed class AdminWebTestFactory : WebApplicationFactory<AdminWebProgram>
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string[] _allowedNetworks;
        private readonly string? _proxy;
        private readonly string _environment;

        /****************************************
        *              Properties
        ****************************************/

        public ManualTimeProvider Clock { get; } = new ManualTimeProvider();
        public AdminWebTestHandler Handler { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebTestFactory(string[]? allowedNetworks = null, string? proxy = null,
            string environment = "Development")
        {
            _allowedNetworks = allowedNetworks ?? Array.Empty<string>();
            _proxy           = proxy;
            _environment     = environment;
            Handler          = new AdminWebTestHandler(Clock);
        }

        public HttpClient CreateBrowser(bool https = false)
        {
            return CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies     = false,
                BaseAddress       = new Uri(https ? "https://admin-web.test" : "http://admin-web.test")
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                Dictionary<string, string?> values = new Dictionary<string, string?>
                {
                    ["AdminWeb:ApiBaseUrl"] = "http://fake-api.test"
                };
                for (int i = 0; i < _allowedNetworks.Length; ++i)
                {
                    values["AdminWeb:AllowedNetworks:" + i] = _allowedNetworks[i];
                }
                if (_proxy != null)
                {
                    values["ForwardedHeaders:KnownProxies:0"] = _proxy;
                }
                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
                services.AddHttpClient<AdminWebApiClient>().ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    internal sealed class AdminWebTestHandler : HttpMessageHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string TOKEN = "secret-api-bearer-never-in-browser";
        private const int LIFETIME_MINUTES = 30;
        private readonly TimeProvider _clock;
        private readonly List<AdminWebTestRequest> _requests = new List<AdminWebTestRequest>();

        /****************************************
        *              Properties
        ****************************************/

        public Func<AdminWebTestRequest, HttpResponseMessage>? Respond { get; set; }
        public IReadOnlyList<AdminWebTestRequest> Requests => _requests;

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebTestHandler(TimeProvider clock) { _clock = clock; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AdminWebTestRequest recorded = new AdminWebTestRequest(request.Method, request.RequestUri!.AbsolutePath,
                request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString(), Header(request, "X-Forwarded-For"), Header(request, "Cookie"));
            _requests.Add(recorded);
            if (Respond != null)
            {
                return Respond(recorded);
            }
            if (recorded.Path == AdminApiRoutes.AUTH_LOGIN)
            {
                return Json(new AdminLoginResponse(TOKEN,
                    _clock.GetUtcNow().AddMinutes(LIFETIME_MINUTES).ToUnixTimeMilliseconds(),
                    Guid.NewGuid().ToString("D"), AdminContractCodes.Role.OWNER));
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonConvert.SerializeObject(value, ContractJson.CreateSettings()),
                    Encoding.UTF8, "application/json")
            };
        }

        private static string? Header(HttpRequestMessage request, string name)
        {
            return request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : null;
        }
    }

    internal sealed class AdminWebTestRequest
    {
        /****************************************
        *              Properties
        ****************************************/

        public HttpMethod Method { get; }
        public string Path { get; }
        public string Body { get; }
        public string? Authorization { get; }
        public string? ForwardedFor { get; }
        public string? Cookie { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebTestRequest(HttpMethod method, string path, string body, string? authorization,
            string? forwardedFor, string? cookie)
        {
            Method        = method;
            Path          = path;
            Body          = body;
            Authorization = authorization;
            ForwardedFor  = forwardedFor;
            Cookie        = cookie;
        }
    }
}