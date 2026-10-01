// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Systems
{
    /// <summary> 웨이브 세션의 종료 결과, 정산 요청에 그대로 넘긴다 </summary>
    public readonly struct WaveSessionOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public ESessionEndReason Reason           { get; }
        public int               ClearedWaveCount { get; }
        public int               DefeatedCount    { get; }
        public double            ElapsedSeconds   { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WaveSessionOutcome(ESessionEndReason reason, int clearedWaveCount, int defeatedCount, double elapsedSeconds)
        {
            Reason           = reason;
            ClearedWaveCount = clearedWaveCount;
            DefeatedCount    = defeatedCount;
            ElapsedSeconds   = elapsedSeconds;
        }
    }
}
