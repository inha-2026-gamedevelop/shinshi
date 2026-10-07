namespace Shinshi.Contracts
{
    /// <summary> 유저 경매장 DTO의 코드 값. 문자열은 DB market_listings의 CHECK 값과 같다 </summary>
    public static class MarketCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 자산 종류, DB의 asset_kind와 같다. 1:1 거래도 같은 값을 쓴다
        public const string ASSET_ACCOUNT_ITEM = "AccountItem";
        public const string ASSET_EQUIPMENT    = "Equipment";

        /// <summary> market_listings.state </summary>
        public static class ListingState
        {
            /****************************************
            *                Fields
            ****************************************/

            public const string ACTIVE    = "Active";
            public const string SOLD_OUT  = "SoldOut";
            public const string CANCELLED = "Cancelled";
            public const string EXPIRED   = "Expired";
        }

        /// <summary> market_listings.category, 검색 분류. TODO(민성): 기획 확정 전 제안값 </summary>
        public static class Category
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int HERB      = 1;
            public const int TALISMAN  = 2;
            public const int EQUIPMENT = 3;
        }

        /// <summary> MarketSearchRequest.sort </summary>
        public static class Sort
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int UNIT_PRICE = 0;
            public const int NEWEST     = 1;
            public const int QUANTITY   = 2;
        }

        /// <summary> MarketSearchRequest.order </summary>
        public static class Order
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int ASCENDING  = 0;
            public const int DESCENDING = 1;
        }

        /// <summary> MarketTradeView.role </summary>
        public static class TradeRole
        {
            /****************************************
            *                Fields
            ****************************************/

            public const string BUYER  = "Buyer";
            public const string SELLER = "Seller";
        }
    }
}
