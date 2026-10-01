using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 일반 가중 선택보다 먼저 평가되는 자리다. 오프닝이나 강제 후속 공격 같은 규칙이 정해지면 여기에 추가한다
    // 지금은 등록된 규칙이 없고, 그런 규칙은 구현하지 않았다
    /// <summary> 일반 가중 선택 앞에서 패턴을 직접 정하는 규칙 </summary>
    public interface IBossSelectionRule
    {
        /// <summary> 패턴을 정했으면 true, 정하지 않으면 false를 돌려주고 가중 선택이 이어진다 </summary>
        bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern);
    }
}
