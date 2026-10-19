// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 서버가 확정한 예고 한 건이다. 표현은 이 값과 서버 시각만으로 진행도를 세고 판정은 다시 계산하지 않는다
    /// <summary> 일반 적 공격 예고 한 건의 모양, 위치, 방향과 시작, 끝 서버 시각 </summary>
    public readonly struct EnemyTelegraphCue
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ushort            _sequence;
        private readonly EEnemyAttackShape _shape;
        private readonly Vector3           _origin;
        private readonly Vector3           _direction;
        private readonly float             _range;
        private readonly float             _width;
        private readonly double            _startServerTime;
        private readonly double            _endServerTime;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버가 예고마다 올리는 번호, 같은 번호는 한 번만 시작한다 </summary>
        public ushort Sequence => _sequence;

        public EEnemyAttackShape Shape => _shape;

        /// <summary> 부채꼴, 원형은 공격자 위치, 돌진, 직선, 투사체는 시작점, 장판은 중심 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 수평 단위 방향 </summary>
        public Vector3 Direction => _direction;

        /// <summary> 부채꼴 반경, 원형 반경, 돌진과 직선과 투사체의 길이 </summary>
        public float Range => _range;

        /// <summary> 부채꼴은 각도(도), 그 밖의 모양은 폭(m) </summary>
        public float Width => _width;

        public double StartServerTime => _startServerTime;
        public double EndServerTime   => _endServerTime;

        /****************************************
        *                Methods
        ****************************************/

        public EnemyTelegraphCue(ushort sequence, EEnemyAttackShape shape, Vector3 origin, Vector3 direction, float range, float width,
            double startServerTime, double endServerTime)
        {
            _sequence        = sequence;
            _shape           = shape;
            _origin          = origin;
            _direction       = direction;
            _range           = range;
            _width           = width;
            _startServerTime = startServerTime;
            _endServerTime   = endServerTime;
        }

        /// <summary> 예고 진행도 0에서 1, 길이가 0 이하면 시작 이후 1이다 </summary>
        public float Progress(double serverNow)
        {
            double length = _endServerTime - _startServerTime;
            if (!(length > 0.0))
            {
                return (serverNow >= _startServerTime) ? 1f : 0f;
            }

            return Mathf.Clamp01((float)((serverNow - _startServerTime) / length));
        }
    }
}
