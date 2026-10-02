// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 경매 화면(UI 담당)은 이 창구의 이벤트만 구독하고 입찰은 RequestBid로 보낸다. 판정은 모두 서버가 한다
    /// <summary> 클라이언트의 파티 경매 수신과 입찰 송신 창구 </summary>
    public static class PartyAuctionClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static AuctionStateMessage _current;
        private static bool                _hasState;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 받은 품목 상태, HasState가 거짓이면 기본값이다 </summary>
        public static AuctionStateMessage Current  => _current;
        public static bool                HasState => _hasState;

        public static event Action<AuctionStateMessage>     StateChanged;
        public static event Action<AuctionBidResultMessage> BidResult;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _current  = default;
            _hasState = false;
            NetworkClient.ReplaceHandler<AuctionStateMessage>(OnState);
            NetworkClient.ReplaceHandler<AuctionBidResultMessage>(OnBidResult);
        }

        /// <summary> 입찰을 보낸다, 연결되지 않았으면 거짓이다 </summary>
        public static bool RequestBid(long amount)
        {
            if (!NetworkClient.isConnected)
            {
                return false;
            }

            NetworkClient.Send(new AuctionBidMessage(amount));
            return true;
        }

        private static void OnState(AuctionStateMessage message)
        {
            _current  = message;
            _hasState = true;
            StateChanged?.Invoke(message);
        }

        private static void OnBidResult(AuctionBidResultMessage message)
        {
            BidResult?.Invoke(message);
        }
    }
}
