namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 조회 경로의 조회 문자열 이름과 검색 일치 종류. 경로 자체는 AdminApiRoutes에 있다 </summary>
    public static class AdminAccountQuery
    {
        /****************************************
        *                Fields
        ****************************************/

        // GET /admin/v1/accounts/{accountId}?reason= . 계정 상세는 민감 조회라 감사 사유가 필수다
        public const string REASON_PARAMETER = "reason";

        // AdminAccountSummary.matchedBy 값. 초대 코드는 원문이 아니라 HMAC으로 찾는다
        public const string MATCH_ACCOUNT_ID     = "AccountId";
        public const string MATCH_CHARACTER_ID   = "CharacterId";
        public const string MATCH_CHARACTER_NAME = "CharacterName";
        public const string MATCH_INVITE_CODE    = "InviteCode";
    }
}
