// System
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Health;

namespace Shinshi.Backend.Tests.Health
{
    public sealed class HealthHttpTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task Live_DatabaseWouldFail_DoesNotProbeAndReturnsOnlyOk()
        {
            StubProbe probe = new StubProbe(_ => throw new InvalidOperationException("secret connection string"));
            using HealthFactory factory = new HealthFactory(probe);
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer invalid");

            using HttpResponseMessage response = await client.GetAsync(HealthController.LIVE_ROUTE);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal(0, probe.Calls);
        }

        [Fact]
        public async Task Ready_Success_AnonymousAndMinimalOk()
        {
            StubProbe probe = new StubProbe(_ => Task.CompletedTask);
            using HealthFactory factory = new HealthFactory(probe);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(HealthController.READY_ROUTE);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal(1, probe.Calls);
            Assert.True(probe.Token.CanBeCanceled);
        }

        [Fact]
        public async Task Ready_Exception_503DoesNotExposeError()
        {
            StubProbe probe = new StubProbe(_ => throw new InvalidOperationException("password=secret"));
            using HealthFactory factory = new HealthFactory(probe);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(HealthController.READY_ROUTE);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("{\"status\":\"unavailable\"}", await response.Content.ReadAsStringAsync());
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        }

        [Fact]
        public async Task Ready_ProbeDoesNotFinish_503WithinDeadlineAndCancelsProbe()
        {
            TaskCompletionSource stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            StubProbe probe = new StubProbe(_ => stalled.Task);
            using HealthFactory factory = new HealthFactory(probe);
            using HttpClient client = factory.CreateClient();
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                using HttpResponseMessage response = await client.GetAsync(HealthController.READY_ROUTE);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("{\"status\":\"unavailable\"}", await response.Content.ReadAsStringAsync());
                Assert.True(probe.Token.IsCancellationRequested);
                Assert.InRange(watch.Elapsed.TotalSeconds, 1.8, 5);
            }
            finally
            {
                stalled.TrySetResult();
            }
        }

        [Fact]
        public async Task Ready_DataSourceCannotBeCreated_503WhileLiveRemains200()
        {
            using HealthFactory factory = new HealthFactory(null);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage ready = await client.GetAsync(HealthController.READY_ROUTE);
            using HttpResponseMessage live = await client.GetAsync(HealthController.LIVE_ROUTE);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Equal("{\"status\":\"unavailable\"}", await ready.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        [PostgresFact]
        public async Task Ready_RealPostgres_SelectOneReturnsOk()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using ServiceProvider services = new ServiceCollection().AddSingleton(database.DataSource).BuildServiceProvider();
            PostgresReadinessProbe probe = new PostgresReadinessProbe(services);
            using HealthFactory factory = new HealthFactory(probe);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(HealthController.READY_ROUTE);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
        }

        private sealed class StubProbe : IReadinessProbe
        {
            private readonly Func<CancellationToken, Task> _check;

            public int Calls { get; private set; }
            public CancellationToken Token { get; private set; }

            public StubProbe(Func<CancellationToken, Task> check)
            {
                _check = check;
            }

            public Task CheckAsync(CancellationToken cancellationToken)
            {
                ++Calls;
                Token = cancellationToken;
                return _check(cancellationToken);
            }
        }

        private sealed class HealthFactory : WebApplicationFactory<Program>
        {
            private readonly IReadinessProbe? _probe;

            public HealthFactory(IReadinessProbe? probe)
            {
                _probe = probe;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
                builder.ConfigureTestServices(services =>
                {
                    // 실제 설정/DB 없이 준비 상태의 실패를 재현한다. live가 이 팩토리를 호출하면 테스트가 실패한다.
                    services.AddSingleton<NpgsqlDataSource>(_ => throw new InvalidOperationException("secret configuration"));
                    if (_probe != null)
                    {
                        services.AddSingleton(_probe);
                    }
                });
            }
        }
    }
}
