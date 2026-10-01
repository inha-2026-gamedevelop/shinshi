namespace Shinshi.AI
{
    // 최종 발악은 이번 구현에서 제외하기로 확정했다. 체력이 0이면 바로 사망을 확정한다
    // 발악을 추가할 때는 이 정책을 교체한다 (IBossDeathPolicy)
    /// <summary> 체력이 0이 되면 바로 사망으로 확정하는 기본 정책 </summary>
    public sealed class ImmediateBossDeathPolicy : IBossDeathPolicy
    {
        public bool IsDeathConfirmed(IBossVitals vitals, double serverTime)
        {
            return vitals.IsHealthZero;
        }
    }
}
