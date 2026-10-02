namespace Shinshi.AI
{
    // ICombatTarget에는 체력 읽기가 없어 보스 FSM이 읽을 창구를 따로 둔다
    // TODO(민성): 체력과 피해 시스템(U05)이 구현한다
    /// <summary> 보스 체력의 읽기 전용 창구 </summary>
    public interface IBossVitals
    {
        /// <summary> 현재 체력 / 최대 체력, 0 이상 1 이하 </summary>
        float HealthRate { get; }

        // 사망 확정과 구분한다. 최종 발악 같은 단계가 생기면 체력은 0이어도 아직 죽지 않을 수 있다
        /// <summary> 체력이 0이다 </summary>
        bool IsHealthZero { get; }
    }
}
