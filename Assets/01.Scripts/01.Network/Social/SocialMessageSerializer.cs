// System
using System;
using System.IO;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 소셜 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class SocialMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다.
        // 서버와 클라이언트가 같은 순서를 써야 하므로 필드를 추가할 때는 끝에 붙이고 프로토콜 버전을 올린다.
        // 배열 길이와 문자열 길이는 읽을 때 계약 상한을 넘으면 실패시켜 변조 패킷이 큰 배열을 만들지 못하게 한다

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteSocialRequestMessage(this NetworkWriter writer, SocialRequestMessage value)
        {
            writer.WriteByte((byte)value.Action);
            writer.WriteGuid(value.RequestId);
            writer.WriteString(value.Name);
            writer.WriteString(value.TargetId);
            writer.WriteString(value.GuildId);
            writer.WriteString(value.MembershipId);
            writer.WriteLong(value.ExpectedLeaderVersion);
        }

        public static SocialRequestMessage ReadSocialRequestMessage(this NetworkReader reader)
        {
            var    action                = (ESocialAction)reader.ReadByte();
            Guid   requestId             = reader.ReadGuid();
            string name                  = ReadBounded(reader, SocialLimits.MAX_NAME_INPUT_LENGTH);
            string targetId              = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
            string guildId               = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
            string membershipId          = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
            long   expectedLeaderVersion = reader.ReadLong();
            return new SocialRequestMessage(action, requestId, name, targetId, guildId, membershipId, expectedLeaderVersion);
        }

        public static void WriteSocialResultMessage(this NetworkWriter writer, SocialResultMessage value)
        {
            writer.WriteByte((byte)value.Action);
            writer.WriteGuid(value.RequestId);
            writer.WriteVarUInt((uint)value.Reason);
        }

        public static SocialResultMessage ReadSocialResultMessage(this NetworkReader reader)
        {
            var  action    = (ESocialAction)reader.ReadByte();
            Guid requestId = reader.ReadGuid();
            var  reason    = (ERejectReason)reader.ReadVarUInt();
            return new SocialResultMessage(action, requestId, reason);
        }

        public static void WriteSocialSnapshotMessage(this NetworkWriter writer, SocialSnapshotMessage value)
        {
            writer.WriteLong(value.ServerTimeUnixMs);

            SocialFriendEntry[] friends = value.Friends;
            writer.WriteInt(friends.Length);
            for (int i = 0; i < friends.Length; ++i)
            {
                ref readonly SocialFriendEntry entry = ref friends[i];
                writer.WriteString(entry.CharacterId);
                writer.WriteString(entry.Name);
                writer.WriteInt(entry.ClassId);
                writer.WriteByte((byte)entry.Presence);
                writer.WriteBool(entry.SameServer);
            }

            WriteRequests(writer, value.Incoming);
            WriteRequests(writer, value.Outgoing);

            SocialGuildView guild = value.Guild;
            writer.WriteBool(guild.HasGuild);
            if (guild.HasGuild)
            {
                writer.WriteString(guild.GuildId);
                writer.WriteString(guild.Name);
                writer.WriteInt(guild.MemberCount);
                writer.WriteByte((byte)guild.MyRole);
                writer.WriteString(guild.MyMembershipId);
                writer.WriteLong(guild.LeaderVersion);
            }

            SocialGuildMemberEntry[] members = value.Members;
            writer.WriteInt(members.Length);
            for (int i = 0; i < members.Length; ++i)
            {
                ref readonly SocialGuildMemberEntry entry = ref members[i];
                writer.WriteString(entry.CharacterId);
                writer.WriteString(entry.MembershipId);
                writer.WriteString(entry.Name);
                writer.WriteInt(entry.ClassId);
                writer.WriteByte((byte)entry.Role);
                writer.WriteByte((byte)entry.Presence);
                writer.WriteBool(entry.SameServer);
            }

            WriteRequests(writer, value.GuildApplications);

            SocialGuildInviteEntry[] invites = value.GuildInvites;
            writer.WriteInt(invites.Length);
            for (int i = 0; i < invites.Length; ++i)
            {
                ref readonly SocialGuildInviteEntry entry = ref invites[i];
                writer.WriteString(entry.GuildId);
                writer.WriteString(entry.GuildName);
                writer.WriteString(entry.InviterName);
                writer.WriteLong(entry.ExpiresAtUnixMs);
            }
        }

        public static SocialSnapshotMessage ReadSocialSnapshotMessage(this NetworkReader reader)
        {
            long serverTime = reader.ReadLong();

            int friendCount = ReadCount(reader, SocialLimits.MAX_SNAPSHOT_FRIENDS);
            var friends     = new SocialFriendEntry[friendCount];
            for (int i = 0; i < friendCount; ++i)
            {
                string characterId = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
                string name        = ReadBounded(reader, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
                int    classId     = reader.ReadInt();
                var    presence    = (EPresence)reader.ReadByte();
                bool   sameServer  = reader.ReadBool();
                friends[i] = new SocialFriendEntry(characterId, name, classId, presence, sameServer);
            }

            SocialRequestEntry[] incoming = ReadRequests(reader, SocialLimits.MAX_SNAPSHOT_REQUESTS);
            SocialRequestEntry[] outgoing = ReadRequests(reader, SocialLimits.MAX_SNAPSHOT_REQUESTS);

            SocialGuildView guild = SocialGuildView.None;
            if (reader.ReadBool())
            {
                string guildId       = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
                string guildName     = ReadBounded(reader, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
                int    memberCount   = reader.ReadInt();
                var    myRole        = (EGuildRole)reader.ReadByte();
                string myMembership  = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
                long   leaderVersion = reader.ReadLong();
                guild = new SocialGuildView(guildId, guildName, memberCount, myRole, myMembership, leaderVersion);
            }

            int memberTotal = ReadCount(reader, SocialLimits.MAX_SNAPSHOT_MEMBERS);
            var members     = new SocialGuildMemberEntry[memberTotal];
            for (int i = 0; i < memberTotal; ++i)
            {
                string characterId  = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
                string membershipId = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
                string name         = ReadBounded(reader, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
                int    classId      = reader.ReadInt();
                var    role         = (EGuildRole)reader.ReadByte();
                var    presence     = (EPresence)reader.ReadByte();
                bool   sameServer   = reader.ReadBool();
                members[i] = new SocialGuildMemberEntry(characterId, membershipId, name, classId, role, presence, sameServer);
            }

            SocialRequestEntry[] applications = ReadRequests(reader, SocialLimits.MAX_SNAPSHOT_APPLICATIONS);

            int inviteCount = ReadCount(reader, SocialLimits.MAX_SNAPSHOT_INVITES);
            var invites     = new SocialGuildInviteEntry[inviteCount];
            for (int i = 0; i < inviteCount; ++i)
            {
                string guildId     = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
                string guildName   = ReadBounded(reader, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
                string inviterName = ReadBounded(reader, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
                long   expiresAt   = reader.ReadLong();
                invites[i] = new SocialGuildInviteEntry(guildId, guildName, inviterName, expiresAt);
            }

            return new SocialSnapshotMessage(serverTime, friends, incoming, outgoing, guild, members, applications, invites);
        }

        public static void WriteSocialNoticeMessage(this NetworkWriter writer, SocialNoticeMessage value)
        {
            writer.WriteByte((byte)value.Kind);
            writer.WriteString(value.ActorName);
            writer.WriteString(value.GuildName);
            writer.WriteLong(value.DeliveryId);
        }

        public static SocialNoticeMessage ReadSocialNoticeMessage(this NetworkReader reader)
        {
            var    kind       = (ESocialNotice)reader.ReadByte();
            string actorName  = ReadBounded(reader, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
            string guildName  = ReadBounded(reader, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
            long   deliveryId = reader.ReadLong();
            return new SocialNoticeMessage(kind, actorName, guildName, deliveryId);
        }

        public static void WriteSocialDeliveryAckMessage(this NetworkWriter writer, SocialDeliveryAckMessage value)
        {
            long[] ids = value.DeliveryIds;
            writer.WriteInt(ids.Length);
            for (int i = 0; i < ids.Length; ++i)
            {
                writer.WriteLong(ids[i]);
            }
        }

        public static SocialDeliveryAckMessage ReadSocialDeliveryAckMessage(this NetworkReader reader)
        {
            int count = ReadCount(reader, Constants.Social.MAX_ACK_IDS_PER_MESSAGE);
            var ids   = new long[count];
            for (int i = 0; i < count; ++i)
            {
                ids[i] = reader.ReadLong();
            }

            return new SocialDeliveryAckMessage(ids);
        }

        private static void WriteRequests(NetworkWriter writer, SocialRequestEntry[] requests)
        {
            writer.WriteInt(requests.Length);
            for (int i = 0; i < requests.Length; ++i)
            {
                ref readonly SocialRequestEntry entry = ref requests[i];
                writer.WriteString(entry.CharacterId);
                writer.WriteString(entry.Name);
                writer.WriteInt(entry.ClassId);
                writer.WriteLong(entry.ExpiresAtUnixMs);
            }
        }

        private static SocialRequestEntry[] ReadRequests(NetworkReader reader, int max)
        {
            int count    = ReadCount(reader, max);
            var requests = new SocialRequestEntry[count];
            for (int i = 0; i < count; ++i)
            {
                string characterId = ReadBounded(reader, Constants.Social.MAX_ID_LENGTH);
                string name        = ReadBounded(reader, Constants.Social.MAX_DISPLAY_NAME_LENGTH);
                int    classId     = reader.ReadInt();
                long   expiresAt   = reader.ReadLong();
                requests[i] = new SocialRequestEntry(characterId, name, classId, expiresAt);
            }

            return requests;
        }

        private static int ReadCount(NetworkReader reader, int max)
        {
            int count = reader.ReadInt();
            if ((count < 0) || (count > max))
            {
                throw new InvalidDataException("소셜 배열 길이가 계약 상한을 넘는다");
            }

            return count;
        }

        private static string ReadBounded(NetworkReader reader, int maxLength)
        {
            string value = reader.ReadString();
            if ((value != null) && (value.Length > maxLength))
            {
                throw new InvalidDataException("소셜 문자열 길이가 계약 상한을 넘는다");
            }

            return value;
        }
    }
}
