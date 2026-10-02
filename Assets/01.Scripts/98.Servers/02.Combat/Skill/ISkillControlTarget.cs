namespace Shinshi.Combat
{
    /// <summary> 적 제어 구현의 창구, 보스의 제어 허용 구간은 적 서버가 결정한다 </summary>
    public interface ISkillControlTarget
    {
        /****************************************
        *              Properties
        ****************************************/

        bool  IsBoss            { get; }
        bool  AllowsRoot        { get; }
        float RootDurationScale { get; }

        /****************************************
        *                Methods
        ****************************************/

        void ApplyRoot(float duration, double serverTime);
    }
}
