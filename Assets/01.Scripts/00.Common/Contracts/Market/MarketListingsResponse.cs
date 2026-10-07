// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 한 정의 ID의 등록 목록 한 페이지 </summary>
    [DataContract]
    public sealed class MarketListingsResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "listings")]            private MarketListingView[] _listings;
        [DataMember(Name = "totalCount")]          private int                 _totalCount;
        [DataMember(Name = "page")]                private int                 _page;
        // 최근 24시간 성사 평균 개당 가격, 성사가 없으면 0
        [DataMember(Name = "averageUnitPrice24h")] private long                _averageUnitPrice24h;
        [DataMember(Name = "serverTimeUnixMs")]    private long                _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public MarketListingView[] Listings            => _listings;
        public int                 TotalCount          => _totalCount;
        public int                 Page                => _page;
        public long                AverageUnitPrice24h => _averageUnitPrice24h;
        public long                ServerTimeUnixMs    => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public MarketListingsResponse(MarketListingView[] listings, int totalCount, int page, long averageUnitPrice24h, long serverTimeUnixMs)
        {
            _listings            = listings;
            _totalCount          = totalCount;
            _page                = page;
            _averageUnitPrice24h = averageUnitPrice24h;
            _serverTimeUnixMs    = serverTimeUnixMs;
        }

        private MarketListingsResponse() { }
    }
}
