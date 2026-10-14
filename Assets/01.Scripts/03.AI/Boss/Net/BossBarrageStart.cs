// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 발 k의 예고 예정 시각은 CastStartServerTime + k x ShotIntervalSeconds, 발사 예정 시각은 거기에 TelegraphSeconds를 더한 값이다
    /// <summary> 하드 2페이즈 P05 전방위 난사 한 시전의 입력 </summary>
    public readonly struct BossBarrageStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _castId;
        private readonly Vector3 _origin;
        private readonly double  _castStartServerTime;
        private readonly int     _shotCount;
        private readonly float   _telegraphSeconds;
        private readonly float   _shotIntervalSeconds;
        private readonly float   _width;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 발사 원점, 시전 시작 순간의 보스 위치 </summary>
        public Vector3 Origin => _origin;

        public double CastStartServerTime => _castStartServerTime;

        public int ShotCount => _shotCount;

        public float TelegraphSeconds => _telegraphSeconds;

        public float ShotIntervalSeconds => _shotIntervalSeconds;

        /// <summary> m, 투사체 폭 </summary>
        public float Width => _width;

        /****************************************
        *                Methods
        ****************************************/

        public BossBarrageStart(int bossId, int castId, Vector3 origin, double castStartServerTime, int shotCount,
            float telegraphSeconds, float shotIntervalSeconds, float width)
        {
            _bossId              = bossId;
            _castId              = castId;
            _origin              = origin;
            _castStartServerTime = castStartServerTime;
            _shotCount           = shotCount;
            _telegraphSeconds    = telegraphSeconds;
            _shotIntervalSeconds = shotIntervalSeconds;
            _width               = width;
        }
    }
}
