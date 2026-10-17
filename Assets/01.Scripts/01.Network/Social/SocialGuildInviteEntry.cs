namespace Shinshi.Network
{
    /// <summary> 내가 받은 길드 초대 한 건. 초대자 캐릭터가 삭제됐으면 InviterName이 비어 있다 </summary>
    public readonly struct SocialGuildInviteEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _guildId;
        private readonly string _guildName;
        private readonly string _inviterName;
        private readonly long   _expiresAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string GuildId         => _guildId ?? string.Empty;
        public string GuildName       => _guildName ?? string.Empty;
        public string InviterName     => _inviterName ?? string.Empty;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildInviteEntry(string guildId, string guildName, string inviterName, long expiresAtUnixMs)
        {
            _guildId         = guildId;
            _guildName       = guildName;
            _inviterName     = inviterName;
            _expiresAtUnixMs = expiresAtUnixMs;
        }
    }
}
