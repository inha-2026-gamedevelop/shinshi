namespace Shinshi.AI
{
    // PatternDefinition에는 타별 형태와 피해가 없어 순차 타격의 한 타를 이 값으로 받는다. 수치는 호출하는 쪽(데이터)이 정한다
    /// <summary> 순차 타격의 타 한 번, 형태와 크기와 방향 오프셋과 피해 배수와 실행 시작 기준 지연 </summary>
    public readonly struct MucheolSequenceStep
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EMucheolSequenceShape _shape;
        private readonly float                 _range;
        private readonly float                 _arcAngle;
        private readonly float                 _width;
        private readonly float                 _aimOffsetDegrees;
        private readonly float                 _damageRef;
        private readonly float                 _delaySeconds;

        /****************************************
        *              Properties
        ****************************************/

        public EMucheolSequenceShape Shape => _shape;

        /// <summary> m, 부채꼴 사거리, 직사각형 길이, 원형 반경 </summary>
        public float Range => _range;

        /// <summary> 도, 부채꼴 전체 각도, 다른 형태는 0 </summary>
        public float ArcAngle => _arcAngle;

        /// <summary> m, 직사각형 폭, 다른 형태는 0 </summary>
        public float Width => _width;

        /// <summary> 도, 조준 방향에서 돌린 각도, 양수는 오른쪽(위에서 볼 때 시계 방향)이다. 원형은 쓰지 않는다 </summary>
        public float AimOffsetDegrees => _aimOffsetDegrees;

        /// <summary> 기준 체력(Href)의 배수 </summary>
        public float DamageRef => _damageRef;

        /// <summary> 초, 실행 시작으로부터 이 타까지 </summary>
        public float DelaySeconds => _delaySeconds;

        /****************************************
        *                Methods
        ****************************************/

        private MucheolSequenceStep(EMucheolSequenceShape shape, float range, float arcAngle, float width, float aimOffsetDegrees,
            float damageRef, float delaySeconds)
        {
            _shape            = shape;
            _range            = range;
            _arcAngle         = arcAngle;
            _width            = width;
            _aimOffsetDegrees = aimOffsetDegrees;
            _damageRef        = damageRef;
            _delaySeconds     = delaySeconds;
        }

        public static MucheolSequenceStep Fan(float range, float arcAngle, float aimOffsetDegrees, float damageRef, float delaySeconds)
        {
            return new MucheolSequenceStep(EMucheolSequenceShape.Fan, range, arcAngle, 0f, aimOffsetDegrees, damageRef, delaySeconds);
        }

        public static MucheolSequenceStep Line(float length, float width, float aimOffsetDegrees, float damageRef, float delaySeconds)
        {
            return new MucheolSequenceStep(EMucheolSequenceShape.Line, length, 0f, width, aimOffsetDegrees, damageRef, delaySeconds);
        }

        public static MucheolSequenceStep Circle(float radius, float damageRef, float delaySeconds)
        {
            return new MucheolSequenceStep(EMucheolSequenceShape.Circle, radius, 0f, 0f, 0f, damageRef, delaySeconds);
        }
    }
}
