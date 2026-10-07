// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 검색 결과의 정의 ID 묶음 하나 </summary>
    [DataContract]
    public sealed class MarketGroupView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "definitionId")]    private string _definitionId;
        [DataMember(Name = "grade")]           private int    _grade;
        [DataMember(Name = "lowestUnitPrice")] private long   _lowestUnitPrice;
        [DataMember(Name = "totalQty")]        private long   _totalQty;
        [DataMember(Name = "listingCount")]    private int    _listingCount;
        // 가장 최근 성사 개당 가격, 성사가 없으면 0
        [DataMember(Name = "recentUnitPrice")] private long   _recentUnitPrice;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId    => _definitionId;
        public int    Grade           => _grade;
        public long   LowestUnitPrice => _lowestUnitPrice;
        public long   TotalQty        => _totalQty;
        public int    ListingCount    => _listingCount;
        public long   RecentUnitPrice => _recentUnitPrice;

        /****************************************
        *                Methods
        ****************************************/

        public MarketGroupView(string definitionId, int grade, long lowestUnitPrice, long totalQty, int listingCount, long recentUnitPrice)
        {
            _definitionId    = definitionId;
            _grade           = grade;
            _lowestUnitPrice = lowestUnitPrice;
            _totalQty        = totalQty;
            _listingCount    = listingCount;
            _recentUnitPrice = recentUnitPrice;
        }

        private MarketGroupView() { }
    }
}
