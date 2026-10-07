// System
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace Shinshi.Backend.Observability
{
    /// <summary> 경로 템플릿과 상태 분류별 누적 요청 지표와 직전 60초 요청 시간 분포 </summary>
    public sealed class ApiMetrics : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string METER_NAME          = "Shinshi.Api";
        public const string DURATION_INSTRUMENT = "api_request_duration_ms";
        public const int    WINDOW_SECONDS      = 60;
        public const int    MAX_WINDOW_SAMPLES  = 100000;
        public const int    MAX_REQUEST_SERIES  = 1024;

        private const int STATUS_CLASS_DIVISOR = 100;
        private const int MIN_STATUS_CLASS     = 1;
        private const int MAX_STATUS_CLASS     = 5;
        private const int MAX_ROUTE_LENGTH     = 512;
        private const double P50 = 0.50;
        private const double P95 = 0.95;
        private const double P99 = 0.99;
        private const string OVERFLOW_ROUTE = "other";

        private static readonly double[] G_DurationBoundsMs =
        {
            1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000
        };

        private readonly Meter _meter;
        private readonly Histogram<double> _duration;
        private readonly TimeProvider _timeProvider;
        private readonly long _windowTicks;
        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private readonly Dictionary<(string Route, string StatusClass), RequestSeries> _requests =
            new Dictionary<(string Route, string StatusClass), RequestSeries>();
        private readonly RequestSeries _overflow = new RequestSeries(OVERFLOW_ROUTE, "other", G_DurationBoundsMs.Length);
        private readonly object _gate = new object();

        /****************************************
        *              Properties
        ****************************************/

        public static IReadOnlyList<double> DurationBoundsMs { get; } = Array.AsReadOnly(G_DurationBoundsMs);

        /****************************************
        *                Methods
        ****************************************/

        public ApiMetrics(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
            _windowTicks  = WINDOW_SECONDS * timeProvider.TimestampFrequency;
            _meter        = new Meter(METER_NAME);
            _duration     = _meter.CreateHistogram<double>(DURATION_INSTRUMENT, "ms", "API request duration");
        }

        public void Dispose() { _meter.Dispose(); }

        /// <summary> 완료된 요청 한 건. 성공, 거절, 실패를 모두 넣는다 </summary>
        public void RecordRequest(string routeTemplate, string method, int statusCode, double durationMs)
        {
            if ((!double.IsFinite(durationMs)) || (durationMs < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(durationMs));
            }
            string statusClass = StatusClass(statusCode);
            long now = _timeProvider.GetTimestamp();
            lock (_gate)
            {
                string route = string.IsNullOrEmpty(routeTemplate) ? RequestLoggingMiddleware.UNMATCHED_ROUTE : routeTemplate;
                RequestSeries series = _overflow;
                if (route.Length <= MAX_ROUTE_LENGTH)
                {
                    (string Route, string StatusClass) key = (route, statusClass);
                    if (!_requests.TryGetValue(key, out RequestSeries? existing))
                    {
                        if (_requests.Count < MAX_REQUEST_SERIES)
                        {
                            existing = new RequestSeries(route, statusClass, G_DurationBoundsMs.Length);
                            _requests.Add(key, existing);
                        }
                    }
                    series = existing ?? _overflow;
                }
                series.Record(durationMs, G_DurationBoundsMs);
                Prune(now);
                _samples.Enqueue(new Sample(now, durationMs));
                if (_samples.Count > MAX_WINDOW_SAMPLES)
                {
                    _samples.Dequeue();
                }
            }
            _duration.Record(durationMs, new KeyValuePair<string, object?>("route", routeTemplate),
                new KeyValuePair<string, object?>("method", method),
                new KeyValuePair<string, object?>("status_class", statusClass));
        }

        /// <summary> 프로세스 시작 이후 누적 요청 수와 히스토그램. 60초 창 만료로 누적값이 줄지 않는다 </summary>
        public IReadOnlyList<ApiRequestMetricSnapshot> GetRequestSnapshots()
        {
            lock (_gate)
            {
                List<ApiRequestMetricSnapshot> snapshots = new List<ApiRequestMetricSnapshot>(_requests.Count + 1);
                foreach (RequestSeries series in _requests.Values)
                {
                    snapshots.Add(series.Snapshot());
                }
                if (_overflow.Count > 0)
                {
                    snapshots.Add(_overflow.Snapshot());
                }
                snapshots.Sort((left, right) =>
                {
                    int route = StringComparer.Ordinal.Compare(left.Route, right.Route);
                    return route != 0 ? route : StringComparer.Ordinal.Compare(left.StatusClass, right.StatusClass);
                });
                return snapshots;
            }
        }

        /// <summary> 직전 60초 완료 요청의 분포. 표본이 없으면 분위수는 null이다 </summary>
        public ApiLatencySnapshot GetSnapshot()
        {
            double[] durations;
            lock (_gate)
            {
                Prune(_timeProvider.GetTimestamp());
                durations = new double[_samples.Count];
                int index = 0;
                foreach (Sample sample in _samples)
                {
                    durations[index] = sample.DurationMs;
                    ++index;
                }
            }
            if (durations.Length == 0)
            {
                return new ApiLatencySnapshot(0, null, null, null);
            }
            Array.Sort(durations);
            return new ApiLatencySnapshot(durations.Length, Percentile(durations, P50), Percentile(durations, P95),
                Percentile(durations, P99));
        }

        /// <summary> 정렬된 표본의 nearest-rank 분위수. 보간하지 않으므로 항상 실제 관측값이다 </summary>
        public static double Percentile(IReadOnlyList<double> sorted, double fraction)
        {
            ArgumentNullException.ThrowIfNull(sorted);
            if ((sorted.Count == 0) || (fraction <= 0) || (fraction > 1))
            {
                throw new ArgumentOutOfRangeException(nameof(fraction));
            }
            int rank = (int)Math.Ceiling(fraction * sorted.Count);
            return sorted[Math.Max(rank, 1) - 1];
        }

        private void Prune(long now)
        {
            while ((_samples.Count > 0) && (now - _samples.Peek().Timestamp >= _windowTicks))
            {
                _samples.Dequeue();
            }
        }

        private static string StatusClass(int statusCode)
        {
            int statusClass = statusCode / STATUS_CLASS_DIVISOR;
            return (statusClass >= MIN_STATUS_CLASS) && (statusClass <= MAX_STATUS_CLASS)
                ? statusClass.ToString(CultureInfo.InvariantCulture) + "xx" : "other";
        }

        private sealed class RequestSeries
        {
            private readonly string _route;
            private readonly string _statusClass;
            private readonly long[] _buckets;
            private double _sumMs;

            public long Count { get; private set; }

            public RequestSeries(string route, string statusClass, int bucketCount)
            {
                _route       = route;
                _statusClass = statusClass;
                _buckets     = new long[bucketCount];
            }

            public void Record(double durationMs, IReadOnlyList<double> bounds)
            {
                ++Count;
                _sumMs += durationMs;
                for (int index = 0; index < bounds.Count; ++index)
                {
                    if (durationMs <= bounds[index])
                    {
                        ++_buckets[index];
                    }
                }
            }

            public ApiRequestMetricSnapshot Snapshot()
            {
                return new ApiRequestMetricSnapshot(_route, _statusClass, Count, _sumMs,
                    Array.AsReadOnly((long[])_buckets.Clone()));
            }
        }

        private readonly struct Sample
        {
            public long Timestamp { get; }
            public double DurationMs { get; }

            public Sample(long timestamp, double durationMs)
            {
                Timestamp  = timestamp;
                DurationMs = durationMs;
            }
        }
    }

    /// <summary> 직전 60초 요청 시간 분포 </summary>
    public sealed class ApiLatencySnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public int SampleCount { get; }
        public double? P50Ms { get; }
        public double? P95Ms { get; }
        public double? P99Ms { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ApiLatencySnapshot(int sampleCount, double? p50Ms, double? p95Ms, double? p99Ms)
        {
            SampleCount = sampleCount;
            P50Ms       = p50Ms;
            P95Ms       = p95Ms;
            P99Ms       = p99Ms;
        }
    }
}
