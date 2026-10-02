// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Sessions
{
    /// <summary> GameServerRoutes와 같은 경로의 ASP.NET Core 템플릿. 두 값이 어긋나지 않는지 테스트로 묶는다 </summary>
    public static class GameServerRouteTemplates
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SERVER_REGISTER    = GameServerRoutes.SERVER_REGISTER;
        public const string SERVER_HEARTBEAT   = "/v1/server-instances/{instanceId}/heartbeat";
        public const string SERVER_DRAIN       = "/v1/server-instances/{instanceId}/drain";
        public const string ACCOUNT_LEASE_RELEASE = "/v1/server-instances/{instanceId}/account-leases/{leaseId}/release";
        public const string RESERVATIONS       = GameServerRoutes.RESERVATIONS;
        public const string RESERVATION_CANCEL = "/v1/game-sessions/{sessionId}/cancel";
        public const string JOIN_TICKETS       = "/v1/game-sessions/{sessionId}/join-tickets";
        public const string LEASE_TRANSFER     = "/v1/game-sessions/{sessionId}/lease-transfer";
        public const string START              = "/v1/game-sessions/{sessionId}/start";
        public const string EVENTS             = "/v1/game-sessions/{sessionId}/events";
        public const string CHECKPOINTS        = "/v1/game-sessions/{sessionId}/checkpoints";
        public const string SETTLEMENT         = "/v1/game-sessions/{sessionId}/settlement";
        public const string AUCTIONS           = "/v1/game-sessions/{sessionId}/auctions";
        public const string AUCTION_BIDS       = "/v1/game-sessions/{sessionId}/auctions/{auctionId}/bids";
        public const string AUCTION_CLOSE      = "/v1/game-sessions/{sessionId}/auctions/{auctionId}/close";
        public const string AUCTION_FINISH     = "/v1/game-sessions/{sessionId}/auctions/finish";
    }
}
