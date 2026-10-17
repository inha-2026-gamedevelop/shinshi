namespace Shinshi.Network
{
    // 탈퇴, 추방, 위임 요청은 화면에 보인 GuildId, MembershipId, LeaderVersion을 그대로 보낸다.
    // 결과 불명 뒤 늦게 도착한 재전송이 그사이 새로 맺은 가입이나 바뀐 길드장에 작용하지 않게 하는 값이다
    /// <summary> 내 길드 요약. GuildId가 비어 있으면 길드가 없다 </summary>
    public readonly struct SocialGuildView
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string     _guildId;
        private readonly string     _name;
        private readonly int        _memberCount;
        private readonly EGuildRole _myRole;
        private readonly string     _myMembershipId;
        private readonly long       _leaderVersion;

        /****************************************
        *              Properties
        ****************************************/

        public static SocialGuildView None => default;

        public bool       HasGuild       => !string.IsNullOrEmpty(_guildId);
        public string     GuildId        => _guildId ?? string.Empty;
        public string     Name           => _name ?? string.Empty;
        public int        MemberCount    => _memberCount;
        public EGuildRole MyRole         => _myRole;
        public string     MyMembershipId => _myMembershipId ?? string.Empty;
        public long       LeaderVersion  => _leaderVersion;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildView(string guildId, string name, int memberCount, EGuildRole myRole, string myMembershipId,
            long leaderVersion)
        {
            _guildId        = guildId;
            _name           = name;
            _memberCount    = memberCount;
            _myRole         = myRole;
            _myMembershipId = myMembershipId;
            _leaderVersion  = leaderVersion;
        }
    }
}
