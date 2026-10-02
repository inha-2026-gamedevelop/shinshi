// System
using System;

namespace Shinshi.Patch.Client
{
    /// <summary> 한 단계의 완료 바이트를 세고, 너무 잦은 보고를 묶어 IProgress로 넘긴다 </summary>
    internal sealed class PatchProgressCounter
    {
        /****************************************
        *                Fields
        ****************************************/

        // 다운로드 버퍼마다 보고하면 UI 스레드로 보내는 호출이 수십만 번 쌓인다
        private const long REPORT_INTERVAL_BYTES = 1024 * 1024;

        private readonly IProgress<PatchProgress>? _progress;
        private readonly EPatchStage               _stage;
        private readonly long                      _totalBytes;

        private long _completedBytes;
        private long _lastReportedBytes;

        /****************************************
        *              Properties
        ****************************************/

        public long CompletedBytes => _completedBytes;

        /****************************************
        *                Methods
        ****************************************/

        public PatchProgressCounter(IProgress<PatchProgress>? progress, EPatchStage stage, long totalBytes)
        {
            _progress   = progress;
            _stage      = stage;
            _totalBytes = totalBytes;
            _progress?.Report(new PatchProgress(_stage, 0, _totalBytes));
        }

        public void Add(long bytes)
        {
            _completedBytes += bytes;
            if ((_completedBytes - _lastReportedBytes) >= REPORT_INTERVAL_BYTES)
            {
                Flush();
            }
        }

        // 재시도로 버린 바이트를 되돌린다
        public void Subtract(long bytes)
        {
            _completedBytes  -= bytes;
            _lastReportedBytes = Math.Min(_lastReportedBytes, _completedBytes);
        }

        public void Flush()
        {
            _lastReportedBytes = _completedBytes;
            _progress?.Report(new PatchProgress(_stage, Math.Min(_completedBytes, _totalBytes), _totalBytes));
        }
    }
}
