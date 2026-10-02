namespace Shinshi.Contracts
{
    /// <summary> 인증 모듈의 추가 경로, 기존 API 경로 계약은 그대로 유지한다 </summary>
    public static class AuthRoutes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 배정된 게임 서버가 참가자별 일회용 토큰을 교환한다
        public const string JOIN_CONSUME = "/v1/auth/join/consume";
    }
}
