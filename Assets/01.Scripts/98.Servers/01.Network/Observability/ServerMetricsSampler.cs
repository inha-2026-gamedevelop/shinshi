// System
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 서버 틱 시간, 관리 힙, 저장 큐, 활성 연결을 모아 60초마다 server.metrics 한 줄로 쓴다 </summary>
    public sealed class ServerMetricsSampler
    {
        // 틱 시간은 1ms 칸 고정 막대로 모아 분위수를 낸다. 표본을 배열에 쌓으면 60초 동안 1800개를 보관하고 정렬해야 해서다.
        // 칸 상한을 넘는 틱은 마지막 칸에 넣고 최댓값은 따로 잰다. 메인 스레드에서만 부르며 기록 경로는 할당하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        public const double EMIT_INTERVAL_SECONDS = 60.0;

        // 1ms 칸 개수, 마지막 칸은 그 이상을 모두 담는다
        private const int    BUCKET_COUNT         = 256;
        private const double MS_PER_SECOND        = 1000.0;
        private const double P50                  = 0.50;
        private const double P95                  = 0.95;
        private const double P99                  = 0.99;
        private const int    BUILDER_CAPACITY     = 512;
        private const string DECIMAL_FORMAT       = "0.###";

        // 값이 없음을 뜻한다. 줄에는 null로 쓴다
        private const double NO_VALUE             = -1.0;

        // 틱 예산은 실제 틱 주기다. 30Hz면 약 33.33ms
        private static readonly double TICK_BUDGET_MS = MS_PER_SECOND / Constants.Network.SERVER_TICK_RATE;

        // Prometheus 히스토그램 경계(ms). 30Hz 예산 33.33ms 앞뒤를 촘촘히 둔다. 이 값은 창을 비워도 누적한다
        private static readonly double[] G_HistogramBoundsMs = { 1.0, 2.0, 5.0, 10.0, 16.0, 25.0, 33.0, 50.0, 100.0, 250.0 };

        // 프레임 간격은 Update 사이 실제 시간이다. 예산의 1.5배를 넘으면 초과로 센다(프레임 제한 대기의 흔들림은 초과가 아니다)
        private const double FRAME_OVERRUN_FACTOR        = 1.5;
        private const double STALL_WINDOW_SECONDS        = 1.0;
        private const double KCP_SAMPLE_INTERVAL_SECONDS = 1.0;

        private static readonly double[] G_FrameBoundsMs = { 25.0, 34.0, 40.0, 50.0, 100.0, 250.0, 500.0, 1000.0, 5000.0 };

        private readonly DungeonSessionHost  _host;
        private readonly int[]               _buckets        = new int[BUCKET_COUNT];
        private readonly long[]              _frameHistogram = new long[G_FrameBoundsMs.Length + 1];
        private readonly ServerKcpQueueProbe _kcp            = new ServerKcpQueueProbe();
        private readonly StringBuilder       _builder        = new StringBuilder(BUILDER_CAPACITY);

        // 인덱스 i는 경계 i 이하인 틱 수(누적 아님), 마지막 칸은 모든 경계를 넘은 틱이다
        private readonly long[] _histogram = new long[G_HistogramBoundsMs.Length + 1];

        private long   _totalCount   = 0;
        private double _totalSumMs   = 0.0;
        private int    _sampleCount  = 0;
        private double _maxMs        = 0.0;
        private long   _overrunTotal = 0;
        private double _nextEmitAt   = 0.0;
        private bool   _started      = false;

        private double _lastFrameAt     = NO_VALUE;
        private double _stallWindowAt   = 0.0;
        private double _stallWindowMs   = 0.0;
        private double _stallLastMs     = NO_VALUE;
        private double _frameMaxCurMs   = 0.0;
        private double _frameMaxPrevMs  = 0.0;
        private double _frameSumMs      = 0.0;
        private long   _frameCount      = 0;
        private long   _frameOverrun    = 0;
        private double _nextKcpSampleAt = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        public long   OverrunTotal => _overrunTotal;
        public int    SampleCount  => _sampleCount;
        public double MaxMs        => _maxMs;

        public static double                TickBudgetMs       => TICK_BUDGET_MS;
        public static IReadOnlyList<double> HistogramBoundsMs  => G_HistogramBoundsMs;
        public long                         TotalTickCount     => _totalCount;
        public double                       TotalTickSumMs     => _totalSumMs;
        public DungeonSessionHost           Host               => _host;

        public static IReadOnlyList<double> FrameBoundsMs      => G_FrameBoundsMs;
        public static double                FrameOverrunMs     => TICK_BUDGET_MS * FRAME_OVERRUN_FACTOR;
        public long                         FrameCount         => _frameCount;
        public double                       FrameSumMs         => _frameSumMs;
        public long                         FrameOverrunTotal  => _frameOverrun;

        // 직전 창과 지금 창 중 큰 값이라 60~120초를 덮는다. 수집 주기보다 길어 스침이 사라지지 않는다
        public double                       FrameMaxMs         => Math.Max(_frameMaxCurMs, _frameMaxPrevMs);

        // 마지막으로 끝난 1초 창의 최대 프레임 간격, 첫 창이 끝나기 전에는 음수
        public double                       StallMax1sMs       => _stallLastMs;
        public ServerKcpQueueProbe          Kcp                => _kcp;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> host는 던전 역할에서만 있다. 없으면 저장 큐 지표를 null로 쓴다 </summary>
        public ServerMetricsSampler(DungeonSessionHost host)
        {
            _host = host;
        }

        /// <summary> 틱 한 회의 수행 시간을 Stopwatch 눈금으로 기록한다, 전투 틱이 매 틱 부른다 </summary>
        public void RecordTick(long elapsedTicks)
        {
            RecordTickMs((elapsedTicks * MS_PER_SECOND) / Stopwatch.Frequency);
        }

        public void RecordTickMs(double elapsedMs)
        {
            if (!(elapsedMs >= 0.0))
            {
                return;
            }

            int bucket = (elapsedMs >= (BUCKET_COUNT - 1)) ? (BUCKET_COUNT - 1) : (int)elapsedMs;
            ++_buckets[bucket];
            ++_sampleCount;
            _maxMs = Math.Max(_maxMs, elapsedMs);
            if (elapsedMs > TICK_BUDGET_MS)
            {
                ++_overrunTotal;
            }

            ++_totalCount;
            _totalSumMs += elapsedMs;
            int slot = G_HistogramBoundsMs.Length;
            for (int i = 0; i < G_HistogramBoundsMs.Length; ++i)
            {
                if (elapsedMs <= G_HistogramBoundsMs[i])
                {
                    slot = i;
                    break;
                }
            }

            ++_histogram[slot];
        }

        /// <summary> 매 프레임 한 번 부른다, 직전 호출과의 간격을 프레임 간격으로 기록한다. 단조 시계 초를 받는다 </summary>
        public void RecordFrame(double nowSeconds)
        {
            if (_lastFrameAt == NO_VALUE)
            {
                _lastFrameAt   = nowSeconds;
                _stallWindowAt = nowSeconds;
                return;
            }

            double intervalMs = (nowSeconds - _lastFrameAt) * MS_PER_SECOND;
            _lastFrameAt = nowSeconds;
            if (!(intervalMs >= 0.0))
            {
                return;
            }

            ++_frameCount;
            _frameSumMs += intervalMs;
            _frameMaxCurMs = Math.Max(_frameMaxCurMs, intervalMs);
            if (intervalMs > FrameOverrunMs)
            {
                ++_frameOverrun;
            }

            int slot = G_FrameBoundsMs.Length;
            for (int i = 0; i < G_FrameBoundsMs.Length; ++i)
            {
                if (intervalMs <= G_FrameBoundsMs[i])
                {
                    slot = i;
                    break;
                }
            }

            ++_frameHistogram[slot];

            // 창을 닫는 프레임의 간격도 닫히는 창에 넣는다. 긴 정지는 끝나는 프레임에서야 보이기 때문이다
            _stallWindowMs = Math.Max(_stallWindowMs, intervalMs);
            if ((nowSeconds - _stallWindowAt) >= STALL_WINDOW_SECONDS)
            {
                _stallLastMs   = _stallWindowMs;
                _stallWindowMs = 0.0;
                _stallWindowAt = nowSeconds;
            }
        }

        /// <summary> 프레임 간격 경계 index 이하인 프레임의 누적 수, index가 경계 개수면 전체 수(+Inf) </summary>
        public long GetFrameCumulativeBucket(int index)
        {
            long cumulative = 0;
            int  last       = Math.Min(index, G_FrameBoundsMs.Length);
            for (int i = 0; i <= last; ++i)
            {
                cumulative += _frameHistogram[i];
            }

            return cumulative;
        }

        /// <summary> 경계 index 이하인 틱의 누적 수, index가 경계 개수면 전체 수(+Inf) </summary>
        public long GetCumulativeBucket(int index)
        {
            long cumulative = 0;
            int  last       = Math.Min(index, G_HistogramBoundsMs.Length);
            for (int i = 0; i <= last; ++i)
            {
                cumulative += _histogram[i];
            }

            return cumulative;
        }

        /// <summary> 매 프레임 부른다, 주기가 되면 한 줄을 쓰고 틱 분포를 비운다. 단조 시계 초를 받는다 </summary>
        public void Tick(double nowSeconds)
        {
            RecordFrame(nowSeconds);
            if (nowSeconds >= _nextKcpSampleAt)
            {
                _nextKcpSampleAt = nowSeconds + KCP_SAMPLE_INTERVAL_SECONDS;
                _kcp.Sample(Transport.active);
            }

            if (!_started)
            {
                _started    = true;
                _nextEmitAt = nowSeconds + EMIT_INTERVAL_SECONDS;
                return;
            }

            if (nowSeconds < _nextEmitAt)
            {
                return;
            }

            _nextEmitAt = nowSeconds + EMIT_INTERVAL_SECONDS;
            ServerJsonLogHandler.LogEvent(ServerLogLine.LEVEL_INFO, ServerLogLine.EVENT_SERVER_METRICS, null, null,
                BuildData());
            ResetWindow();
            _frameMaxPrevMs = _frameMaxCurMs;
            _frameMaxCurMs  = 0.0;
        }

        /// <summary> 직전 창의 분위수, 상한 칸은 칸 시작값으로 센다. 표본이 없으면 음수 </summary>
        public double GetPercentileMs(double percentile)
        {
            if (_sampleCount == 0)
            {
                return NO_VALUE;
            }

            // 가장 가까운 순위 방식. 칸 상한(다음 칸 시작)으로 보고해 실제보다 낮게 보이지 않게 한다
            int rank       = Math.Max(1, (int)Math.Ceiling(percentile * _sampleCount));
            int cumulative = 0;
            for (int i = 0; i < BUCKET_COUNT; ++i)
            {
                cumulative += _buckets[i];
                if (cumulative >= rank)
                {
                    return (i == (BUCKET_COUNT - 1)) ? _maxMs : Math.Min(i + 1, _maxMs);
                }
            }

            return _maxMs;
        }

        public void ResetWindow()
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            _sampleCount = 0;
            _maxMs       = 0.0;
        }

        private string BuildData()
        {
            StringBuilder b = _builder;
            b.Clear();
            b.Append("{\"metrics\":{");
            AppendNumberOrNull(b, "server_tick_sample_count", _sampleCount, true);
            AppendPercentile(b, "server_tick_duration_p50_ms", P50);
            AppendPercentile(b, "server_tick_duration_p95_ms", P95);
            AppendPercentile(b, "server_tick_duration_p99_ms", P99);
            AppendDouble(b, "server_tick_duration_max_ms", (_sampleCount > 0) ? _maxMs : NO_VALUE);
            AppendNumberOrNull(b, "server_tick_overrun_total", _overrunTotal, true);
            AppendNumberOrNull(b, "process_managed_heap_bytes", GC.GetTotalMemory(false), true);

            SessionEventQueue events = _host?.Session?.Events;
            bool hasQueue = events != null;
            AppendNumberOrNull(b, "storage_queue_depth", hasQueue ? events.QueuedCount : 0, hasQueue);
            AppendDouble(b, "storage_queue_oldest_age_seconds", hasQueue ? events.OldestAgeSeconds : NO_VALUE);
            AppendNumberOrNull(b, "storage_retry_total", hasQueue ? events.RetryCount : 0, hasQueue);
            AppendDouble(b, "server_frame_interval_max_ms", (_frameCount > 0) ? FrameMaxMs : NO_VALUE);
            AppendNumberOrNull(b, "server_frame_overrun_total", _frameOverrun, true);
            AppendDouble(b, "server_frame_stall_max_1s_ms", _stallLastMs);
            AppendNumberOrNull(b, "kcp_send_queue_total", _kcp.SendQueueTotal, _kcp.IsAvailable);
            AppendNumberOrNull(b, "kcp_connection_backlog_max", _kcp.BacklogMax, _kcp.IsAvailable);
            AppendNumberOrNull(b, "kcp_send_queue_warn_total", _kcp.WarnTotal, _kcp.IsAvailable);
            AppendNumberOrNull(b, "log_dropped_total", ServerJsonLogHandler.InstalledDroppedCount, true);
            AppendNumberOrNull(b, "server_active_connections", CountAuthenticatedConnections(), true);

            // 마지막 쉼표를 지운다
            b.Length -= 1;
            b.Append("}}");
            return b.ToString();
        }

        // 인증을 마친 연결만 센다. 인증 대기 중인 연결은 아직 참가자가 아니다
        public static int CountAuthenticatedConnections()
        {
            if (!NetworkServer.active)
            {
                return 0;
            }

            int count = 0;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((connection != null) && (connection.isAuthenticated))
                {
                    ++count;
                }
            }

            return count;
        }

        private void AppendPercentile(StringBuilder b, string name, double percentile)
        {
            AppendDouble(b, name, GetPercentileMs(percentile));
        }

        // 음수는 값이 없다는 뜻이라 null로 쓴다. 표본이 0이면 분위수를 null로 남기는 규약을 따른다
        private static void AppendDouble(StringBuilder b, string name, double value)
        {
            b.Append('"').Append(name).Append("\":");
            if ((value < 0.0) || (double.IsNaN(value)) || (double.IsInfinity(value)))
            {
                b.Append("null,");
                return;
            }

            b.Append(value.ToString(DECIMAL_FORMAT, CultureInfo.InvariantCulture)).Append(',');
        }

        private static void AppendNumberOrNull(StringBuilder b, string name, long value, bool hasValue)
        {
            b.Append('"').Append(name).Append("\":");
            if (!hasValue)
            {
                b.Append("null,");
                return;
            }

            b.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        }
    }
}
