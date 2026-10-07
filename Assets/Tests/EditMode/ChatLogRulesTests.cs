// System
using System;
using System.Collections.Generic;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class ChatLogRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    SENDER_ID    = 1;
        private const int    MATE_ID      = 2;
        private const int    OTHER_PLAYER = 9;
        private const string API_ID       = "11111111-2222-3333-4444-555555555555";
        private const string CHARACTER_ID = "char-7";

        private static readonly DateTime G_Time = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

        private FakeAudience    _audience = null;
        private FakeParty       _party    = null;
        private FakeSink        _sink     = null;
        private double          _now      = 0.0;
        private ChatServer      _server   = null;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _audience = new FakeAudience();
            _party    = new FakeParty();
            _sink     = new FakeSink();
            _now      = 10.0;
            _server   = new ChatServer(_audience, _party, () => _now, _sink);
            _party.Rooms[SENDER_ID] = new List<int> { SENDER_ID, MATE_ID };
        }

        /****************************************
        *          Record (ChatLogRules)
        ****************************************/

        [Test]
        public void BuildMessageData_AllChannel_WritesFourKeysInOrder()
        {
            string json = ChatLogRules.BuildMessageData(EChatChannel.All, SENDER_ID, "여우", CHARACTER_ID, "안녕");

            Assert.That(json, Is.EqualTo("{\"channel\":\"All\",\"connParticipant\":1,\"characterId\":\"char-7\",\"sender\":\"여우\",\"text\":\"안녕\"}"));
        }

        [Test]
        public void BuildMessageData_PartyChannelAndNoCharacterId_WritesNull()
        {
            string json = ChatLogRules.BuildMessageData(EChatChannel.Party, SENDER_ID, "여우", null, "안녕");

            Assert.That(json, Does.StartWith("{\"channel\":\"Party\",\"connParticipant\":1,\"characterId\":null,"));
        }

        [Test]
        public void BuildMessageData_QuotesBackslashAndNewline_AreEscapedOnOneLine()
        {
            string json = ChatLogRules.BuildMessageData(EChatChannel.All, SENDER_ID, "a\"b", null, "c\\d\ne");

            Assert.That(json, Does.Contain("\"sender\":\"a\\\"b\""));
            Assert.That(json, Does.Contain("\"text\":\"c\\\\d\\ne\""));
            Assert.That(json, Does.Not.Contain("\n"));
        }

        [Test]
        public void BuildMessageData_FullLine_HasNoAccountOrAddressKeys()
        {
            var    line = new ServerLogLine("town", "town-01", "cv-1");
            string text = line.Format(G_Time, ServerLogLine.LEVEL_INFO, ServerLogLine.EVENT_CHAT_MESSAGE, null, API_ID, null, null,
                ServerLogLine.NO_REASON, ChatLogRules.BuildMessageData(EChatChannel.All, SENDER_ID, "여우", CHARACTER_ID, "안녕"));

            Assert.That(text, Does.Contain("\"event\":\"chat.message\""));
            Assert.That(text, Does.Contain("\"participantId\":\"" + API_ID + "\""));
            Assert.That(text, Does.Not.Contain("\"account").And.Not.Contain("\"ip\""));
            Assert.That(text, Does.Not.Contain("\n"));
        }

        [Test]
        public void BuildMessageData_BearerTokenInText_IsRedacted()
        {
            string token = new string('a', 43);

            string json = ChatLogRules.BuildMessageData(EChatChannel.All, SENDER_ID, "여우", null, "내 토큰 Bearer " + token + " 이야");

            Assert.That(json, Does.Not.Contain(token));
            Assert.That(json, Does.Contain("Bearer [REDACTED]"));
        }

        [Test]
        public void ChannelName_AllChannels_AreFixedNames()
        {
            Assert.That(ChatLogRules.ChannelName(EChatChannel.All), Is.EqualTo("All"));
            Assert.That(ChatLogRules.ChannelName(EChatChannel.Party), Is.EqualTo("Party"));
            Assert.That(ChatLogRules.ChannelName(EChatChannel.System), Is.EqualTo("System"));
        }

        /****************************************
        *            Sink (ChatServer)
        ****************************************/

        [Test]
        public void Handle_AcceptedAll_RecordsNormalizedTextAndSender()
        {
            var request = new ChatRequestMessage(EChatChannel.All, "  <b>안녕</b>\n하세요  ");

            EChatReject result = _server.Handle(SENDER_ID, "여우", in request, CHARACTER_ID, API_ID);

            Assert.That(result, Is.EqualTo(EChatReject.None));
            Assert.That(_sink.Records.Count, Is.EqualTo(1));
            Assert.That(_sink.Records[0].ParticipantId, Is.EqualTo(SENDER_ID));
            Assert.That(_sink.Records[0].CharacterId, Is.EqualTo(CHARACTER_ID));
            Assert.That(_sink.Records[0].ApiParticipantId, Is.EqualTo(API_ID));
            Assert.That(_sink.Records[0].Channel, Is.EqualTo(EChatChannel.All));
            Assert.That(_sink.Records[0].Sender, Is.EqualTo("여우"));
            Assert.That(_sink.Records[0].Text, Is.EqualTo("＜b＞안녕＜/b＞ 하세요"));
        }

        [Test]
        public void Handle_AcceptedParty_RecordsPartyChannelOnce()
        {
            var request = new ChatRequestMessage(EChatChannel.Party, "모여");

            _server.Handle(SENDER_ID, "여우", in request);

            Assert.That(_sink.Records.Count, Is.EqualTo(1));
            Assert.That(_sink.Records[0].Channel, Is.EqualTo(EChatChannel.Party));
            Assert.That(_sink.Records[0].CharacterId, Is.Null);
            Assert.That(_sink.Records[0].ApiParticipantId, Is.Null);
        }

        [Test]
        public void Handle_PartyWithoutRoom_RejectsAndRecordsNothing()
        {
            var request = new ChatRequestMessage(EChatChannel.Party, "모여");

            EChatReject result = _server.Handle(OTHER_PLAYER, "곰", in request);

            Assert.That(result, Is.EqualTo(EChatReject.NoParty));
            Assert.That(_sink.Records, Is.Empty);
        }

        [Test]
        public void Handle_RateLimitedAndInvalid_AreNotRecorded()
        {
            var ok      = new ChatRequestMessage(EChatChannel.All, "하나");
            var second  = new ChatRequestMessage(EChatChannel.All, "둘");
            var empty   = new ChatRequestMessage(EChatChannel.All, "   ");
            var system  = new ChatRequestMessage(EChatChannel.System, "사칭");

            _server.Handle(SENDER_ID, "여우", in ok);
            EChatReject limited = _server.Handle(SENDER_ID, "여우", in second);
            _now += 2.0;
            EChatReject emptyResult  = _server.Handle(SENDER_ID, "여우", in empty);
            _now += 2.0;
            EChatReject systemResult = _server.Handle(SENDER_ID, "여우", in system);

            Assert.That(limited, Is.EqualTo(EChatReject.RateLimited));
            Assert.That(emptyResult, Is.EqualTo(EChatReject.Empty));
            Assert.That(systemResult, Is.EqualTo(EChatReject.InvalidChannel));
            Assert.That(_sink.Records.Count, Is.EqualTo(1));
        }

        [Test]
        public void Handle_NullSink_StillBroadcasts()
        {
            var server  = new ChatServer(_audience, _party, () => _now);
            var request = new ChatRequestMessage(EChatChannel.All, "안녕");

            Assert.That(server.Handle(SENDER_ID, "여우", in request), Is.EqualTo(EChatReject.None));
            Assert.That(_audience.BroadcastCount, Is.EqualTo(1));
        }

        /****************************************
        *                Types
        ****************************************/

        private readonly struct Record
        {
            public readonly int          ParticipantId;
            public readonly string       ApiParticipantId;
            public readonly string       CharacterId;
            public readonly EChatChannel Channel;
            public readonly string       Sender;
            public readonly string       Text;

            public Record(int participantId, string apiParticipantId, string characterId, EChatChannel channel, string sender, string text)
            {
                ParticipantId    = participantId;
                ApiParticipantId = apiParticipantId;
                CharacterId   = characterId;
                Channel       = channel;
                Sender        = sender;
                Text          = text;
            }
        }

        private sealed class FakeSink : IChatLogSink
        {
            public readonly List<Record> Records = new List<Record>();

            public void Accepted(int participantId, string apiParticipantId, string characterId, EChatChannel channel, string sender,
                string text)
            {
                Records.Add(new Record(participantId, apiParticipantId, characterId, channel, sender, text));
            }
        }

        private sealed class FakeAudience : IChatAudience
        {
            public int BroadcastCount = 0;

            public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
            {
                if (message is ChatBroadcastMessage)
                {
                    ++BroadcastCount;
                }
            }

            public void SendToAll<T>(T message) where T : struct, NetworkMessage
            {
                if (message is ChatBroadcastMessage)
                {
                    ++BroadcastCount;
                }
            }
        }

        private sealed class FakeParty : IChatPartyLookup
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
