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
        private readonly byte   _option1Kind;
        private readonly int    _option1Value;
        private readonly byte   _option2Kind;
        private readonly int    _option2Value;

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

        // 옵션 두 줄, 종류는 EquipmentOptionRules의 KIND_* 값이고 값은 만분율이다. 종류 0은 옵션 없음
        public byte   Option1Kind  => _option1Kind;
        public int    Option1Value => _option1Value;
        public byte   Option2Kind  => _option2Kind;
        public int    Option2Value => _option2Value;

        /****************************************
        *                Methods
        ****************************************/

        public TownEquipmentEntry(string itemId, string definitionId, byte slot, byte grade, bool isTraining, string origin,
            int enhancement, bool equipped, bool locked, bool bound, bool inMarket = false,
            byte option1Kind = 0, int option1Value = 0, byte option2Kind = 0, int option2Value = 0)
        {
            _option1Kind  = option1Kind;
            _option1Value = option1Value;
            _option2Kind  = option2Kind;
            _option2Value = option2Value;
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
