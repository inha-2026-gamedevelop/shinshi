namespace Shinshi.AI
{
    // 지금 공격 FSM은 회복 시간을 실행이 끝나는 경계에서 정의의 값으로 고정하므로 이 계약을 읽지 않는다.
    // FSM이 이 계약을 읽도록 바뀌기 전까지는 패턴이 결정한 값이 실제 회복 시간에 반영되지 않는다. 변경은 공용 FSM 소유자의 승인이 필요하다
    /// <summary> 실행 결과에 따라 회복 시간이 달라지는 패턴이 구현하는 선택 계약, 구현하지 않은 패턴은 정의의 값을 쓴다 </summary>
    public interface IBossRecoveryDuration
    {
        /// <summary> 실행 종료 경계에서 회복 단계에 들어간 뒤에 읽는다. 실행 종료 경계부터 재는 회복 시간(초)을 돌려주며, 아직 정할 수 없으면 정의의 값을 그대로 돌려준다 </summary>
        float GetRecoverySeconds(float definitionSeconds);
    }
}
