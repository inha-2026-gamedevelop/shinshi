namespace Shinshi.AI
{
    // 패턴별 리스너의 OnHit는 적중 수를 알리는 것이라 적중이 없으면 오지 않는 패턴(돌진)이 있고 시각도 싣지 않는다.
    // 표현은 적중과 상관없이 발동 순간과 정상 종료를 알아야 하므로 공격 FSM의 단계 경계에서 따로 알린다.
    // 기존 리스너에 멤버를 더하면 기존 구현과 테스트가 깨지므로 별도 계약으로 둔다
    /// <summary> 시전 한 번의 발동과 정상 종료 시점 알림, 시각은 틱 시각이 아니라 FSM의 단계 경계다 </summary>
    public interface IBossCastListener
    {
        /// <summary> 예고가 끝나 실행에 들어갔다, 패턴의 실행 진입 처리(방향 고정, 타격, 돌진 시작, 발사, 활성)가 끝난 뒤에 온다 </summary>
        void OnCastExecuted(int castInstanceId, double serverTime);

        /// <summary> 회복까지 정상으로 끝났다, 취소된 시전에는 오지 않는다 </summary>
        void OnCastFinished(int castInstanceId, double serverTime);
    }
}
