// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 길드 이름으로 가입 신청 </summary>
    [DataContract]
    public sealed class SocialGuildApplyRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "guildName")] private string _guildName;

        /****************************************
        *              Properties
        ****************************************/

        public string GuildName => _guildName;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildApplyRequest(string guildName)
        {
            _guildName = guildName;
        }

        private SocialGuildApplyRequest() { }
    }
}
