namespace Shinshi.AI
{
    // 선택 규칙이 패턴을 고르는 것과 실제로 시전이 시작되는 것은 다르다. 선택 뒤 패턴을 만들지 못하면 시전이 시작되지 않고,
    // 시작한 시전이 정상 종료 알림 없이 취소될 수도 있다. 기믹이 선택만 보고 시전 중이라고 믿으면 이 두 경우에
    // 끝나지 않는 시전을 기다리게 된다. 그래서 시전의 실제 시작과 취소는 패턴이 직접 알린다.
    // 정상 종료는 시전 알림(IBossCastListener.OnCastFinished)이 경계 시각과 함께 알리므로 여기에 두지 않는다.
    // 시전 ID는 공격 FSM이 시전마다 발급해 패턴 시작 입력(BossPatternContext.CastInstanceId)과 시전 알림에 똑같이 싣는 값이다
    /// <summary> 뿔 들이받기 시전의 실제 시작과 취소를 알리는 계약, 기믹이 추적 중인 시전을 확정하는 데 쓴다 </summary>
    public interface IBossChargeCastTracker
    {
        /// <summary> 시전이 실제로 시작됐다, 패턴 시작 처리가 끝난 뒤 한 번 온다 </summary>
        void OnChargeCastStarted(int castInstanceId);

        /// <summary> 시전이 끝까지 가지 못하고 취소됐다, 정상 종료한 시전에는 오지 않는다. 취소 시각은 싣지 않는다 </summary>
        void OnChargeCastCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
