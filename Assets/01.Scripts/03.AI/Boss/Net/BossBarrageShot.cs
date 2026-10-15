// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 방향은 서버가 시전 시작에 뽑아 둔 값이고 예고 시점에 처음 알린다
    /// <summary> 난사의 발 하나의 예고 값 </summary>
    public readonly struct BossBarrageShot
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _castId;
        private readonly int     _shotIndex;
        private readonly int     _sectorIndex;
        private readonly Vector3 _direction;
        private readonly float   _pathLength;
        private readonly double  _scheduledServerTime;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 0부터 세는 발 번호 </summary>
        public int ShotIndex => _shotIndex;

        /// <summary> 발이 나가는 구역 번호 </summary>
        public int SectorIndex => _sectorIndex;

        public Vector3 Direction => _direction;

        /// <summary> m, 벽 앞까지의 경로 길이 </summary>
        public float PathLength => _pathLength;

        /// <summary> 예고 예정 서버 시각 </summary>
        public double ScheduledServerTime => _scheduledServerTime;

        /****************************************
        *                Methods
        ****************************************/

        public BossBarrageShot(int castId, int shotIndex, int sectorIndex, Vector3 direction, float pathLength,
            double scheduledServerTime)
        {
            _castId              = castId;
            _shotIndex           = shotIndex;
            _sectorIndex         = sectorIndex;
            _direction           = direction;
            _pathLength          = pathLength;
            _scheduledServerTime = scheduledServerTime;
        }
    }
}
