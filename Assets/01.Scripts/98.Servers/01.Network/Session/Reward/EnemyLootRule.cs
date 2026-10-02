// System
using System;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 시작 시 GameDB 개인 드롭 수치를 복사한 값, 실행 중 데이터 변경으로 재시도 결과가 달라지지 않는다 </summary>
    public readonly struct EnemyLootRule
    {
        /****************************************
        *              Properties
        ****************************************/

        public long   ExtraEssence     { get; }
        public double TicketChance     { get; }
        public int    TicketQuantity   { get; }
        public double TalismanChance   { get; }
        public int    TalismanQuantity { get; }

        /****************************************
        *                Methods
        ****************************************/

        public EnemyLootRule(RewardCatalogSO.EnemyDropSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if ((spec.ExtraEssence < 0) || (spec.TicketQuantity < 0) || (spec.TalismanQuantity < 0)
                || (!(spec.TicketChance >= 0f)) || (!(spec.TicketChance <= 1f))
                || (!(spec.TalismanChance >= 0f)) || (!(spec.TalismanChance <= 1f)))
            {
                throw new ArgumentException("개인 드롭 수치가 유효하지 않다", nameof(spec));
            }

            ExtraEssence     = spec.ExtraEssence;
            TicketChance     = spec.TicketChance;
            TicketQuantity   = spec.TicketQuantity;
            TalismanChance   = spec.TalismanChance;
            TalismanQuantity = spec.TalismanQuantity;
        }
    }
}
