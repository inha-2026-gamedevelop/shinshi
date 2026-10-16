// System
using System;
using System.IO;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

// 같은 이름의 Contracts DTO(백엔드 응답)와 구분한다
using SocialGuildView = Shinshi.Network.SocialGuildView;

namespace Shinshi.Tests
{
    public sealed class SocialMessageSerializerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long SERVER_TIME    = 1760000000000L;
        private const long EXPIRES        = SERVER_TIME + 1000L;
        private const long LEADER_VERSION = 7;
        private const long DELIVERY_ID    = 42;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void SocialRequestMessage_RoundTrip_KeepsEveryField()
        {
            var request = new SocialRequestMessage(ESocialAction.GuildDelegate, Guid.NewGuid(), "청룡단", "c2", "g1", "m2", LEADER_VERSION);

            SocialRequestMessage read = RoundTrip(request, (w, v) => w.WriteSocialRequestMessage(v), r => r.ReadSocialRequestMessage());

            Assert.That(read.Action, Is.EqualTo(ESocialAction.GuildDelegate));
            Assert.That(read.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(read.Name, Is.EqualTo("청룡단"));
            Assert.That(read.TargetId, Is.EqualTo("c2"));
            Assert.That(read.GuildId, Is.EqualTo("g1"));
            Assert.That(read.MembershipId, Is.EqualTo("m2"));
            Assert.That(read.ExpectedLeaderVersion, Is.EqualTo(LEADER_VERSION));
        }

        [Test]
        public void SocialRequestMessage_NameOverLimit_ReadFails()
        {
            var request = new SocialRequestMessage(ESocialAction.FriendRequest, Guid.NewGuid(),
                new string('a', SocialLimits.MAX_NAME_INPUT_LENGTH + 1), null, null, null, 0);
            var writer = new NetworkWriter();
            writer.WriteSocialRequestMessage(request);
            var reader = new NetworkReader(writer.ToArraySegment());

            Assert.Throws<InvalidDataException>(() => reader.ReadSocialRequestMessage());
        }

        [Test]
        public void SocialResultMessage_RoundTrip_KeepsReason()
        {
            var result = new SocialResultMessage(ESocialAction.GuildCreate, Guid.NewGuid(), ERejectReason.GuildNameTaken);

            SocialResultMessage read = RoundTrip(result, (w, v) => w.WriteSocialResultMessage(v), r => r.ReadSocialResultMessage());

            Assert.That(read.Action, Is.EqualTo(ESocialAction.GuildCreate));
            Assert.That(read.RequestId, Is.EqualTo(result.RequestId));
            Assert.That(read.Reason, Is.EqualTo(ERejectReason.GuildNameTaken));
        }

        [Test]
        public void SocialSnapshotMessage_RoundTrip_KeepsListsAndGuild()
        {
            var snapshot = new SocialSnapshotMessage(SERVER_TIME,
                new[] { new SocialFriendEntry("f1", "홍길동", 5, EPresence.Town, true) },
                new[] { new SocialRequestEntry("r1", "박민수", 3, EXPIRES) },
                new[] { new SocialRequestEntry("o1", "최지우", 4, EXPIRES) },
                new SocialGuildView("g1", "청룡단", 2, EGuildRole.Leader, "m1", LEADER_VERSION),
                new[] { new SocialGuildMemberEntry("c2", "m2", "김철수", 4, EGuildRole.Member, EPresence.Dungeon, false) },
                new[] { new SocialRequestEntry("a1", "윤호", 3, EXPIRES) },
                new[] { new SocialGuildInviteEntry("g2", "백호회", "이영희", EXPIRES) });

            SocialSnapshotMessage read = RoundTrip(snapshot, (w, v) => w.WriteSocialSnapshotMessage(v), r => r.ReadSocialSnapshotMessage());

            Assert.That(read.ServerTimeUnixMs, Is.EqualTo(SERVER_TIME));
            Assert.That(read.Friends[0].Name, Is.EqualTo("홍길동"));
            Assert.That(read.Friends[0].Presence, Is.EqualTo(EPresence.Town));
            Assert.That(read.Friends[0].SameServer, Is.True);
            Assert.That(read.Incoming[0].ExpiresAtUnixMs, Is.EqualTo(EXPIRES));
            Assert.That(read.Outgoing[0].CharacterId, Is.EqualTo("o1"));
            Assert.That(read.Guild.HasGuild, Is.True);
            Assert.That(read.Guild.MyRole, Is.EqualTo(EGuildRole.Leader));
            Assert.That(read.Guild.MyMembershipId, Is.EqualTo("m1"));
            Assert.That(read.Guild.LeaderVersion, Is.EqualTo(LEADER_VERSION));
            Assert.That(read.Members[0].MembershipId, Is.EqualTo("m2"));
            Assert.That(read.Members[0].Presence, Is.EqualTo(EPresence.Dungeon));
            Assert.That(read.GuildApplications[0].Name, Is.EqualTo("윤호"));
            Assert.That(read.GuildInvites[0].GuildName, Is.EqualTo("백호회"));
        }

        [Test]
        public void SocialSnapshotMessage_NoGuild_ReadsEmptyGuildAndArrays()
        {
            var snapshot = new SocialSnapshotMessage(SERVER_TIME, null, null, null, SocialGuildView.None, null, null, null);

            SocialSnapshotMessage read = RoundTrip(snapshot, (w, v) => w.WriteSocialSnapshotMessage(v), r => r.ReadSocialSnapshotMessage());

            Assert.That(read.Guild.HasGuild, Is.False);
            Assert.That(read.Friends, Is.Empty);
            Assert.That(read.Members, Is.Empty);
            Assert.That(read.GuildInvites, Is.Empty);
        }

        [Test]
        public void SocialSnapshotMessage_InvitesOverLimit_ReadFails()
        {
            var invites = new SocialGuildInviteEntry[SocialLimits.MAX_SNAPSHOT_INVITES + 1];
            for (int i = 0; i < invites.Length; ++i)
            {
                invites[i] = new SocialGuildInviteEntry("g" + i, "길드" + i, "초대자", EXPIRES);
            }

            var snapshot = new SocialSnapshotMessage(SERVER_TIME, null, null, null, SocialGuildView.None, null, null, invites);
            var writer   = new NetworkWriter();
            writer.WriteSocialSnapshotMessage(snapshot);
            var reader = new NetworkReader(writer.ToArraySegment());

            Assert.Throws<InvalidDataException>(() => reader.ReadSocialSnapshotMessage());
        }

        [Test]
        public void SocialSnapshotMessage_FriendsOverLimit_ReadFails()
        {
            var friends = new SocialFriendEntry[SocialLimits.MAX_SNAPSHOT_FRIENDS + 1];
            var snapshot = new SocialSnapshotMessage(SERVER_TIME, friends, null, null, SocialGuildView.None, null, null, null);
            var writer   = new NetworkWriter();
            writer.WriteSocialSnapshotMessage(snapshot);
            var reader = new NetworkReader(writer.ToArraySegment());

            Assert.Throws<InvalidDataException>(() => reader.ReadSocialSnapshotMessage());
        }

        [Test]
        public void SocialNoticeMessage_RoundTrip_KeepsDeliveryId()
        {
            var notice = new SocialNoticeMessage(ESocialNotice.GuildInvited, "홍길동", "청룡단", DELIVERY_ID);

            SocialNoticeMessage read = RoundTrip(notice, (w, v) => w.WriteSocialNoticeMessage(v), r => r.ReadSocialNoticeMessage());

            Assert.That(read.Kind, Is.EqualTo(ESocialNotice.GuildInvited));
            Assert.That(read.ActorName, Is.EqualTo("홍길동"));
            Assert.That(read.GuildName, Is.EqualTo("청룡단"));
            Assert.That(read.DeliveryId, Is.EqualTo(DELIVERY_ID));
        }

        [Test]
        public void SocialDeliveryAckMessage_RoundTrip_AndOverLimitThrows()
        {
            var ack = new SocialDeliveryAckMessage(new long[] { 1, 2, DELIVERY_ID });

            SocialDeliveryAckMessage read = RoundTrip(ack, (w, v) => w.WriteSocialDeliveryAckMessage(v),
                r => r.ReadSocialDeliveryAckMessage());
            Assert.That(read.DeliveryIds, Is.EqualTo(new long[] { 1, 2, DELIVERY_ID }));

            var tooMany = new SocialDeliveryAckMessage(new long[Constants.Social.MAX_ACK_IDS_PER_MESSAGE + 1]);
            var writer  = new NetworkWriter();
            writer.WriteSocialDeliveryAckMessage(tooMany);
            var reader = new NetworkReader(writer.ToArraySegment());
            Assert.Throws<InvalidDataException>(() => reader.ReadSocialDeliveryAckMessage());
        }

        [Test]
        public void ChatBroadcastMessage_RoundTrip_KeepsDeliveryId()
        {
            var line = new ChatBroadcastMessage(EChatChannel.Whisper, "홍길동", "안녕", DELIVERY_ID);

            ChatBroadcastMessage read = RoundTrip(line, (w, v) => w.WriteChatBroadcastMessage(v), r => r.ReadChatBroadcastMessage());

            Assert.That(read.Channel, Is.EqualTo(EChatChannel.Whisper));
            Assert.That(read.Sender, Is.EqualTo("홍길동"));
            Assert.That(read.Text, Is.EqualTo("안녕"));
            Assert.That(read.DeliveryId, Is.EqualTo(DELIVERY_ID));
        }

        [Test]
        public void ChatBroadcastMessage_DefaultDeliveryId_IsZero()
        {
            Assert.That(new ChatBroadcastMessage(EChatChannel.All, "a", "b").DeliveryId, Is.EqualTo(0));
        }

        [Test]
        public void ChatWhisperRequestMessage_RoundTrip_AndLongNameThrows()
        {
            var whisper = new ChatWhisperRequestMessage("홍길동", "안녕");

            ChatWhisperRequestMessage read = RoundTrip(whisper, (w, v) => w.WriteChatWhisperRequestMessage(v),
                r => r.ReadChatWhisperRequestMessage());
            Assert.That(read.TargetName, Is.EqualTo("홍길동"));
            Assert.That(read.Text, Is.EqualTo("안녕"));

            var tooLong = new ChatWhisperRequestMessage(new string('a', SocialLimits.MAX_NAME_INPUT_LENGTH + 1), "x");
            var writer  = new NetworkWriter();
            writer.WriteChatWhisperRequestMessage(tooLong);
            var reader = new NetworkReader(writer.ToArraySegment());
            Assert.Throws<InvalidDataException>(() => reader.ReadChatWhisperRequestMessage());
        }

        [Test]
        public void SocialEnums_SerializedValues_MatchContract()
        {
            Assert.That((int)ESocialAction.GuildDelegate, Is.EqualTo(15));
            Assert.That((int)ESocialNotice.GuildMemberLeft, Is.EqualTo(8));
            Assert.That((int)EPresence.Dungeon, Is.EqualTo(2));
            Assert.That((int)EGuildRole.Leader, Is.EqualTo(1));
            Assert.That((int)EChatChannel.Guild, Is.EqualTo(3));
            Assert.That((int)EChatChannel.Whisper, Is.EqualTo(4));
            Assert.That((int)EChatChannel.WhisperSent, Is.EqualTo(5));
            Assert.That((int)EChatReject.FeatureDisabled, Is.EqualTo(13));
            Assert.That((int)ERejectReason.SocialTargetNotFound, Is.EqualTo(1600));
            Assert.That((int)ERejectReason.GuildLeaderChanged, Is.EqualTo(1645));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static T RoundTrip<T>(T value, Action<NetworkWriter, T> write, Func<NetworkReader, T> read)
        {
            var writer = new NetworkWriter();
            write(writer, value);
            var reader = new NetworkReader(writer.ToArraySegment());
            T result = read(reader);
            Assert.That(reader.Remaining, Is.EqualTo(0));
            return result;
        }
    }
}
