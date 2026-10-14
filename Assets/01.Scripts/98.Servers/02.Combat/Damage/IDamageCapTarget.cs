namespace Shinshi.Combat
{
    // IDamageable에 넣지 않고 선택 계약으로 둔다. 구현체가 많고 상한이 필요한 대상은 체력 경계에서 멈추는 보스 몸체뿐이다.
    // DamageResolver가 방어와 치명 배율을 적용한 뒤에 부르므로, 깎인 값이 적용량과 치명 여부와 흡혈과 피해 통지에 그대로 쓰인다
    /// <summary> 받는 피해에 상한을 두는 전투 대상의 선택 계약 </summary>
    public interface IDamageCapTarget
    {
        /// <summary> 방어와 치명 배율까지 적용한 피해를 줄인 값, 0 이상 damage 이하의 유한값이 아니면 판정기가 무시하고 원래 피해를 쓴다 </summary>
        float CapIncomingDamage(float currentHealth, float damage);
    }
}
