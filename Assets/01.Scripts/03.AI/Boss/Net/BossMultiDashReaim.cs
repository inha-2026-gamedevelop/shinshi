// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 앞 돌진이 선 위치에서 다시 조준한다. 재조준이 그 돌진의 예고다
    /// <summary> 연속 돌진의 둘째 돌진부터 쓰는 재조준 입력 </summary>
    public readonly struct BossMultiDashReaim
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _castId;
        private readonly int     _dashIndex;
        private readonly Vector3 _origin;
        private readonly Vector3 _direction;
        private readonly float   _length;
        private readonly float   _width;
        private readonly double  _startServerTime;
        private readonly float   _reaimSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 0부터 세는 돌진 번호, 재조준은 1부터 온다 </summary>
        public int DashIndex => _dashIndex;

        /// <summary> 앞 돌진이 선 위치 </summary>
        public Vector3 Origin => _origin;

        public Vector3 Direction => _direction;

        /// <summary> m </summary>
        public float Length => _length;

        /// <summary> m </summary>
        public float Width => _width;

        /// <summary> 재조준 시작 서버 시각 </summary>
        public double StartServerTime => _startServerTime;

        /// <summary> 재조준 시간(초), 끝나면 돌진이 시작된다 </summary>
        public float ReaimSeconds => _reaimSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossMultiDashReaim(int castId, int dashIndex, Vector3 origin, Vector3 direction, float length,
            float width, double startServerTime, float reaimSeconds)
        {
            _castId          = castId;
            _dashIndex       = dashIndex;
            _origin          = origin;
            _direction       = direction;
            _length          = length;
            _width           = width;
            _startServerTime = startServerTime;
            _reaimSeconds    = reaimSeconds;
        }
    }
}
