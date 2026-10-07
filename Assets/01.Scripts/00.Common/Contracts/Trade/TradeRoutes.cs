namespace Shinshi.Contracts
{
    /// <summary> 마을 서버가 부르는 1:1 거래 실행 경로 </summary>
    public static class TradeRoutes
    {
        // 모든 경로는 마을 서버 임대 Bearer와 X-Shinshi-Instance-Id 헤더가 필요하다. 응답은 TradeExecutionResponse다.
        // string.Format으로 {0}에 executionId를 넣는다

        /****************************************
        *                Fields
        ****************************************/

        // POST, TradePrepareRequest. 경제 효과 없이 동결된 제안만 남긴다
        public const string PREPARE = "/v1/town/trades/prepare";

        // POST, TradeCommitRequest. operationId는 prepare가 저장한 commitOperationId와 같아야 한다. 같은 요청의 재전송은 처음 결과를 받는다
        public const string COMMIT_FORMAT = "/v1/town/trades/{0}/commit";

        // POST, 본문 없음. 결과 불명일 때 commit 재전송 대신 쓴다. 저장된 commit 결과로 실행 행을 종결하고, 만료됐으면 Abandoned로 끝낸다
        public const string RESOLVE_FORMAT = "/v1/town/trades/{0}/resolve";
    }
}
