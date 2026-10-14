// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // StartServerTime부터 FollowSeconds 동안 대상을 따라가고, 그 뒤 FixedSeconds 동안 고정된 뒤 같은 번호의 장판으로 활성된다
    /// <summary> 하드 2페이즈 P04 추적 장판 하나의 이동 예고 입력 </summary>
    public readonly struct BossTrackingAreaStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _castId;
        private readonly int     _areaId;
        private readonly int     _targetCombatId;
        private readonly Vector3 _center;
        private readonly float   _radius;
        private readonly double  _startServerTime;
        private readonly float   _followSeconds;
        private readonly float   _fixedSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 서버가 발급한 장판 번호, 활성 사건의 장판 번호와 같다 </summary>
        public int AreaId => _areaId;

        /// <summary> 따라가는 대상의 전투 번호 </summary>
        public int TargetCombatId => _targetCombatId;

        /// <summary> 시작 순간의 중심 </summary>
        public Vector3 Center => _center;

        /// <summary> m </summary>
        public float Radius => _radius;

        public double StartServerTime => _startServerTime;

        public float FollowSeconds => _followSeconds;

        public float FixedSeconds => _fixedSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossTrackingAreaStart(int bossId, int castId, int areaId, int targetCombatId, Vector3 center,
            float radius, double startServerTime, float followSeconds, float fixedSeconds)
        {
            _bossId          = bossId;
            _castId          = castId;
            _areaId          = areaId;
            _targetCombatId  = targetCombatId;
            _center          = center;
            _radius          = radius;
            _startServerTime = startServerTime;
            _followSeconds   = followSeconds;
            _fixedSeconds    = fixedSeconds;
        }
    }
}
