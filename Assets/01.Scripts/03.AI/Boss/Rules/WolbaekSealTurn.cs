// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 차례의 핵 수와 여우불 수는 시작 순간에 정해지고 차례 도중 바뀌지 않는다
    /// <summary> 한 차례의 입력, 시각표와 핵 목록과 추적 여우불 목록 </summary>
    public readonly struct WolbaekSealTurn
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 0부터 오르는 차례 번호 </summary>
        public int Index { get; }

        /// <summary> 핵과 여우불이 나타난 서버 시각 </summary>
        public double StartServerTime { get; }

        /// <summary> 낙하 위치 추적이 시작되는 서버 시각 </summary>
        public double TrackStartServerTime { get; }

        /// <summary> 낙하 위치가 고정되는 서버 시각 </summary>
        public double LockServerTime { get; }

        /// <summary> 낙하와 핵 파괴가 판정되는 서버 시각 </summary>
        public double ResolveServerTime { get; }

        /// <summary> 다음 차례가 시작되는 서버 시각 </summary>
        public double EndServerTime { get; }

        public IReadOnlyList<WolbaekSealCore> Cores { get; }

        public IReadOnlyList<WolbaekSealFoxfire> Foxfires { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekSealTurn(int index, double startServerTime, double trackStartServerTime, double lockServerTime,
            double resolveServerTime, double endServerTime, IReadOnlyList<WolbaekSealCore> cores,
            IReadOnlyList<WolbaekSealFoxfire> foxfires)
        {
            Index                = index;
            StartServerTime      = startServerTime;
            TrackStartServerTime = trackStartServerTime;
            LockServerTime       = lockServerTime;
            ResolveServerTime    = resolveServerTime;
            EndServerTime        = endServerTime;
            Cores                = cores;
            Foxfires             = foxfires;
        }
    }
}
