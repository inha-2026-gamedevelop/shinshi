namespace Shinshi.Combat
{
    // IDamageable에 넣지 않고 선택 계약으로 둔다. 구현체가 많고 보호막을 가진 대상은 플레이어뿐이다.
    // DamageResolver가 상한 뒤, 체력 하한 앞에서 부르므로 흡수한 몫은 적용량, 처치 판정, 흡혈, 피해 통지에 들어가지 않는다
    /// <summary> 받는 피해를 보호막으로 먼저 흡수하는 전투 대상의 선택 계약 </summary>
    public interface IDamageShieldTarget
    {
        /// <summary> 보호막이 흡수하고 체력으로 넘길 피해, 0 이상 damage 이하의 유한값이 아니면 판정기가 무시하고 원래 피해를 쓴다 </summary>
        float AbsorbWithShield(float damage, double serverTime);
    }
}
