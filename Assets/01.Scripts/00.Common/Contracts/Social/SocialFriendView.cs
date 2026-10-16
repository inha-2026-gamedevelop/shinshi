// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 스냅샷의 친구 한 명 </summary>
    [DataContract]
    public sealed class SocialFriendView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")] private string _characterId;
        [DataMember(Name = "name")]        private string _name;
        [DataMember(Name = "classId")]     private int    _classId;
        // SocialCodes.Presence
        [DataMember(Name = "presence")]    private int    _presence;
        // 상대 임대가 호출 인스턴스에 있다
        [DataMember(Name = "sameServer")]  private bool   _sameServer;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId => _characterId;
        public string Name        => _name;
        public int    ClassId     => _classId;
        public int    Presence    => _presence;
        public bool   SameServer  => _sameServer;

        /****************************************
        *                Methods
        ****************************************/

        public SocialFriendView(string characterId, string name, int classId, int presence, bool sameServer)
        {
            _characterId = characterId;
            _name        = name;
            _classId     = classId;
            _presence    = presence;
            _sameServer  = sameServer;
        }

        private SocialFriendView() { }
    }
}
