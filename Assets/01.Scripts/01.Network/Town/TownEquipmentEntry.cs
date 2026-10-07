namespace Shinshi.Network
{
    /// <summary> 성장 상태의 보유 장비 하나 </summary>
    public readonly struct TownEquipmentEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _itemId;
        private readonly string _definitionId;
        private readonly byte   _slot;
        private readonly byte   _grade;
        private readonly bool   _isTraining;
        private readonly string _origin;
        private readonly int    _enhancement;
        private readonly bool   _equipped;
        private readonly bool   _locked;
        private readonly bool   _bound;
        private readonly bool   _inMarket;

        /****************************************
        *              Properties
        ****************************************/

        public string ItemId       => _itemId;
        public string DefinitionId => _definitionId;

        // EEquipmentSlot
        public byte   Slot         => _slot;

        // EItemGrade
        public byte   Grade        => _grade;
        public bool   IsTraining   => _isTraining;

        // GrowthCodes.EquipmentOrigin
        public string Origin       => _origin;
        public int    Enhancement  => _enhancement;
        public bool   Equipped     => _equipped;
        public bool   Locked       => _locked;
        public bool   Bound        => _bound;

        // 유저 경매장에 보관 중이라 착용, 강화, 분해, 거래를 할 수 없다
        public bool   InMarket     => _inMarket;

        /****************************************
        *                Methods
        ****************************************/

        public TownEquipmentEntry(string itemId, string definitionId, byte slot, byte grade, bool isTraining, string origin,
            int enhancement, bool equipped, bool locked, bool bound, bool inMarket = false)
        {
            _itemId       = itemId;
            _definitionId = definitionId;
            _slot         = slot;
            _grade        = grade;
            _isTraining   = isTraining;
            _origin       = origin;
            _enhancement  = enhancement;
            _equipped     = equipped;
            _locked       = locked;
            _bound        = bound;
            _inMarket     = inMarket;
        }
    }
}
