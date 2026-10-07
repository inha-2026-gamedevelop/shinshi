namespace Shinshi.Contracts
{
    /// <summary> 마을 서버가 부르는 유저 경매장 경로 </summary>
    public static class MarketRoutes
    {
        // 모든 경로는 마을 서버 임대 Bearer와 X-Shinshi-Instance-Id 헤더가 필요하고, 그 캐릭터 계정의 접속 임대가 호출한 마을에 있어야 한다.
        // string.Format으로 {0}에 characterId, {1}에 listingId를 넣는다. 쓰기 응답은 MarketOperationResponse이며
        // 같은 operationId 재전송은 처음 결과를 받는다. 검색과 조회는 operations를 만들지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // POST, MarketSearchRequest -> MarketSearchResponse
        public const string SEARCH = "/v1/town/market/search";

        // POST, MarketListingsQueryRequest -> MarketListingsResponse
        public const string LISTINGS_QUERY = "/v1/town/market/listings/query";

        // POST, MarketListRequest -> MarketOperationResponse
        public const string LIST_FORMAT = "/v1/town/characters/{0}/market/listings";

        // POST, MarketBuyRequest -> MarketOperationResponse
        public const string PURCHASE_FORMAT = "/v1/town/characters/{0}/market/purchases";

        // POST, MarketCancelRequest -> MarketOperationResponse
        public const string CANCEL_FORMAT = "/v1/town/characters/{0}/market/listings/{1}/cancel";

        // GET, MarketMineResponse
        public const string MINE_FORMAT = "/v1/town/characters/{0}/market/mine";

        // GET, 쿼리 PAGE_PARAMETER(0부터), MarketHistoryResponse
        public const string HISTORY_FORMAT = "/v1/town/characters/{0}/market/history";

        // POST, MarketAckRequest -> MarketOperationResponse. 경제 효과가 없다
        public const string ACK_FORMAT = "/v1/town/characters/{0}/market/ack";

        public const string PAGE_PARAMETER = "page";
    }
}
