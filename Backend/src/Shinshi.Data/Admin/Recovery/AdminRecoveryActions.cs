namespace Shinshi.Data.Admin
{
    /// <summary> 수동 세션 복구의 어드민 작업 종류(감사 action과 같다)와 감사 대상 종류. 조회 필터가 이 값으로 찾으므로 바꾸지 않는다 </summary>
    public static class AdminRecoveryActions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string RETRY_SETTLEMENT = "admin.session.retry-settlement";
        public const string SERVICE_CANCEL   = "admin.session.service-cancel";
        public const string FORCE_CLOSE      = "admin.session.force-close";

        // 정산 재시도와 장애 취소는 실제 종결이 배정 서버 actor의 별도 트랜잭션이라, 그 전에 이 행을 따로 커밋해 둔다.
        // 어드민 작업 확정이 실패해도 누가 어떤 operationId로 종결을 시도했는지 남는다
        public const string RECOVERY_INTENT  = "admin.session.recovery.intent";

        public const string TARGET_GAME_SESSION = "game_session";
    }
}
