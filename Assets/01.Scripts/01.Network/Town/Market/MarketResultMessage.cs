// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // Reason이 None이면 요청한 종류에 맞는 필드가 채워진다. 마을 서버가 API 응답을 이 형식으로 다시 만들어 보낸다.
    // BackendUnavailable이면 같은 RequestId로 다시 보내도 쓰기 효과는 한 번이다
    /// <summary> 유저 경매장 요청 한 건의 결과, 요청한 클라이언트에게만 온다 </summary>
    public readonly struct MarketResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid                 _requestId;
        private readonly EMarketAction        _action;
        private readonly ERejectReason        _reason;
        private readonly MarketGroupEntry[]   _groups;
        private readonly MarketListingEntry[] _listings;
        private readonly MarketTradeEntry[]   _trades;
        private readonly int                  _totalCount;
        private readonly int                  _page;
        private readonly bool                 _hasMore;
        private readonly long                 _averageUnitPrice24h;
        private readonly long                 _availableYeopjeon;
        private readonly int                  _unseenCount;
        private readonly long                 _serverTimeUnixMs;
        private readonly string               _listingId;
        private readonly string               _tradeId;
        private readonly long                 _gross;
        private readonly long                 _fee;
        private readonly long                 _net;

        /****************************************
        *              Properties
        ****************************************/

        public Guid                 RequestId           => _requestId;
        public EMarketAction        Action              => _action;
        public ERejectReason        Reason              => _reason;
        public bool                 IsAccepted          => _reason == ERejectReason.None;
        public MarketGroupEntry[]   Groups              => _groups ?? Array.Empty<MarketGroupEntry>();
        public MarketListingEntry[] Listings            => _listings ?? Array.Empty<MarketListingEntry>();
        public MarketTradeEntry[]   Trades              => _trades ?? Array.Empty<MarketTradeEntry>();
        public int                  TotalCount          => _totalCount;
        public int                  Page                => _page;
        public bool                 HasMore             => _hasMore;
        public long                 AverageUnitPrice24h => _averageUnitPrice24h;
        public long                 AvailableYeopjeon   => _availableYeopjeon;
        public int                  UnseenCount         => _unseenCount;
        public long                 ServerTimeUnixMs    => _serverTimeUnixMs;
        public string               ListingId           => _listingId;
        public string               TradeId             => _tradeId;
        public long                 Gross               => _gross;
        public long                 Fee                 => _fee;
        public long                 Net                 => _net;

        /****************************************
        *                Methods
        ****************************************/

        public MarketResultMessage(Guid requestId, EMarketAction action, ERejectReason reason, MarketGroupEntry[] groups,
            MarketListingEntry[] listings, MarketTradeEntry[] trades, int totalCount, int page, bool hasMore, long averageUnitPrice24h,
            long availableYeopjeon, int unseenCount, long serverTimeUnixMs, string listingId, string tradeId, long gross, long fee,
            long net)
        {
            _requestId           = requestId;
            _action              = action;
            _reason              = reason;
            _groups              = groups;
            _listings            = listings;
            _trades              = trades;
            _totalCount          = totalCount;
            _page                = page;
            _hasMore             = hasMore;
            _averageUnitPrice24h = averageUnitPrice24h;
            _availableYeopjeon   = availableYeopjeon;
            _unseenCount         = unseenCount;
            _serverTimeUnixMs    = serverTimeUnixMs;
            _listingId           = listingId;
            _tradeId             = tradeId;
            _gross               = gross;
            _fee                 = fee;
            _net                 = net;
        }

        /// <summary> 거절이나 실패 결과, 내용 없이 사유만 담는다 </summary>
        public static MarketResultMessage Rejected(Guid requestId, EMarketAction action, ERejectReason reason)
        {
            return new MarketResultMessage(requestId, action, reason, null, null, null, 0, 0, false, 0, 0, 0, 0, null, null, 0, 0, 0);
        }
    }
}
