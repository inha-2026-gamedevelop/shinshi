// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 소유 캐릭터 한 명의 소셜 상태. 친구와 같은 길드원의 접속 위치만 담는다 </summary>
    [DataContract]
    public sealed class SocialSnapshotResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "serverTimeUnixMs")]  private long                    _serverTimeUnixMs;
        // 이름순
        [DataMember(Name = "friends")]           private SocialFriendView[]      _friends;
        // 받은 친구 요청, 만료 전, 오래된 순
        [DataMember(Name = "incoming")]          private SocialRequestView[]     _incoming;
        // 보낸 친구 요청, 만료 전, 오래된 순
        [DataMember(Name = "outgoing")]          private SocialRequestView[]     _outgoing;
        [DataMember(Name = "guild")]             private SocialGuildView         _guild;
        // 길드장, 가입 순
        [DataMember(Name = "members")]           private SocialGuildMemberView[] _members;
        // 길드장만 채운다. 그 외 빈 배열
        [DataMember(Name = "guildApplications")] private SocialRequestView[]     _guildApplications;
        // 받은 길드 초대, 만료 전
        [DataMember(Name = "guildInvites")]      private SocialGuildInviteView[] _guildInvites;

        /****************************************
        *              Properties
        ****************************************/

        public long                    ServerTimeUnixMs  => _serverTimeUnixMs;
        public SocialFriendView[]      Friends           => _friends;
        public SocialRequestView[]     Incoming          => _incoming;
        public SocialRequestView[]     Outgoing          => _outgoing;
        public SocialGuildView         Guild             => _guild;
        public SocialGuildMemberView[] Members           => _members;
        public SocialRequestView[]     GuildApplications => _guildApplications;
        public SocialGuildInviteView[] GuildInvites      => _guildInvites;

        /****************************************
        *                Methods
        ****************************************/

        public SocialSnapshotResponse(long serverTimeUnixMs, SocialFriendView[] friends, SocialRequestView[] incoming, SocialRequestView[] outgoing, SocialGuildView guild, SocialGuildMemberView[] members, SocialRequestView[] guildApplications, SocialGuildInviteView[] guildInvites)
        {
            _serverTimeUnixMs  = serverTimeUnixMs;
            _friends           = friends;
            _incoming          = incoming;
            _outgoing          = outgoing;
            _guild             = guild;
            _members           = members;
            _guildApplications = guildApplications;
            _guildInvites      = guildInvites;
        }

        private SocialSnapshotResponse() { }
    }
}
