namespace Shinshi.Network
{
    /// <summary> 친구 요청(받은, 보낸)과 길드 가입 신청 한 건. 상대 캐릭터와 만료 시각(Unix ms) </summary>
    public readonly struct SocialRequestEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _characterId;
        private readonly string _name;
        private readonly int    _classId;
        private readonly long   _expiresAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId     => _characterId ?? string.Empty;
        public string Name            => _name ?? string.Empty;
        public int    ClassId         => _classId;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public SocialRequestEntry(string characterId, string name, int classId, long expiresAtUnixMs)
        {
            _characterId     = characterId;
            _name            = name;
            _classId         = classId;
            _expiresAtUnixMs = expiresAtUnixMs;
        }
    }
}
