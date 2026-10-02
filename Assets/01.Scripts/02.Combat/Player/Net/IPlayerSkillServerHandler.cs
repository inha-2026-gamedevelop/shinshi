namespace Shinshi.Combat
{
    /// <summary> 기술 Command의 서버 창구, 구현 타입은 서버 어셈블리에 둔다 </summary>
    public interface IPlayerSkillServerHandler
    {
        /****************************************
        *                Methods
        ****************************************/

        void HandleSkill(in PlayerSkillInput input);
    }
}
