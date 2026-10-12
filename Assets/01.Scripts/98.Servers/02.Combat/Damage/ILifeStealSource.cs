namespace Shinshi.Combat
{
    /// <summary> 서버 판정기가 피해의 주인에게 생명력 흡수 회복을 넘기는 창구 </summary>
    public interface ILifeStealSource
    {
        /// <summary> 실제 적용 피해를 받아 흡수 규칙대로 회복하고 실제로 오른 체력을 돌려준다, 회복이 없으면 0 </summary>
        float ReceiveLifeSteal(float appliedDamage, double serverTime);
    }
}
