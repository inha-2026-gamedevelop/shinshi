// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 활성 값만 담는다. 클라이언트는 피해와 대상 조회와 위치 추적을 하지 않고 이 값으로 장판만 그린다
    /// <summary> 장판 한 개의 활성 입력, 시전 번호가 장판을 가리키는 키다 </summary>
    public readonly struct BossAreaStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _areaId;
        private readonly int     _castId;
        private readonly Vector3 _center;
        private readonly float   _radius;
        private readonly float   _durationSeconds;
        private readonly float   _tickInterval;
        private readonly double  _activateServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public int BossId => _bossId;

        /// <summary> 장판 한 개의 번호, 서버가 발급한 장판 번호다. 같은 번호의 예고 표현과 이어지지만 예고가 끝나도 장판은 따로 남는다 </summary>
        public int AreaId => _areaId;

        /// <summary> 이 장판이 속한 시전 번호, 한 시전이 장판을 여러 개 설치해도 각 장판은 장판 번호로 구분하므로 묶음 표시에만 쓴다. 단일 장판은 장판 번호와 같다 </summary>
        public int CastId => _castId;

        /// <summary> 예고 시작 순간 서버가 고정한 중심 </summary>
        public Vector3 Center => _center;

        /// <summary> m </summary>
        public float Radius => _radius;

        /// <summary> 활성 뒤 유지 시간(초) </summary>
        public float DurationSeconds => _durationSeconds;

        /// <summary> 타격 간격(초), 표현이 주기를 알리는 데 쓰며 타격 자체는 서버 알림으로만 표시한다 </summary>
        public float TickInterval => _tickInterval;

        /// <summary> 활성된 서버 시각 </summary>
        public double ActivateServerTime => _activateServerTime;

        /// <summary> 소멸 서버 시각, 활성 시각에 지속 시간을 더한 값이다 </summary>
        public double EndServerTime => _activateServerTime + _durationSeconds;

        /****************************************
        *                Methods
        ****************************************/

        // 시전 번호를 따로 주지 않으면 장판 번호가 시전 번호다(단일 장판)
        public BossAreaStart(int bossId, int areaId, Vector3 center, float radius, float durationSeconds, float tickInterval,
            double activateServerTime)
            : this(bossId, areaId, areaId, center, radius, durationSeconds, tickInterval, activateServerTime)
        {
        }

        public BossAreaStart(int bossId, int areaId, int castId, Vector3 center, float radius, float durationSeconds,
            float tickInterval, double activateServerTime)
        {
            _bossId             = bossId;
            _areaId             = areaId;
            _castId             = castId;
            _center             = center;
            _radius             = radius;
            _durationSeconds    = durationSeconds;
            _tickInterval       = tickInterval;
            _activateServerTime = activateServerTime;
        }
    }
}
