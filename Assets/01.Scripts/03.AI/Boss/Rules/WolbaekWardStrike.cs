// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 범위는 전장 전체다. 올바른 색 결계 안에 있는지와 피해와 밀려남은 서버가 판정하고 클라이언트는 예고만 그린다.
    // 결계 위치를 알려 주는 예고가 아니라 공격 준비만 알리는 예고다
    /// <summary> 광역 공격 하나의 예고 입력 </summary>
    public readonly struct WolbaekWardStrike
    {
        /****************************************
        *              Properties
        ****************************************/

        public int StageIndex { get; }

        /// <summary> 지점 안에서 0부터 오르는 공격 번호 </summary>
        public int StrikeIndex { get; }

        /// <summary> 예고를 시작한 서버 시각 </summary>
        public double TelegraphServerTime { get; }

        /// <summary> 판정이 나는 서버 시각 </summary>
        public double StrikeServerTime { get; }

        /// <summary> 밀려남의 기준점, 월백의 위치다 </summary>
        public Vector3 Origin { get; }

        /// <summary> m, 결계 밖에서 맞았을 때 월백 바깥쪽으로 밀리는 거리 </summary>
        public float PushDistance { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekWardStrike(int stageIndex, int strikeIndex, double telegraphServerTime, double strikeServerTime,
            Vector3 origin, float pushDistance)
        {
            StageIndex          = stageIndex;
            StrikeIndex         = strikeIndex;
            TelegraphServerTime = telegraphServerTime;
            StrikeServerTime    = strikeServerTime;
            Origin              = origin;
            PushDistance        = pushDistance;
        }
    }
}
