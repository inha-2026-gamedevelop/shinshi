namespace Shinshi.Combat
{
    /// <summary> 기본 공격 의도, 조준 방향만 보내고 대상과 피해는 서버가 정한다 </summary>
    public readonly struct PlayerAttackInput
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PlayerInputHeader _header;
        private readonly ushort            _aimYaw;

        /****************************************
        *              Properties
        ****************************************/

        public PlayerInputHeader Header => _header;

        // 커서 방향, FacingRules로 변환한 값
        public ushort AimYaw => _aimYaw;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerAttackInput(in PlayerInputHeader header, ushort aimYaw)
        {
            _header = header;
            _aimYaw = aimYaw;
        }
    }
}
