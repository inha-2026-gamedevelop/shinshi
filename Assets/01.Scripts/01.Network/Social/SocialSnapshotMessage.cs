// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 소유자 연결에만 온다. 가입 신청 목록은 길드장일 때만 채우고 그 외에는 빈 배열이다. 만료 시각 표시는 ServerTimeUnixMs 기준으로 센다
    /// <summary> 내 친구, 친구 요청, 길드, 길드원, 가입 신청, 받은 길드 초대 전체 </summary>
    public readonly struct SocialSnapshotMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly long                     _serverTimeUnixMs;
        private readonly SocialFriendEntry[]      _friends;
        private readonly SocialRequestEntry[]     _incoming;
        private readonly SocialRequestEntry[]     _outgoing;
        private readonly SocialGuildView          _guild;
        private readonly SocialGuildMemberEntry[] _members;
        private readonly SocialRequestEntry[]     _guildApplications;
        private readonly SocialGuildInviteEntry[] _guildInvites;

        /****************************************
        *              Properties
        ****************************************/

        public long                     ServerTimeUnixMs  => _serverTimeUnixMs;
        public SocialFriendEntry[]      Friends           => _friends ?? Array.Empty<SocialFriendEntry>();
        public SocialRequestEntry[]     Incoming          => _incoming ?? Array.Empty<SocialRequestEntry>();
        public SocialRequestEntry[]     Outgoing          => _outgoing ?? Array.Empty<SocialRequestEntry>();
        public SocialGuildView          Guild             => _guild;
        public SocialGuildMemberEntry[] Members           => _members ?? Array.Empty<SocialGuildMemberEntry>();
        public SocialRequestEntry[]     GuildApplications => _guildApplications ?? Array.Empty<SocialRequestEntry>();
        public SocialGuildInviteEntry[] GuildInvites      => _guildInvites ?? Array.Empty<SocialGuildInviteEntry>();

        /****************************************
        *                Methods
        ****************************************/

        public SocialSnapshotMessage(long serverTimeUnixMs, SocialFriendEntry[] friends, SocialRequestEntry[] incoming,
            SocialRequestEntry[] outgoing, SocialGuildView guild, SocialGuildMemberEntry[] members,
            SocialRequestEntry[] guildApplications, SocialGuildInviteEntry[] guildInvites)
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
    }
}
