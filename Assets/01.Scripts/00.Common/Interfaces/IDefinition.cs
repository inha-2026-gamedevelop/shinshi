namespace Shinshi.Common
{
    /// <summary> GameDB 카탈로그 항목의 안정적 식별자와 표시 키 </summary>
    public interface IDefinition
    {
        // 저장 데이터와 서버 판정이 참조하므로 이름을 바꿔도 ID는 바꾸지 않는다
        string DefinitionId { get; }
        string DisplayKey   { get; }
    }
}
