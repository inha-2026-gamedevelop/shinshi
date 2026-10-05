// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 계승 판정 결과. 거절이면 ReasonCode만 의미가 있다 </summary>
    public sealed class InheritanceDecision
    {
        /****************************************
        *              Properties
        ****************************************/

        public int  ReasonCode  { get; }
        public int  ResultLevel { get; }
        public long EssenceCost { get; }
        public bool IsAccepted  => ReasonCode == ContractCodes.RejectReason.NONE;

        /****************************************
        *                Methods
        ****************************************/

        private InheritanceDecision(int reasonCode, int resultLevel, long essenceCost)
        {
            ReasonCode  = reasonCode;
            ResultLevel = resultLevel;
            EssenceCost = essenceCost;
        }

        public static InheritanceDecision Accepted(int resultLevel, long essenceCost)
        {
            return new InheritanceDecision(ContractCodes.RejectReason.NONE, resultLevel, essenceCost);
        }

        public static InheritanceDecision Rejected(int reasonCode)
        {
            return new InheritanceDecision(reasonCode, 0, 0);
        }
    }
}
