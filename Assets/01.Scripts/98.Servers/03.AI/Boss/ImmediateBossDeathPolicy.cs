namespace Shinshi.AI
{
    // TODO(기웅): 기획 미정 - 최종 발악을 도입하면 이 기본 구현을 교체한다. 지금은 체력 0이면 바로 확정한다
    /// <summary> 체력이 0이 되면 바로 사망으로 확정하는 임시 기본 정책 </summary>
    public sealed class ImmediateBossDeathPolicy : IBossDeathPolicy
    {
        public bool IsDeathConfirmed(IBossVitals vitals, double serverTime)
        {
            return vitals.IsHealthZero;
        }
    }
}
