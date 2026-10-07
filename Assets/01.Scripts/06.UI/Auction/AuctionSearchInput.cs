namespace Shinshi.UI
{
    /// <summary> 구매 탭 검색 조건, 조건 없음은 NONE이다 </summary>
    public readonly struct AuctionSearchInput
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버 요청의 조건 없음 값과 같다
        public const int NONE = -1;

        private readonly string _text;
        private readonly int    _category;
        private readonly int    _classId;
        private readonly int    _grade;
        private readonly int    _sort;
        private readonly int    _order;

        /****************************************
        *              Properties
        ****************************************/

        public string Text     => _text ?? string.Empty;
        public int    Category => _category;
        public int    ClassId  => _classId;
        public int    Grade    => _grade;
        public int    Sort     => _sort;
        public int    Order    => _order;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionSearchInput(string text, int category, int classId, int grade, int sort, int order)
        {
            _text     = text;
            _category = category;
            _classId  = classId;
            _grade    = grade;
            _sort     = sort;
            _order    = order;
        }
    }
}
