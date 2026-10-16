// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 스냅샷의 친구 요청 또는 길드 가입 신청 한 건 </summary>
    [DataContract]
    public sealed class SocialRequestView
    {
        /****************************************
        *                Fields
        ****************************************/

        // 상대 캐릭터(받은 요청은 보낸 사람, 보낸 요청은 대상, 길드 신청은 신청자)
        [DataMember(Name = "characterId")]     private string _characterId;
        [DataMember(Name = "name")]            private string _name;
        [DataMember(Name = "classId")]         private int    _classId;
        [DataMember(Name = "expiresAtUnixMs")] private long   _expiresAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId     => _characterId;
        public string Name            => _name;
        public int    ClassId         => _classId;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public SocialRequestView(string characterId, string name, int classId, long expiresAtUnixMs)
        {
            _characterId     = characterId;
            _name            = name;
            _classId         = classId;
            _expiresAtUnixMs = expiresAtUnixMs;
        }

        private SocialRequestView() { }
    }
}
