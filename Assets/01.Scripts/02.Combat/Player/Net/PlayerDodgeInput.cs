namespace Shinshi.Combat
{
    /// <summary> 회피 의도, 방향만 보내고 도착점과 막힘은 서버가 정한다 </summary>
    public readonly struct PlayerDodgeInput
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

        // 회피 방향. FacingRules로 양자화한 수평 방향이다
        public ushort AimYaw => _aimYaw;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerDodgeInput(in PlayerInputHeader header, ushort aimYaw)
        {
            _header = header;
            _aimYaw = aimYaw;
        }
    }
}
