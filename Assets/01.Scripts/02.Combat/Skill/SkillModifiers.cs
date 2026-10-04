namespace Shinshi.Combat
{
    /// <summary> 한 시전에서 고정하는 부적 수정값, 원래 기술 수치에 곱하거나 더하는 값과 핵심 변형이다 </summary>
    public readonly struct SkillModifiers
    {
        // 값은 TalismanModifierRules가 원본 -> 핵심 변형 -> 보조 -> 레벨 보너스 순서로 한 번 계산한다. 실제 판정 범위는
        // TalismanModifierRules의 Get 메서드로 읽어 서버 판정과 클라이언트 범위 표시가 같은 값을 쓰게 한다.
        // 비율은 0.1이 10%다. 배율(Scale)은 1이 원래 값이고 차이(Delta)는 원래 값에 더한다

        /****************************************
        *                Fields
        ****************************************/

        private readonly float            _energyScale;
        private readonly float            _cooldownScale;
        private readonly float            _damageScale;
        private readonly float            _resonanceBonus;
        private readonly float            _radiusDelta;
        private readonly float            _fixedRadius;
        private readonly float            _arcAngleDelta;
        private readonly float            _fixedArcAngle;
        private readonly float            _rangeDelta;
        private readonly float            _moveDistanceDelta;
        private readonly float            _durationDelta;
        private readonly float            _guardDurationDelta;
        private readonly float            _attackIntervalScale;
        private readonly float            _markedDamageBonus;
        private readonly SkillCoreVariant _core;

        /****************************************
        *              Properties
        ****************************************/

        public float EnergyScale    => _energyScale;
        public float CooldownScale  => _cooldownScale;

        // 해당 기술 타격 계수에 곱한다. 핵심 변형의 후속 타격, 충격파, 폭발에도 곱한다
        public float DamageScale    => _damageScale;

        // 최초 적중 공명에 더한다, 시전당 상한은 공명 원장이 자른다
        public float ResonanceBonus => _resonanceBonus;

        // m, 원 반경이나 부채꼴 길이에 더한다. 원형 변형의 넓은, 집중 검세도 여기로 들어온다
        public float RadiusDelta    => _radiusDelta;

        // m, 0보다 크면 원래 반경 대신 쓴다
        public float FixedRadius    => _fixedRadius;

        // 도, 부채꼴 각도에 더한다
        public float ArcAngleDelta  => _arcAngleDelta;

        // 도, 0보다 크면 원래 각도 대신 쓴다
        public float FixedArcAngle  => _fixedArcAngle;

        // m, 투사체 사거리나 동료 공격 사거리에 더한다
        public float RangeDelta     => _rangeDelta;

        // m, 돌진 거리에 더한다
        public float MoveDistanceDelta => _moveDistanceDelta;

        // 초, 소환이나 결계 유지 시간에 더한다
        public float DurationDelta  => _durationDelta;

        // 초, 반격 전방 방어 창에 더한다
        public float GuardDurationDelta => _guardDurationDelta;

        // 동료 공격 간격 배율
        public float AttackIntervalScale => _attackIntervalScale;

        // 표적 결계 안 적을 칠 때 동료 피해 보너스에 더하는 비율 포인트
        public float MarkedDamageBonus => _markedDamageBonus;

        public SkillCoreVariant Core => _core;

        public static SkillModifiers Identity => new SkillModifiers(1f, 1f, 1f, 0f);

        /****************************************
        *                Methods
        ****************************************/

        public SkillModifiers(float energyScale, float cooldownScale, float damageScale, float resonanceBonus)
            : this(energyScale, cooldownScale, damageScale, resonanceBonus, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, default)
        {
        }

        public SkillModifiers(float energyScale, float cooldownScale, float damageScale, float resonanceBonus,
            float radiusDelta, float fixedRadius, float arcAngleDelta, float fixedArcAngle, float rangeDelta,
            float moveDistanceDelta, float durationDelta, float guardDurationDelta, float attackIntervalScale,
            float markedDamageBonus, in SkillCoreVariant core)
        {
            _energyScale         = energyScale;
            _cooldownScale       = cooldownScale;
            _damageScale         = damageScale;
            _resonanceBonus      = resonanceBonus;
            _radiusDelta         = radiusDelta;
            _fixedRadius         = fixedRadius;
            _arcAngleDelta       = arcAngleDelta;
            _fixedArcAngle       = fixedArcAngle;
            _rangeDelta          = rangeDelta;
            _moveDistanceDelta   = moveDistanceDelta;
            _durationDelta       = durationDelta;
            _guardDurationDelta  = guardDurationDelta;
            _attackIntervalScale = attackIntervalScale;
            _markedDamageBonus   = markedDamageBonus;
            _core                = core;
        }
    }
}
