namespace Shinshi.Contracts
{
    /// <summary> 마을, 던전 서버만 부르는 API 경로. SERVER_LEASE만 서비스 자격으로, 나머지는 서버 임대 토큰으로 호출하며 플레이어 토큰으로는 호출할 수 없다 </summary>
    public static class GameServerRoutes
    {
        // TODO(민성): ApiRoutes로 합칠지 정한다. ApiRoutes는 클라이언트와 런처 경로라 여기에 따로 둔다
        // FORMAT 경로는 string.Format으로 ID를 넣는다

        /****************************************
        *                Fields
        ****************************************/

        // 서버 풀 등록과 heartbeat(5초 주기, 15초 부재 시 배정 중지)
        // 서비스 자격으로 서버 임대 발급, 갱신도 같은 경로. 이 경로만 임대 없이 호출한다
        public const string SERVER_LEASE              = "/v1/server-instances/lease";
        public const string SERVER_REGISTER           = "/v1/server-instances/register";
        public const string SERVER_HEARTBEAT_FORMAT   = "/v1/server-instances/{0}/heartbeat";
        public const string SERVER_DRAIN_FORMAT       = "/v1/server-instances/{0}/drain";

        // 마을 서버: 접속이 끊긴 계정의 접속 임대를 푼다. {0} instanceId, {1} leaseId. 갱신은 heartbeat가 함께 한다
        public const string ACCOUNT_LEASE_RELEASE_FORMAT = "/v1/server-instances/{0}/account-leases/{1}/release";

        // 마을 서버: 전원 티켓, 물약 일괄 예약과 취소, 참가자별 일회용 접속 토큰
        public const string RESERVATIONS              = "/v1/game-sessions/reservations";
        public const string RESERVATION_CANCEL_FORMAT = "/v1/game-sessions/{0}/cancel";
        public const string JOIN_TICKETS_FORMAT       = "/v1/game-sessions/{0}/join-tickets";

        // 던전 서버: 접속 임대 이관, 시작, 순번 이벤트, 체크포인트, 정산
        public const string LEASE_TRANSFER_FORMAT     = "/v1/game-sessions/{0}/lease-transfer";
        public const string START_FORMAT              = "/v1/game-sessions/{0}/start";
        public const string EVENTS_FORMAT             = "/v1/game-sessions/{0}/events";
        public const string CHECKPOINTS_FORMAT        = "/v1/game-sessions/{0}/checkpoints";
        public const string SETTLEMENT_FORMAT         = "/v1/game-sessions/{0}/settlement";

        // 던전 서버: 정산을 마친 보스 성공 출정의 파티 경매. {0} sessionId, {1} auctionId
        public const string AUCTIONS_FORMAT           = "/v1/game-sessions/{0}/auctions";
        public const string AUCTION_BIDS_FORMAT       = "/v1/game-sessions/{0}/auctions/{1}/bids";
        public const string AUCTION_CLOSE_FORMAT      = "/v1/game-sessions/{0}/auctions/{1}/close";

        // 던전 서버: 모든 품목을 마친 뒤 출정을 닫는다(참가자 정산 완료, 접속 임대 해제, 서버 Draining)
        public const string AUCTION_FINISH_FORMAT     = "/v1/game-sessions/{0}/auctions/finish";
    }
}
