namespace Shinshi.Network
{
    /// <summary> 길드원 한 명. MembershipId는 가입할 때마다 새로 생기며 추방, 위임 요청에 그대로 보낸다 </summary>
    public readonly struct SocialGuildMemberEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string     _characterId;
        private readonly string     _membershipId;
        private readonly string     _name;
        private readonly int        _classId;
        private readonly EGuildRole _role;
        private readonly EPresence  _presence;
        private readonly bool       _sameServer;

        /****************************************
        *              Properties
        ****************************************/

        public string     CharacterId  => _characterId ?? string.Empty;
        public string     MembershipId => _membershipId ?? string.Empty;
        public string     Name         => _name ?? string.Empty;
        public int        ClassId      => _classId;
        public EGuildRole Role         => _role;
        public EPresence  Presence     => _presence;
        public bool       SameServer   => _sameServer;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildMemberEntry(string characterId, string membershipId, string name, int classId, EGuildRole role,
            EPresence presence, bool sameServer)
        {
            _characterId  = characterId;
            _membershipId = membershipId;
            _name         = name;
            _classId      = classId;
            _role         = role;
            _presence     = presence;
            _sameServer   = sameServer;
        }
    }
}
