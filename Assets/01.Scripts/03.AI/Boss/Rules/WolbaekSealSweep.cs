// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 하드 전용이다. 범위와 방향 값은 기획이 확정하지 않아 입력 계약(설정)으로만 받고, 부채꼴 모양은 임시 가정이다.
    // 판정과 피해와 밀려남은 서버가 하고 클라이언트는 예고만 그린다
    /// <summary> 넓은 휩쓸기 하나의 예고 입력 </summary>
    public readonly struct WolbaekSealSweep
    {
        /****************************************
        *              Properties
        ****************************************/

        public int TurnIndex { get; }

        /// <summary> 차례 안에서 0부터 오르는 휩쓸기 번호 </summary>
        public int SweepIndex { get; }

        /// <summary> 예고를 시작한 서버 시각 </summary>
        public double TelegraphServerTime { get; }

        /// <summary> 판정이 나는 서버 시각 </summary>
        public double StrikeServerTime { get; }

        /// <summary> 휩쓸기 중심, 예고 시작 시점의 월백 위치다 </summary>
        public Vector3 Origin { get; }

        /// <summary> 수평 단위 방향, 번호가 하나 오를 때마다 반대로 뒤집힌다 </summary>
        public Vector3 Direction { get; }

        /// <summary> m, 중심에서 닿는 거리 </summary>
        public float Range { get; }

        /// <summary> 도, 방향 기준 좌우 각도 </summary>
        public float HalfAngleDegrees { get; }

        /// <summary> m, 맞은 대상이 중심에서 바깥쪽으로 밀리는 거리 </summary>
        public float PushDistance { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekSealSweep(int turnIndex, int sweepIndex, double telegraphServerTime, double strikeServerTime,
            Vector3 origin, Vector3 direction, float range, float halfAngleDegrees, float pushDistance)
        {
            TurnIndex           = turnIndex;
            SweepIndex          = sweepIndex;
            TelegraphServerTime = telegraphServerTime;
            StrikeServerTime    = strikeServerTime;
            Origin              = origin;
            Direction           = direction;
            Range               = range;
            HalfAngleDegrees    = halfAngleDegrees;
            PushDistance        = pushDistance;
        }
    }
}
