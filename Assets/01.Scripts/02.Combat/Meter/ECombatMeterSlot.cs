namespace Shinshi.Combat
{
    /// <summary> 딜미터 집계 슬롯, 직업 기술명이 아니라 고정 자리로 분류한다 </summary>
    public enum ECombatMeterSlot : byte
    {
        BasicAttack = 0,
        Skill1      = 1,
        Skill2      = 2,
        Skill3      = 3,
        Skill4      = 4,
        Summon      = 5,
        Extra       = 6,
        Other       = 7
    }
}
