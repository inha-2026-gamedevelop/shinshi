using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // IBossPatternCatalog에 멤버를 더하면 그 인터페이스를 구현한 다른 담당의 코드가 깨지므로 따로 둔 선택 인터페이스다
    // 구현하지 않은 카탈로그는 데이터가 준비된 것으로 본다
    /// <summary> 패턴 한 개의 데이터(수치와 선택 값)가 후보로 쓸 수 있게 지정됐는가를 답하는 카탈로그 쪽 확인 </summary>
    public interface IBossPatternReadiness
    {
        /// <summary> 공격 구현 여부가 아니라 데이터 지정 여부만 본다 </summary>
        bool IsPatternReady(string bossId, PatternDefinition pattern);
    }
}
