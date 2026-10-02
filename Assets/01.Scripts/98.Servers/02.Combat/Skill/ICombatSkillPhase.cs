namespace Shinshi.Combat
{
    /// <summary> 세션 서버가 여는 기술 허용 단계, 신시와 종료 단계에서는 닫는다 </summary>
    public interface ICombatSkillPhase
    {
        /****************************************
        *              Properties
        ****************************************/

        bool AllowsCombat { get; }
    }
}
