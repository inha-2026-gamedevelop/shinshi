// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Shinshi.Backend.Observability
{
    /// <summary> 60초마다 api.metrics 한 줄(직전 60초 요청 시간 분위수, 표본 수, DB가 있으면 미전달 outbox 수)을 남긴다 </summary>
    public sealed class MetricsReporterService : BackgroundService
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string EVENT_NAME        = "api.metrics";
        public const string FAILED_EVENT_NAME = "api.metrics.failed";
        public const int    INTERVAL_SECONDS  = ApiMetrics.WINDOW_SECONDS;

        // 배포 로그 규약의 지표 이름
        public const string P50_METRIC            = "api_request_duration_p50_ms";
        public const string P95_METRIC            = "api_request_duration_p95_ms";
        public const string P99_METRIC            = "api_request_duration_p99_ms";
        public const string SAMPLE_COUNT_METRIC   = "api_request_sample_count";
        public const string OUTBOX_PENDING_METRIC = "settlement_outbox_pending";

        private const int REPORT_EVENT_ID = 1001;
        private const int FAILED_EVENT_ID = 1002;

        private static readonly Action<ILogger, string, IReadOnlyDictionary<string, object?>, Exception?> G_Report =
            LoggerMessage.Define<string, IReadOnlyDictionary<string, object?>>(LogLevel.Information,
                new EventId(REPORT_EVENT_ID, EVENT_NAME), "{Event} {Metrics}");
        private static readonly Action<ILogger, string, string, Exception?> G_Failed =
            LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(FAILED_EVENT_ID, FAILED_EVENT_NAME),
                "{Event} {ExceptionType}");

        private readonly ApiMetrics _metrics;
        private readonly ILogger<MetricsReporterService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly IServiceProvider _services;
        private readonly bool _databaseAvailable;

        /****************************************
        *                Methods
        ****************************************/

        // DB 연결 문자열이 없는 실행(공개 상태만)에서는 데이터 소스를 만들지 않는다. 만들면 연결 문자열 누락 예외가 난다
        public MetricsReporterService(ApiMetrics metrics, ILogger<MetricsReporterService> logger, TimeProvider timeProvider,
            IServiceProvider services, bool databaseAvailable)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(services);
            _metrics           = metrics;
            _logger            = logger;
            _timeProvider      = timeProvider;
            _services          = services;
            _databaseAvailable = databaseAvailable;
        }

        /// <summary> 지표 한 줄을 만들고 남긴다. 미전달 outbox 조회가 실패하면 그 값만 null로 두고 실패 형식을 따로 남긴다 </summary>
        public async Task<IReadOnlyDictionary<string, object?>> ReportOnceAsync(CancellationToken cancellationToken)
        {
            ApiLatencySnapshot latency = _metrics.GetSnapshot();
            Dictionary<string, object?> metrics = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [P50_METRIC]          = latency.P50Ms,
                [P95_METRIC]          = latency.P95Ms,
                [P99_METRIC]          = latency.P99Ms,
                [SAMPLE_COUNT_METRIC] = latency.SampleCount
            };
            if (_databaseAvailable)
            {
                metrics[OUTBOX_PENDING_METRIC] = await ReadOutboxPendingAsync(cancellationToken);
            }

            G_Report(_logger, EVENT_NAME, metrics, null);
            return metrics;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(INTERVAL_SECONDS), _timeProvider);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await ReportOnceAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 종료 신호
            }
        }

        // 정산 전용 outbox 소비자 키가 아직 없어 미전달 행 전체를 센다. 부분 인덱스 ix_outbox_dispatch(delivered_at IS NULL)를 쓴다
        private async Task<long?> ReadOutboxPendingAsync(CancellationToken cancellationToken)
        {
            try
            {
                NpgsqlDataSource dataSource = _services.GetRequiredService<NpgsqlDataSource>();
                await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
                return await connection.QuerySingleAsync<long>(new CommandDefinition(
                    "SELECT count(*) FROM outbox WHERE delivered_at IS NULL;", cancellationToken: cancellationToken));
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                // 예외 메시지에 연결 정보가 섞일 수 있어 형식 이름만 남긴다
                G_Failed(_logger, FAILED_EVENT_NAME, exception.GetType().Name, null);
                return null;
            }
        }
    }
}
