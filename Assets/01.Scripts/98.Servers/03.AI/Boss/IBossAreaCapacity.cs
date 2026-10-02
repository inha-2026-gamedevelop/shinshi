namespace Shinshi.AI
{
    // IBossEffectLifetime에 멤버를 더하면 그 인터페이스를 구현한 다른 담당의 코드가 깨지므로 따로 둔 선택 계약이다
    // 구현하지 않은 수명 관리자는 패턴별 ActiveAreaCount만 쓴다
    /// <summary> 패턴 ID와 상관없이 보스가 가진 장판(예약과 활성) 총 개수 </summary>
    public interface IBossAreaCapacity
    {
        int TotalAreaCount { get; }
    }
}
