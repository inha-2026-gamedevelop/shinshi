namespace Shinshi.Network
{
    /// <summary> 마을 성장 요청 종류, 값은 직렬화되므로 바꾸지 않는다 </summary>
    public enum EGrowthAction
    {
        Snapshot            = 0,
        TalismanRegister    = 1,
        TalismanUpgrade     = 2,
        TalismanLoadout     = 3,
        TalismanDisassemble = 4,
        Training            = 5,
        TrainingRespec      = 6,
        EquipmentCraft      = 7,
        EquipmentEnhance    = 8,
        EquipmentInherit    = 9,
        EquipmentEquip      = 10,
        EquipmentUnequip    = 11,
        EquipmentLock       = 12,
        EquipmentDismantle  = 13
    }
}
