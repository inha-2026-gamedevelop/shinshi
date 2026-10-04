// System
using System;

namespace Shinshi.Data.Growth
{
    /// <summary> 계승 판정에 쓰는 장비 상태 </summary>
    public sealed class InheritanceItem
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                              ItemId      { get; }
        public GrowthContent.EquipmentDefinition Definition  { get; }
        public int                               Enhancement { get; }
        public bool                              Equipped    { get; }
        public bool                              Locked      { get; }

        /****************************************
        *                Methods
        ****************************************/

        public InheritanceItem(Guid itemId, GrowthContent.EquipmentDefinition definition, int enhancement, bool equipped,
            bool locked)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ItemId      = itemId;
            Definition  = definition;
            Enhancement = enhancement;
            Equipped    = equipped;
            Locked      = locked;
        }
    }
}
