namespace Shinshi.Contracts.Admin
{
    /// <summary> 어드민 API 경로. 컨트롤러는 템플릿 상수를, 명령줄 도구는 FORMAT 상수에 string.Format으로 ID를 넣어 쓴다 </summary>
    public static class AdminApiRoutes
    {
        // 어드민 경로는 게임 토큰과 다른 bearer를 쓰고, 허용 네트워크 밖에서는 존재 자체를 숨긴다(404).
        // 쓰기 요청은 모두 operationId와 reason을 본문에 담는다. 응답에 비밀값이 있는 경로는 Cache-Control: no-store다

        /****************************************
        *                Fields
        ****************************************/

        public const string PREFIX = "/admin/v1";

        // 경로 조회 문자열 이름. GET /admin/v1/accounts?query=, GET /admin/v1/sessions?state=
        public const string QUERY_PARAMETER = "query";
        public const string STATE_PARAMETER = "state";

        // 인증. login은 토큰 없이, logout은 현재 어드민 토큰으로 부른다
        public const string AUTH_LOGIN  = PREFIX + "/auth/login";
        public const string AUTH_LOGOUT = PREFIX + "/auth/logout";

        // 운영자 계정 관리(Owner)
        public const string OPERATORS               = PREFIX + "/operators";
        public const string OPERATOR_DISABLE        = PREFIX + "/operators/{adminId}/disable";
        public const string OPERATOR_DISABLE_FORMAT = PREFIX + "/operators/{0}/disable";

        // 계정 조회와 강제 종료, 개인 재화 조정. 초대 코드 원문 검색은 조회 문자열이 아닌 ACCOUNTS_SEARCH 본문으로만 받는다
        public const string ACCOUNTS                   = PREFIX + "/accounts";
        public const string ACCOUNTS_SEARCH            = PREFIX + "/accounts/search";
        public const string ACCOUNT                    = PREFIX + "/accounts/{accountId}";
        public const string ACCOUNT_FORMAT             = PREFIX + "/accounts/{0}";
        public const string ACCOUNT_KICK               = PREFIX + "/accounts/{accountId}/kick";
        public const string ACCOUNT_KICK_FORMAT        = PREFIX + "/accounts/{0}/kick";
        public const string ACCOUNT_ADJUSTMENTS        = PREFIX + "/accounts/{accountId}/adjustments";
        public const string ACCOUNT_ADJUSTMENTS_FORMAT = PREFIX + "/accounts/{0}/adjustments";

        // 초대 코드 발급, 폐기
        public const string INVITES              = PREFIX + "/invites";
        public const string INVITE_REVOKE        = PREFIX + "/invites/{inviteId}/revoke";
        public const string INVITE_REVOKE_FORMAT = PREFIX + "/invites/{0}/revoke";

        // 점검, 기능 차단, 허용 빌드와 공지
        public const string SERVICE_STATE     = PREFIX + "/service-state";
        public const string NOTICES           = PREFIX + "/notices";
        public const string NOTICE_END        = PREFIX + "/notices/{noticeId}/end";
        public const string NOTICE_END_FORMAT = PREFIX + "/notices/{0}/end";

        // 운영자 지급(우편함)
        public const string GRANTS                = PREFIX + "/grants";
        public const string GRANT                 = PREFIX + "/grants/{grantId}";
        public const string GRANT_FORMAT          = PREFIX + "/grants/{0}";
        public const string GRANT_EXECUTE         = PREFIX + "/grants/{grantId}/execute";
        public const string GRANT_EXECUTE_FORMAT  = PREFIX + "/grants/{0}/execute";
        public const string GRANT_CANCEL          = PREFIX + "/grants/{grantId}/cancel";
        public const string GRANT_CANCEL_FORMAT   = PREFIX + "/grants/{0}/cancel";

        // 로그 조회
        public const string LEDGER                = PREFIX + "/ledger";
        public const string ITEM_HISTORY          = PREFIX + "/items/{itemId}/history";
        public const string ITEM_HISTORY_FORMAT   = PREFIX + "/items/{0}/history";
        public const string SESSION_EVENTS        = PREFIX + "/sessions/{sessionId}/events";
        public const string SESSION_EVENTS_FORMAT = PREFIX + "/sessions/{0}/events";
        public const string OPERATION             = PREFIX + "/operations/{actorId}/{operationId}";
        public const string OPERATION_FORMAT      = PREFIX + "/operations/{0}/{1}";
        public const string AUDIT                 = PREFIX + "/audit";

        // 복구 대상 세션 목록과 수동 복구
        public const string SESSIONS                = PREFIX + "/sessions";
        public const string SESSION_RECOVERY        = PREFIX + "/sessions/{sessionId}/recovery";
        public const string SESSION_RECOVERY_FORMAT = PREFIX + "/sessions/{0}/recovery";
    }
}
