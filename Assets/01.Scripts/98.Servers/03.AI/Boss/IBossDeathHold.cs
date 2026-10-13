namespace Shinshi.AI
{
    // 하드는 반격 중 체력이 정확히 0에 멈춰도 보스를 죽이지 않고 다음 기믹으로 넘겨야 한다. 사망 확정은 IBossDeathPolicy가 하므로
    // 기믹이 이 계약으로 보류를 알리고 BossGimmickDeathPolicy가 정책 앞에서 읽는다. 사망 확정을 읽는 다른 경로가 있으면 그쪽도 같은 보류를 읽어야 한다
    /// <summary> 체력이 0이어도 보스 사망을 확정하지 말아야 하는 기믹의 보류 창구 </summary>
    public interface IBossDeathHold
    {
        /// <summary> 참이면 체력 0을 사망으로 확정하지 않는다 </summary>
        bool IsDeathHeld { get; }
    }
}
