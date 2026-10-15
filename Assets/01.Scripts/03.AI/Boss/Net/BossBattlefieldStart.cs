// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 공격 영역은 Direction 기준 ArcAngle 부채꼴이고 나머지 뒤쪽이 안전지대다. 예고 중 방향이 바뀌지 않는다
    /// <summary> 무철 하드 전장 공격 한 시전의 예고 입력 </summary>
    public readonly struct BossBattlefieldStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _castId;
        private readonly Vector3 _origin;
        private readonly Vector3 _direction;
        private readonly float   _radius;
        private readonly float   _arcAngle;
        private readonly double  _startServerTime;
        private readonly float   _telegraphSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        public Vector3 Origin => _origin;

        public Vector3 Direction => _direction;

        /// <summary> m </summary>
        public float Radius => _radius;

        /// <summary> 공격 부채꼴 전체 각도(도) </summary>
        public float ArcAngle => _arcAngle;

        /// <summary> 예고 시작 서버 시각 </summary>
        public double StartServerTime => _startServerTime;

        public float TelegraphSeconds => _telegraphSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossBattlefieldStart(int bossId, int castId, Vector3 origin, Vector3 direction, float radius,
            float arcAngle, double startServerTime, float telegraphSeconds)
        {
            _bossId           = bossId;
            _castId           = castId;
            _origin           = origin;
            _direction        = direction;
            _radius           = radius;
            _arcAngle         = arcAngle;
            _startServerTime  = startServerTime;
            _telegraphSeconds = telegraphSeconds;
        }
    }
}
