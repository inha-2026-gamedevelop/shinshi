// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 내 등록 목록(활성과 최근 MarketLimits.HISTORY_DAYS일 종료), 가용 엽전, 미확인 수 </summary>
    [DataContract]
    public sealed class MarketMineResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "listings")]          private MarketListingView[] _listings;
        // currency - currency_reserved
        [DataMember(Name = "availableYeopjeon")] private long                _availableYeopjeon;
        [DataMember(Name = "unseenCount")]       private int                 _unseenCount;
        [DataMember(Name = "serverTimeUnixMs")]  private long                _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public MarketListingView[] Listings          => _listings;
        public long                AvailableYeopjeon => _availableYeopjeon;
        public int                 UnseenCount       => _unseenCount;
        public long                ServerTimeUnixMs  => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public MarketMineResponse(MarketListingView[] listings, long availableYeopjeon, int unseenCount, long serverTimeUnixMs)
        {
            _listings          = listings;
            _availableYeopjeon = availableYeopjeon;
            _unseenCount       = unseenCount;
            _serverTimeUnixMs  = serverTimeUnixMs;
        }

        private MarketMineResponse() { }
    }
}
