namespace Shinshi.AdminCli
{
    /// <summary> 명령별 옵션 이름. 도움말과 허용 목록 검사가 같은 상수를 쓴다 </summary>
    internal static class AdminCliOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        // 쓰기 공통
        public const string REASON = "reason";

        // 로그인, 운영자
        public const string USER = "user";
        public const string ROLE = "role";

        // 초대, 지급, 조정
        public const string ACCOUNT           = "account";
        public const string ACCOUNTS          = "accounts";
        public const string MAX_USES          = "max-uses";
        public const string HOURS             = "hours";
        public const string REWARD            = "reward";
        public const string CLAIM_DAYS        = "claim-days";
        public const string SOURCE_KIND       = "source-kind";
        public const string SOURCE_REF        = "source-ref";
        public const string ASSET             = "asset";
        public const string DELTA             = "delta";
        public const string CHARACTER         = "character";

        // 서비스 상태, 공지
        public const string MAINTENANCE       = "maintenance";
        public const string DISABLE           = "disable";
        public const string ALLOW_BUILDS      = "allow-builds";
        public const string EXPECTED_REVISION = "expected-revision";
        public const string TITLE             = "title";
        public const string BODY              = "body";
        public const string TARGET            = "target";
        public const string SEVERITY          = "severity";
        public const string STARTS            = "starts";
        public const string ENDS              = "ends";

        // 로그 조회
        public const string SOURCE_TYPE       = "source-type";
        public const string SOURCE_ID         = "source-id";
        public const string ASSET_KIND        = "asset-kind";
        public const string ASSET_ID          = "asset-id";
        public const string ECONOMY_REASON    = "economy-reason";
        public const string FROM              = "from";
        public const string TO                = "to";
        public const string LIMIT             = "limit";
        public const string CURSOR            = "cursor";
        public const string ADMIN             = "admin";
        public const string TARGET_KIND       = "target-kind";
        public const string TARGET_ID         = "target-id";

        // 세션 복구
        public const string STATE             = "state";
        public const string ACTION            = "action";
    }
}
