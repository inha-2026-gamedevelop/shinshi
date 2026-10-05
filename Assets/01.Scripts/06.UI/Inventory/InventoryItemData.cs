namespace Shinshi.UI
{
    /// <summary>인벤토리 한 칸에 놓인 아이템 값이며 서버 값을 화면용으로 옮긴 사본이다. 기본값은 빈 칸이다</summary>
    public readonly struct InventoryItemData
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ItemSlotData       _slot;
        private readonly EInventoryCategory _category;
        private readonly long               _acquiredOrder;

        /****************************************
        *              Properties
        ****************************************/

        public ItemSlotData       Slot          => _slot;
        public EInventoryCategory Category      => _category;
        public long               AcquiredOrder => _acquiredOrder;
        public bool               IsEmpty       => _slot.IsEmpty;

        /****************************************
        *                Methods
        ****************************************/

        // 최근 획득순 정렬에 쓰며 클수록 나중에 얻은 것이다
        public InventoryItemData(ItemSlotData slot, EInventoryCategory category, long acquiredOrder)
        {
            _slot          = slot;
            _category      = category;
            _acquiredOrder = acquiredOrder;
        }
    }
}
