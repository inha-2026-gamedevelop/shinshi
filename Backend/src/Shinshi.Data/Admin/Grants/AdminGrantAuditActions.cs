namespace Shinshi.Data.Admin.Grants
{
    /// <summary> 지급, 개인 조정의 감사 action과 target_kind. 작업 종류(operation_kind)로도 쓰므로 기존 값을 바꾸지 않는다 </summary>
    public static class AdminGrantAuditActions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string GRANT_CREATE   = "admin.grant.create";
        public const string GRANT_EXECUTE  = "admin.grant.execute";
        public const string GRANT_CANCEL   = "admin.grant.cancel";
        public const string ACCOUNT_ADJUST = "admin.account.adjust";

        public const string TARGET_GRANT   = "admin_grant";
        public const string TARGET_ACCOUNT = "account";

        // 조정의 원장 출처. source_id는 그 조정의 operationId다(조정은 별도 개체 행이 없다)
        public const string ADJUSTMENT_SOURCE_TYPE = "admin_adjustment";
    }
}
