namespace Shinshi.Network
{
    // 생활 물약 재고에서는 거래 속성이 없어 BindingKind가 null이다
    /// <summary> 계정 보관 수량형 아이템 한 줄, 가용량은 Quantity - Reserved </summary>
    public readonly struct TownItemEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _definitionId;
        private readonly string _bindingKind;
        private readonly long   _quantity;
        private readonly long   _reserved;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public string BindingKind  => _bindingKind;
        public long   Quantity     => _quantity;
        public long   Reserved     => _reserved;

        /****************************************
        *                Methods
        ****************************************/

        public TownItemEntry(string definitionId, string bindingKind, long quantity, long reserved)
        {
            _definitionId = definitionId;
            _bindingKind  = bindingKind;
            _quantity     = quantity;
            _reserved     = reserved;
        }
    }
}
