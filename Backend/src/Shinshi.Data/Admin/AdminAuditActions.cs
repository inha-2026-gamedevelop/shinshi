namespace Shinshi.Data.Admin
{
    /// <summary> 감사 로그 action과 target_kind 문자열. 조회 필터가 이 값으로 찾으므로 기존 값을 바꾸지 않는다 </summary>
    public static class AdminAuditActions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string BOOTSTRAP_OWNER  = "admin.bootstrap.owner";
        public const string LOGIN            = "admin.auth.login";
        public const string LOGOUT           = "admin.auth.logout";
        public const string LOCKOUT          = "admin.auth.lockout";
        public const string OPERATOR_CREATE  = "admin.operator.create";
        public const string OPERATOR_DISABLE = "admin.operator.disable";

        public const string TARGET_ADMIN_USER = "admin_user";
    }
}
