namespace Shinshi.Network
{
    /// <summary> 정산 보상 아이템 한 줄, 같은 정의 ID는 서버가 이미 한 줄로 합쳤다 </summary>
    public readonly struct SortieRewardLine
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _definitionId;
        private readonly long   _quantity;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public long   Quantity     => _quantity;

        /****************************************
        *                Methods
        ****************************************/

        public SortieRewardLine(string definitionId, long quantity)
        {
            _definitionId = definitionId;
            _quantity     = quantity;
        }
    }
}
