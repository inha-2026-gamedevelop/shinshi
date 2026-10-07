// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 경매장 화면(UI 담당)은 이 창구의 이벤트만 구독하고 요청 메서드로 보낸다. 가격, 수수료, 소유 판정은 마을 서버와 API가 한다.
    // 요청 메서드는 새 RequestId를 만들어 돌려준다(쓰기에서는 API operationId다). 쓰기 결과가 BackendUnavailable이면 RetryLastWrite로
    // 같은 RequestId를 다시 보낸다. 연결되지 않았으면 Guid.Empty를 돌려주고 아무것도 보내지 않는다
    /// <summary> 클라이언트의 유저 경매장 요청 송신과 결과 수신 창구 </summary>
    public static class TownMarketClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static MarketRequestMessage _lastWrite;
        private static bool                 _hasLastWrite;

        /****************************************
        *              Properties
        ****************************************/

        public static event Action<MarketResultMessage> ResultReceived;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _lastWrite    = default;
            _hasLastWrite = false;
            NetworkClient.ReplaceHandler<MarketResultMessage>(OnResult);
        }

        /// <summary> category, classId, grade는 MarketRequestMessage.NONE이면 조건 없음, definitionIds는 이름 검색 결과(없으면 null) </summary>
        public static Guid Search(int category, int classId, int grade, string[] definitionIds, int sort, int order, int page)
        {
            return Send(new MarketRequestMessage(Guid.NewGuid(), EMarketAction.Search, category, classId, grade, definitionIds, sort,
                order, page, null, null, null, null, 0, 0, null), false);
        }

        public static Guid QueryListings(string definitionId, int page)
        {
            return Send(Build(EMarketAction.Listings, page, definitionId, null, null, null, 0, 0, null), false);
        }

        public static Guid RequestMine()
        {
            return Send(Build(EMarketAction.Mine, 0, null, null, null, null, 0, 0, null), false);
        }

        public static Guid RequestHistory(int page)
        {
            return Send(Build(EMarketAction.History, page, null, null, null, null, 0, 0, null), false);
        }

        /// <summary> assetKind는 MarketCodes.ASSET_ACCOUNT_ITEM 또는 ASSET_EQUIPMENT, itemId는 장비일 때만 </summary>
        public static Guid List(string assetKind, string definitionId, string itemId, int quantity, long unitPrice)
        {
            return Send(Build(EMarketAction.List, 0, definitionId, null, assetKind, itemId, quantity, unitPrice, null), true);
        }

        /// <summary> expectedUnitPrice는 화면에서 확인한 개당 가격, 달라졌으면 서버가 MarketPriceChanged로 거절한다 </summary>
        public static Guid Buy(string listingId, int quantity, long expectedUnitPrice)
        {
            return Send(Build(EMarketAction.Buy, 0, null, listingId, null, null, quantity, expectedUnitPrice, null), true);
        }

        public static Guid Cancel(string listingId)
        {
            return Send(Build(EMarketAction.Cancel, 0, null, listingId, null, null, 0, 0, null), true);
        }

        /// <summary> 판매 결과를 확인했다고 표시한다 </summary>
        public static Guid Acknowledge(string[] listingIds)
        {
            return Send(Build(EMarketAction.Ack, 0, null, null, null, null, 0, 0, listingIds), true);
        }

        /// <summary> 마지막 쓰기를 같은 RequestId로 다시 보낸다, 서버와 API가 처음 결과를 돌려준다 </summary>
        public static bool RetryLastWrite()
        {
            if ((!NetworkClient.isConnected) || (!_hasLastWrite))
            {
                return false;
            }

            NetworkClient.Send(_lastWrite);
            return true;
        }

        private static MarketRequestMessage Build(EMarketAction action, int page, string definitionId, string listingId, string assetKind,
            string itemId, int quantity, long unitPrice, string[] listingIds)
        {
            return new MarketRequestMessage(Guid.NewGuid(), action, MarketRequestMessage.NONE, MarketRequestMessage.NONE,
                MarketRequestMessage.NONE, null, 0, 0, page, definitionId, listingId, assetKind, itemId, quantity, unitPrice, listingIds);
        }

        private static Guid Send(MarketRequestMessage message, bool isWrite)
        {
            if (!NetworkClient.isConnected)
            {
                return Guid.Empty;
            }

            if (isWrite)
            {
                _lastWrite    = message;
                _hasLastWrite = true;
            }

            NetworkClient.Send(message);
            return message.RequestId;
        }

        private static void OnResult(MarketResultMessage message)
        {
            ResultReceived?.Invoke(message);
        }
    }
}
