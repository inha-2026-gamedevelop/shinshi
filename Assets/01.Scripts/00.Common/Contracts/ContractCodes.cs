namespace Shinshi.Contracts
{
    /// <summary> DTO의 정수 코드 값, Shinshi.Common의 같은 이름 enum과 값이 일치해야 한다 </summary>
    public static class ContractCodes
    {
        // 이 어셈블리는 백엔드와 함께 쓰려고 Shinshi.Runtime을 참조하지 않으므로 Runtime의 enum을 볼 수 없다.
        // 백엔드가 숫자를 직접 적지 않도록 같은 값을 상수로 둔다. enum 자체를 옮기면 위치와 네임스페이스가 바뀌어
        // 그 enum을 쓰는 모든 파트 코드가 함께 바뀌므로 옮기지 않았다.
        // 한쪽만 고치면 클라이언트와 API가 같은 숫자를 다른 뜻으로 읽는다
        // TODO(민성): enum과 이 상수의 이름, 값 일치를 EditMode 테스트로 묶는다

        /// <summary> EOperationStatus와 같은 값, PENDING은 수락만 했을 뿐 성공이 아니다 </summary>
        public static class OperationStatus
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int PENDING   = 0;
            public const int COMMITTED = 1;
            public const int REJECTED  = 2;
        }

        /// <summary> ERejectReason과 같은 값, 기존 값을 바꾸거나 재배열하지 않는다 </summary>
        public static class RejectReason
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int NONE                  = 0;

            // 전투 행동
            public const int DEAD                  = 100;
            public const int WRONG_PHASE           = 101;
            public const int COOLDOWN              = 102;
            public const int INSUFFICIENT_ENERGY   = 103;
            public const int INVALID_TARGET        = 104;

            // 물약
            public const int FULL_HEALTH           = 200;
            public const int DISABLED              = 201;
            public const int POTION_LIMIT          = 202;
            public const int PENDING_COMMIT        = 203;

            // 부활
            public const int TOO_EARLY             = 300;
            public const int NO_SAFE_POINT         = 301;
            public const int NO_REVIVES            = 302;
            public const int SESSION_TERMINAL      = 303;

            // 출정 준비
            public const int LOCKED                = 400;
            public const int MISSING_UNLOCK        = 401;
            public const int INSUFFICIENT_TICKET   = 402;
            public const int STALE_REVISION        = 403;
            public const int NOT_LEADER            = 404;
            public const int NOT_ALL_READY         = 405;
            public const int NO_CAPACITY           = 406;
            public const int RESERVATION_CONFLICT  = 407;

            // 성장, 제작, 계승
            public const int WRONG_LOCATION        = 500;
            public const int INVENTORY_FULL        = 501;
            public const int INSUFFICIENT_ITEM     = 502;
            public const int BOUND_ITEM            = 503;
            public const int INVALID_INHERITANCE   = 504;

            // 채집
            public const int OUT_OF_RANGE          = 600;
            public const int OCCUPIED              = 601;
            public const int INSUFFICIENT_ACTION   = 602;

            // 경매
            public const int NOT_ELIGIBLE          = 700;
            public const int CLOSED                = 701;
            public const int BID_TOO_LOW           = 702;
            public const int INSUFFICIENT_CURRENCY = 703;

            // 인증, 접속
            public const int INVALID_INVITE_CODE   = 800;
            public const int INVITE_CODE_EXPIRED   = 801;
            public const int INVITE_CODE_USED      = 802;
            public const int TOKEN_EXPIRED         = 803;
            public const int TOKEN_REVOKED         = 804;
            public const int VERSION_MISMATCH      = 805;
            public const int BUILD_NOT_ALLOWED     = 806;
            public const int MAINTENANCE           = 807;
            public const int SANCTIONED            = 808;

            // 정산, 공통
            public const int ALREADY_SETTLED       = 900;
            public const int SESSION_MISMATCH      = 901;
            public const int IDEMPOTENCY_CONFLICT  = 902;

            // 캐릭터 생성
            public const int INVALID_APPEARANCE    = 1000;
        }

        /// <summary> EPlayerClass와 같은 값, 캐릭터마다 고정 </summary>
        public static class PlayerClass
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int WARRIOR  = 0;
            public const int SORCERER = 1;
            public const int SUMMONER = 2;
        }

        /// <summary> EServerRole과 같은 값 </summary>
        public static class ServerRole
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int TOWN    = 0;
            public const int DUNGEON = 1;
        }
    }
}
