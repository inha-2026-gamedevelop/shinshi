namespace Shinshi.Combat
{
    /// <summary> 기술이 거는 보호막을 받는 시전자, 보호막은 받는 피해를 체력보다 먼저 흡수한다 </summary>
    public interface IShieldTarget
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 남은 보호막을 amount로 덮어쓰고 만료 시각을 untilServerTime으로 바꾼다 </summary>
        void GrantShield(float amount, double untilServerTime);
    }
}
