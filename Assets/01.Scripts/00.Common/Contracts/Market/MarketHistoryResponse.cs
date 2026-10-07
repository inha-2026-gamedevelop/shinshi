// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 내 구매와 판매 기록 한 페이지, 최근 순서 </summary>
    [DataContract]
    public sealed class MarketHistoryResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "trades")]           private MarketTradeView[] _trades;
        [DataMember(Name = "page")]             private int               _page;
        [DataMember(Name = "hasMore")]          private bool              _hasMore;
        [DataMember(Name = "serverTimeUnixMs")] private long              _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public MarketTradeView[] Trades           => _trades;
        public int               Page             => _page;
        public bool              HasMore          => _hasMore;
        public long              ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public MarketHistoryResponse(MarketTradeView[] trades, int page, bool hasMore, long serverTimeUnixMs)
        {
            _trades           = trades;
            _page             = page;
            _hasMore          = hasMore;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private MarketHistoryResponse() { }
    }
}
