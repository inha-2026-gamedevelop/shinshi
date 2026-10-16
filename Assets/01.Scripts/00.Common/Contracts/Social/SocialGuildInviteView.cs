// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 스냅샷의 받은 길드 초대 한 건 </summary>
    [DataContract]
    public sealed class SocialGuildInviteView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "guildId")]         private string _guildId;
        [DataMember(Name = "guildName")]       private string _guildName;
        // 초대한 캐릭터가 삭제됐으면 빈 문자열
        [DataMember(Name = "inviterName")]     private string _inviterName;
        [DataMember(Name = "expiresAtUnixMs")] private long   _expiresAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string GuildId         => _guildId;
        public string GuildName       => _guildName;
        public string InviterName     => _inviterName;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildInviteView(string guildId, string guildName, string inviterName, long expiresAtUnixMs)
        {
            _guildId         = guildId;
            _guildName       = guildName;
            _inviterName     = inviterName;
            _expiresAtUnixMs = expiresAtUnixMs;
        }

        private SocialGuildInviteView() { }
    }
}
