// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 돌진 횟수는 시전을 만들 때 정해져 시전 중 바뀌지 않는다. 둘째 돌진부터는 재조준 사건(BossMultiDashReaim)이 위치와 방향을 새로 준다
    /// <summary> 연속 돌진 한 시전의 첫 돌진 예고 입력 </summary>
    public readonly struct BossMultiDashStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _castId;
        private readonly string  _patternId;
        private readonly Vector3 _origin;
        private readonly Vector3 _direction;
        private readonly float   _length;
        private readonly float   _width;
        private readonly double  _startServerTime;
        private readonly float   _telegraphSeconds;
        private readonly int     _dashCount;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 패턴 정의 ID </summary>
        public string PatternId => _patternId;

        /// <summary> 첫 돌진의 시작 위치 </summary>
        public Vector3 Origin => _origin;

        public Vector3 Direction => _direction;

        /// <summary> m, 몸체가 벽 앞에서 실제로 갈 수 있는 거리 </summary>
        public float Length => _length;

        /// <summary> m </summary>
        public float Width => _width;

        /// <summary> 예고 시작 서버 시각 </summary>
        public double StartServerTime => _startServerTime;

        public float TelegraphSeconds => _telegraphSeconds;

        /// <summary> 이 시전이 달릴 돌진 횟수 </summary>
        public int DashCount => _dashCount;

        /****************************************
        *                Methods
        ****************************************/

        public BossMultiDashStart(int bossId, int castId, string patternId, Vector3 origin, Vector3 direction,
            float length, float width, double startServerTime, float telegraphSeconds, int dashCount)
        {
            _bossId           = bossId;
            _castId           = castId;
            _patternId        = patternId;
            _origin           = origin;
            _direction        = direction;
            _length           = length;
            _width            = width;
            _startServerTime  = startServerTime;
            _telegraphSeconds = telegraphSeconds;
            _dashCount        = dashCount;
        }
    }
}
