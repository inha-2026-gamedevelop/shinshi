using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // IBossPatternFactory에 멤버를 더하면 그 인터페이스를 구현한 다른 코드가 깨지므로 따로 둔 선택 인터페이스다
    // 구현하지 않은 팩토리는 만들 수 있는 것으로 본다
    /// <summary> 정의로 실행 객체를 만들 수 있는가를 할당 없이 답하는 팩토리 쪽 확인, Create가 null을 돌려줄 정의를 미리 가른다 </summary>
    public interface IBossPatternAvailability
    {
        bool CanCreate(PatternDefinition definition);
    }
}
