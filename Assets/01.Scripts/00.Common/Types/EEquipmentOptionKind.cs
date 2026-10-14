namespace Shinshi.Common
{
    /// <summary> 장비 옵션 한 줄의 종류, 저장 데이터와 EquipmentOptionRules의 KIND 값이 참조하므로 값을 바꾸지 않는다 </summary>
    public enum EEquipmentOptionKind
    {
        None        = 0,
        AttackRate  = 1,
        HealthRate  = 2,
        DefenseRate = 3,
        CritChance  = 4
    }
}
