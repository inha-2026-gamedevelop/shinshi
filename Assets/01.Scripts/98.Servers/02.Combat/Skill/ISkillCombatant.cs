namespace Shinshi.Combat
{
    /// <summary> 반격 창과 적중 결과를 피해 판정에 연결한다 </summary>
    public interface ISkillCombatant : ISkillCaster
    {
        /****************************************
        *              Properties
        ****************************************/

        SkillServer Skills { get; }
    }
}
