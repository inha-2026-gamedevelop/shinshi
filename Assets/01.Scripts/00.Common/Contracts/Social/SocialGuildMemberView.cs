// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 스냅샷의 길드원 한 명 </summary>
    [DataContract]
    public sealed class SocialGuildMemberView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")]  private string _characterId;
        // 추방, 위임 요청에 그대로 보낸다
        [DataMember(Name = "membershipId")] private string _membershipId;
        [DataMember(Name = "name")]         private string _name;
        [DataMember(Name = "classId")]      private int    _classId;
        // SocialCodes.GuildRole
        [DataMember(Name = "role")]         private int    _role;
        // SocialCodes.Presence
        [DataMember(Name = "presence")]     private int    _presence;
        [DataMember(Name = "sameServer")]   private bool   _sameServer;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId  => _characterId;
        public string MembershipId => _membershipId;
        public string Name         => _name;
        public int    ClassId      => _classId;
        public int    Role         => _role;
        public int    Presence     => _presence;
        public bool   SameServer   => _sameServer;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildMemberView(string characterId, string membershipId, string name, int classId, int role, int presence, bool sameServer)
        {
            _characterId  = characterId;
            _membershipId = membershipId;
            _name         = name;
            _classId      = classId;
            _role         = role;
            _presence     = presence;
            _sameServer   = sameServer;
        }

        private SocialGuildMemberView() { }
    }
}
