// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 배치(BossChainFanLayout)의 값을 풀어 실었다. 피난 구역은 SafeDirection 가운데로 360 - ZoneCount x ZoneArc도이고,
    // 구역 k의 가운데 방향은 SafeDirection을 오른쪽으로 (피난 각도 / 2) + ZoneArc x (k + 0.5)도 돌린 방향이다.
    // 구역 k의 폭발 예정 시각은 CastStartServerTime + FirstBlastSeconds + k x BlastIntervalSeconds다
    /// <summary> 하드 2페이즈 P03 잔류 부채꼴 연쇄 한 시전의 입력 </summary>
    public readonly struct BossChainFanStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _castId;
        private readonly Vector3 _origin;
        private readonly Vector3 _safeDirection;
        private readonly float   _range;
        private readonly int     _zoneCount;
        private readonly float   _zoneArc;
        private readonly double  _castStartServerTime;
        private readonly float   _safeRevealSeconds;
        private readonly float   _firstBlastSeconds;
        private readonly float   _blastIntervalSeconds;
        private readonly float   _lingerSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 시전 시작 순간의 보스 위치, 모든 구역의 중심 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 피난 방향 가운데 </summary>
        public Vector3 SafeDirection => _safeDirection;

        /// <summary> m, 판정 사거리 </summary>
        public float Range => _range;

        public int ZoneCount => _zoneCount;

        /// <summary> 구역 하나의 각도(도) </summary>
        public float ZoneArc => _zoneArc;

        public double CastStartServerTime => _castStartServerTime;

        /// <summary> 이 시간까지는 피난 방향만 보인다 </summary>
        public float SafeRevealSeconds => _safeRevealSeconds;

        public float FirstBlastSeconds => _firstBlastSeconds;

        public float BlastIntervalSeconds => _blastIntervalSeconds;

        /// <summary> 마지막 폭발 뒤 위험이 남는 시간(초) </summary>
        public float LingerSeconds => _lingerSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossChainFanStart(int bossId, int castId, Vector3 origin, Vector3 safeDirection, float range,
            int zoneCount, float zoneArc, double castStartServerTime, float safeRevealSeconds, float firstBlastSeconds,
            float blastIntervalSeconds, float lingerSeconds)
        {
            _bossId               = bossId;
            _castId               = castId;
            _origin               = origin;
            _safeDirection        = safeDirection;
            _range                = range;
            _zoneCount            = zoneCount;
            _zoneArc              = zoneArc;
            _castStartServerTime  = castStartServerTime;
            _safeRevealSeconds    = safeRevealSeconds;
            _firstBlastSeconds    = firstBlastSeconds;
            _blastIntervalSeconds = blastIntervalSeconds;
            _lingerSeconds        = lingerSeconds;
        }
    }
}
