// System
using System.Collections.Generic;
using System.Diagnostics;

namespace Shinshi.Launcher.Presentation
{
    /// <summary> 최근 몇 초 동안 늘어난 바이트로 전송 속도를 잰다. 순간값보다 덜 흔들린다 </summary>
    public sealed class TransferSpeedMeter
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double WINDOW_SECONDS   = 3.0;
        private const double MIN_SPAN_SECONDS = 0.5;

        private readonly Queue<(double Seconds, long Bytes)> _samples = new Queue<(double Seconds, long Bytes)>();
        private readonly Stopwatch                           _clock   = Stopwatch.StartNew();

        private long _lastBytes;

        /****************************************
        *              Properties
        ****************************************/

        public double BytesPerSecond { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public void Reset()
        {
            _samples.Clear();
            _lastBytes     = 0;
            BytesPerSecond = 0.0;
        }

        public void Add(long completedBytes)
        {
            // 엔진은 단계마다 바이트를 0부터 다시 센다. 줄어들면 새 단계로 보고 처음부터 잰다
            if (completedBytes < _lastBytes)
            {
                Reset();
            }

            double now = _clock.Elapsed.TotalSeconds;
            _lastBytes = completedBytes;
            _samples.Enqueue((now, completedBytes));
            while ((_samples.Count > 1) && ((now - _samples.Peek().Seconds) > WINDOW_SECONDS))
            {
                _samples.Dequeue();
            }

            (double Seconds, long Bytes) oldest = _samples.Peek();
            double span = now - oldest.Seconds;
            if (span < MIN_SPAN_SECONDS)
            {
                return;
            }

            BytesPerSecond = (completedBytes - oldest.Bytes) / span;
        }
    }
}
