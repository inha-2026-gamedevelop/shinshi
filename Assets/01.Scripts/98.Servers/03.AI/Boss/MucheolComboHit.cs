namespace Shinshi.AI
{
    // PatternDefinition에는 타별 형태와 피해가 없어 무철 콤보의 타 하나를 이 값으로 받는다. 수치는 호출하는 쪽(데이터)이 정한다
    /// <summary> 콤보의 타 한 번, 전방 부채꼴의 사거리와 각도와 피해 배수와 실행 시작 기준 지연 </summary>
    public readonly struct MucheolComboHit
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float _range;
        private readonly float _arcAngle;
        private readonly float _damageRef;
        private readonly float _delaySeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> m, 보스 중심 사거리 </summary>
        public float Range => _range;

        /// <summary> 도, 부채꼴 전체 각도 </summary>
        public float ArcAngle => _arcAngle;

        /// <summary> 기준 체력(Href)의 배수 </summary>
        public float DamageRef => _damageRef;

        /// <summary> 초, 실행 시작으로부터 이 타까지 </summary>
        public float DelaySeconds => _delaySeconds;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolComboHit(float range, float arcAngle, float damageRef, float delaySeconds)
        {
            _range        = range;
            _arcAngle     = arcAngle;
            _damageRef    = damageRef;
            _delaySeconds = delaySeconds;
        }
    }
}
