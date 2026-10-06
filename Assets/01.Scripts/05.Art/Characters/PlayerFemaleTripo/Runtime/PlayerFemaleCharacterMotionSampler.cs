// System
using System;

namespace Shinshi.Art
{
    /// <summary> 보정과 분리된 누적 이동 거리를 같은 비배율 시각의 표본 간격으로 나눈다 </summary>
    public sealed class PlayerFemaleCharacterMotionSampler
    {
        /****************************************
        *                Fields
        ****************************************/

        private float  _previousDistance;
        private double _previousTime;
        private bool   _hasBaseline;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 유효한 두 표본으로 속도를 읽는다, 역할 중단이나 유효하지 않은 누적값은 기준을 초기화한다 </summary>
        public bool TryRead(bool isAvailable, float travelledDistance, double unscaledTime, out float speed)
        {
            speed = 0f;
            if ((!isAvailable) || (float.IsNaN(travelledDistance)) || (float.IsInfinity(travelledDistance))
                || (travelledDistance < 0f) || (double.IsNaN(unscaledTime)) || (double.IsInfinity(unscaledTime))
                || (unscaledTime < 0.0))
            {
                ResetMotionSample();
                return false;
            }

            if ((!_hasBaseline) || (travelledDistance < _previousDistance) || (unscaledTime <= _previousTime))
            {
                StoreBaseline(travelledDistance, unscaledTime);
                return false;
            }

            double measured = ((double)travelledDistance - _previousDistance) / (unscaledTime - _previousTime);
            StoreBaseline(travelledDistance, unscaledTime);
            if ((double.IsNaN(measured)) || (double.IsInfinity(measured)) || (measured > float.MaxValue))
            {
                ResetMotionSample();
                return false;
            }

            speed = (float)measured;
            return true;
        }

        /// <summary> 중단된 동안의 이동을 다음 프레임 속도에 포함하지 않는다 </summary>
        public void ResetMotionSample()
        {
            _hasBaseline = false;
        }

        private void StoreBaseline(float travelledDistance, double unscaledTime)
        {
            _previousDistance = travelledDistance;
            _previousTime     = unscaledTime;
            _hasBaseline      = true;
        }
    }
}
