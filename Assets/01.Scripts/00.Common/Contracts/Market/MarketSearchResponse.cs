// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 검색 결과 한 페이지 </summary>
    [DataContract]
    public sealed class MarketSearchResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "groups")]           private MarketGroupView[] _groups;
        // 조건에 맞는 전체 묶음 수
        [DataMember(Name = "totalCount")]       private int               _totalCount;
        [DataMember(Name = "page")]             private int               _page;
        [DataMember(Name = "serverTimeUnixMs")] private long              _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public MarketGroupView[] Groups           => _groups;
        public int               TotalCount       => _totalCount;
        public int               Page             => _page;
        public long              ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public MarketSearchResponse(MarketGroupView[] groups, int totalCount, int page, long serverTimeUnixMs)
        {
            _groups           = groups;
            _totalCount       = totalCount;
            _page             = page;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private MarketSearchResponse() { }
    }
}
