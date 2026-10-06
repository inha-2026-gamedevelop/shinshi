// System
using System;

namespace Shinshi.AI
{
    // 돌진 한 번의 거리와 폭과 피해와 방어 가능 여부는 패턴 정의에서 읽고, 이 값은 연속 돌진의 횟수와 시간만 가진다.
    // 횟수는 강화 구간(체력 기준)이냐에 따라 둘 중 하나다. 강화 구간의 기준은 보스 정의가 정하며 여기에 복사해 두지 않는다
    /// <summary> 무철 연속 돌진의 돌진 횟수와 돌진 한 번의 시간과 돌진 사이 재조준 시간, 만든 뒤에는 바뀌지 않는다 </summary>
    public sealed class MucheolMultiDashSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int   _dashCount;
        private readonly int   _enhancedDashCount;
        private readonly float _dashSeconds;
        private readonly float _reaimSeconds;
        private readonly float _reaimTurnRate;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 강화 구간이 아닐 때의 돌진 횟수 </summary>
        public int DashCount => _dashCount;

        /// <summary> 강화 구간의 돌진 횟수, 강화가 없는 패턴은 DashCount와 같다 </summary>
        public int EnhancedDashCount => _enhancedDashCount;

        /// <summary> 초, 돌진 한 번이 정의의 거리를 가는 시간 </summary>
        public float DashSeconds => _dashSeconds;

        /// <summary> 초, 돌진이 끝나고 다음 돌진이 시작하기까지 </summary>
        public float ReaimSeconds => _reaimSeconds;

        /// <summary> 도/초, 돌진 사이에 대상 쪽으로 돌 수 있는 속도, 0이면 재조준하지 않는다 </summary>
        public float ReaimTurnRate => _reaimTurnRate;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolMultiDashSpec(int dashCount, int enhancedDashCount, float dashSeconds, float reaimSeconds, float reaimTurnRate)
        {
            if (dashCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(dashCount), "돌진 횟수는 1 이상이다");
            }

            if (enhancedDashCount < dashCount)
            {
                throw new ArgumentOutOfRangeException(nameof(enhancedDashCount), "강화 구간의 돌진 횟수는 기본 횟수 이상이다");
            }

            if ((!(dashSeconds > 0f)) || float.IsInfinity(dashSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(dashSeconds), "돌진 시간은 0보다 큰 유한값이다");
            }

            if ((!(reaimSeconds >= 0f)) || float.IsInfinity(reaimSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(reaimSeconds), "재조준 시간은 0 이상의 유한값이다");
            }

            if ((!(reaimTurnRate >= 0f)) || float.IsInfinity(reaimTurnRate))
            {
                throw new ArgumentOutOfRangeException(nameof(reaimTurnRate), "재조준 회전 속도는 0 이상의 유한값이다");
            }

            _dashCount         = dashCount;
            _enhancedDashCount = enhancedDashCount;
            _dashSeconds       = dashSeconds;
            _reaimSeconds      = reaimSeconds;
            _reaimTurnRate     = reaimTurnRate;
        }

        /// <summary> 강화 구간 여부에 따른 돌진 횟수 </summary>
        public int GetDashCount(bool isEnhanced)
        {
            return isEnhanced ? _enhancedDashCount : _dashCount;
        }

        /// <summary> 돌진 횟수만큼 이어 달리는 데 걸리는 실행 시간(초), 첫 돌진 시작부터 마지막 돌진 끝까지다 </summary>
        public float GetExecuteSeconds(int dashCount)
        {
            return (dashCount * _dashSeconds) + ((dashCount - 1) * _reaimSeconds);
        }
    }
}
