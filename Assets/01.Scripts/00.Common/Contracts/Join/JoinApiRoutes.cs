namespace Shinshi.Contracts
{
    /// <summary> 게임 서버만 부르는 접속 토큰 교환 경로와 헤더, 일반 로그인 토큰으로는 호출할 수 없다 </summary>
    public static class JoinApiRoutes
    {
        // 서비스 자격은 ApiRoutes.AUTHORIZATION_HEADER에 Bearer로 싣는다. 인스턴스 헤더는 API가 자격에 묶인 인스턴스와
        // 본문의 instanceId를 대조하는 데 쓰며, 이것만으로 인스턴스를 증명하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // POST, 본문 JoinTokenExchangeRequest, 승인 200 JoinTokenExchangeResponse, 거절 4xx ApiRejection.
        // 토큰은 한 번만 교환된다. 응답을 잃으면 같은 토큰으로 다시 교환할 수 없으므로 서버는 이 호출을 재시도하지 않는다
        public const string JOIN_TOKEN_EXCHANGE = "/v1/game-servers/join-tokens/exchange";

        // 호출한 게임 서버 프로세스의 INSTANCE_ID
        public const string SERVER_INSTANCE_HEADER = "X-Shinshi-Instance-Id";
    }
}
