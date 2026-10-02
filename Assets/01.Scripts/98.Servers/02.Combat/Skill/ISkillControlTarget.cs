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

        /// <summary> sourceId는 속박을 건 기술이나 효과의 정의 ID다 </summary>
        void ApplyRoot(string sourceId, float duration, double serverTime);
    }
}
