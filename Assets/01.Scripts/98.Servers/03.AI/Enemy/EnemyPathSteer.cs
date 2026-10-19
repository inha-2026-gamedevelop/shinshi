// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 매 틱 경로를 새로 계산하지 않도록 다음 꺾임 지점을 잠깐 들고 있는다. 지점에 닿거나 주기가 지나면 다시 묻는다
    /// <summary> 개체마다 두뇌가 들고 있는 경로 조향 기억 </summary>
    public sealed class EnemyPathSteer
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float REPATH_SECONDS = 0.25f;
        private const float REACHED_RADIUS = 0.3f; // m

        /****************************************
        *                Fields
        ****************************************/

        private Vector3 _point;
        private bool    _has;
        private double  _nextRepath = double.NegativeInfinity;

        /****************************************
        *                Methods
        ****************************************/

        public void Reset()
        {
            _has        = false;
            _nextRepath = double.NegativeInfinity;
        }

        /// <summary> from에서 to로 향할 다음 지점, 경로가 없으면 거짓 </summary>
        public bool TryGetPoint(IEnemyNavigation navigation, Vector3 from, Vector3 to, double serverTime, out Vector3 point)
        {
            if (_has && (serverTime < _nextRepath))
            {
                float x = _point.x - from.x;
                float z = _point.z - from.z;
                if (((x * x) + (z * z)) > (REACHED_RADIUS * REACHED_RADIUS))
                {
                    point = _point;
                    return true;
                }
            }

            _nextRepath = serverTime + REPATH_SECONDS;
            _has        = navigation.TryGetSteerPoint(from, to, out _point);
            point       = _point;
            return _has;
        }
    }
}
