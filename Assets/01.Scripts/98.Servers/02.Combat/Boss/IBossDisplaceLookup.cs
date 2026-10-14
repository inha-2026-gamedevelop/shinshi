namespace Shinshi.Combat
{
    // ICombatTarget은 읽기 전용이라 전투 대상 목록으로는 위치를 바꿀 수 없다. 밀 수 있는 대상만 이 창구로 따로 찾는다
    /// <summary> 전투 번호로 밀어낼 수 있는 대상을 찾는다 </summary>
    public interface IBossDisplaceLookup
    {
        /// <summary> 그 전투 번호의 대상이 있으면 참이다. 지금 밀어도 되는지는 대상의 CanBeDisplaced가 답한다, 할당하지 않는다 </summary>
        bool TryGetDisplaceTarget(int combatId, out IBossDisplaceTarget target);
    }
}
