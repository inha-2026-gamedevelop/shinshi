namespace Shinshi.Network
{
    /// <summary> 친구 한 명의 표시 값. SameServer는 상대 임대가 지금 이 마을 서버에 있는지다(파티 초대 조건) </summary>
    public readonly struct SocialFriendEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string    _characterId;
        private readonly string    _name;
        private readonly int       _classId;
        private readonly EPresence _presence;
        private readonly bool      _sameServer;

        /****************************************
        *              Properties
        ****************************************/

        public string    CharacterId => _characterId ?? string.Empty;
        public string    Name        => _name ?? string.Empty;
        public int       ClassId     => _classId;
        public EPresence Presence    => _presence;
        public bool      SameServer  => _sameServer;

        /****************************************
        *                Methods
        ****************************************/

        public SocialFriendEntry(string characterId, string name, int classId, EPresence presence, bool sameServer)
        {
            _characterId = characterId;
            _name        = name;
            _classId     = classId;
            _presence    = presence;
            _sameServer  = sameServer;
        }
    }
}
