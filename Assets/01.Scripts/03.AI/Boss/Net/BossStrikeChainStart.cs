// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 전용 타 값(MucheolComboHit, MucheolSequenceStep)에서 피해 배수를 뺀 표현용 값만 싣는다.
    // 콤보는 모든 타가 Fan이고 폭과 오프셋이 0이다. 타 k의 확정 예정 시각은 StartServerTime + TelegraphSeconds + DelaySeconds[k]다.
    // 배열은 만든 뒤 바꾸지 않는다. 길이가 서로 다르면 직렬화가 거절한다
    /// <summary> 콤보나 순차 타격 한 시전의 예고 입력, 타 목록은 같은 길이의 배열 여섯 개다 </summary>
    public readonly struct BossStrikeChainStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int                   _bossId;
        private readonly int                   _castId;
        private readonly string                _patternId;
        private readonly EBossStrikeChainKind  _kind;
        private readonly Vector3               _origin;
        private readonly Vector3               _direction;
        private readonly double                _startServerTime;
        private readonly float                 _telegraphSeconds;
        private readonly EBossTelegraphShape[] _shapes;
        private readonly float[]               _ranges;
        private readonly float[]               _arcAngles;
        private readonly float[]               _widths;
        private readonly float[]               _aimOffsetDegrees;
        private readonly float[]               _delaySeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 패턴 정의 ID </summary>
        public string PatternId => _patternId;

        public EBossStrikeChainKind Kind => _kind;

        /// <summary> 모든 타의 기준점, 예고 시작 순간의 보스 위치 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 예고 시작 순간의 조준 방향, 모든 타가 공유한다 </summary>
        public Vector3 Direction => _direction;

        /// <summary> 예고 시작 서버 시각 </summary>
        public double StartServerTime => _startServerTime;

        /// <summary> 예고 시간(초), 끝나면 실행이 시작된다 </summary>
        public float TelegraphSeconds => _telegraphSeconds;

        /// <summary> 타별 형태, Fan, Line, Circle만 쓴다 </summary>
        public IReadOnlyList<EBossTelegraphShape> Shapes => _shapes;

        /// <summary> 타별 사거리(m), 부채꼴 사거리, 직사각형 길이, 원형 반경 </summary>
        public IReadOnlyList<float> Ranges => _ranges;

        /// <summary> 타별 부채꼴 전체 각도(도), 다른 형태는 0 </summary>
        public IReadOnlyList<float> ArcAngles => _arcAngles;

        /// <summary> 타별 직사각형 폭(m), 다른 형태는 0 </summary>
        public IReadOnlyList<float> Widths => _widths;

        /// <summary> 타별 조준 방향 오프셋(도), 양수가 오른쪽이다 </summary>
        public IReadOnlyList<float> AimOffsetDegrees => _aimOffsetDegrees;

        /// <summary> 타별 지연(초), 실행 시작부터 그 타까지 </summary>
        public IReadOnlyList<float> DelaySeconds => _delaySeconds;

        /// <summary> 타 수 </summary>
        public int StepCount => (_shapes != null) ? _shapes.Length : 0;

        /****************************************
        *                Methods
        ****************************************/

        public BossStrikeChainStart(int bossId, int castId, string patternId, EBossStrikeChainKind kind, Vector3 origin,
            Vector3 direction, double startServerTime, float telegraphSeconds, EBossTelegraphShape[] shapes,
            float[] ranges, float[] arcAngles, float[] widths, float[] aimOffsetDegrees, float[] delaySeconds)
        {
            _bossId           = bossId;
            _castId           = castId;
            _patternId        = patternId;
            _kind             = kind;
            _origin           = origin;
            _direction        = direction;
            _startServerTime  = startServerTime;
            _telegraphSeconds = telegraphSeconds;
            _shapes           = shapes;
            _ranges           = ranges;
            _arcAngles        = arcAngles;
            _widths           = widths;
            _aimOffsetDegrees = aimOffsetDegrees;
            _delaySeconds     = delaySeconds;
        }
    }
}
