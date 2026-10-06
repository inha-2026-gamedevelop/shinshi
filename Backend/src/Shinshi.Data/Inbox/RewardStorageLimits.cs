// System
using System;

// Project
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Inbox
{
    /// <summary> 보관 상한(docs/design/growth-economy.md 9장)과 30일 회수함 기간. 정산과 경매 낙찰 지급은 상한을 넘긴 몫만 회수함으로 옮기고,
    /// 회수함 수령은 상한까지 남은 공간만큼만 옮긴다 </summary>
    public static class RewardStorageLimits
    {
        /****************************************
        *                Fields
        ****************************************/

        // 출처: Assets/01.Scripts/00.Common/Data/RewardCatalogSO.cs MATERIAL_STACK(보스 재료, 정련편, 티켓),
        // LifeConfigSO.cs HERB_STACK, POTION_STACK, GrowthConfigSO.cs _unregisteredCardStackLimit
        public const long MATERIAL_STACK = 9999;
        public const long HERB_STACK     = 9999;
        public const long POTION_STACK   = 999;
        public const long TALISMAN_STACK = 999;

        // 서버 시간으로 원래 획득 시각부터 30일. 재접속으로 다시 시작하지 않는다
        public const int RECOVERY_DAYS = 30;

        // 출처: RewardCatalogSO._items의 보스, 경매 재료 ID 접두와 LifeConfigSO._herbs, _potions
        // TODO(민성): 콘텐츠 정의 목록이 API에 생기면(묶음 G growth-content.json) ID 규칙 대신 정의의 상한을 읽는다
        public const string BOSS_MATERIAL_PREFIX    = "item_boss_material_";
        public const string AUCTION_MATERIAL_PREFIX = "item_auction_material_";
        public const string HERB_ID                 = "item_herb";
        public const string POTION_SMALL_ID         = "item_potion_small";
        public const string POTION_LARGE_ID         = "item_potion_large";

        private static readonly string[] G_PotionIds = { POTION_SMALL_ID, POTION_LARGE_ID };

        /****************************************
        *              Properties
        ****************************************/

        public static TimeSpan RecoveryPeriod => TimeSpan.FromDays(RECOVERY_DAYS);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 출정 준비 확인이 가용 수량을 알려 주는 물약 정의. 호출자가 고쳐도 원본이 바뀌지 않게 복사한다 </summary>
        public static string[] PotionIds()
        {
            return (string[])G_PotionIds.Clone();
        }

        /// <summary> 자산 하나의 보관 상한. 정기, 엽전처럼 상한이 없거나 상한을 모르는 아이템이면 null이다 </summary>
        public static long? LimitFor(string assetKind, string? assetId)
        {
            switch (assetKind)
            {
                case EconomyAsset.TICKETS:
                case EconomyAsset.SHARDS:
                    return MATERIAL_STACK;
                case EconomyAsset.ACCOUNT_ITEM:
                    return ItemLimit(assetId ?? string.Empty);
                default:
                    // TODO(민성): 정기와 엽전의 보관 상한은 기획 미정이라 상한 없음(RewardCatalogSO NO_STACK_LIMIT)
                    return null;
            }
        }

        /// <summary> 현재 보유량에서 상한까지 들어갈 수 있는 수량. 0 이상 amount 이하다 </summary>
        public static long Fits(long? limit, long current, long amount)
        {
            if (amount <= 0)
            {
                return 0;
            }
            if (!limit.HasValue)
            {
                return amount;
            }

            return Math.Clamp(limit.Value - current, 0, amount);
        }

        private static long? ItemLimit(string definitionId)
        {
            if (SettlementSanityLimits.IsTalismanEffectId(definitionId))
            {
                return TALISMAN_STACK;
            }
            if (Array.IndexOf(G_PotionIds, definitionId) >= 0)
            {
                return POTION_STACK;
            }
            if (definitionId == HERB_ID)
            {
                return HERB_STACK;
            }
            if (definitionId.StartsWith(BOSS_MATERIAL_PREFIX, StringComparison.Ordinal)
                || definitionId.StartsWith(AUCTION_MATERIAL_PREFIX, StringComparison.Ordinal))
            {
                return MATERIAL_STACK;
            }

            // TODO(민성): 상한이 정해지지 않은 아이템은 회수함으로 보내지 않고 그대로 지급한다.
            // 장비는 account_items가 아니라 equipment_instances 행이고 캐릭터당 100칸은 성장 경로(제작, 해제)가 지킨다.
            // 정산과 낙찰은 아직 장비 행을 지급하지 않으므로 장비 회수함 줄(asset_kind)도 없다. 장비 보상이 생기면 함께 정한다
            return null;
        }
    }
}
