// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 길드장 위임. 기대 leaderVersion이 맞을 때만 적용한다 </summary>
    [DataContract]
    public sealed class SocialGuildDelegateRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "guildId")]               private string _guildId;
        [DataMember(Name = "newLeaderCharacterId")]  private string _newLeaderCharacterId;
        // 새 길드장의 가입 버전
        [DataMember(Name = "membershipId")]          private string _membershipId;
        // 스냅샷의 leaderVersion
        [DataMember(Name = "expectedLeaderVersion")] private long   _expectedLeaderVersion;

        /****************************************
        *              Properties
        ****************************************/

        public string GuildId               => _guildId;
        public string NewLeaderCharacterId  => _newLeaderCharacterId;
        public string MembershipId          => _membershipId;
        public long   ExpectedLeaderVersion => _expectedLeaderVersion;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildDelegateRequest(string guildId, string newLeaderCharacterId, string membershipId, long expectedLeaderVersion)
        {
            _guildId               = guildId;
            _newLeaderCharacterId  = newLeaderCharacterId;
            _membershipId          = membershipId;
            _expectedLeaderVersion = expectedLeaderVersion;
        }

        private SocialGuildDelegateRequest() { }
    }
}
