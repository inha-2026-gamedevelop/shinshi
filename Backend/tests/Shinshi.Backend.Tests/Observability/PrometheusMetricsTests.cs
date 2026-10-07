// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Project
using Shinshi.Backend.Observability;

namespace Shinshi.Backend.Tests.Observability
{
    public sealed class PrometheusMetricsTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ROUTE = "/v1/accounts/{accountId}";
        private const string LABELS = "{route=\"/v1/accounts/{accountId}\",status_class=\"2xx\"}";
        private const int PARALLEL_REQUESTS = 2000;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Render_CumulativeHistogram_UsesSecondsAndLegacyMilliseconds()
        {
            using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);
            metrics.RecordRequest(ROUTE, "GET", 200, 5);
            metrics.RecordRequest(ROUTE, "POST", 201, 1000);
            metrics.RecordRequest(ROUTE, "GET", 409, 25);

            string rendered = Render(metrics, new DatabaseMetricsSnapshot(7, 1234.5, 2));

            Assert.Contains(PrometheusMetricsWriter.REQUESTS_TOTAL + LABELS + " 2\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.REQUEST_DURATION
                + "_bucket{route=\"/v1/accounts/{accountId}\",status_class=\"2xx\",le=\"0.005\"} 1\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.REQUEST_DURATION
                + "_bucket{route=\"/v1/accounts/{accountId}\",status_class=\"2xx\",le=\"+Inf\"} 2\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.REQUEST_DURATION + "_sum" + LABELS + " 1.005\n", rendered);
            Assert.Contains(ApiMetrics.DURATION_INSTRUMENT + "_sum" + LABELS + " 1005\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.OUTBOX_PENDING + " 7\n", rendered);
            Assert.Contains(MetricsReporterService.OUTBOX_PENDING_METRIC + " 7\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_TIME + " 1234.5\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_COUNT + " 2\n", rendered);
            Assert.DoesNotContain("method=", rendered);
        }

        [Fact]
        public void Render_EmptyOrUnavailableValues_AreNanWithZeroSampleCount()
        {
            using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);

            string rendered = Render(metrics, new DatabaseMetricsSnapshot(null, null, null));

            Assert.Contains(MetricsReporterService.P50_METRIC + " NaN\n", rendered);
            Assert.Contains(MetricsReporterService.P95_METRIC + " NaN\n", rendered);
            Assert.Contains(MetricsReporterService.P99_METRIC + " NaN\n", rendered);
            Assert.Contains(MetricsReporterService.SAMPLE_COUNT_METRIC + " 0\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.OUTBOX_PENDING + " NaN\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_TIME + " NaN\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_COUNT + " NaN\n", rendered);
        }

        [Fact]
        public void Render_NoCompletedReconciliation_UsesZeroTimeAndNanMismatchCount()
        {
            using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);

            string rendered = Render(metrics, new DatabaseMetricsSnapshot(0, 0, null));

            Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_TIME + " 0\n", rendered);
            Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_COUNT + " NaN\n", rendered);
        }

        [Fact]
        public void Render_NonEnglishCulture_UsesInvariantNumbersAndEscapesLabels()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);
                metrics.RecordRequest("/a\\\"\nb", "GET", 200, 12.5);

                string rendered = Render(metrics, new DatabaseMetricsSnapshot(0, 1.25, 0));

                Assert.Contains("route=\"/a\\\\\\\"\\nb\"", rendered);
                Assert.Contains(" 0.0125\n", rendered);
                Assert.Contains(PrometheusMetricsWriter.RECONCILIATION_TIME + " 1.25\n", rendered);
                Assert.DoesNotContain("1,25", rendered);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void RecordRequest_ParallelRequests_PreservesEveryCountAndCumulativeBucket()
        {
            using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);
            Parallel.For(0, PARALLEL_REQUESTS, _ => metrics.RecordRequest(ROUTE, "GET", 200, 5));

            ApiRequestMetricSnapshot snapshot = Assert.Single(metrics.GetRequestSnapshots());

            Assert.Equal(PARALLEL_REQUESTS, snapshot.Count);
            Assert.Equal(PARALLEL_REQUESTS * 5, snapshot.SumMs);
            Assert.Equal(0, snapshot.Buckets[0]);
            Assert.All(snapshot.Buckets.Skip(1), count => Assert.Equal(PARALLEL_REQUESTS, count));
        }

        [Fact]
        public void RecordRequest_TooManyRoutes_CapsSeriesAndKeepsCountsInOverflow()
        {
            using ApiMetrics metrics = new ApiMetrics(TimeProvider.System);
            int requests = ApiMetrics.MAX_REQUEST_SERIES + 10;
            for (int index = 0; index < requests; ++index)
            {
                metrics.RecordRequest("/route/" + index.ToString(CultureInfo.InvariantCulture), "GET", 200, 1);
            }

            IReadOnlyList<ApiRequestMetricSnapshot> snapshots = metrics.GetRequestSnapshots();

            Assert.Equal(ApiMetrics.MAX_REQUEST_SERIES + 1, snapshots.Count);
            Assert.Equal(requests, snapshots.Sum(snapshot => snapshot.Count));
            Assert.Equal(10, Assert.Single(snapshots, snapshot => snapshot.Route == "other").Count);
        }

        [Fact]
        public void RecordRequest_WindowExpires_CumulativeSeriesRemain()
        {
            ManualTimeProvider time = new ManualTimeProvider();
            using ApiMetrics metrics = new ApiMetrics(time);
            metrics.RecordRequest(ROUTE, "GET", 200, 12.5);
            time.Advance(TimeSpan.FromSeconds(ApiMetrics.WINDOW_SECONDS));

            Assert.Equal(0, metrics.GetSnapshot().SampleCount);
            Assert.Equal(1, Assert.Single(metrics.GetRequestSnapshots()).Count);
        }

        [Fact]
        public async Task ReadAsync_WithoutDatabase_DoesNotResolveDataSourceAndReturnsUnknown()
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            PostgresMetricsDatabaseReader reader = new PostgresMetricsDatabaseReader(services, false,
                NullLogger<PostgresMetricsDatabaseReader>.Instance);

            DatabaseMetricsSnapshot result = await reader.ReadAsync(CancellationToken.None);

            Assert.Null(result.OutboxPending);
            Assert.Null(result.LastRunTimestampSeconds);
            Assert.Null(result.LastMismatchCount);
        }

        [Fact]
        public async Task ReadAsync_MissingDataSource_DoesNotReportFalseZeros()
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            PostgresMetricsDatabaseReader reader = new PostgresMetricsDatabaseReader(services, true,
                NullLogger<PostgresMetricsDatabaseReader>.Instance);

            DatabaseMetricsSnapshot result = await reader.ReadAsync(CancellationToken.None);

            Assert.Null(result.OutboxPending);
            Assert.Null(result.LastRunTimestampSeconds);
            Assert.Null(result.LastMismatchCount);
        }

        [Fact]
        public async Task ReadAsync_RequestCanceled_PropagatesCancellation()
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            PostgresMetricsDatabaseReader reader = new PostgresMetricsDatabaseReader(services, false,
                NullLogger<PostgresMetricsDatabaseReader>.Instance);
            using CancellationTokenSource canceled = new CancellationTokenSource();
            canceled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(canceled.Token));
        }

        private static string Render(ApiMetrics metrics, DatabaseMetricsSnapshot database)
        {
            return PrometheusMetricsWriter.Render(metrics.GetRequestSnapshots(), metrics.GetSnapshot(), database);
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private long _timestamp = 1;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() { return _timestamp; }
            public void Advance(TimeSpan delta) { _timestamp += delta.Ticks; }
        }
    }
}
