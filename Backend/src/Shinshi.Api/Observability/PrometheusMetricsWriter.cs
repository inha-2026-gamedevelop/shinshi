// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Shinshi.Backend.Observability
{
    /// <summary> 누적 Prometheus text 0.0.4. 미관측 값과 표본 없는 분위수는 NaN으로 명시한다 </summary>
    public static class PrometheusMetricsWriter
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string REQUESTS_TOTAL       = "shinshi_api_http_requests_total";
        public const string REQUEST_DURATION     = "shinshi_api_http_request_duration_seconds";
        public const string OUTBOX_PENDING       = "shinshi_api_settlement_outbox_pending";
        public const string RECONCILIATION_TIME  = "shinshi_api_reconciliation_last_run_timestamp_seconds";
        public const string RECONCILIATION_COUNT = "shinshi_api_reconciliation_last_mismatch_count";

        private const double MILLISECONDS_PER_SECOND = 1000;

        /****************************************
        *                Methods
        ****************************************/

        public static string Render(IReadOnlyList<ApiRequestMetricSnapshot> requests, ApiLatencySnapshot latency,
            DatabaseMetricsSnapshot database)
        {
            ArgumentNullException.ThrowIfNull(requests);
            ArgumentNullException.ThrowIfNull(latency);
            ArgumentNullException.ThrowIfNull(database);
            StringBuilder output = new StringBuilder();
            Header(output, REQUESTS_TOTAL, "counter", "Completed HTTP requests by route template and status class.");
            foreach (ApiRequestMetricSnapshot request in requests)
            {
                output.Append(REQUESTS_TOTAL).Append(Labels(request)).Append(' ')
                    .Append(request.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            Histogram(output, REQUEST_DURATION, requests, MILLISECONDS_PER_SECOND);
            Histogram(output, ApiMetrics.DURATION_INSTRUMENT, requests, 1);
            Gauge(output, MetricsReporterService.P50_METRIC, latency.P50Ms, "Last 60 seconds nearest-rank p50, in milliseconds.");
            Gauge(output, MetricsReporterService.P95_METRIC, latency.P95Ms, "Last 60 seconds nearest-rank p95, in milliseconds.");
            Gauge(output, MetricsReporterService.P99_METRIC, latency.P99Ms, "Last 60 seconds nearest-rank p99, in milliseconds.");
            Gauge(output, MetricsReporterService.SAMPLE_COUNT_METRIC, latency.SampleCount, "Samples in the last 60 seconds window.");
            Gauge(output, OUTBOX_PENDING, database.OutboxPending, "Undelivered outbox rows; NaN when not observed.");
            Gauge(output, MetricsReporterService.OUTBOX_PENDING_METRIC, database.OutboxPending,
                "Undelivered outbox rows; NaN when not observed.");
            Gauge(output, RECONCILIATION_TIME, database.LastRunTimestampSeconds,
                "Latest DB completed reconciliation finish time; zero if none, NaN if not observed.");
            Gauge(output, RECONCILIATION_COUNT, database.LastMismatchCount,
                "Mismatch count of latest DB completed reconciliation; NaN if none or not observed.");
            return output.ToString();
        }

        private static void Histogram(StringBuilder output, string name, IReadOnlyList<ApiRequestMetricSnapshot> requests,
            double divisor)
        {
            Header(output, name, "histogram", "Cumulative completed HTTP request duration.");
            foreach (ApiRequestMetricSnapshot request in requests)
            {
                string labels = Labels(request);
                string bucketLabels = labels.Substring(0, labels.Length - 1) + ",le=\"";
                for (int index = 0; index < ApiMetrics.DurationBoundsMs.Count; ++index)
                {
                    output.Append(name).Append("_bucket").Append(bucketLabels)
                        .Append(Number(ApiMetrics.DurationBoundsMs[index] / divisor)).Append("\"} ")
                        .Append(request.Buckets[index].ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
                output.Append(name).Append("_bucket").Append(bucketLabels).Append("+Inf\"} ")
                    .Append(request.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
                output.Append(name).Append("_sum").Append(labels).Append(' ').Append(Number(request.SumMs / divisor)).Append('\n');
                output.Append(name).Append("_count").Append(labels).Append(' ')
                    .Append(request.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        private static string Labels(ApiRequestMetricSnapshot request)
        {
            return "{route=\"" + Escape(request.Route) + "\",status_class=\"" + Escape(request.StatusClass) + "\"}";
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static string Number(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }

        private static void Header(StringBuilder output, string name, string type, string help)
        {
            output.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            output.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        }

        private static void Gauge(StringBuilder output, string name, double? value, string help)
        {
            Header(output, name, "gauge", help);
            output.Append(name).Append(' ').Append(value.HasValue ? Number(value.Value) : "NaN").Append('\n');
        }
    }
}
