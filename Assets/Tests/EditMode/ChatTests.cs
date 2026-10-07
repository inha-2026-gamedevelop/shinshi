// System
using System.Collections.Generic;
using System.Text;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class ChatTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SENDER_ID = 1;
        private const int MATE_ID   = 2;
        private const int OTHER_ID  = 3;

        private FakeChatAudience _audience = null;
        private FakePartyLookup  _party    = null;
        private double           _now      = 0.0;
        private ChatServer       _server   = null;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _audience = new FakeChatAudience();
            _party    = new FakePartyLookup();
            _now      = 10.0;
            _server   = new ChatServer(_audience, _party, () => _now);
            _party.Rooms[SENDER_ID] = new List<int> { SENDER_ID, MATE_ID };
            _party.Rooms[MATE_ID]   = new List<int> { SENDER_ID, MATE_ID };
            ChatLog.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ChatLog.Clear();
        }

        /****************************************
        *              ChatRules
        ****************************************/

        [TestCase("  안녕\n\t하세요  ", "안녕 하세요")]
        [TestCase("<color=red>태그</color>", "＜color=red＞태그＜/color＞")]
        [TestCase("emoji 😀 ok", "emoji 😀 ok")]
        public void TryNormalize_ValidText_ReturnsOnePlainLine(string input, string expected)
        {
            Assert.That(ChatRules.TryNormalize(input, out string text), Is.EqualTo(EChatReject.None));
            Assert.That(text, Is.EqualTo(expected));
        }

        [Test]
        public void TryNormalize_InvalidInput_Rejects()
        {
            string loneHigh = "a" + (char)0xD83D;
            string loneLow  = (char)0xDE00 + "a";
            Assert.That(ChatRules.TryNormalize("   ", out _), Is.EqualTo(EChatReject.Empty));
            Assert.That(ChatRules.TryNormalize(new string('a', Constants.Chat.MAX_MESSAGE_LENGTH + 1), out _),
                Is.EqualTo(EChatReject.TooLong));
            Assert.That(ChatRules.TryNormalize("a\u0007b", out _), Is.EqualTo(EChatReject.InvalidCharacter));
            Assert.That(ChatRules.TryNormalize("a‮b", out _), Is.EqualTo(EChatReject.InvalidCharacter));
            Assert.That(ChatRules.TryNormalize(loneHigh, out _), Is.EqualTo(EChatReject.InvalidCharacter));
            Assert.That(ChatRules.TryNormalize(loneLow, out _), Is.EqualTo(EChatReject.InvalidCharacter));
        }

        [Test]
        public void NormalizeSender_MissingOrTaggedName_UsesSafeName()
        {
            Assert.That(ChatRules.NormalizeSender(null, 7), Is.EqualTo("모험가 7"));
            Assert.That(ChatRules.NormalizeSender("<b>신시</b>", 7), Is.EqualTo("＜b＞신시＜/b＞"));
            Assert.That(ChatRules.NormalizeSender(new string('가', 40), 7).Length, Is.EqualTo(Constants.Chat.MAX_SENDER_LENGTH));
        }

        /****************************************
        *              ChatServer
        ****************************************/

        [Test]
        public void Handle_AllChannel_BroadcastsToEveryoneWithServerName()
        {
            EChatReject reason = _server.Handle(SENDER_ID, "호랑이", new ChatRequestMessage(EChatChannel.All, " 안녕 "));

            Assert.That(reason, Is.EqualTo(EChatReject.None));
            Assert.That(_audience.Broadcasts.Count, Is.EqualTo(1));
            Assert.That(_audience.Broadcasts[0].Sender, Is.EqualTo("호랑이"));
            Assert.That(_audience.Broadcasts[0].Text, Is.EqualTo("안녕"));
            Assert.That(_audience.Direct.Count, Is.EqualTo(0));
        }

        [Test]
        public void Handle_PartyChannel_SendsOnlyToRoomMembers()
        {
            _server.Handle(SENDER_ID, "호랑이", new ChatRequestMessage(EChatChannel.Party, "모이자"));

            Assert.That(_audience.Broadcasts.Count, Is.EqualTo(0));
            Assert.That(_audience.DirectTargets, Is.EquivalentTo(new[] { SENDER_ID, MATE_ID }));
            Assert.That(_audience.DirectTargets, Has.No.Member(OTHER_ID));
        }

        [Test]
        public void Handle_PartyChannelWithoutRoom_RejectsNoParty()
        {
            EChatReject reason = _server.Handle(OTHER_ID, "늑대", new ChatRequestMessage(EChatChannel.Party, "누구 있나"));

            Assert.That(reason, Is.EqualTo(EChatReject.NoParty));
            Assert.That(_audience.Rejects, Is.EqualTo(new[] { EChatReject.NoParty }));
        }

        [Test]
        public void Handle_PartyChannelInDungeon_SendsToEveryone()
        {
            var dungeon = new ChatServer(_audience, null, () => _now);

            dungeon.Handle(OTHER_ID, "늑대", new ChatRequestMessage(EChatChannel.Party, "보스 간다"));

            Assert.That(_audience.Broadcasts.Count, Is.EqualTo(1));
            Assert.That(_audience.Broadcasts[0].Channel, Is.EqualTo(EChatChannel.Party));
        }

        [Test]
        public void Handle_SystemChannelFromClient_RejectsAndNeverBroadcasts()
        {
            EChatReject reason = _server.Handle(SENDER_ID, "호랑이", new ChatRequestMessage(EChatChannel.System, "점검"));

            Assert.That(reason, Is.EqualTo(EChatReject.InvalidChannel));
            Assert.That(_audience.Broadcasts.Count, Is.EqualTo(0));
        }

        [Test]
        public void Handle_SecondMessageInWindow_RateLimitedAndInvalidConsumesWindow()
        {
            Assert.That(_server.Handle(SENDER_ID, "호랑이", new ChatRequestMessage(EChatChannel.All, "   ")),
                Is.EqualTo(EChatReject.Empty));
            Assert.That(_server.Handle(SENDER_ID, "호랑이", new ChatRequestMessage(EChatChannel.All, "안녕")),
                Is.EqualTo(EChatReject.RateLimited));
            Assert.That(_audience.Broadcasts.Count, Is.EqualTo(0));

            // 거절 안내는 간격 안에서 한 번만 간다
            Assert.That(_audience.Rejects.Count, Is.EqualTo(1));

            _now += Constants.Chat.SEND_WINDOW_SECONDS;
            Assert.That(_server.Handle(SENDER_ID, "호랑이", new ChatRequestMessage(EChatChannel.All, "안녕")),
                Is.EqualTo(EChatReject.None));
        }

        [Test]
        public void Forget_Disconnected_ClearsRateState()
        {
            _server.Handle(SENDER_ID, "호랑이", new ChatRequestMessage(EChatChannel.All, "안녕"));
            Assert.That(_server.TrackedSenderCount, Is.EqualTo(1));

            _server.Forget(SENDER_ID);

            Assert.That(_server.TrackedSenderCount, Is.EqualTo(0));
        }

        [Test]
        public void Serializer_RoundTrip_KeepsFields()
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteChatBroadcastMessage(new ChatBroadcastMessage(EChatChannel.Party, "호랑이", "안녕"));
                writer.WriteChatRequestMessage(new ChatRequestMessage(EChatChannel.All, "본문"));
                writer.WriteChatRejectMessage(new ChatRejectMessage(EChatReject.RateLimited));
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    ChatBroadcastMessage broadcast = reader.ReadChatBroadcastMessage();
                    ChatRequestMessage   request   = reader.ReadChatRequestMessage();
                    ChatRejectMessage    reject    = reader.ReadChatRejectMessage();
                    Assert.That(broadcast.Channel, Is.EqualTo(EChatChannel.Party));
                    Assert.That(broadcast.Sender, Is.EqualTo("호랑이"));
                    Assert.That(broadcast.Text, Is.EqualTo("안녕"));
                    Assert.That(request.Channel, Is.EqualTo(EChatChannel.All));
                    Assert.That(request.Text, Is.EqualTo("본문"));
                    Assert.That(reject.Reason, Is.EqualTo(EChatReject.RateLimited));
                }
            }
        }

        /****************************************
        *              ChatLog
        ****************************************/

        [Test]
        public void AddSystem_SameTextWithinRepeatWindow_AddsOnce()
        {
            ChatLog.AddSystem("안내", 100.0);
            ChatLog.AddSystem("안내", 100.5);
            ChatLog.AddSystem("안내", 100.0 + Constants.Chat.SYSTEM_REPEAT_SECONDS);

            Assert.That(ChatLog.Entries.Count, Is.EqualTo(2));
        }

        [Test]
        public void AddMessage_OverCapacityOrSystemChannel_KeepsRecentServerLines()
        {
            for (int i = 0; i < Constants.Chat.LOG_CAPACITY + 5; ++i)
            {
                ChatLog.AddMessage(EChatChannel.All, "a", i.ToString());
            }

            ChatLog.AddMessage(EChatChannel.System, "위조", "가짜 안내");

            Assert.That(ChatLog.Entries.Count, Is.EqualTo(Constants.Chat.LOG_CAPACITY));
            Assert.That(ChatLog.Entries[0].Text, Is.EqualTo("5"));
        }

        /****************************************
        *              ChatViewRules
        ****************************************/

        [TestCase(EChatTab.All, EChatChannel.Party, true)]
        [TestCase(EChatTab.All, EChatChannel.System, true)]
        [TestCase(EChatTab.Party, EChatChannel.All, false)]
        [TestCase(EChatTab.Party, EChatChannel.Party, true)]
        [TestCase(EChatTab.System, EChatChannel.Party, false)]
        public void Matches_Tab_FiltersChannels(EChatTab tab, EChatChannel channel, bool expected)
        {
            Assert.That(ChatViewRules.Matches(tab, channel), Is.EqualTo(expected));
        }

        [Test]
        public void AppendLine_Entries_FormatsPrefixAndColor()
        {
            var builder = new StringBuilder();
            var party   = new ChatEntry(EChatChannel.Party, "호랑이", "안녕");
            var system  = new ChatEntry(EChatChannel.System, string.Empty, "점검");
            var all     = new ChatEntry(EChatChannel.All, "늑대", "반가워");

            ChatViewRules.AppendLine(builder, in party, null, "8FD3FF", "E9C97A");
            ChatViewRules.AppendLine(builder, in system, null, "8FD3FF", "E9C97A");
            ChatViewRules.AppendLine(builder, in all, null, "8FD3FF", "E9C97A");

            Assert.That(builder.ToString(), Is.EqualTo(
                "<color=#8FD3FF>[파티] 호랑이 : 안녕</color>\n<color=#E9C97A>[시스템] 점검</color>\n[전체] 늑대 : 반가워"));
        }

        [Test]
        public void ToSendChannel_DropdownIndex_MapsAllAndParty()
        {
            Assert.That(ChatViewRules.ToSendChannel(0), Is.EqualTo(EChatChannel.All));
            Assert.That(ChatViewRules.ToSendChannel(1), Is.EqualTo(EChatChannel.Party));
            Assert.That(ChatViewRules.ToDropdownIndex(EChatTab.Party), Is.EqualTo(1));
            Assert.That(ChatViewRules.ToDropdownIndex(EChatTab.All), Is.EqualTo(0));
        }

        /****************************************
        *                Types
        ****************************************/

        private sealed class FakeChatAudience : IChatAudience
        {
            public readonly List<ChatBroadcastMessage> Broadcasts    = new List<ChatBroadcastMessage>();
            public readonly List<object>               Direct        = new List<object>();
            public readonly List<int>                  DirectTargets = new List<int>();
            public readonly List<EChatReject>          Rejects       = new List<EChatReject>();

            public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
            {
                if (message is ChatRejectMessage reject)
                {
                    Rejects.Add(reject.Reason);
                    return;
                }

                Direct.Add(message);
                DirectTargets.Add(participantId);
            }

            public void SendToAll<T>(T message) where T : struct, NetworkMessage
            {
                if (message is ChatBroadcastMessage broadcast)
                {
                    Broadcasts.Add(broadcast);
                }
            }
        }

        private sealed class FakePartyLookup : IChatPartyLookup
        {
            public readonly Dictionary<int, List<int>> Rooms = new Dictionary<int, List<int>>();

            public bool TryGetPartyMembers(int participantId, List<int> into)
            {
                if (!Rooms.TryGetValue(participantId, out List<int> members))
                {
                    return false;
                }

                into.AddRange(members);
                return true;
            }
        }
    }
}
