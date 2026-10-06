namespace Shinshi.AI
{
    // 지금 공격 FSM은 실행 시간을 예고가 끝나는 경계에서 PatternDefinition.ActiveSeconds 하나로 고정하므로 이 계약을 읽지 않는다.
    // FSM이 이 계약을 읽도록 바뀌기 전까지는 패턴이 필요로 하는 실행 시간이 실제 실행 구간에 반영되지 않는다. 변경은 공용 FSM 소유자의 승인이 필요하다
    /// <summary> 시전마다 실행 시간이 달라지는 패턴이 구현하는 선택 계약, 구현하지 않은 패턴은 정의의 값을 쓴다 </summary>
    public interface IBossExecuteDuration
    {
        /// <summary> 실행 단계에 들어간 직후에 읽는다. 실행 시작 경계부터 재는 실행 시간(초)을 돌려주며, 아직 정할 수 없으면 정의의 값을 그대로 돌려준다 </summary>
        float GetExecuteSeconds(float definitionSeconds);
    }
}
