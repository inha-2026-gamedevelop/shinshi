namespace Shinshi.Backend.Trade
{
    /// <summary> TradeRoutes와 같은 경로의 ASP.NET Core 템플릿. 두 값이 어긋나지 않는지 테스트로 묶는다 </summary>
    public static class TradeRouteTemplates
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PREPARE = "/v1/town/trades/prepare";
        public const string COMMIT  = "/v1/town/trades/{executionId}/commit";
        public const string RESOLVE = "/v1/town/trades/{executionId}/resolve";
    }
}
