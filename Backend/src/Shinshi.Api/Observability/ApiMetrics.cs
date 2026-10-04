// System
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Shinshi.Backend.Observability
{
    // 요청 시간은 System.Diagnostics.Metrics 히스토그램(api_request_duration_ms)으로도 내보내고, 수집기가 없는 배포에서도
    // api.metrics 로그로 볼 수 있게 직전 60초 원표본으로 분위수를 계산한다. 인스턴스별 분위수를 평균해 전체 값으로 보지 않는다
    /// <summary> API 요청 시간 지표(싱글턴). 경로 템플릿, 메서드, 상태 분류만 태그로 쓴다 </summary>
    public sealed class ApiMetrics : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string METER_NAME          = "Shinshi.Api";
        public const string DURATION_INSTRUMENT = "api_request_duration_ms";
        public const int    WINDOW_SECONDS      = 60;

        // 창 안의 표본 상한. 넘으면 오래된 표본부터 버려 메모리를 묶는다(초당 1600건 이상일 때만 닿는다)
        public const int MAX_WINDOW_SAMPLES = 100000;

        private const double P50 = 0.50;
        private const double P95 = 0.95;
        private const double P99 = 0.99;

        private readonly Meter _meter;
        private readonly Histogram<double> _duration;
        private readonly TimeProvider _timeProvider;
        private readonly long _windowTicks;
        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private readonly object _gate = new object();

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

        public void Dispose()
        {
            _meter.Dispose();
        }

        /// <summary> 완료된 요청 한 건. 성공, 거절, 실패를 모두 넣는다 </summary>
        public void RecordRequest(string routeTemplate, string method, int statusCode, double durationMs)
        {
            _duration.Record(durationMs, new KeyValuePair<string, object?>("route", routeTemplate),
                new KeyValuePair<string, object?>("method", method),
                new KeyValuePair<string, object?>("status_class", StatusClass(statusCode)));
            long now = _timeProvider.GetTimestamp();
            lock (_gate)
            {
                Prune(now);
                _samples.Enqueue(new Sample(now, durationMs));
                if (_samples.Count > MAX_WINDOW_SAMPLES)
                {
                    _samples.Dequeue();
                }
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
            return (statusCode / 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "xx";
        }

        private readonly struct Sample
        {
            public long   Timestamp  { get; }
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

        public int     SampleCount { get; }
        public double? P50Ms       { get; }
        public double? P95Ms       { get; }
        public double? P99Ms       { get; }

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
