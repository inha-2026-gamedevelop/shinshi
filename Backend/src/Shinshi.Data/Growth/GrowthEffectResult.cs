// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 성장 효과 콜백의 판정. 거절이면 실행기가 효과 전체를 되돌리고 사유만 저장한다 </summary>
    public sealed class GrowthEffectResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public int   ReasonCode     { get; }
        public int   Outcome        { get; }
        public Guid? ItemId         { get; }

        // 전투 준비 상태를 바꾸지 않은 확정(배분이 같은 재분배)은 loadout_revision을 올리지 않는다
        public bool  ChangesLoadout { get; }
        public bool  IsRejected     => ReasonCode != ContractCodes.RejectReason.NONE;

        /****************************************
        *                Methods
        ****************************************/

        private GrowthEffectResult(int reasonCode, int outcome, Guid? itemId, bool changesLoadout)
        {
            ReasonCode     = reasonCode;
            Outcome        = outcome;
            ItemId         = itemId;
            ChangesLoadout = changesLoadout;
        }

        public static GrowthEffectResult Rejected(int reasonCode)
        {
            if (reasonCode <= ContractCodes.RejectReason.NONE)
            {
                throw new ArgumentOutOfRangeException(nameof(reasonCode));
            }

            return new GrowthEffectResult(reasonCode, GrowthCodes.Outcome.NONE, null, false);
        }

        public static GrowthEffectResult Committed(int outcome = GrowthCodes.Outcome.NONE, Guid? itemId = null,
            bool changesLoadout = true)
        {
            return new GrowthEffectResult(ContractCodes.RejectReason.NONE, outcome, itemId, changesLoadout);
        }
    }
}
