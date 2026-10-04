namespace Shinshi.Network
{
    // 방 목록에서 필터와 표시에 필요한 값만 담는다. 직업 구성은 직업별 인원 수로 보낸다
    /// <summary> 방 목록의 방 하나 </summary>
    public readonly struct PartyRoomSummary
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int         _roomId;
        private readonly string      _name;
        private readonly EPartyMode  _mode;
        private readonly string      _contentId;
        private readonly sbyte       _difficulty;
        private readonly EPartyPhase _phase;
        private readonly byte        _memberCount;
        private readonly byte        _readyCount;
        private readonly bool        _hasPassword;
        private readonly byte        _warriors;
        private readonly byte        _sorcerers;
        private readonly byte        _summoners;

        /****************************************
        *              Properties
        ****************************************/

        public int         RoomId      => _roomId;
        public string      Name        => _name;
        public EPartyMode  Mode        => _mode;
        public string      ContentId   => _contentId;

        // GameSessionCodes.Difficulty, 일반전은 NONE(-1)
        public sbyte       Difficulty  => _difficulty;
        public EPartyPhase Phase       => _phase;
        public byte        MemberCount => _memberCount;
        public byte        ReadyCount  => _readyCount;
        public bool        HasPassword => _hasPassword;
        public byte        Warriors    => _warriors;
        public byte        Sorcerers   => _sorcerers;
        public byte        Summoners   => _summoners;

        /****************************************
        *                Methods
        ****************************************/

        public PartyRoomSummary(int roomId, string name, EPartyMode mode, string contentId, sbyte difficulty, EPartyPhase phase,
            byte memberCount, byte readyCount, bool hasPassword, byte warriors, byte sorcerers, byte summoners)
        {
            _roomId      = roomId;
            _name        = name;
            _mode        = mode;
            _contentId   = contentId;
            _difficulty  = difficulty;
            _phase       = phase;
            _memberCount = memberCount;
            _readyCount  = readyCount;
            _hasPassword = hasPassword;
            _warriors    = warriors;
            _sorcerers   = sorcerers;
            _summoners   = summoners;
        }
    }
}
