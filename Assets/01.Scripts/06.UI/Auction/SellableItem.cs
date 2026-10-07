namespace Shinshi.UI
{
    /// <summary> 경매장과 거래 창에 올릴 수 있는 품목 하나, 수량형은 보유 수량 전체, 장비는 한 개다 </summary>
    public readonly struct SellableItem
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _definitionId;
        private readonly string _itemId;
        private readonly int    _maxQuantity;
        private readonly int    _grade;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;

        // 장비 ID, 수량형이면 null
        public string ItemId       => _itemId;
        public int    MaxQuantity  => _maxQuantity;
        public int    Grade        => _grade;
        public bool   IsEquipment  => !string.IsNullOrEmpty(_itemId);

        /****************************************
        *                Methods
        ****************************************/

        public SellableItem(string definitionId, string itemId, int maxQuantity, int grade)
        {
            _definitionId = definitionId;
            _itemId       = itemId;
            _maxQuantity  = maxQuantity;
            _grade        = grade;
        }
    }
}
