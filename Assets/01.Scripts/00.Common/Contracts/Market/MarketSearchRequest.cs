// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 검색 한 페이지. 품목 이름 검색은 서버가 하지 않으므로 클라이언트가 글자로 정의 ID 목록을 만들어 보낸다. 읽기 전용이라 operationId가 없다 </summary>
    [DataContract]
    public sealed class MarketSearchRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // MarketCodes.Category, 없으면 전체
        [DataMember(Name = "category")]      private int?     _category;
        // EPlayerClass 값, 없으면 전체
        [DataMember(Name = "classId")]       private int?     _classId;
        // EItemGrade 값, 없으면 전체
        [DataMember(Name = "grade")]         private int?     _grade;
        // MarketLimits.MAX_SEARCH_DEFINITIONS 이하, 없거나 비면 제한 없음
        [DataMember(Name = "definitionIds")] private string[] _definitionIds;
        // MarketCodes.Sort
        [DataMember(Name = "sort")]          private int      _sort;
        // MarketCodes.Order
        [DataMember(Name = "order")]         private int      _order;
        // 0부터, 한 페이지 MarketLimits.PAGE_SIZE
        [DataMember(Name = "page")]          private int      _page;

        /****************************************
        *              Properties
        ****************************************/

        public int?     Category      => _category;
        public int?     ClassId       => _classId;
        public int?     Grade         => _grade;
        public string[] DefinitionIds => _definitionIds;
        public int      Sort          => _sort;
        public int      Order         => _order;
        public int      Page          => _page;

        /****************************************
        *                Methods
        ****************************************/

        public MarketSearchRequest(int? category, int? classId, int? grade, string[] definitionIds, int sort, int order, int page)
        {
            _category      = category;
            _classId       = classId;
            _grade         = grade;
            _definitionIds = definitionIds;
            _sort          = sort;
            _order         = order;
            _page          = page;
        }

        private MarketSearchRequest() { }
    }
}
