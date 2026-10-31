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
        CharacterNotFound    = 1003, // 없거나 다른 계정이거나 이미 삭제한 캐릭터다
        CharacterDeletePending      = 1004, // 삭제 예약 뒤 대기 시간이 아직 지나지 않았다
        CharacterDeleteNotRequested = 1005, // 삭제 예약을 하지 않은 캐릭터라 바로 삭제하거나 예약을 취소할 수 없다

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
        AccountLocked          = 1206, // 이 IP에서 같은 아이디로 연속 실패해 잠시 막혔다. 다른 IP는 막히지 않는다

        // 마을 파티
        RoomNotFound           = 1300, // 방이 없거나 이미 출정했다
        RoomPasswordMismatch   = 1301,
        InviteExpired          = 1302, // 초대를 받은 지 30초가 지났다

        // 유저 경매장
        MarketListingLimit     = 1400, // 계정 동시 등록 10건을 넘는다
        MarketNotTradable      = 1401, // 거래 가능 품목이 아니다
        MarketPriceOutOfRange  = 1402, // 개당 가격이나 수량이 범위 밖이다
        MarketSoldOut          = 1403, // 이미 다 팔렸거나 취소됐다
        MarketQuantityChanged  = 1404, // 남은 수량이 요청보다 적다
        MarketPriceChanged     = 1405, // 개당 가격이 확인한 값과 다르다
        MarketExpired          = 1406, // 등록 기한이 지났다
        MarketSelfPurchase     = 1407, // 같은 계정의 등록은 살 수 없다
        MarketEscrowed         = 1408, // 경매장에 보관 중인 장비라 다른 작업을 할 수 없다
        MarketNotOwner         = 1409, // 본인 소유나 본인 등록이 아니다
        CharacterHasMarketListing = 1410, // 활성 장비 등록이 있는 캐릭터는 삭제할 수 없다

        // 1:1 거래
        TradeItemChanged          = 1500, // 제안한 품목이나 수량이 실행 시점에 맞지 않는다
        TradeNotTradable          = 1501,
        TradeInventoryFull        = 1502, // 받는 쪽 공간이 부족하다
        TradeInsufficientCurrency = 1503,
        TradeSameAccount          = 1504,
        TradeNotInTown            = 1505, // 접속 임대가 이 마을에 없다
        TradeBusy                 = 1506, // 이미 진행 중인 실행이 있다
        TradeTooFar               = 1507,
        TradeRequestExpired       = 1508, // 거래 요청 수락 시간이 지났다
        TradeExpired              = 1509, // prepare 뒤 120초가 지났다

        // 친구, 귓속말, 길드
        SocialTargetNotFound      = 1600, // 이름이나 ID의 살아 있는 캐릭터가 없다
        SocialTargetSelf          = 1601,
        SocialNotConnected        = 1602, // 행위 캐릭터의 임대가 호출 서버에 없다
        FriendAlready             = 1610,
        FriendLimit               = 1611, // 내 친구가 50명이다
        FriendTargetLimit         = 1612, // 상대 친구가 50명이다
        FriendRequestLimit        = 1613, // 보낸 대기 요청이 20건이다
        FriendTargetRequestsFull  = 1614, // 상대가 받은 대기 요청이 50건이다
        FriendRequestNotFound     = 1615, // 요청이 없거나 만료됐다
        WhisperTargetOffline      = 1620,
        GuildNameInvalid          = 1630,
        GuildNameTaken            = 1631, // 살아 있는 길드와 이름 키가 겹친다
        GuildAlreadyMember        = 1632, // 내가 이미 길드에 있다
        GuildTargetInGuild        = 1633, // 상대가 이미 길드에 있다
        GuildNotFound             = 1634, // 없거나 해체됐다
        GuildNotMember            = 1635,
        GuildNotLeader            = 1636,
        GuildFull                 = 1637, // 정원 30명이다
        GuildLeaderMustDelegate   = 1638, // 다른 길드원이 있어 길드장은 탈퇴할 수 없다
        GuildApplicationNotFound  = 1639,
        GuildInviteNotFound       = 1640,
        GuildApplicationLimit     = 1641, // 내 대기 가입 신청이 5건이다
        GuildPendingFull          = 1642, // 길드 대기 신청과 초대가 50건이다
        CharacterIsGuildLeader    = 1643, // 길드원이 있는 길드장은 캐릭터를 삭제할 수 없다
        GuildTargetInvitesFull    = 1644, // 상대가 받은 대기 길드 초대가 5건이다
        GuildLeaderChanged        = 1645, // 요청 뒤 길드장이 바뀌어 위임 기대 버전이 맞지 않는다

        // 프롤로그, 성장 메뉴 안내
        PrologueRequired          = 1700, // 계정의 첫 프롤로그는 건너뛸 수 없다
        PrologueAlreadyDone       = 1701, // 이 캐릭터는 프롤로그를 이미 마쳤다
        PrologueStageInvalid      = 1702, // 알 수 없는 단계이거나 뒤로 가는 단계다
        MenuGuideInvalid          = 1710, // 메뉴 ID 형식이 맞지 않는다
        MenuGuideLimit            = 1711  // 계정의 안내 완료 기록이 상한이다
    }
}
