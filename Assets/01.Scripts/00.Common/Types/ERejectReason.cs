namespace Shinshi.Common
{
    /// <summary> 거절된 요청의 사유 코드, 값은 API와 주고받으므로 기존 값을 바꾸거나 재배열하지 않는다 </summary>
    public enum ERejectReason
    {
        None                 = 0,

        // 전투 행동
        Dead                 = 100,
        WrongPhase           = 101,
        Cooldown             = 102,
        InsufficientEnergy   = 103,
        InvalidTarget        = 104,

        // 물약
        FullHealth           = 200,
        Disabled             = 201,
        PotionLimit          = 202,
        PendingCommit        = 203,

        // 부활
        TooEarly             = 300,
        NoSafePoint          = 301,
        NoRevives            = 302,
        SessionTerminal      = 303,

        // 출정 준비
        Locked               = 400,
        MissingUnlock        = 401,
        InsufficientTicket   = 402,
        StaleRevision        = 403,
        NotLeader            = 404,
        NotAllReady          = 405,
        NoCapacity           = 406,
        ReservationConflict  = 407,

        // 성장, 제작, 계승
        WrongLocation        = 500,
        InventoryFull        = 501,
        InsufficientItem     = 502,
        BoundItem            = 503,
        InvalidInheritance   = 504,

        // 채집
        OutOfRange           = 600,
        Occupied             = 601,
        InsufficientAction   = 602,

        // 경매
        NotEligible          = 700,
        Closed               = 701,
        BidTooLow            = 702,
        InsufficientCurrency = 703,

        // 인증, 접속
        InvalidInviteCode    = 800,
        InviteCodeExpired    = 801,
        InviteCodeUsed       = 802,
        TokenExpired         = 803,
        TokenRevoked         = 804, // 회전으로 바뀐 이전 토큰, 로그아웃이나 기기 변경으로 폐기된 세션
        VersionMismatch      = 805, // protocolVersion이나 contentHash가 서버와 다르다
        BuildNotAllowed      = 806, // 허용 buildId 목록에 없다
        Maintenance          = 807,
        Sanctioned           = 808,
        AlreadyConnected     = 809, // 같은 계정이 이미 접속 중이다
        BackendUnavailable   = 810, // 백엔드 API에 일시적으로 닿지 않는다, 클라이언트 탓이 아니다
        RateLimited          = 811,

        // 정산, 공통
        AlreadySettled       = 900,
        SessionMismatch      = 901,
        IdempotencyConflict  = 902,
        SettlementLimitExceeded = 904,

        // 캐릭터 생성
        InvalidAppearance    = 1000, // 외형 구조, 값 범위, 팔레트, 직업별 허용 헤어 중 하나가 맞지 않는다
        CharacterNameTaken   = 1001, // 전각, 반각과 대소문자만 다른 이름도 같은 이름이다
        InvalidCharacterName = 1002, // 정규화 뒤 2~12자의 완성형 한글, 영문, 숫자가 아니다

        // 운영, 우편함
        FeatureDisabled        = 1100, // 운영자가 기능 차단 스위치로 끈 기능이다
        InboxExpired           = 1101,
        InboxAlreadyClaimed    = 1102,
        InboxCancelled         = 1103, // 운영자가 미수령 지급을 취소했다
        InboxCharacterRequired = 1104, // 캐릭터 귀속 자산이 있어 수령할 캐릭터를 골라야 한다
        InboxNotFound          = 1105,

        // 계정 가입, 아이디와 비밀번호 로그인
        LoginIdTaken           = 1200,
        EmailTaken             = 1201, // 이메일 인증 기능에서 쓰려고 번호만 잡아 둔다
        InvalidLoginId         = 1202,
        InvalidEmail           = 1203,
        WeakPassword           = 1204,
        InvalidCredentials     = 1205, // 아이디가 없든 비밀번호가 틀리든 같은 거절이다
        AccountLocked          = 1206  // 이 IP에서 같은 아이디로 연속 실패해 잠시 막혔다. 다른 IP는 막히지 않는다
    }
}
