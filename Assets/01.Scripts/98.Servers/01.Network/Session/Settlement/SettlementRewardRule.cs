// System
using System;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 시작 전에 GameDB 웨이브 완료 보상을 복사한 값, 실행 중 데이터가 바뀌어도 정산이 달라지지 않는다 </summary>
    public readonly struct SettlementRewardRule
    {
        /****************************************
        *              Properties
        ****************************************/

        public long   BaseEssencePerWave  { get; }
        public long   ExtraEssencePerWave { get; }
        public long   RefineShardsPerWave { get; }
        public string RefineShardItemId   { get; }
        public string BossTicketItemId    { get; }
        public bool   IsValid             => (RefineShardItemId != null) && (BossTicketItemId != null);

        /****************************************
        *                Methods
        ****************************************/

        public SettlementRewardRule(RewardCatalogSO rewards)
        {
            if (rewards == null)
            {
                throw new ArgumentNullException(nameof(rewards));
            }

            if ((rewards.BaseEssencePerWave < 0) || (rewards.ExtraEssencePerWave < 0) || (rewards.RefineShardsPerWave < 0)
                || (string.IsNullOrEmpty(rewards.RefineShardItemId)) || (string.IsNullOrEmpty(rewards.BossTicketItemId)))
            {
                throw new ArgumentException("웨이브 완료 보상 수치가 유효하지 않다", nameof(rewards));
            }

            BaseEssencePerWave  = rewards.BaseEssencePerWave;
            ExtraEssencePerWave = rewards.ExtraEssencePerWave;
            RefineShardsPerWave = rewards.RefineShardsPerWave;
            RefineShardItemId   = rewards.RefineShardItemId;
            BossTicketItemId    = rewards.BossTicketItemId;
        }
    }
}
