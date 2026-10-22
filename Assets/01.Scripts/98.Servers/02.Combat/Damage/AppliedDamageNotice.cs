// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 사망 정리 전에 포착한 귀속과 좌표, 실제 피해 적용 뒤 공통 판정기가 알리는 표시 자료 </summary>
    public readonly struct AppliedDamageNotice
    {
        /****************************************
        *              Properties
        ****************************************/

        public int OwnerCombatId { get; }
        public int TargetCombatId { get; }
        public Vector3 TargetPosition { get; }
        public DamageResult Result { get; }
        public double ServerTime { get; }
        public bool IsDamageOverTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AppliedDamageNotice(int ownerCombatId, int targetCombatId, Vector3 targetPosition,
            DamageResult result, double serverTime, bool isDamageOverTime = false)
        {
            OwnerCombatId  = ownerCombatId;
            TargetCombatId = targetCombatId;
            TargetPosition = targetPosition;
            Result         = result;
            ServerTime     = serverTime;
            IsDamageOverTime = isDamageOverTime;
        }
    }
}
