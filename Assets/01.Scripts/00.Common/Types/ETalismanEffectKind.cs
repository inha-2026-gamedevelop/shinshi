namespace Shinshi.Common
{
    /// <summary> 부적 효과가 기술 명세를 바꾸는 방식, FollowUpStrike부터는 핵심 변형이다 </summary>
    public enum ETalismanEffectKind
    {
        // 보조 효과
        CooldownReduction       = 0,
        EnergyReduction         = 1,
        // 부채꼴 각도 증가, 원형 변형과 조합하면 반경 증가
        WidenArc                = 2,
        // 부채꼴 각도 고정과 피해 증가, 원형 변형과 조합하면 반경 감소와 피해 증가
        FocusArc                = 3,
        RadiusIncrease          = 4,
        // 반경 고정과 피해 증가
        FocusRadius             = 5,
        RangeIncrease           = 6,
        MoveDistanceIncrease    = 7,
        DamageIncrease          = 8,
        GuardDurationIncrease   = 9,
        // 소환, 결계 유지 시간 증가
        DurationIncrease        = 10,
        AttackIntervalReduction = 11,
        // 표적 결계 안의 적을 칠 때만 피해 증가
        MarkedTargetDamage      = 12,
        ResonanceGainIncrease   = 13,

        // 핵심 변형
        // 같은 동작 안에서 짧은 간격의 후속 타격 추가
        FollowUpStrike          = 14,
        // 판정을 시전자 중심 원형으로 교체
        CircleSweep             = 15,
        // 적 몸체를 관통하고 벽과 지형 앞에서 정지
        PierceDash              = 16,
        // 첫 타격 뒤 주변 충격파 추가
        Shockwave               = 17,
        // 반격을 전방 직선 파동으로 교체
        CounterWave             = 18,
        // 한 발을 여러 갈래로 나눠 발사
        SpreadShot              = 19,
        // 지속 피해를 지연 뒤 단일 폭발로 교체
        Detonate                = 20,
        // 지속 피해 종료 때 폭발 추가
        EndExplosion            = 21,
        // 중심으로 한 번 끌어온 뒤 속박
        PullRoot                = 22,
        // 서로 다른 대상에게 나눠 발사
        SplitTargets            = 23,
        // 투사체가 첫 적을 관통
        PierceShot              = 24,
        // 결계를 적 하나에 부착해 따라가게 함
        AttachBarrier           = 25
    }
}
