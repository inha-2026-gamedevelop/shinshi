// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 시전자 결계의 우선 표적과 자기 동료 피해 보너스, 동료 적중 공명을 연결한다 </summary>
    public interface ICompanionContext
    {
        /****************************************
        *                Methods
        ****************************************/

        bool TryGetMarkedTarget(Vector3 origin, out ICombatTarget target);
        float GetCompanionDamageBonus(ICombatTarget target);

        // 유효 피해가 난 공격 동작만 통지하며 다중 적중 변형은 동작 번호로 공명을 한 번만 인정한다
        void OnCompanionHit(in DamageRequest request, in DamageResult result);
    }
}
