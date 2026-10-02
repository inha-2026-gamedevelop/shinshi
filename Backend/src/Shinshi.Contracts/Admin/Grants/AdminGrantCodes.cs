namespace Shinshi.Contracts.Admin
{
    /// <summary> 운영자 지급, 우편함, 개인 조정의 문자열 값과 입력 한도. 서버 검증과 명령줄 도구가 같은 값을 쓴다 </summary>
    public static class AdminGrantCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 지급 출처. reward_inbox.source_kind와 admin_grants.source_kind CHECK 값이다
        public const string SOURCE_ADMIN_GRANT           = "AdminGrant";
        public const string SOURCE_RECOVERY_COMPENSATION = "RecoveryCompensation";

        // 지급 상태. Draft -> Executed -> Cancelled, Draft -> Cancelled
        public const string STATE_DRAFT     = "Draft";
        public const string STATE_EXECUTED  = "Executed";
        public const string STATE_CANCELLED = "Cancelled";

        // 자산 종류. economy_ledger.asset_kind와 같다. 행동력은 지급 대상이 아니다
        public const string ASSET_CURRENCY     = "Currency";
        public const string ASSET_TICKETS      = "Tickets";
        public const string ASSET_ESSENCE      = "Essence";
        public const string ASSET_SHARDS       = "Shards";
        public const string ASSET_ACCOUNT_ITEM = "AccountItem";

        // 지급 한 건의 대상 계정 수와 보상 줄 수. admin_grants, reward_inbox CHECK와 같다
        public const int MAX_TARGET_ACCOUNTS = 1000;
        public const int MAX_REWARD_LINES    = 50;

        // TODO(민성): 운영 지급 상한은 기획 미정이다. 오타 한 번(0 하나 더)이 경제를 무너뜨리지 않게 잡은 임시값이다
        public const long MAX_REWARD_QUANTITY = 1000000;
        public const long MAX_ADJUSTMENT      = 1000000;

        // 수령 기한(일). 생략하면 기본값이다
        public const int DEFAULT_CLAIM_DAYS = 30;
        public const int MIN_CLAIM_DAYS     = 1;
        public const int MAX_CLAIM_DAYS     = 365;

        // source_ref 최대 길이. DB CHECK와 같다
        public const int MAX_SOURCE_REF_LENGTH = 200;
    }
}
