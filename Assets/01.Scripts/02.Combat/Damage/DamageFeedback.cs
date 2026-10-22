// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 소유자 전용 피해 숫자 자료, TargetRpc는 이 자료의 기본형 값만 직렬화한다 </summary>
    public readonly struct DamageFeedback
    {
        /****************************************
        *              Properties
        ****************************************/

        public int SessionEpoch { get; }
        public uint Sequence { get; }
        public int TargetCombatId { get; }
        public Vector3 WorldPosition { get; }
        public float Amount { get; }
        public bool IsCritical { get; }
        public double ServerTime { get; }
        public bool IsDamageOverTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public DamageFeedback(int sessionEpoch, uint sequence, int targetCombatId, Vector3 worldPosition,
            float amount, bool isCritical, double serverTime, bool isDamageOverTime = false)
        {
            SessionEpoch   = sessionEpoch;
            Sequence       = sequence;
            TargetCombatId = targetCombatId;
            WorldPosition  = worldPosition;
            Amount         = amount;
            IsCritical     = isCritical;
            ServerTime     = serverTime;
            IsDamageOverTime = isDamageOverTime;
        }
    }
}
