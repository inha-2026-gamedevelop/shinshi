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

        // 정산, 공통
        AlreadySettled       = 900,
        SessionMismatch      = 901,
        IdempotencyConflict  = 902,

        // 캐릭터 생성
        InvalidAppearance    = 1000  // 외형 구조, 값 범위, 팔레트, 직업별 허용 헤어 중 하나가 맞지 않는다
    }
}
