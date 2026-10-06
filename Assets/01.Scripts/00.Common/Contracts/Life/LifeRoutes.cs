namespace Shinshi.Contracts
{
    /// <summary> 마을 서버만 부르는 생활(행동력, 채집, 물약 제작) API 경로. 서버 임대 토큰으로 호출하며 플레이어 토큰으로는 호출할 수 없다 </summary>
    public static class LifeRoutes
    {
        // 클라이언트는 마을 서버에 요청하고, 마을 서버가 계정 접속 임대를 확인해 이 경로로 저장한다.
        // 채집, 제작 응답은 공통 OperationResult다. 같은 operationId 재전송은 처음 확정한 결과를 그대로 받는다

        /****************************************
        *                Fields
        ****************************************/

        // 행동력(DB 시각 기준 회복 반영), 약초, 물약 재고 조회. 상태를 바꾸지 않는다
        public const string SNAPSHOT      = "/v1/town/life/snapshot";

        // 채집 완료 한 번. 행동력 차감과 약초 지급을 한 트랜잭션에 확정한다
        public const string GATHERS       = "/v1/town/life/gathers";

        // 물약 제작. 약초 차감과 완제품 지급을 한 트랜잭션에 확정한다
        public const string POTION_CRAFTS = "/v1/town/life/potion-crafts";
    }
}
