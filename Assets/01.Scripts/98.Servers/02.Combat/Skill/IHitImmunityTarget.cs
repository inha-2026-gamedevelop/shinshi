namespace Shinshi.Combat
{
    /// <summary> 기술이 거는 피격이상 면역을 받는 시전자, 면역 중에는 기절, 속박, 밀림을 받지 않고 피해는 그대로 받는다 </summary>
    public interface IHitImmunityTarget
    {
        /****************************************
        *                Methods
        ****************************************/

        void GrantHitImmunity(double untilServerTime);
    }
}
