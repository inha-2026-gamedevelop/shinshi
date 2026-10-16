// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 스냅샷의 내 길드. hasGuild가 false면 나머지는 기본값이다 </summary>
    [DataContract]
    public sealed class SocialGuildView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "hasGuild")]       private bool   _hasGuild;
        [DataMember(Name = "guildId")]        private string _guildId;
        [DataMember(Name = "name")]           private string _name;
        [DataMember(Name = "memberCount")]    private int    _memberCount;
        // SocialCodes.GuildRole
        [DataMember(Name = "myRole")]         private int    _myRole;
        // 탈퇴 요청에 그대로 보낸다
        [DataMember(Name = "myMembershipId")] private string _myMembershipId;
        // 위임 요청의 expectedLeaderVersion
        [DataMember(Name = "leaderVersion")]  private long   _leaderVersion;

        /****************************************
        *              Properties
        ****************************************/

        public bool   HasGuild       => _hasGuild;
        public string GuildId        => _guildId;
        public string Name           => _name;
        public int    MemberCount    => _memberCount;
        public int    MyRole         => _myRole;
        public string MyMembershipId => _myMembershipId;
        public long   LeaderVersion  => _leaderVersion;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildView(bool hasGuild, string guildId, string name, int memberCount, int myRole, string myMembershipId, long leaderVersion)
        {
            _hasGuild       = hasGuild;
            _guildId        = guildId;
            _name           = name;
            _memberCount    = memberCount;
            _myRole         = myRole;
            _myMembershipId = myMembershipId;
            _leaderVersion  = leaderVersion;
        }

        private SocialGuildView() { }
    }
}
