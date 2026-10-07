// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Shinshi.Backend.Observability
{
    /// <summary> 조회하지 못한 DB 지표는 null, 완료한 대사가 없으면 시각은 0이고 불일치 수는 null이다 </summary>
    public sealed class DatabaseMetricsSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public long? OutboxPending { get; }
        public double? LastRunTimestampSeconds { get; }
        public long? LastMismatchCount { get; }

        /****************************************
        *                Methods
        ****************************************/

        public DatabaseMetricsSnapshot(long? outboxPending, double? lastRunTimestampSeconds, long? lastMismatchCount)
        {
            OutboxPending           = outboxPending;
            LastRunTimestampSeconds = lastRunTimestampSeconds;
            LastMismatchCount       = lastMismatchCount;
        }
    }

    public interface IMetricsDatabaseReader
    {
        Task<DatabaseMetricsSnapshot> ReadAsync(CancellationToken cancellationToken);
    }

    /// <summary> 모든 복제본이 같은 DB 완료 대사를 관측한다. 한 번의 읽기에 2초만 허용한다 </summary>
    public sealed class PostgresMetricsDatabaseReader : IMetricsDatabaseReader
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int TIMEOUT_SECONDS = 2;

        private readonly IServiceProvider _services;
        private readonly bool _databaseAvailable;
        private readonly ILogger<PostgresMetricsDatabaseReader> _logger;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresMetricsDatabaseReader(IServiceProvider services, bool databaseAvailable,
            ILogger<PostgresMetricsDatabaseReader> logger)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(logger);
            _services          = services;
            _databaseAvailable = databaseAvailable;
            _logger            = logger;
        }

        public async Task<DatabaseMetricsSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_databaseAvailable)
            {
                return new DatabaseMetricsSnapshot(null, null, null);
            }
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            try
            {
                // PostgreSQL 취소 확인과 연결 정리가 지연돼도 스크레이프는 제한 시각에 끝나게 한다
                return await ReadCoreAsync(timeout.Token).WaitAsync(timeout.Token);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogWarning("Metrics database read failed ({ExceptionType}).", exception.GetType().Name);
                }
                catch (Exception)
                {
                    // 로그 공급자 장애가 미관측 지표의 응답을 500으로 바꾸지 않게 한다
                }
                return new DatabaseMetricsSnapshot(null, null, null);
            }
        }

        private async Task<DatabaseMetricsSnapshot> ReadCoreAsync(CancellationToken cancellationToken)
        {
            NpgsqlDataSource dataSource = _services.GetRequiredService<NpgsqlDataSource>();
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            DatabaseMetricRow row = await connection.QuerySingleAsync<DatabaseMetricRow>(new CommandDefinition(
                """
                SELECT (SELECT count(*) FROM outbox WHERE delivered_at IS NULL) AS "OutboxPending",
                       COALESCE(extract(epoch FROM completed.finished_at)::double precision, 0) AS "LastRunTimestampSeconds",
                       completed.mismatch_count AS "LastMismatchCount"
                FROM (SELECT 1) AS singleton
                LEFT JOIN LATERAL (
                    SELECT finished_at, mismatch_count
                    FROM economy_reconciliation_runs
                    WHERE finished_at IS NOT NULL
                    ORDER BY finished_at DESC, run_date DESC
                    LIMIT 1
                ) AS completed ON true;
                """, commandTimeout: TIMEOUT_SECONDS, cancellationToken: cancellationToken));
            return new DatabaseMetricsSnapshot(row.OutboxPending, row.LastRunTimestampSeconds, row.LastMismatchCount);
        }

        private sealed class DatabaseMetricRow
        {
            public long OutboxPending { get; set; }
            public double LastRunTimestampSeconds { get; set; }
            public long? LastMismatchCount { get; set; }
        }
    }
}
