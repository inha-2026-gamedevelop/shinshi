namespace Shinshi.Common
{
    /// <summary> 성장 미리보기의 비용 한 줄, 표시용이며 로컬 보유량에서 미리 빼지 않는다 </summary>
    public readonly struct GrowthPreviewCost
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _definitionId;
        private readonly long   _amount;
        private readonly long   _owned;

        /****************************************
        *              Properties
        ****************************************/

        // 정기, 정련편, 부적처럼 소모되는 재화나 아이템의 GameDB 정의 ID
        public string DefinitionId => _definitionId;
        public long   Amount       => _amount;

        // 미리보기를 계산한 스냅샷 기준 보유량, 서버 잔액과 다를 수 있다
        public long Owned => _owned;

        public bool IsSufficient => _owned >= _amount;

        /****************************************
        *                Methods
        ****************************************/

        public GrowthPreviewCost(string definitionId, long amount, long owned)
        {
            _definitionId = definitionId;
            _amount       = amount;
            _owned        = owned;
        }
    }
}
