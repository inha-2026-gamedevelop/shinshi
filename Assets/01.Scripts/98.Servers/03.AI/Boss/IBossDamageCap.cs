namespace Shinshi.AI
{
    // 보스 몸체(IDamageable)가 IsInvulnerable에 IsDamageBlocked를 더하고, 피해 판정기가 방어와 치명타를 계산한 뒤 CapDamage로 줄인 값을
    // 실제 적용 피해로 써야 경계에서 멈추고 초과분이 개인 총 피해량에 들어가지 않는다. 이 연결은 보스 몸체와 피해 판정기 쪽 일이다
    /// <summary> 체력 경계에서 멈추기와 기믹 중 무적, 보스 몸체의 피해 처리가 읽는다 </summary>
    public interface IBossDamageCap
    {
        /// <summary> 지금 보스는 어떤 피해도 받지 않는다, 기믹 중 무적과 경계에 닿아 더 받을 수 없는 구간이다 </summary>
        bool IsDamageBlocked { get; }

        /// <summary> 피해를 현재 경계까지로 줄인 값, 경계를 넘는 초과분은 버린다. 반환이 0이면 이 피해는 없던 것으로 본다 </summary>
        float CapDamage(float currentHealth, float maxHealth, float damage);
    }
}
