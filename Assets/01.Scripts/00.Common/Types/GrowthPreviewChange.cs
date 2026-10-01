namespace Shinshi.Common
{
    /// <summary> 성장 미리보기의 전후 수치 한 줄, 성공했을 때의 예상값이다 </summary>
    public readonly struct GrowthPreviewChange
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _displayKey;
        private readonly float  _before;
        private readonly float  _after;

        /****************************************
        *              Properties
        ****************************************/

        // 공격, 생명, 강화 단계, 효과 레벨처럼 화면에 보일 항목의 표시 키
        public string DisplayKey => _displayKey;
        public float  Before     => _before;
        public float  After      => _after;

        /****************************************
        *                Methods
        ****************************************/

        public GrowthPreviewChange(string displayKey, float before, float after)
        {
            _displayKey = displayKey;
            _before     = before;
            _after      = after;
        }
    }
}
