namespace Shinshi.Contracts.Admin
{
    /// <summary> 어드민 와이어 계약의 숫자 코드와 입력 한도. Unity가 읽지 않으므로 게임 거절 코드와 겹치지 않는 대역을 쓴다 </summary>
    public static class AdminContractCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 쓰기 요청 reason의 최대 길이. 감사 로그 CHECK와 같다
        public const int MAX_REASON_LENGTH = 500;

        /// <summary> 서버 EAdminRole과 같은 값. 높을수록 권한이 크고 낮은 역할의 권한을 모두 가진다 </summary>
        public static class Role
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int VIEWER   = 0;
            public const int OPERATOR = 1;
            public const int ADMIN    = 2;
            public const int OWNER    = 3;
        }

        /// <summary> ApiRejection과 어드민 작업 OperationResult의 reasonCode. 어드민 bearer 거절(401)은 게임과 같은 TOKEN_REVOKED, 요청 한도는 RATE_LIMITED를 쓴다 </summary>
        public static class RejectReason
        {
            /****************************************
            *                Fields
            ****************************************/

            // 로그인 실패. 없는 계정, 비밀번호, TOTP, 재사용, 잠금, 비활성을 구분하지 않는다
            public const int INVALID_CREDENTIALS = 9001;

            // 토큰은 유효하지만 역할이 부족하다(403)
            public const int FORBIDDEN_ROLE      = 9002;

            // operationId, reason 누락이나 본문 형식 오류(400)
            public const int INVALID_REQUEST     = 9003;

            // 대상 운영자, 계정, 지급 등이 없다
            public const int NOT_FOUND           = 9004;

            // 대상의 현재 상태에서 할 수 없는 작업이다(이미 비활성, 이미 실행한 지급 등)
            public const int INVALID_STATE       = 9005;

            // 같은 로그인 이름의 운영자가 이미 있다
            public const int LOGIN_NAME_TAKEN    = 9006;

            // 자기 자신을 비활성화할 수 없다. 마지막 Owner가 스스로 잠기는 것을 막는다
            public const int SELF_TARGET         = 9007;
        }
    }
}
