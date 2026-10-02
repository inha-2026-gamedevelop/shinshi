namespace Shinshi.Data.Admin
{
    /// <summary> 로그 조회(민감 조회) 감사 action과 target_kind. 조회 필터가 이 값으로 찾으므로 바꾸지 않는다 </summary>
    public static class AdminLogAuditActions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string LEDGER_VIEW         = "admin.ledger.view";
        public const string ITEM_HISTORY_VIEW   = "admin.item.history.view";
        public const string SESSION_EVENTS_VIEW = "admin.session.events.view";
        public const string OPERATION_VIEW      = "admin.operation.view";

        // 원인으로 찾는 원장 조회의 대상. target_id는 "sourceType:sourceId"다
        public const string TARGET_LEDGER_SOURCE = "ledger_source";
        public const string TARGET_ITEM          = "item";

        // 작업 조회의 대상. target_id는 "actorId/operationId"다
        public const string TARGET_OPERATION     = "operation";
    }
}
