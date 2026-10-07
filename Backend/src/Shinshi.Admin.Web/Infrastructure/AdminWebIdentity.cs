namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 화면이 표시할 수 있는 운영자 정보. API 토큰과 브라우저 세션 ID를 포함하지 않는다 </summary>
    public sealed class AdminWebIdentity
    {
        /****************************************
        *              Properties
        ****************************************/

        public string AdminId { get; }
        public string LoginName { get; }
        public int Role { get; }
        public long ExpiresAtUnixMs { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal AdminWebIdentity(string adminId, string loginName, int role, long expiresAtUnixMs)
        {
            AdminId         = adminId;
            LoginName       = loginName;
            Role            = role;
            ExpiresAtUnixMs = expiresAtUnixMs;
        }
    }
}
