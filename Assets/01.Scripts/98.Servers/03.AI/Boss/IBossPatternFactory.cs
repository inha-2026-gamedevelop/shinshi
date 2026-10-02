using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // TODO(기웅): 월백 6패턴 구현은 이번 범위 밖이다
    /// <summary> 패턴 정의로 시전 한 번의 실행 객체를 만든다 </summary>
    public interface IBossPatternFactory
    {
        /// <summary> 구현이 없는 정의면 null을 돌려준다 </summary>
        IBossPattern Create(PatternDefinition definition);
    }
}
