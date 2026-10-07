// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminWebSessionMiddlewareTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CSRF_NAME = "__RequestVerificationToken";
        private const string ERROR_MARKER = "development-exception-secret-marker";
        private const int SESSION_MINUTES = 30;

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData("/")]
        [InlineData("/accounts")]
        [InlineData("/notices")]
        [InlineData("/missing.js")]
        public async Task Request_AnonymousProtectedPath_RedirectsBeforeCircuitHtml(string path)
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser();
            using HttpResponseMessage response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/login", response.Headers.Location!.OriginalString);
            Assert.DoesNotContain("Blazor:", await response.Content.ReadAsStringAsync());
            Assert.Empty(factory.Handler.Requests);
        }

        [Theory]
        [InlineData("/login")]
        [InlineData("/healthz")]
        [InlineData("/admin.css")]
        [InlineData("/_framework/blazor.web.js")]
        public async Task Request_AnonymousPublicEndpoint_Returns200(string path)
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser();
            using HttpResponseMessage response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(factory.Handler.Requests);
        }

        [Theory]
        [InlineData("/_blazor", false)]
        [InlineData("/_blazor/negotiate?negotiateVersion=1", true)]
        [InlineData("/_blazor/initializers", false)]
        public async Task Blazor_AnonymousConnection_Returns401WithoutRedirect(string path, bool post)
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser();
            using HttpRequestMessage request = new(post ? HttpMethod.Post : HttpMethod.Get, path);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Request_ActiveSession_AllowsProtectedPageAndBlazorNegotiation(bool https)
        {
            using AdminWebTestFactory factory = new(environment: "Production");
            using HttpClient client = factory.CreateBrowser(https);
            string sessionId = CreateSession(factory);
            string name = https ? AdminWebSessionCookies.HTTPS_COOKIE_NAME : AdminWebSessionStore.COOKIE_NAME;
            using HttpRequestMessage pageRequest = new(HttpMethod.Get, "/accounts");
            pageRequest.Headers.Add("Cookie", name + "=" + sessionId);
            using HttpResponseMessage page = await client.SendAsync(pageRequest);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("Blazor:", await page.Content.ReadAsStringAsync());

            using HttpRequestMessage negotiation = new(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
            negotiation.Headers.Add("Cookie", name + "=" + sessionId);
            using HttpResponseMessage response = await client.SendAsync(negotiation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("connectionToken", await response.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Request_ExpiredOrRevokedCookie_RedirectsToLogin(bool expired)
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser();
            string sessionId = CreateSession(factory);
            AdminWebSessionStore sessions = factory.Services.GetRequiredService<AdminWebSessionStore>();
            if (expired)
            {
                factory.Clock.Now += TimeSpan.FromMinutes(SESSION_MINUTES);
            }
            else
            {
                sessions.Revoke(sessionId);
            }
            using HttpRequestMessage request = new(HttpMethod.Get, "/accounts");
            request.Headers.Add("Cookie", AdminWebSessionStore.COOKIE_NAME + "=" + sessionId);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/login", response.Headers.Location!.OriginalString);
        }

        [Fact]
        public async Task Request_HttpsLegacyCookie_DoesNotAuthenticate()
        {
            using AdminWebTestFactory factory = new();
            using HttpClient client = factory.CreateBrowser(https: true);
            string sessionId = CreateSession(factory);
            using HttpRequestMessage request = new(HttpMethod.Get, "/accounts");
            request.Headers.Add("Cookie", AdminWebSessionStore.COOKIE_NAME + "=" + sessionId);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/login", response.Headers.Location!.OriginalString);
        }

        [Theory]
        [InlineData(false, "ws://admin-web.test")]
        [InlineData(true, "wss://admin-web.test")]
        public async Task SecurityHeaders_RequestScheme_RestrictsSourcesAndSetsHttpsHsts(bool https, string socket)
        {
            using AdminWebTestFactory factory = new(environment: "Production");
            using HttpClient client = factory.CreateBrowser(https);
            using HttpResponseMessage response = await client.GetAsync("/login");
            string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

            Assert.Contains("default-src 'self'", csp);
            Assert.Contains("script-src 'self';", csp);
            Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
            Assert.Contains("connect-src 'self' " + socket + ";", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.DoesNotContain("'unsafe-eval'", csp);
            if (https)
            {
                Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
            }
            else
            {
                Assert.False(response.Headers.Contains("Strict-Transport-Security"));
            }
            string html = await response.Content.ReadAsStringAsync();
            Assert.Matches("<script src=\"_framework/blazor.web.js\"></script>", html);
            Assert.DoesNotMatch("<script(?:\\s[^>]*)?>\\s*[^<\\s]", html);
        }

        [Fact]
        public async Task SecurityHeaders_TrustedHttpsProxy_UsesRestoredScheme()
        {
            const string proxyIp = "10.10.0.5";
            using AdminWebTestFactory factory = new(new[] { "192.0.2.0/24" }, proxyIp, "Production");
            using HttpClient client = factory.CreateBrowser();
            using HttpRequestMessage request = new(HttpMethod.Get, "/login");
            request.Headers.Add(AdminTestSupport.REMOTE_IP_HEADER, proxyIp);
            request.Headers.Add("X-Forwarded-For", "192.0.2.8");
            request.Headers.Add("X-Forwarded-Proto", "https");
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("wss://admin-web.test", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
            Assert.True(response.Headers.Contains("Strict-Transport-Security"));
        }

        [Theory]
        [InlineData("Production")]
        [InlineData("Development")]
        public async Task Login_UnhandledException_ReturnsGenericResponseWithoutDeveloperPage(string environment)
        {
            using AdminWebTestFactory factory = new(environment: environment);
            using HttpClient client = factory.CreateBrowser();
            using HttpResponseMessage form = await client.GetAsync("/login");
            string html = await form.Content.ReadAsStringAsync();
            string csrf = WebUtility.HtmlDecode(Regex.Match(html,
                "name=\"" + CSRF_NAME + "\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.NotEmpty(csrf);
            string cookies = string.Join("; ", form.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
            factory.Handler.Respond = _ => throw new InvalidOperationException(ERROR_MARKER);
            using HttpRequestMessage request = new(HttpMethod.Post, "/login")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    [CSRF_NAME] = csrf, ["loginName"] = "owner",
                    ["password"] = "test-password", ["totpCode"] = "123456"
                })
            };
            request.Headers.Add("Cookie", cookies);
            using HttpResponseMessage response = await client.SendAsync(request);
            string error = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("요청을 처리하지 못했습니다", error);
            Assert.DoesNotContain(ERROR_MARKER, error);
            Assert.DoesNotContain("Stack", error);
        }

        [Fact]
        public void Configuration_AppSettings_DoesNotOverrideEnvironmentBinding()
        {
            using AdminWebTestFactory factory = new(environment: "Production");
            IWebHostEnvironment environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
            JObject settings = JObject.Parse(File.ReadAllText(Path.Combine(environment.ContentRootPath, "appsettings.json")));

            Assert.Null(settings["Urls"]);
            JObject launch = JObject.Parse(File.ReadAllText(Path.Combine(environment.ContentRootPath,
                "Properties", "launchSettings.json")));
            Assert.Equal("Production", (string?)launch["profiles"]?["Shinshi.Admin.Web"]?["environmentVariables"]?["ASPNETCORE_ENVIRONMENT"]);
            Assert.Equal("http://127.0.0.1:5081", (string?)launch["profiles"]?["Shinshi.Admin.Web"]?["applicationUrl"]);
        }

        private static string CreateSession(AdminWebTestFactory factory)
        {
            return factory.Services.GetRequiredService<AdminWebSessionStore>().Create(new AdminLoginResponse(
                AdminWebTestHandler.TOKEN, factory.Clock.GetUtcNow().AddMinutes(SESSION_MINUTES).ToUnixTimeMilliseconds(),
                Guid.NewGuid().ToString("D"), AdminContractCodes.Role.OWNER), "owner");
        }
    }
}
