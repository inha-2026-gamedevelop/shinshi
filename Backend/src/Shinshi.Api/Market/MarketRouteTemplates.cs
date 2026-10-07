namespace Shinshi.Backend.Market
{
    /// <summary> MarketRoutes와 같은 경로의 ASP.NET Core 템플릿. 두 값이 어긋나지 않는지 테스트로 묶는다 </summary>
    public static class MarketRouteTemplates
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CHARACTER = "/v1/town/characters/{characterId}/market";

        public const string SEARCH         = "/v1/town/market/search";
        public const string LISTINGS_QUERY = "/v1/town/market/listings/query";
        public const string LIST           = CHARACTER + "/listings";
        public const string PURCHASE       = CHARACTER + "/purchases";
        public const string CANCEL         = CHARACTER + "/listings/{listingId}/cancel";
        public const string MINE           = CHARACTER + "/mine";
        public const string HISTORY        = CHARACTER + "/history";
        public const string ACK            = CHARACTER + "/ack";
    }
}
