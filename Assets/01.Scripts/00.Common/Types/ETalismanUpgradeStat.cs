namespace Shinshi.Common
{
    /// <summary> 부적 효과 레벨이 올라갈 때 커지는 수치의 종류와 단위 </summary>
    public enum ETalismanUpgradeStat
    {
        // 원래 쿨다운에 대한 감소 비율
        CooldownRate       = 0,
        // 원래 기력 비용에 대한 감소 비율
        EnergyCostRate     = 1,
        // 도
        ArcAngle           = 2,
        // m
        Radius             = 3,
        // m
        Range              = 4,
        // m
        MoveDistance       = 5,
        // 해당 타격 계수에 대한 증가 비율
        DamageRate         = 6,
        // 공격력 배율 A로 더하는 추가 계수
        AddedCoefficient   = 7,
        // 초, 전방 방어 유효 시간
        GuardDuration      = 8,
        // 초, 소환이나 결계의 유지 시간
        EffectDuration     = 9,
        // 초
        RootDuration       = 10,
        // 원래 공격 간격에 대한 감소 비율
        AttackIntervalRate = 11,
        // 피해 보너스에 더하는 비율 포인트
        DamageBonusPoint   = 12,
        // 공명
        ResonanceGain      = 13
    }
}
