namespace Shinshi.Data.Admin
{
    /// <summary> 서비스 상태와 공지 감사 로그의 action, target_kind. 조회 필터가 이 값으로 찾으므로 기존 값을 바꾸지 않는다 </summary>
    public static class AdminServiceStateAuditActions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SERVICE_STATE_UPDATE = "admin.service_state.update";
        public const string NOTICE_CREATE        = "admin.notice.create";
        public const string NOTICE_END           = "admin.notice.end";

        public const string TARGET_SERVICE_STATE = "service_state";
        public const string TARGET_NOTICE        = "service_notice";

        // service_state는 id = 1 단일 행이다
        public const string SERVICE_STATE_ID = "1";
    }
}
