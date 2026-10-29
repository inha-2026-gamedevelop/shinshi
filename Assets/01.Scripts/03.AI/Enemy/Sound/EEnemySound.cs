namespace Shinshi.AI
{
    /// <summary> 일반 적 효과음 종류, 제작 목록의 ID(E01~E14) 순서를 따른다. 모델별로 다른 소리는 모델 이름을 붙인다 </summary>
    public enum EEnemySound
    {
        None = 0,

        // E01
        SpawnWarning,
        // E03
        UnblockableWarning,
        // E04
        MeleeAttack,
        // E05
        HitBeast,
        HitVendigo,
        // E06
        DeathBeast,
        DeathVendigo,
        // E07
        RangedCast,
        // E08
        ProjectileFly,
        // E09
        ProjectileHit,
        // E10
        ChargeRush,
        // E11
        ChargeImpact,
        // E12
        EliteAppear,
        // E13
        EliteSmash,
        // E14
        DeathDissolve,
    }
}
