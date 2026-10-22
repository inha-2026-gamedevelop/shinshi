using System;

namespace Shinshi.Combat
{
    /// <summary> 발사 전 동작 취소가 지우는 상태 묶음, 이미 날아간 화살은 FiredArrows가 있는 사유만 지운다 </summary>
    [Flags]
    public enum EPreFireTargets : ushort
    {
        None          = 0,
        PendingMove   = 1 << 0,
        PendingAttack = 1 << 1,
        Windup        = 1 << 2,
        Combo         = 1 << 3,
        FiredArrows   = 1 << 4,
        PendingSkill  = 1 << 5,
        PendingDodge  = 1 << 6,
        ActiveDodge   = 1 << 7,
        ReviveRequest = 1 << 8,
        Path          = 1 << 9,
        ComboImmunity = 1 << 10
    }
}
