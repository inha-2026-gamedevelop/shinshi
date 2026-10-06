// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Backend.Observability;

namespace Shinshi.Backend.Tests.Observability
{
    /// <summary> 공통 JSON 로그 포매터의 키와 비밀값 제외, 요청 로그 미들웨어, 60초 분위수 지표를 검사한다 </summary>
    public sealed class ObservabilityTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PROJECT_CATEGORY   = "Shinshi.Backend.Tests.Observability";
        private const string FRAMEWORK_CATEGORY = "Microsoft.AspNetCore.Hosting.Diagnostics";
        private const string SECRET             = "hunter2-secret";
        private const string INSTANCE_ID        = "api-01";
        private const string BUILD_ID           = "build-abc";
        private const string EVENTS_TEMPLATE    = "/v1/game-sessions/{sessionId}/events";

        private static readonly string[] G_CommonKeys =
        {
            "ts", "level", "service", "instanceId", "sessionId", "participantId", "operationId", "event", "buildId",
            "contentVersion", "eventId", "reasonCode", "data"
        };

        private static readonly Action<ILogger, string, string, string, int, long, Exception?> G_Domain =
            LoggerMessage.Define<string, string, string, int, long>(LogLevel.Warning, new EventId(1, "domain"),
                "{Event} {SessionId} {OperationId} {ReasonCode} {Amount}");

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Formatter_ProjectLog_WritesCommonKeysInOrderWithNullsAndData()
        {
            CapturingLogger logger = new CapturingLogger(PROJECT_CATEGORY, Options());
            string session = Guid.NewGuid().ToString("D");
            string operation = Guid.NewGuid().ToString("D");

            G_Domain(logger, "storage.retry", session, operation, 902, 7, null);

            JObject line = Assert.Single(logger.Lines);
            Assert.Equal(G_CommonKeys, line.Properties().Select(property => property.Name));
            Assert.Equal("warn", line["level"]!.Value<string>());
            Assert.Equal("api", line["service"]!.Value<string>());
            Assert.Equal(INSTANCE_ID, line["instanceId"]!.Value<string>());
            Assert.Equal(session, line["sessionId"]!.Value<string>());
            Assert.Equal(JTokenType.Null, line["participantId"]!.Type);
            Assert.Equal(operation, line["operationId"]!.Value<string>());
            Assert.Equal("storage.retry", line["event"]!.Value<string>());
            Assert.Equal(BUILD_ID, line["buildId"]!.Value<string>());
            Assert.Equal(JTokenType.Null, line["contentVersion"]!.Type);
            Assert.Equal(JTokenType.Integer, line["reasonCode"]!.Type);
            Assert.Equal(902, line["reasonCode"]!.Value<int>());
            Assert.Equal(7L, line["data"]!["amount"]!.Value<long>());
            string ts = line["ts"]!.Value<string>()!;
            Assert.EndsWith("Z", ts);
            Assert.Equal(24, ts.Length);
        }

        [Fact]
        public void Formatter_FrameworkLogAndException_DropsValuesAndExceptionMessage()
        {
            CapturingLogger logger = new CapturingLogger(FRAMEWORK_CATEGORY, Options());

            logger.LogInformation("Request starting {Path}{QueryString}", "/v1/inbox", "?token=" + SECRET);
            logger.LogError(new InvalidOperationException("Password=" + SECRET), "Failed {Reason}", SECRET);

            Assert.Equal(2, logger.Lines.Count);
            Assert.All(logger.RawLines, raw => Assert.DoesNotContain(SECRET, raw));
            Assert.Equal("Request starting {Path}{QueryString}", logger.Lines[0]["data"]!["template"]!.Value<string>());
            Assert.Equal(typeof(InvalidOperationException).FullName,
                logger.Lines[1]["data"]!["exceptionType"]!.Value<string>());
            Assert.Equal(FRAMEWORK_CATEGORY, logger.Lines[0]["event"]!.Value<string>());
            Assert.Equal(JTokenType.Null, logger.Lines[0]["sessionId"]!.Type);
        }

        /// <summary> 경로 템플릿, 메서드, 상태, 소요 시간만 남기고 실제 경로의 query는 남기지 않는다 </summary>
        [Fact]
        public async Task Middleware_CompletedRequest_LogsTemplateStatusAndRecordsMetric()
        {
            CapturingLogger logger = new CapturingLogger(typeof(RequestLoggingMiddleware).FullName!, Options());
            using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);
            RequestLoggingMiddleware middleware = new RequestLoggingMiddleware(context =>
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                return Task.CompletedTask;
            }, new TypedLogger<RequestLoggingMiddleware>(logger), metrics);
            Guid session = Guid.NewGuid();
            DefaultHttpContext context = CreateContext(session);

            await middleware.InvokeAsync(context);

            JObject line = Assert.Single(logger.Lines);
            Assert.Equal(RequestLoggingMiddleware.EVENT_NAME, line["event"]!.Value<string>());
            Assert.Equal(session.ToString("D"), line["sessionId"]!.Value<string>());
            Assert.Equal(EVENTS_TEMPLATE, line["data"]!["route"]!.Value<string>());
            Assert.Equal("POST", line["data"]!["method"]!.Value<string>());
            Assert.Equal(StatusCodes.Status409Conflict, line["data"]!["statusCode"]!.Value<int>());
            Assert.True(line["data"]!["durationMs"]!.Value<double>() >= 0);
            Assert.DoesNotContain(SECRET, Assert.Single(logger.RawLines));
            Assert.Equal(1, metrics.GetSnapshot().SampleCount);
        }

        [Fact]
        public async Task Middleware_UnhandledException_RecordsStatus500AndRethrows()
        {
            CapturingLogger logger = new CapturingLogger(typeof(RequestLoggingMiddleware).FullName!, Options());
            using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);
            RequestLoggingMiddleware middleware = new RequestLoggingMiddleware(
                _ => throw new InvalidOperationException(SECRET), new TypedLogger<RequestLoggingMiddleware>(logger),
                metrics);

            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext()));

            JObject line = Assert.Single(logger.Lines);
            Assert.Equal(StatusCodes.Status500InternalServerError, line["data"]!["statusCode"]!.Value<int>());
            Assert.Equal(RequestLoggingMiddleware.UNMATCHED_ROUTE, line["data"]!["route"]!.Value<string>());
            Assert.Equal(1, metrics.GetSnapshot().SampleCount);
        }

        [Fact]
        public void Metrics_RollingWindow_ComputesNearestRankAndExpiresAfter60Seconds()
        {
            ManualTimeProvider time = new ManualTimeProvider();
            using ApiMetrics metrics = new ApiMetrics(time);
            for (int value = 100; value >= 1; --value)
            {
                metrics.RecordRequest(EVENTS_TEMPLATE, "POST", StatusCodes.Status200OK, value);
            }

            ApiLatencySnapshot full = metrics.GetSnapshot();
            time.Advance(TimeSpan.FromSeconds(ApiMetrics.WINDOW_SECONDS - 1));
            metrics.RecordRequest(EVENTS_TEMPLATE, "POST", StatusCodes.Status200OK, 1000);
            time.Advance(TimeSpan.FromSeconds(1));
            ApiLatencySnapshot rolled = metrics.GetSnapshot();
            time.Advance(TimeSpan.FromSeconds(ApiMetrics.WINDOW_SECONDS));
            ApiLatencySnapshot empty = metrics.GetSnapshot();

            Assert.Equal(100, full.SampleCount);
            Assert.Equal(50.0, full.P50Ms);
            Assert.Equal(95.0, full.P95Ms);
            Assert.Equal(99.0, full.P99Ms);
            Assert.Equal(1, rolled.SampleCount);
            Assert.Equal(1000.0, rolled.P99Ms);
            Assert.Equal(0, empty.SampleCount);
            Assert.Null(empty.P50Ms);
            Assert.Null(empty.P99Ms);
        }

        [Theory]
        [InlineData(0.5, 2.0)]
        [InlineData(0.95, 4.0)]
        [InlineData(0.99, 4.0)]
        [InlineData(0.25, 1.0)]
        public void Percentile_NearestRank_ReturnsObservedValue(double fraction, double expected)
        {
            Assert.Equal(expected, ApiMetrics.Percentile(new[] { 1.0, 2.0, 3.0, 4.0 }, fraction));
        }

        [Fact]
        public async Task Reporter_WithoutDatabase_LogsMetricsWithoutOutboxValue()
        {
            CapturingLogger logger = new CapturingLogger(typeof(MetricsReporterService).FullName!, Options());
            using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);
            metrics.RecordRequest(EVENTS_TEMPLATE, "POST", StatusCodes.Status200OK, 12.5);
            using MetricsReporterService reporter = new MetricsReporterService(metrics,
                new TypedLogger<MetricsReporterService>(logger), TimeProvider.System, new EmptyServiceProvider(), false);

            IReadOnlyDictionary<string, object?> reported = await reporter.ReportOnceAsync(CancellationToken.None);

            Assert.False(reported.ContainsKey(MetricsReporterService.OUTBOX_PENDING_METRIC));
            JObject line = Assert.Single(logger.Lines);
            Assert.Equal(MetricsReporterService.EVENT_NAME, line["event"]!.Value<string>());
            JToken metricsJson = line["data"]!["metrics"]!;
            Assert.Equal(1, metricsJson[MetricsReporterService.SAMPLE_COUNT_METRIC]!.Value<int>());
            Assert.Equal(12.5, metricsJson[MetricsReporterService.P95_METRIC]!.Value<double>());
        }

        private static DefaultHttpContext CreateContext(Guid session)
        {
            DefaultHttpContext context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/v1/game-sessions/" + session.ToString("D") + "/events";
            context.Request.QueryString = new QueryString("?reason=" + SECRET);
            context.Request.RouteValues["sessionId"] = session.ToString("D");
            context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(EVENTS_TEMPLATE), 0,
                EndpointMetadataCollection.Empty, "events"));
            return context;
        }

        private static ShinshiJsonFormatterOptions Options()
        {
            return new ShinshiJsonFormatterOptions { InstanceId = INSTANCE_ID, BuildId = BUILD_ID };
        }

        /// <summary> 실제 포매터로 한 줄씩 써서 모으는 로거 </summary>
        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly ShinshiJsonConsoleFormatter _formatter;

            public List<string>  RawLines { get; } = new List<string>();
            public List<JObject> Lines    { get; } = new List<JObject>();

            public CapturingLogger(string category, ShinshiJsonFormatterOptions options)
            {
                _category  = category;
                _formatter = new ShinshiJsonConsoleFormatter(new StaticOptionsMonitor(options), TimeProvider.System);
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull { return null; }

            public bool IsEnabled(LogLevel logLevel) { return true; }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                using StringWriter writer = new StringWriter();
                _formatter.Write(new LogEntry<TState>(logLevel, _category, eventId, state, exception, formatter), null,
                    writer);
                string raw = writer.ToString();
                Assert.EndsWith(Environment.NewLine, raw);
                Assert.Single(raw.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
                RawLines.Add(raw);
                // ts를 날짜로 바꾸지 않고 원문 문자열 그대로 읽는다
                using JsonTextReader reader = new JsonTextReader(new StringReader(raw))
                {
                    DateParseHandling = DateParseHandling.None
                };
                Lines.Add(JObject.Load(reader));
            }
        }

        private sealed class TypedLogger<T> : ILogger<T>
        {
            private readonly ILogger _inner;

            public TypedLogger(ILogger inner) { _inner = inner; }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull { return _inner.BeginScope(state); }

            public bool IsEnabled(LogLevel logLevel) { return _inner.IsEnabled(logLevel); }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        private sealed class StaticOptionsMonitor : IOptionsMonitor<ShinshiJsonFormatterOptions>
        {
            public StaticOptionsMonitor(ShinshiJsonFormatterOptions value) { CurrentValue = value; }

            public ShinshiJsonFormatterOptions CurrentValue { get; }

            public ShinshiJsonFormatterOptions Get(string? name) { return CurrentValue; }

            public IDisposable? OnChange(Action<ShinshiJsonFormatterOptions, string?> listener) { return null; }
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private long _timestamp = 1;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override long GetTimestamp() { return _timestamp; }

            public void Advance(TimeSpan delta) { _timestamp += delta.Ticks; }
        }

        private sealed class EmptyServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) { return null; }
        }
    }
}
