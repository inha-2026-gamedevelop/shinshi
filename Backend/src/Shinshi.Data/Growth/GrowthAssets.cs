// Project
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Growth
{
    /// <summary> 성장 작업이 쓰는 자산 식별자. 지갑 자산 ID는 정산과 같은 값이다 </summary>
    public static class GrowthAssets
    {
        /****************************************
        *                Fields
        ****************************************/

        // 보스 재료, 경매 재료, 미등록 부적, 지급 물약은 모두 계정 귀속이다. 거래 가능 속성이 생기면 귀속을 나눈다
        public const string ACCOUNT_BINDING = SettlementSanityLimits.ACCOUNT_BINDING_KIND;

        public const string ESSENCE_ITEM_ID  = SettlementSanityLimits.ESSENCE_DEFINITION_ID;
        public const string SHARD_ITEM_ID    = SettlementSanityLimits.SHARD_DEFINITION_ID;
        public const string TICKET_ITEM_ID   = SettlementSanityLimits.TICKET_DEFINITION_ID;
        public const string CURRENCY_ITEM_ID = SettlementSanityLimits.CURRENCY_DEFINITION_ID;

        // item_history.event_kind
        public const string HISTORY_GRANTED          = "Granted";
        public const string HISTORY_CRAFTED          = "Crafted";
        public const string HISTORY_ENHANCED         = "Enhanced";
        public const string HISTORY_ENHANCE_FAILED   = "EnhanceFailed";
        public const string HISTORY_INHERITED        = "Inherited";
        public const string HISTORY_INHERIT_CONSUMED = "InheritConsumed";
        public const string HISTORY_EQUIPPED         = "Equipped";
        public const string HISTORY_UNEQUIPPED       = "Unequipped";
        public const string HISTORY_LOCK_CHANGED     = "LockChanged";
        public const string HISTORY_DISMANTLED       = "Dismantled";
        public const string HISTORY_MARKET_ESCROWED  = "MarketEscrowed";
        public const string HISTORY_MARKET_RETURNED  = "MarketReturned";
        public const string HISTORY_MARKET_SOLD      = "MarketSold";
        public const string HISTORY_TRADED           = "Traded";
    }
}
