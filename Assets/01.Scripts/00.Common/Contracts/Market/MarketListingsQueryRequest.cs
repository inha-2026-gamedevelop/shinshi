// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 한 정의 ID의 활성 등록 목록. 개당 가격 오름차순, 같은 가격은 먼저 등록한 순서다 </summary>
    [DataContract]
    public sealed class MarketListingsQueryRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "definitionId")] private string _definitionId;
        // 0부터, 한 페이지 MarketLimits.PAGE_SIZE
        [DataMember(Name = "page")]         private int    _page;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public int    Page         => _page;

        /****************************************
        *                Methods
        ****************************************/

        public MarketListingsQueryRequest(string definitionId, int page)
        {
            _definitionId = definitionId;
            _page         = page;
        }

        private MarketListingsQueryRequest() { }
    }
}
