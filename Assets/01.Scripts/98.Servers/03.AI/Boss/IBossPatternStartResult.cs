namespace Shinshi.AI
{
    // 시작할 수 없는 시전(유효한 대상이나 빈 슬롯이 없는 경우)을 두뇌가 알아보게 하는 선택 계약이다
    // 구현하지 않은 패턴은 항상 시작한 것으로 본다. 거절되면 두뇌는 기록을 남기지 않고 기존 재조회 시간 뒤에 다시 고른다
    /// <summary> 패턴이 시작 직후 스스로 시작을 거절했는가 </summary>
    public interface IBossPatternStartResult
    {
        bool IsStartRejected { get; }
    }
}
