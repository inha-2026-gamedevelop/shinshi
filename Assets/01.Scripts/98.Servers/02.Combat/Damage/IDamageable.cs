namespace Shinshi.Combat
{
    /// <summary> 서버가 체력을 가진 전투 대상, DamageResolver만 ApplyDamage를 부른다 </summary>
    public interface IDamageable : ICombatTarget
    {
        float Health    { get; }
        float MaxHealth { get; }

        // 피해 감소 공식에 넣는 현재 방어력
        float Defense { get; }

        // 피해와 공명을 모두 받지 않는 구간
        bool IsInvulnerable { get; }

        // 전방 반격 방어 창이 열려 있다. 후방과 방어 불가 피해는 막지 못한다
        bool IsGuardingFront { get; }

        /// <summary> 판정이 끝난 피해를 반영한다, isLethal이면 체력 0과 사망 전이를 같이 한다 </summary>
        void ApplyDamage(float appliedAmount, bool isLethal, double serverTime);
    }
}
