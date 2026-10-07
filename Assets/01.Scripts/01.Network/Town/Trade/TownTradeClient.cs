// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 거래 화면(UI 담당)은 이 창구의 이벤트만 구독하고 요청 메서드로 보낸다. 거래 상태, 거리, 시간, 실행은 마을 서버와 API가 한다.
    // 이 창구는 마지막으로 받은 상태의 거래 ID와 두 세대 값을 들고 있다가 요청에 싣는다. 거래가 끝났거나 없으면 보내지 않는다.
    // 요청 메서드는 보냈으면 참이다. 연결되지 않았으면 거짓이고 아무것도 보내지 않는다
    /// <summary> 클라이언트의 1:1 거래 요청 송신과 상태 수신 창구 </summary>
    public static class TownTradeClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static TradeStateMessage _state;
        private static bool              _hasState;

        /****************************************
        *              Properties
        ****************************************/

        public static TradeStateMessage State    => _state;

        // 끝나지 않은 거래가 있으면 참
        public static bool              IsActive => (_hasState) && (!_state.IsFinished);

        public static event Action<TradeStateMessage>  StateChanged;
        public static event Action<TradeInviteMessage> InviteReceived;
        public static event Action<TradeResultMessage> ResultReceived;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _state    = default;
            _hasState = false;
            NetworkClient.ReplaceHandler<TradeStateMessage>(OnState);
            NetworkClient.ReplaceHandler<TradeInviteMessage>(OnInvite);
            NetworkClient.ReplaceHandler<TradeResultMessage>(OnResult);
        }

        /// <summary> 캐릭터 이름으로 거래를 요청한다, 서버가 거리와 상태를 본다 </summary>
        public static bool Request(string characterName)
        {
            return Send(ETradeAction.Request, Guid.Empty, characterName, false, null, 0, 0, 0);
        }

        /// <summary> 받은 거래 요청에 답한다 </summary>
        public static bool Respond(Guid tradeId, bool accept)
        {
            return Send(ETradeAction.Respond, tradeId, null, accept, null, 0, 0, 0);
        }

        /// <summary> 내 제안을 통째로 바꾼다, 바꾸면 양쪽 확정과 수락이 풀린다 </summary>
        public static bool SetOffer(TradeOfferLine[] lines, long currency)
        {
            return SendCurrent(ETradeAction.SetOffer, lines, currency);
        }

        public static bool Lock()
        {
            return SendCurrent(ETradeAction.Lock, null, 0);
        }

        public static bool Unlock()
        {
            return SendCurrent(ETradeAction.Unlock, null, 0);
        }

        public static bool Accept()
        {
            return SendCurrent(ETradeAction.Accept, null, 0);
        }

        public static bool Cancel()
        {
            return SendCurrent(ETradeAction.Cancel, null, 0);
        }

        private static bool SendCurrent(ETradeAction action, TradeOfferLine[] lines, long currency)
        {
            if (!IsActive)
            {
                return false;
            }

            return Send(action, _state.TradeId, null, false, lines, currency, _state.OfferRevision, _state.ConfirmGeneration);
        }

        private static bool Send(ETradeAction action, Guid tradeId, string targetName, bool accept, TradeOfferLine[] lines, long currency,
            int offerRevision, int confirmGeneration)
        {
            if (!NetworkClient.isConnected)
            {
                return false;
            }

            NetworkClient.Send(new TradeRequestMessage(Guid.NewGuid(), tradeId, action, targetName, accept, lines, currency,
                offerRevision, confirmGeneration));
            return true;
        }

        private static void OnState(TradeStateMessage message)
        {
            // 끝난 거래의 늦은 메시지가 새 거래를 덮지 않게, 진행 중인 거래와 다른 ID는 새 거래일 때만 받는다
            if ((_hasState) && (!_state.IsFinished) && (_state.TradeId != message.TradeId))
            {
                return;
            }

            _state    = message;
            _hasState = true;
            StateChanged?.Invoke(message);
        }

        private static void OnInvite(TradeInviteMessage message)
        {
            InviteReceived?.Invoke(message);
        }

        private static void OnResult(TradeResultMessage message)
        {
            ResultReceived?.Invoke(message);
        }
    }
}
