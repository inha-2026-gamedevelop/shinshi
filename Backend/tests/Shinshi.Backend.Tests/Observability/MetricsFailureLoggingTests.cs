// System
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

// Project
using Shinshi.Backend.Observability;

namespace Shinshi.Backend.Tests.Observability
{
    /// <summary> 로그 공급자의 예외가 지표의 미관측, 네트워크 거절 응답을 바꾸지 않는지 검사한다 </summary>
    public sealed class MetricsFailureLoggingTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task ReadAsync_LoggerThrows_ReturnsUnknownInsteadOfThrowing()
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            ThrowingLogger<PostgresMetricsDatabaseReader> logger = new ThrowingLogger<PostgresMetricsDatabaseReader>();
            PostgresMetricsDatabaseReader reader = new PostgresMetricsDatabaseReader(services, true, logger);

            DatabaseMetricsSnapshot result = await reader.ReadAsync(CancellationToken.None);

            Assert.Null(result.OutboxPending);
            Assert.Null(result.LastRunTimestampSeconds);
            Assert.Null(result.LastMismatchCount);
            Assert.Equal(1, logger.Calls);
        }

        [Fact]
        public void IsAllowed_UntrustedForwardingAndLoggerThrows_RejectsAndAttemptsLogOnce()
        {
            ThrowingLogger<MetricsNetworkPolicy> logger = new ThrowingLogger<MetricsNetworkPolicy>();
            IConfiguration configuration = new ConfigurationBuilder().Build();
            MetricsNetworkPolicy policy = new MetricsNetworkPolicy(Options.Create(new MetricsOptions()), configuration,
                logger);
            DefaultHttpContext context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.5";

            Assert.False(policy.IsAllowed(context));
            Assert.False(policy.IsAllowed(context));
            Assert.Equal(1, logger.Calls);
        }

        private sealed class ThrowingLogger<T> : ILogger<T>
        {
            public int Calls { get; private set; }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull { return null; }
            public bool IsEnabled(LogLevel logLevel) { return true; }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ++Calls;
                throw new InvalidOperationException("Test log provider failure.");
            }
        }
    }
}
