namespace Shinshi.AI
{
    // 기믹은 P06 시전의 실제 시작과 취소를 알려 주는 수신자 연결이 없으면 회복이 끝나기를 기다릴 시전을 알 수 없다.
    // 그 상태에서 마지막 구조물 파괴가 확인되면 회복이 끝나기 전에 그로기가 시작되므로, 기믹은 자신이 정말 연결돼 있는지 확인하고
    // 확인되지 않으면 발동하지 않는다. 팩토리를 감싸는 쪽(제외 필터, 기록용 래퍼 등)은 이 계약을 그대로 전달해야 한다.
    // 전달하지 않으면 기믹이 연결을 확인할 수 없어 발동하지 않는다(안전한 쪽으로 실패한다)
    /// <summary> 패턴 팩토리가 P06 시전의 시작과 취소를 알리는 수신자를 밝히는 선택 계약 </summary>
    public interface IBossChargeCastTrackerSource
    {
        /// <summary> 지금 연결된 시전 추적 수신자, 연결되지 않았으면 null이다 </summary>
        IBossChargeCastTracker ChargeCastTracker { get; }
    }
}
