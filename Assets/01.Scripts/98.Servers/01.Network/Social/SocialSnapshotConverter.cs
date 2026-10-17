// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

// 같은 이름의 네트워크 구조체(클라이언트 표시용)와 구분한다
using GuildDto = Shinshi.Contracts.SocialGuildView;

namespace Shinshi.Network
{
    // 클라이언트 직렬화 읽기는 배열 길이와 문자열 길이가 상한을 넘으면 메시지 전체를 실패시킨다. API 값이 상한을 넘어도
    // 연결이 끊기지 않게 여기서 배열은 상한까지만 옮기고 문자열은 잘라 낸다. 모르는 위치, 직급 값은 오프라인, 길드원으로 본다
    /// <summary> 소셜 스냅샷 API 응답을 SocialSnapshotMessage로 바꾸는 순수 변환 </summary>
    public static class SocialSnapshotConverter
    {
        /****************************************
        *                Methods
        ****************************************/

        public static SocialSnapshotMessage ToMessage(SocialSnapshotResponse response)
        {
            if (response == null)
            {
                return default;
            }

            return new SocialSnapshotMessage(response.ServerTimeUnixMs, ToFriends(response.Friends), ToRequests(response.Incoming),
                ToRequests(response.Outgoing), ToGuild(response.Guild), ToMembers(response.Members),
                ToRequests(response.GuildApplications, SocialLimits.MAX_SNAPSHOT_APPLICATIONS), ToInvites(response.GuildInvites));
        }

        private static SocialFriendEntry[] ToFriends(SocialFriendView[] source)
        {
            int count = CountOf(source, SocialLimits.MAX_SNAPSHOT_FRIENDS);
            if (count == 0)
            {
                return Array.Empty<SocialFriendEntry>();
            }

            var entries = new SocialFriendEntry[count];
            for (int i = 0; i < count; ++i)
            {
                SocialFriendView view = source[i];
                entries[i] = new SocialFriendEntry(Id(view.CharacterId), Name(view.Name), view.ClassId, PresenceOf(view.Presence),
                    view.SameServer);
            }

            return entries;
        }

        private static SocialRequestEntry[] ToRequests(SocialRequestView[] source)
        {
            return ToRequests(source, SocialLimits.MAX_SNAPSHOT_REQUESTS);
        }

        private static SocialRequestEntry[] ToRequests(SocialRequestView[] source, int max)
        {
            int count = CountOf(source, max);
            if (count == 0)
            {
                return Array.Empty<SocialRequestEntry>();
            }

            var entries = new SocialRequestEntry[count];
            for (int i = 0; i < count; ++i)
            {
                SocialRequestView view = source[i];
                entries[i] = new SocialRequestEntry(Id(view.CharacterId), Name(view.Name), view.ClassId, view.ExpiresAtUnixMs);
            }

            return entries;
        }

        private static SocialGuildView ToGuild(GuildDto guild)
        {
            if ((guild == null) || (!guild.HasGuild) || (string.IsNullOrEmpty(guild.GuildId)))
            {
                return SocialGuildView.None;
            }

            return new SocialGuildView(Id(guild.GuildId), Name(guild.Name), guild.MemberCount, RoleOf(guild.MyRole),
                Id(guild.MyMembershipId), guild.LeaderVersion);
        }

        private static SocialGuildMemberEntry[] ToMembers(SocialGuildMemberView[] source)
        {
            int count = CountOf(source, SocialLimits.MAX_SNAPSHOT_MEMBERS);
            if (count == 0)
            {
                return Array.Empty<SocialGuildMemberEntry>();
            }

            var entries = new SocialGuildMemberEntry[count];
            for (int i = 0; i < count; ++i)
            {
                SocialGuildMemberView view = source[i];
                entries[i] = new SocialGuildMemberEntry(Id(view.CharacterId), Id(view.MembershipId), Name(view.Name), view.ClassId,
                    RoleOf(view.Role), PresenceOf(view.Presence), view.SameServer);
            }

            return entries;
        }

        private static SocialGuildInviteEntry[] ToInvites(SocialGuildInviteView[] source)
        {
            int count = CountOf(source, SocialLimits.MAX_SNAPSHOT_INVITES);
            if (count == 0)
            {
                return Array.Empty<SocialGuildInviteEntry>();
            }

            var entries = new SocialGuildInviteEntry[count];
            for (int i = 0; i < count; ++i)
            {
                SocialGuildInviteView view = source[i];
                entries[i] = new SocialGuildInviteEntry(Id(view.GuildId), Name(view.GuildName), Name(view.InviterName),
                    view.ExpiresAtUnixMs);
            }

            return entries;
        }

        // null 항목이 섞이면 그 뒤를 버린다. 정상 API는 null 항목을 보내지 않는다
        private static int CountOf<T>(T[] source, int max) where T : class
        {
            if (source == null)
            {
                return 0;
            }

            int count = Math.Min(source.Length, max);
            for (int i = 0; i < count; ++i)
            {
                if (source[i] == null)
                {
                    return i;
                }
            }

            return count;
        }

        private static EPresence PresenceOf(int value)
        {
            return ((value >= (int)EPresence.Offline) && (value <= (int)EPresence.Dungeon)) ? (EPresence)value : EPresence.Offline;
        }

        private static EGuildRole RoleOf(int value)
        {
            return (value == (int)EGuildRole.Leader) ? EGuildRole.Leader : EGuildRole.Member;
        }

        private static string Id(string value)
        {
            return Clip(value, Constants.Social.MAX_ID_LENGTH);
        }

        private static string Name(string value)
        {
            return Clip(value, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
        }

        private static string Clip(string value, int max)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return (value.Length > max) ? value.Substring(0, max) : value;
        }
    }
}
