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

            public const int NONE = 0;

            // 전투 행동
            public const int DEAD                = 100;
            public const int WRONG_PHASE         = 101;
            public const int COOLDOWN            = 102;
            public const int INSUFFICIENT_ENERGY = 103;
            public const int INVALID_TARGET      = 104;

            // 물약
            public const int FULL_HEALTH    = 200;
            public const int DISABLED       = 201;
            public const int POTION_LIMIT   = 202;
            public const int PENDING_COMMIT = 203;

            // 부활
            public const int TOO_EARLY        = 300;
            public const int NO_SAFE_POINT    = 301;
            public const int NO_REVIVES       = 302;
            public const int SESSION_TERMINAL = 303;

            // 출정 준비
            public const int LOCKED               = 400;
            public const int MISSING_UNLOCK       = 401;
            public const int INSUFFICIENT_TICKET  = 402;
            public const int STALE_REVISION       = 403;
            public const int NOT_LEADER           = 404;
            public const int NOT_ALL_READY        = 405;
            public const int NO_CAPACITY          = 406;
            public const int RESERVATION_CONFLICT = 407;

            // 성장, 제작, 계승
            public const int WRONG_LOCATION      = 500;
            public const int INVENTORY_FULL      = 501;
            public const int INSUFFICIENT_ITEM   = 502;
            public const int BOUND_ITEM          = 503;
            public const int INVALID_INHERITANCE = 504;

            // 채집
            public const int OUT_OF_RANGE        = 600;
            public const int OCCUPIED            = 601;
            public const int INSUFFICIENT_ACTION = 602;

            // 경매
            public const int NOT_ELIGIBLE          = 700;
            public const int CLOSED                = 701;
            public const int BID_TOO_LOW           = 702;
            public const int INSUFFICIENT_CURRENCY = 703;

            // 인증, 접속
            public const int INVALID_INVITE_CODE = 800;
            public const int INVITE_CODE_EXPIRED = 801;
            public const int INVITE_CODE_USED    = 802;
            public const int TOKEN_EXPIRED       = 803;
            public const int TOKEN_REVOKED       = 804;
            public const int VERSION_MISMATCH    = 805;
            public const int BUILD_NOT_ALLOWED   = 806;
            public const int MAINTENANCE         = 807;
            public const int SANCTIONED          = 808;
            public const int ALREADY_CONNECTED   = 809;
            public const int BACKEND_UNAVAILABLE = 810;
            public const int RATE_LIMITED        = 811;

            // 정산, 공통
            public const int ALREADY_SETTLED           = 900;
            public const int SESSION_MISMATCH          = 901;
            public const int IDEMPOTENCY_CONFLICT      = 902;
            public const int SETTLEMENT_LIMIT_EXCEEDED = 904;

            // 캐릭터 생성
            public const int INVALID_APPEARANCE     = 1000;
            public const int CHARACTER_NAME_TAKEN   = 1001;
            public const int INVALID_CHARACTER_NAME = 1002;

            // 캐릭터 삭제. 다른 계정의 캐릭터와 이미 삭제한 캐릭터도 같은 코드다
            public const int CHARACTER_NOT_FOUND    = 1003;

            // 캐릭터 삭제 예약. 예약 뒤 대기 시간이 지나야 삭제할 수 있다
            public const int CHARACTER_DELETE_PENDING       = 1004;
            public const int CHARACTER_DELETE_NOT_REQUESTED = 1005;

            // 운영, 우편함
            public const int FEATURE_DISABLED         = 1100;
            public const int INBOX_EXPIRED            = 1101;
            public const int INBOX_ALREADY_CLAIMED    = 1102;
            public const int INBOX_CANCELLED          = 1103;
            public const int INBOX_CHARACTER_REQUIRED = 1104;
            public const int INBOX_NOT_FOUND          = 1105;

            // 계정 가입, 아이디와 비밀번호 로그인. EMAIL_TAKEN은 이메일 인증 기능에서 쓰려고 번호만 잡아 둔다
            public const int LOGIN_ID_TAKEN      = 1200;
            public const int EMAIL_TAKEN         = 1201;
            public const int INVALID_LOGIN_ID    = 1202;
            public const int INVALID_EMAIL       = 1203;
            public const int WEAK_PASSWORD       = 1204;
            public const int INVALID_CREDENTIALS = 1205;
            public const int ACCOUNT_LOCKED      = 1206;

            // 마을 파티(방 목록, 비밀번호 방, 초대)
            public const int ROOM_NOT_FOUND         = 1300;
            public const int ROOM_PASSWORD_MISMATCH = 1301;
            public const int INVITE_EXPIRED         = 1302;

            // 유저 경매장
            public const int MARKET_LISTING_LIMIT         = 1400;
            public const int MARKET_NOT_TRADABLE          = 1401;
            public const int MARKET_PRICE_OUT_OF_RANGE    = 1402;
            public const int MARKET_SOLD_OUT              = 1403;
            public const int MARKET_QUANTITY_CHANGED      = 1404;
            public const int MARKET_PRICE_CHANGED         = 1405;
            public const int MARKET_EXPIRED               = 1406;
            public const int MARKET_SELF_PURCHASE         = 1407;
            public const int MARKET_ESCROWED              = 1408;
            public const int MARKET_NOT_OWNER             = 1409;
            public const int CHARACTER_HAS_MARKET_LISTING = 1410;

            // 1:1 거래
            public const int TRADE_ITEM_CHANGED          = 1500;
            public const int TRADE_NOT_TRADABLE          = 1501;
            public const int TRADE_INVENTORY_FULL        = 1502;
            public const int TRADE_INSUFFICIENT_CURRENCY = 1503;
            public const int TRADE_SAME_ACCOUNT          = 1504;
            public const int TRADE_NOT_IN_TOWN           = 1505;
            public const int TRADE_BUSY                  = 1506;
            public const int TRADE_TOO_FAR               = 1507;
            public const int TRADE_REQUEST_EXPIRED       = 1508;
            public const int TRADE_EXPIRED               = 1509;

            // 친구, 귓속말, 길드
            public const int SOCIAL_TARGET_NOT_FOUND     = 1600;
            public const int SOCIAL_TARGET_SELF          = 1601;
            public const int SOCIAL_NOT_CONNECTED        = 1602;
            public const int FRIEND_ALREADY              = 1610;
            public const int FRIEND_LIMIT                = 1611;
            public const int FRIEND_TARGET_LIMIT         = 1612;
            public const int FRIEND_REQUEST_LIMIT        = 1613;
            public const int FRIEND_TARGET_REQUESTS_FULL = 1614;
            public const int FRIEND_REQUEST_NOT_FOUND    = 1615;
            public const int WHISPER_TARGET_OFFLINE      = 1620;
            public const int GUILD_NAME_INVALID          = 1630;
            public const int GUILD_NAME_TAKEN            = 1631;
            public const int GUILD_ALREADY_MEMBER        = 1632;
            public const int GUILD_TARGET_IN_GUILD       = 1633;
            public const int GUILD_NOT_FOUND             = 1634;
            public const int GUILD_NOT_MEMBER            = 1635;
            public const int GUILD_NOT_LEADER            = 1636;
            public const int GUILD_FULL                  = 1637;
            public const int GUILD_LEADER_MUST_DELEGATE  = 1638;
            public const int GUILD_APPLICATION_NOT_FOUND = 1639;
            public const int GUILD_INVITE_NOT_FOUND      = 1640;
            public const int GUILD_APPLICATION_LIMIT     = 1641;
            public const int GUILD_PENDING_FULL          = 1642;
            public const int CHARACTER_IS_GUILD_LEADER   = 1643;
            public const int GUILD_TARGET_INVITES_FULL   = 1644;
            public const int GUILD_LEADER_CHANGED        = 1645;
        }

        /// <summary> EEconomyReason과 같은 값, 경제 원장에 기록되므로 기존 값을 바꾸거나 재배열하지 않는다 </summary>
        public static class EconomyReason
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int SESSION_START_TICKET   = 1;
            public const int SESSION_POTION_USE     = 2;
            public const int SESSION_SETTLEMENT     = 3;
            public const int SERVICE_CANCEL_REFUND  = 4;
            public const int SERVICE_CANCEL_ESSENCE = 5;
            public const int SESSION_REWARD         = 6;
            public const int BOSS_REWARD            = 7;
            public const int AUCTION_RESERVE        = 8;
            public const int AUCTION_RELEASE        = 9;
            public const int AUCTION_SETTLE         = 10;
            public const int AUCTION_PAYOUT         = 11;
            public const int TRAINING               = 12;
            public const int CRAFT                  = 13;
            public const int ENHANCE                = 14;
            public const int INHERIT                = 15;
            public const int TALISMAN_REGISTER      = 16;
            public const int TALISMAN_UPGRADE       = 17;
            public const int DISMANTLE              = 18;
            public const int GATHERING              = 19;
            public const int POTION_USE             = 20;
            public const int POTION_RETURN          = 21;
            public const int TICKET_CONSUME         = 22;
            public const int TICKET_REFUND          = 23;
            public const int INBOX_CLAIM            = 24;
            public const int INITIAL_GRANT          = 25;
            public const int ADMIN_GRANT            = 26;
            public const int ADMIN_REVOKE           = 27;
            public const int EVENT_REWARD           = 28;
            public const int COUPON                 = 29;
            public const int RECOVERY_COMPENSATION  = 30;
            public const int MARKET_ESCROW          = 31;
            public const int MARKET_RETURN          = 32;
            public const int MARKET_PURCHASE        = 33;
            public const int MARKET_PROCEEDS        = 34;
            public const int MARKET_RECEIVE         = 35;
            public const int TRADE_GIVE             = 36;
            public const int TRADE_RECEIVE          = 37;
            public const int REROLL_OPTIONS         = 38;
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
            public const int SWORDSMAN  = 3;
            public const int MAGEKNIGHT = 4;
            public const int ARCHER     = 5;
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
