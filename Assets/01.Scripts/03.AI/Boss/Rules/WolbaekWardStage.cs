// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 새 지점의 결계 위치는 이 값으로 처음 알려진다. 위치로 향하는 화살표나 사전 방향 안내는 만들지 않는다
    /// <summary> 색 결계 한 지점의 입력, 두 색 결계의 위치와 탐색 시간과 광역 공격 시각 </summary>
    public readonly struct WolbaekWardStage
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 0부터 오르는 지점 번호 </summary>
        public int Index { get; }

        /// <summary> 결계가 나타나 탐색 시간이 시작된 서버 시각 </summary>
        public double StartServerTime { get; }

        /// <summary> 첫 광역 공격이 판정되는 서버 시각, 탐색 시간이 끝나는 시각이다 </summary>
        public double FirstStrikeServerTime { get; }

        /// <summary> 광역 공격 사이의 간격 </summary>
        public float StrikeIntervalSeconds { get; }

        /// <summary> 이 지점의 광역 공격 횟수 </summary>
        public int StrikeCount { get; }

        public Vector3 RedCenter { get; }

        public Vector3 BlueCenter { get; }

        /// <summary> m, 결계의 보호 반경 </summary>
        public float Radius { get; }

        /// <summary> 마지막 광역 공격이 판정되는 서버 시각, 이 시각에 결계가 사라진다 </summary>
        public double LastStrikeServerTime => FirstStrikeServerTime + ((StrikeCount - 1) * (double)StrikeIntervalSeconds);

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekWardStage(int index, double startServerTime, double firstStrikeServerTime, float strikeIntervalSeconds,
            int strikeCount, Vector3 redCenter, Vector3 blueCenter, float radius)
        {
            Index                 = index;
            StartServerTime       = startServerTime;
            FirstStrikeServerTime = firstStrikeServerTime;
            StrikeIntervalSeconds = strikeIntervalSeconds;
            StrikeCount           = strikeCount;
            RedCenter             = redCenter;
            BlueCenter            = blueCenter;
            Radius                = radius;
        }

        /// <summary> 색에 맞는 결계 중심 </summary>
        public Vector3 GetCenter(EWolbaekWardColor color)
        {
            return (color == EWolbaekWardColor.Red) ? RedCenter : BlueCenter;
        }
    }
}
