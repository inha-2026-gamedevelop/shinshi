// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Observability;
using Shinshi.Backend.Tests.Admin;

namespace Shinshi.Backend.Tests.Observability
{
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class MetricsHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INSIDE_IP = "10.1.2.3";
        private const string OUTSIDE_IP = "203.0.113.5";
        private const string PROXY_IP = "192.168.10.5";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task Metrics_DefaultLoopback_AnonymousTextWithRequiredNamesAndAliases()
        {
            using MetricsFactory factory = new MetricsFactory();
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer arbitrary-invalid-token");

            using HttpResponseMessage response = await client.GetAsync(MetricsOptions.ROUTE);
            string text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(MetricsOptions.CONTENT_TYPE, response.Content.Headers.ContentType!.ToString());
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Contains("# TYPE " + PrometheusMetricsWriter.REQUESTS_TOTAL + " counter\n", text);
            Assert.Contains("# TYPE " + PrometheusMetricsWriter.REQUEST_DURATION + " histogram\n", text);
            Assert.Contains("# TYPE " + ApiMetrics.DURATION_INSTRUMENT + " histogram\n", text);
            Assert.Contains(PrometheusMetricsWriter.OUTBOX_PENDING + " 3\n", text);
            Assert.Contains(MetricsReporterService.OUTBOX_PENDING_METRIC + " 3\n", text);
            Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_TIME + " 1234.5\n", text);
            Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_COUNT + " 2\n", text);
            Assert.DoesNotContain("arbitrary-invalid-token", text);
            Assert.Equal(1, factory.Reader.Calls);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("HEAD")]
        public async Task Metrics_OutsideNetwork_Empty404BeforeDatabase(string method)
        {
            using MetricsFactory factory = new MetricsFactory();
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await SendAsync(client, method, OUTSIDE_IP);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.Equal(0, factory.Reader.Calls);
        }

        [Fact]
        public async Task Metrics_Disabled_Empty404BeforeDatabase()
        {
            using MetricsFactory factory = new MetricsFactory(enabled: false);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(MetricsOptions.ROUTE);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.Equal(0, factory.Reader.Calls);
        }

        [Fact]
        public async Task Metrics_ConfiguredNetworks_ReplaceLoopbackAndAcceptMappedIpv4()
        {
            using MetricsFactory factory = new MetricsFactory(new[] { "10.0.0.0/8" });
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage inside = await SendAsync(client, "GET", INSIDE_IP);
            using HttpResponseMessage mapped = await SendAsync(client, "GET", "::ffff:" + INSIDE_IP);
            using HttpResponseMessage loopback = await client.GetAsync(MetricsOptions.ROUTE);

            Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
            Assert.Equal(HttpStatusCode.OK, mapped.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, loopback.StatusCode);
        }

        [Fact]
        public async Task Metrics_ForwardingHeadersWithoutTrustedProxy_404AndLogsOnce()
        {
            using MetricsFactory factory = new MetricsFactory();
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage forwardedFor = await SendAsync(client, "GET", null, "X-Forwarded-For", INSIDE_IP);
            using HttpResponseMessage forwarded = await SendAsync(client, "GET", null, "Forwarded", "for=" + INSIDE_IP);
            using HttpResponseMessage direct = await client.GetAsync(MetricsOptions.ROUTE);

            Assert.Equal(HttpStatusCode.NotFound, forwardedFor.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, forwarded.StatusCode);
            Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
            Assert.Equal(1, factory.Logs.Count(LogLevel.Error, "ForwardedHeaders:KnownProxies"));
        }

        [Fact]
        public async Task Metrics_TrustedProxy_UsesRestoredAddressAndHidesMissingForwardingHeader()
        {
            using MetricsFactory factory = new MetricsFactory(new[] { "10.0.0.0/8" }, PROXY_IP);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage restored = await SendAsync(client, "GET", PROXY_IP, "X-Forwarded-For", INSIDE_IP);
            using HttpResponseMessage external = await SendAsync(client, "GET", PROXY_IP, "X-Forwarded-For", OUTSIDE_IP);
            using HttpResponseMessage missing = await SendAsync(client, "GET", PROXY_IP);

            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, external.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        [Theory]
        [InlineData(new[] { "10.0.0.0/8" }, "10.0.0.5")]
        [InlineData(new string[0], "127.0.0.1")]
        public void Metrics_ProxyInsideAllowedNetworks_FailsStartup(string[] allowedNetworks, string proxy)
        {
            using MetricsFactory factory = new MetricsFactory(allowedNetworks, proxy);

            Exception? failure = Record.Exception(() => factory.CreateClient());

            Assert.NotNull(failure);
            Assert.Contains("Metrics:AllowedNetworks", failure.ToString());
        }

        [Fact]
        public async Task Metrics_RawUrlsAndQueryValues_OnlyTemplatesOrUnmatchedAreExported()
        {
            using MetricsFactory factory = new MetricsFactory();
            using HttpClient client = factory.CreateClient();
            const string QUERY_SECRET = "query-must-not-be-exported";
            string unknownId = Guid.NewGuid().ToString("D");

            using HttpResponseMessage first = await client.GetAsync("/unmatched/" + unknownId + "?name=" + QUERY_SECRET);
            await WaitForCountAsync(factory.Services.GetRequiredService<ApiMetrics>());
            using HttpResponseMessage response = await client.GetAsync(MetricsOptions.ROUTE);
            string text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("route=\"unmatched\",status_class=\"4xx\"", text);
            Assert.DoesNotContain(unknownId, text);
            Assert.DoesNotContain(QUERY_SECRET, text);
            Assert.DoesNotContain("/unmatched/", text);
        }

        private static async Task WaitForCountAsync(ApiMetrics metrics)
        {
            const int ATTEMPTS = 100;
            const int DELAY_MS = 10;
            for (int attempt = 0; attempt < ATTEMPTS; ++attempt)
            {
                if (metrics.GetRequestSnapshots().Sum(snapshot => snapshot.Count) > 0)
                {
                    return;
                }
                await Task.Delay(DELAY_MS);
            }
            Assert.Fail("Request metric was not recorded.");
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string? remoteIp,
            string? headerName = null, string? headerValue = null)
        {
            using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), MetricsOptions.ROUTE);
            if (remoteIp != null)
            {
                request.Headers.Add(AdminTestSupport.REMOTE_IP_HEADER, remoteIp);
            }
            if (headerName != null)
            {
                request.Headers.Add(headerName, headerValue);
            }
            return await client.SendAsync(request);
        }

        private sealed class MetricsFactory : WebApplicationFactory<Program>
        {
            private readonly string[] _allowedNetworks;
            private readonly string? _knownProxy;
            private readonly bool _enabled;

            public StubReader Reader { get; } = new StubReader();
            public RecordingLoggerProvider Logs { get; } = new RecordingLoggerProvider();

            public MetricsFactory(string[]? allowedNetworks = null, string? knownProxy = null, bool enabled = true)
            {
                _allowedNetworks = allowedNetworks ?? Array.Empty<string>();
                _knownProxy      = knownProxy;
                _enabled         = enabled;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                if (_knownProxy != null)
                {
                    builder.UseSetting(ForwardedHeadersConfiguration.KNOWN_PROXIES_SECTION + ":0", _knownProxy);
                }
                builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().AddProvider(Logs));
                builder.ConfigureTestServices(services =>
                {
                    services.PostConfigure<MetricsOptions>(options =>
                    {
                        options.Enabled         = _enabled;
                        options.AllowedNetworks = _allowedNetworks;
                    });
                    // 시작 실패가 지표 네트워크에서 난다는 것을 독립적으로 확인한다
                    services.PostConfigure<AdminOptions>(options => options.AllowedNetworks = new[] { "198.51.100.0/24" });
                    services.RemoveAll<IMetricsDatabaseReader>();
                    services.AddSingleton<IMetricsDatabaseReader>(Reader);
                    services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
                });
            }
        }

        private sealed class StubReader : IMetricsDatabaseReader
        {
            public int Calls { get; private set; }

            public Task<DatabaseMetricsSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                ++Calls;
                return Task.FromResult(new DatabaseMetricsSnapshot(3, 1234.5, 2));
            }
        }
    }
}
