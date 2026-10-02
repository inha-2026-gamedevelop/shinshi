// System
using System;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 시작 전에 GameDB 보스 처치 보상을 복사한 값, 처치 성공 정산에서만 쓴다 </summary>
    public readonly struct BossRewardRule
    {
        // 엽전(TradeCurrency)은 정산안의 currency 칸으로 처치 성공 때만 지급한다.
        // 경매 전용 재료(AuctionItemId)는 파티 경매 단계가 맡으므로 개인 정산에 넣지 않는다

        /****************************************
        *              Properties
        ****************************************/

        public long   Essence            { get; }
        public long   TradeCurrency      { get; }
        public string MaterialItemId     { get; }
        public int    MaterialRolls      { get; }
        public double MaterialRollChance { get; }
        public long   MaterialPerSuccess { get; }

        /****************************************
        *                Methods
        ****************************************/

        public BossRewardRule(RewardCatalogSO.BossRewardSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if ((spec.Essence < 0) || (spec.TradeCurrency < 0) || (spec.MaterialRolls < 0) || (spec.MaterialPerSuccess < 0)
                || (!(spec.MaterialRollChance >= 0f)) || (!(spec.MaterialRollChance <= 1f))
                || (string.IsNullOrEmpty(spec.MaterialItemId)))
            {
                throw new ArgumentException("보스 처치 보상 수치가 유효하지 않다", nameof(spec));
            }

            Essence            = spec.Essence;
            TradeCurrency      = spec.TradeCurrency;
            MaterialItemId     = spec.MaterialItemId;
            MaterialRolls      = spec.MaterialRolls;
            MaterialRollChance = spec.MaterialRollChance;
            MaterialPerSuccess = spec.MaterialPerSuccess;
        }
    }
}
