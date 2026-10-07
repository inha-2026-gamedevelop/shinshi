namespace Shinshi.Contracts
{
    /// <summary> 유저 경매장의 값과 한도. 클라이언트 표시와 API 검증이 같은 값을 쓴다 </summary>
    public static class MarketLimits
    {
        /****************************************
        *                Fields
        ****************************************/

        public const long MIN_UNIT_PRICE         = 1;
        public const long MAX_UNIT_PRICE         = 100000000;
        public const int  MAX_QUANTITY           = 9999;
        public const int  MAX_ACTIVE_LISTINGS    = 10;
        public const int  LISTING_HOURS          = 24;
        public const int  PAGE_SIZE              = 20;
        public const int  MAX_SEARCH_DEFINITIONS = 50;

        // 페이지 번호 상한, 클라이언트 중계와 API 조회가 같은 값을 쓴다
        public const int  MAX_PAGE               = 1000;

        // 성사 수수료 5%, 구매 한 건 총액 기준 올림
        public const int FEE_PERCENT = 5;

        // 화면에 보이는 종료 기록 기간. 기록 자체는 삭제하지 않는다
        public const int HISTORY_DAYS = 30;

        // 최근 평균가 구간
        public const int RECENT_PRICE_HOURS = 24;
    }
}
