namespace Shinshi.AI
{
    // 보스 하나의 기믹(체력 구간에서 한 번 발동해 일반 선택을 대신하는 진행)이 공용 두뇌에 연결되는 계약이다.
    // 강제 선택은 기존 선택 규칙(IBossSelectionRule)으로, 시전 종료 경계는 기존 시전 알림(IBossCastListener)으로 이미 받을 수 있다.
    // 공용 두뇌는 BossBrain.AddGimmick으로 받은 기믹의 아래 셋을 읽는다. 기믹을 두뇌에 더하는 일은 보스 몸체를 만드는 쪽이 한다.
    //  - Tick: 전투 틱 맨 앞, 페이즈 전환 판정보다 먼저 부른다
    //  - DefersPhaseTransition: 페이즈 전환 판정의 조건에 더한다
    //  - BlocksSelection: 새 패턴 선택 평가 앞에서 읽는다
    // 진입 정리 요청과 정지 알림이 필요한 기믹은 IBossGimmickCleanup도 구현한다. 시전 추적 같은 팩토리 쪽 연결은 기믹마다 따로 필요하다
    /// <summary> 보스별 기믹 계약, 강제 선택 규칙과 시전 종료 알림을 겸하고 틱 갱신과 전환 보류와 선택 차단을 알린다 </summary>
    public interface IBossGimmick : IBossSelectionRule, IBossCastListener
    {
        /// <summary> 전투 틱마다 맨 앞에서 부른다, phase는 지금 페이즈 번호다. 체력 조건 판정과 시간 경과 처리를 한다 </summary>
        void Tick(double serverTime, int phase);

        /// <summary> 참이면 페이즈 전환을 시작하지 않는다, 기믹이 끝난 뒤 첫 틱에서 전환이 일반 선택보다 먼저 판정된다 </summary>
        bool DefersPhaseTransition { get; }

        /// <summary> 참이면 새 패턴을 선택하지 않는다, 진행 중인 시전과 피해 처리는 막지 않는다 </summary>
        bool BlocksSelection { get; }
    }
}
