namespace Shinshi.Data.Admin
{
    /// <summary> 계정 조회, 강제 종료, 초대 감사 로그의 action, target_kind. 조회 필터가 이 값으로 찾으므로 기존 값을 바꾸지 않는다 </summary>
    public static class AdminAccountAuditActions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ACCOUNT_VIEW  = "admin.account.view";
        public const string ACCOUNT_KICK  = "admin.account.kick";
        public const string INVITE_ISSUE  = "admin.invite.issue";
        public const string INVITE_REVOKE = "admin.invite.revoke";

        public const string TARGET_ACCOUNT = "account";
        public const string TARGET_INVITE  = "auth_invite";
    }
}
