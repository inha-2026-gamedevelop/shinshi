namespace Shinshi.Data.Admin
{
    /// <summary> 계정 토큰 일괄 폐기에서 이번에 새로 폐기한 행 수. 감사 로그 상태로 그대로 남긴다 </summary>
    public sealed class AdminRevokedTokens
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Families { get; }
        public int RefreshTokens { get; }
        public int AccessTokens { get; }
        public int GameSessions { get; }
        public int JoinTokens { get; }
        public int Invites { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminRevokedTokens(int families, int refreshTokens, int accessTokens, int gameSessions, int joinTokens,
            int invites)
        {
            Families      = families;
            RefreshTokens = refreshTokens;
            AccessTokens  = accessTokens;
            GameSessions  = gameSessions;
            JoinTokens    = joinTokens;
            Invites       = invites;
        }
    }
}
