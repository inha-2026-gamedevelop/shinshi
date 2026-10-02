namespace Shinshi.AI
{
    /// <summary> 체력 0을 사망 확정으로 볼지 정한다 </summary>
    public interface IBossDeathPolicy
    {
        bool IsDeathConfirmed(IBossVitals vitals, double serverTime);
    }
}
