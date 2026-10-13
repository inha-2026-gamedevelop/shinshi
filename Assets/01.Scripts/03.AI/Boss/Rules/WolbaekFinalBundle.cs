// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 묶음의 빨간 여우불 수는 시작 순간에 정해지고 도중 바뀌지 않는다
    /// <summary> 빨간 여우불 한 묶음의 입력, 시각표와 여우불 목록 </summary>
    public readonly struct WolbaekFinalBundle
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 0부터 오르는 묶음 번호 </summary>
        public int Index { get; }

        /// <summary> 묶음이 나타나 추적이 시작된 서버 시각 </summary>
        public double StartServerTime { get; }

        /// <summary> 낙하 위치가 고정되는 서버 시각 </summary>
        public double LockServerTime { get; }

        /// <summary> 반경 폭발이 판정되는 서버 시각 </summary>
        public double BlastServerTime { get; }

        public IReadOnlyList<WolbaekFinalRed> Reds { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekFinalBundle(int index, double startServerTime, double lockServerTime, double blastServerTime,
            IReadOnlyList<WolbaekFinalRed> reds)
        {
            Index           = index;
            StartServerTime = startServerTime;
            LockServerTime  = lockServerTime;
            BlastServerTime = blastServerTime;
            Reds            = reds;
        }
    }
}
