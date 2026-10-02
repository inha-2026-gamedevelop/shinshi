namespace Shinshi.Contracts.Admin
{
    /// <summary> 로그 조회와 복구 대상 목록의 조회 문자열 이름과 페이지 한도. 시각은 UTC 유닉스 ms이며 from은 포함, to는 제외한다 </summary>
    public static class AdminLogQueryParameters
    {
        /****************************************
        *                Fields
        ****************************************/

        // 민감 조회 감사 사유(문자열, 1..500자, 필수). ledger, items/{itemId}/history, sessions/{sessionId}/events,
        // operations/{actorId}/{operationId}는 사유가 없으면 400이고, 데이터를 읽기 전에 감사 행을 커밋한다.
        // 계정 상세(AdminAccountQuery.REASON_PARAMETER)와 같은 이름이다
        public const string REASON       = "reason";

        // GET /admin/v1/ledger. accountId 또는 sourceType과 sourceId 둘 다 중 하나는 있어야 한다(전체 스캔 방지).
        // reasonCode는 원장 사유 코드(ContractCodes.EconomyReason 정수) 필터다
        public const string ACCOUNT_ID   = "accountId";
        public const string CHARACTER_ID = "characterId";
        public const string ASSET_KIND   = "assetKind";
        public const string ASSET_ID     = "assetId";
        public const string REASON_CODE  = "reasonCode";
        public const string SOURCE_TYPE  = "sourceType";
        public const string SOURCE_ID    = "sourceId";

        // GET /admin/v1/audit. targetId는 targetKind와 함께만 쓴다
        public const string ADMIN_ID    = "adminId";
        public const string TARGET_KIND = "targetKind";
        public const string TARGET_ID   = "targetId";

        // 기간과 페이지. cursor는 앞 페이지 응답의 nextCursor를 그대로 넘기는 불투명 문자열이다
        public const string FROM_UNIX_MS = "fromUnixMs";
        public const string TO_UNIX_MS   = "toUnixMs";
        public const string CURSOR       = "cursor";
        public const string LIMIT        = "limit";

        public const int DEFAULT_LIMIT = 100;
        public const int MAX_LIMIT     = 500;
    }
}
