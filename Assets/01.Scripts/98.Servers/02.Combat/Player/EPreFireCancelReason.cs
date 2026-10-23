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
        MoveInputWindupCancel,

        // 기본 공격 동작 중에 들어온 이동. 동작이 끝난 뒤 이동하므로 대기 공격만 지운다
        MoveInputHeld
    }
}
