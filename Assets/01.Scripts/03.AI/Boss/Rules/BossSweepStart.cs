// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 값만 담는다. 클라이언트는 좌우 추첨, 타깃 추적, 판정을 하지 않고 이 값으로 여우불의 이동만 그린다
    // 구역 4개의 모양과 폭발은 구역마다 따로 오는 부채꼴 예고와 서버 폭발 확정이 그린다. 이 값은 공격 방향을 미리 알리는 여우불 하나의 이동만 정한다
    /// <summary> 반월 연쇄 폭발의 여우불 이동 입력, 고정한 중심과 기준 방향에서 선택한 쪽으로 반원을 따라 이동한다 </summary>
    public readonly struct BossSweepStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _castId;
        private readonly Vector3 _origin;
        private readonly Vector3 _baseDirection;
        private readonly bool    _isRight;
        private readonly float   _radius;
        private readonly float   _totalArcAngle;
        private readonly float   _telegraphSeconds;
        private readonly float   _blastSpanSeconds;
        private readonly double  _startServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public int BossId => _bossId;

        /// <summary> 서버의 시전 번호, 구역 예고들이 묶음 번호로 쓰는 값과 같다 </summary>
        public int CastId => _castId;

        /// <summary> 서버가 고정한 공격 중심 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 서버가 고정한 기준 방향, 수평 단위 벡터 </summary>
        public Vector3 BaseDirection => _baseDirection;

        /// <summary> 서버가 추첨한 쪽, 참이면 오른쪽(위에서 볼 때 시계 방향)이다 </summary>
        public bool IsRight => _isRight;

        /// <summary> 구역 부채꼴의 반경(m) </summary>
        public float Radius => _radius;

        /// <summary> 선택한 쪽 반원의 전체 각도 </summary>
        public float TotalArcAngle => _totalArcAngle;

        /// <summary> 예고 길이(초), 첫 구역이 폭발하는 때까지다 </summary>
        public float TelegraphSeconds => _telegraphSeconds;

        /// <summary> 첫 구역 폭발부터 마지막 구역 폭발까지의 길이(초) </summary>
        public float BlastSpanSeconds => _blastSpanSeconds;

        /// <summary> 시작된 서버 시각 </summary>
        public double StartServerTime => _startServerTime;

        /// <summary> 마지막 구역이 폭발하는 서버 시각 </summary>
        public double EndServerTime => _startServerTime + _telegraphSeconds + _blastSpanSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossSweepStart(int bossId, int castId, Vector3 origin, Vector3 baseDirection, bool isRight, float radius,
            float totalArcAngle, float telegraphSeconds, float blastSpanSeconds, double startServerTime)
        {
            _bossId           = bossId;
            _castId           = castId;
            _origin           = origin;
            _baseDirection    = baseDirection;
            _isRight          = isRight;
            _radius           = radius;
            _totalArcAngle    = totalArcAngle;
            _telegraphSeconds = telegraphSeconds;
            _blastSpanSeconds = blastSpanSeconds;
            _startServerTime  = startServerTime;
        }
    }
}
