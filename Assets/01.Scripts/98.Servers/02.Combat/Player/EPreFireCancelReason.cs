namespace Shinshi.Combat
{
    /// <summary> 발사 전 동작과 보관 입력을 끊는 사유, 사유마다 끊는 범위와 클라이언트에 알리는 거절 사유가 다르다 </summary>
    public enum EPreFireCancelReason : byte
    {
        Detach,
        Shutdown,
        SkillCommit,
        SkillBuffered,
        SkillDodgeCancel,
        SkillCombatStop,
        Dodge,
        Death,
        ControlInterrupt,
        StunInterrupt,
        MoveInput,
        MoveInputWindupCancel
    }
}
