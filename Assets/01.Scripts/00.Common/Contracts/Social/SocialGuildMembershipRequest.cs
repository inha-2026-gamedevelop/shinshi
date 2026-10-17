// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 탈퇴(내 가입)와 추방(대상 가입)의 기대 가입. 다르면 효과 없이 성공한다 </summary>
    [DataContract]
    public sealed class SocialGuildMembershipRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "guildId")]      private string _guildId;
        // 스냅샷이 준 가입 버전
        [DataMember(Name = "membershipId")] private string _membershipId;

        /****************************************
        *              Properties
        ****************************************/

        public string GuildId      => _guildId;
        public string MembershipId => _membershipId;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildMembershipRequest(string guildId, string membershipId)
        {
            _guildId      = guildId;
            _membershipId = membershipId;
        }

        private SocialGuildMembershipRequest() { }
    }
}
