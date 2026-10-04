// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 성장 쓰기의 저장된 결과와 응답 시점의 성장 상태. Result가 null이면 차단 중 처음 보는 operationId다 </summary>
    public sealed class GrowthOperationOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public OperationResult?         Result  { get; }
        public int                      Outcome { get; }
        public Guid?                    ItemId  { get; }
        public CharacterGrowthSnapshot? Growth  { get; }

        /****************************************
        *                Methods
        ****************************************/

        public GrowthOperationOutcome(OperationResult? result, int outcome, Guid? itemId, CharacterGrowthSnapshot? growth)
        {
            Result  = result;
            Outcome = outcome;
            ItemId  = itemId;
            Growth  = growth;
        }
    }
}
