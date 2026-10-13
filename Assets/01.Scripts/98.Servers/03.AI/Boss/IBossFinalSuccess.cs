namespace Shinshi.AI
{
    // 하드 0%의 최종 성공은 보스 체력 0과 별개로 기믹이 확정한다. 세션은 같은 틱의 전멸과 전체 제한 시간 만료보다 이 신호를 먼저 읽어야 하고,
    // 보스 처치와 정산은 BossFinalSuccess.BossCombatId(전투 식별자) 기준으로 한 번만 실행해야 한다. 기믹은 한 번만 내려 주지만 세션과 정산의 중복 방지는
    // 이 계약만으로 보장되지 않는다. 세션과 정산 연결은 아직 없다
    /// <summary> 하드 0% 최종 성공 신호의 읽기 창구 </summary>
    public interface IBossFinalSuccess
    {
        /// <summary> 최종 성공이 확정됐고 아직 소비되지 않았다 </summary>
        bool HasFinalSuccess { get; }

        /// <summary> 최종 성공을 한 번 소비한다, 이미 소비했거나 성공 전이면 거짓이다 </summary>
        bool TryConsumeFinalSuccess(out BossFinalSuccess success);
    }
}
