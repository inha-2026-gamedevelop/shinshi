// System
using System.Text;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    // ChatLog와 DeliveryDedupe는 전역 정적 상태다. 시작과 끝에 비운다
    public sealed class SocialChatClientTests
    {
        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
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

        [TestCase(EChatChannel.All, true)]
        [TestCase(EChatChannel.Party, true)]
        [TestCase(EChatChannel.Guild, true)]
        [TestCase(EChatChannel.System, false)]
        [TestCase(EChatChannel.Whisper, false)]
        [TestCase(EChatChannel.WhisperSent, false)]
        public void IsSendable_Channel_OnlyRequestChannels(EChatChannel channel, bool expected)
        {
            Assert.That(ChatRules.IsSendable(channel), Is.EqualTo(expected));
        }

        [TestCase(EChatChannel.All, true)]
        [TestCase(EChatChannel.Party, true)]
        [TestCase(EChatChannel.Guild, true)]
        [TestCase(EChatChannel.Whisper, true)]
        [TestCase(EChatChannel.WhisperSent, true)]
        [TestCase(EChatChannel.System, false)]
        public void IsDisplayable_Channel_IncludesWhispers(EChatChannel channel, bool expected)
        {
            Assert.That(ChatRules.IsDisplayable(channel), Is.EqualTo(expected));
        }

        [Test]
        public void Describe_NewRejects_HaveNotices()
        {
            Assert.That(ChatRules.Describe(EChatReject.NoGuild), Is.Not.Empty);
            Assert.That(ChatRules.Describe(EChatReject.WhisperTargetNotFound), Is.EqualTo("해당 이름의 캐릭터가 없습니다."));
            Assert.That(ChatRules.Describe(EChatReject.WhisperTargetOffline), Is.EqualTo("상대가 접속 중이 아닙니다."));
            Assert.That(ChatRules.Describe(EChatReject.WhisperSelf), Is.Not.Empty);
            Assert.That(ChatRules.Describe(EChatReject.BackendUnavailable), Is.Not.Empty);
            Assert.That(ChatRules.Describe(EChatReject.FeatureDisabled), Is.Not.Empty);
        }

        /****************************************
        *              ChatLog
        ****************************************/

        [Test]
        public void AddMessage_WhisperGuildAndSent_AreDisplayed()
        {
            ChatLog.AddMessage(EChatChannel.Whisper, "홍길동", "안녕", 1);
            ChatLog.AddMessage(EChatChannel.WhisperSent, "김철수", "반가워", 0);
            ChatLog.AddMessage(EChatChannel.Guild, "이영희", "모여", 2);

            Assert.That(ChatLog.Entries.Count, Is.EqualTo(3));
            Assert.That(ChatLog.Entries[0].Channel, Is.EqualTo(EChatChannel.Whisper));
            Assert.That(ChatLog.Entries[1].Channel, Is.EqualTo(EChatChannel.WhisperSent));
            Assert.That(ChatLog.Entries[2].Channel, Is.EqualTo(EChatChannel.Guild));
        }

        [Test]
        public void AddMessage_SameDeliveryIdTwice_SecondDroppedButAcked()
        {
            ChatLog.AddMessage(EChatChannel.Whisper, "홍길동", "안녕", 7);
            ChatLog.AddMessage(EChatChannel.Whisper, "홍길동", "안녕", 7);

            Assert.That(ChatLog.Entries.Count, Is.EqualTo(1));
            Assert.That(DeliveryDedupe.PendingAckCount, Is.EqualTo(1));
        }

        [Test]
        public void AddMessage_ZeroDeliveryId_NeverDeduped()
        {
            ChatLog.AddMessage(EChatChannel.All, "a", "같은 말");
            ChatLog.AddMessage(EChatChannel.All, "a", "같은 말");

            Assert.That(ChatLog.Entries.Count, Is.EqualTo(2));
            Assert.That(DeliveryDedupe.PendingAckCount, Is.EqualTo(0));
        }

        [Test]
        public void AddMessage_SmallerIdLater_AppendsAtBottom()
        {
            ChatLog.AddMessage(EChatChannel.Guild, "a", "둘째", 20);
            ChatLog.AddMessage(EChatChannel.Guild, "b", "첫째", 10);

            Assert.That(ChatLog.Entries[1].Text, Is.EqualTo("첫째"));
        }

        [Test]
        public void AddMessage_SystemFromServer_StillRejected()
        {
            ChatLog.AddMessage(EChatChannel.System, "위조", "가짜", 3);

            Assert.That(ChatLog.Entries.Count, Is.EqualTo(0));
            Assert.That(DeliveryDedupe.PendingAckCount, Is.EqualTo(1));
        }

        [Test]
        public void ConnectionReset_KeepsSeenMemory_ClearDropsIt()
        {
            ChatLog.AddMessage(EChatChannel.Whisper, "a", "하나", 5);

            // 서버 이동: 확인 대기만 비우고 본 ID는 남긴다
            DeliveryDedupe.ClearPendingAcks();
            ChatLog.AddMessage(EChatChannel.Whisper, "a", "하나", 5);
            Assert.That(ChatLog.Entries.Count, Is.EqualTo(1));
            Assert.That(DeliveryDedupe.PendingAckCount, Is.EqualTo(1));

            // 완전 종료: 기록과 기억을 함께 비운다
            ChatLog.Clear();
            ChatLog.AddMessage(EChatChannel.Whisper, "a", "하나", 5);
            Assert.That(ChatLog.Entries.Count, Is.EqualTo(1));
        }

        /****************************************
        *              DeliveryDedupe
        ****************************************/

        [Test]
        public void TryMarkSeen_OverCapacity_ForgetsOldest()
        {
            for (long id = 1; id <= Constants.Chat.DELIVERY_DEDUPE_CAPACITY; ++id)
            {
                Assert.That(DeliveryDedupe.TryMarkSeen(id), Is.True);
            }

            Assert.That(DeliveryDedupe.TryMarkSeen(1), Is.False);
            Assert.That(DeliveryDedupe.TryMarkSeen(Constants.Chat.DELIVERY_DEDUPE_CAPACITY + 1), Is.True);
            Assert.That(DeliveryDedupe.TryMarkSeen(1), Is.True);
            Assert.That(DeliveryDedupe.SeenCount, Is.EqualTo(Constants.Chat.DELIVERY_DEDUPE_CAPACITY));
        }

        [Test]
        public void TakeAcks_MoreThanMax_ReturnsBatchInOrder()
        {
            int total = Constants.Social.MAX_ACK_IDS_PER_MESSAGE + 3;
            for (long id = 1; id <= total; ++id)
            {
                DeliveryDedupe.Accept(id);
            }

            long[] first = DeliveryDedupe.TakeAcks(Constants.Social.MAX_ACK_IDS_PER_MESSAGE);
            Assert.That(first.Length, Is.EqualTo(Constants.Social.MAX_ACK_IDS_PER_MESSAGE));
            Assert.That(first[0], Is.EqualTo(1));
            Assert.That(DeliveryDedupe.PendingAckCount, Is.EqualTo(3));
            Assert.That(DeliveryDedupe.TakeAcks(Constants.Social.MAX_ACK_IDS_PER_MESSAGE).Length, Is.EqualTo(3));
            Assert.That(DeliveryDedupe.TakeAcks(Constants.Social.MAX_ACK_IDS_PER_MESSAGE), Is.Null);
        }

        [Test]
        public void Accept_NoticeAndChat_ShareMemory()
        {
            Assert.That(DeliveryDedupe.Accept(9), Is.True);
            ChatLog.AddMessage(EChatChannel.Guild, "a", "같은 ID", 9);

            Assert.That(ChatLog.Entries.Count, Is.EqualTo(0));
            Assert.That(DeliveryDedupe.PendingAckCount, Is.EqualTo(1));
        }

        /****************************************
        *              ChatViewRules
        ****************************************/

        [TestCase("/w 홍길동 안녕 하세요", EChatCommand.Whisper, "홍길동", "안녕 하세요")]
        [TestCase("/W 홍길동 안녕", EChatCommand.Whisper, "홍길동", "안녕")]
        [TestCase("  /귓  홍길동   반가워  ", EChatCommand.Whisper, "홍길동", "반가워")]
        [TestCase("/w 홍길동", EChatCommand.Whisper, "홍길동", "")]
        [TestCase("/r 고마워", EChatCommand.Reply, "", "고마워")]
        [TestCase("/R", EChatCommand.Reply, "", "")]
        [TestCase("/w", EChatCommand.Invalid, "", "")]
        [TestCase("/w   ", EChatCommand.Invalid, "", "")]
        [TestCase("안녕", EChatCommand.None, "", "안녕")]
        [TestCase("/춤", EChatCommand.None, "", "/춤")]
        [TestCase("/ww 홍길동 x", EChatCommand.None, "", "/ww 홍길동 x")]
        public void TryParseCommand_Input_ParsesWhisperAndReply(string input, EChatCommand expected, string target, string body)
        {
            EChatCommand command = ChatViewRules.TryParseCommand(input, out string parsedTarget, out string parsedBody);

            Assert.That(command, Is.EqualTo(expected));
            Assert.That(parsedTarget, Is.EqualTo(target));
            Assert.That(parsedBody, Is.EqualTo(body));
        }

        [TestCase(EChatTab.Guild, EChatChannel.Guild, true)]
        [TestCase(EChatTab.Guild, EChatChannel.All, false)]
        [TestCase(EChatTab.Whisper, EChatChannel.Whisper, true)]
        [TestCase(EChatTab.Whisper, EChatChannel.WhisperSent, true)]
        [TestCase(EChatTab.Whisper, EChatChannel.Party, false)]
        [TestCase(EChatTab.All, EChatChannel.WhisperSent, true)]
        public void Matches_NewTabs_FilterChannels(EChatTab tab, EChatChannel channel, bool expected)
        {
            Assert.That(ChatViewRules.Matches(tab, channel), Is.EqualTo(expected));
        }

        [Test]
        public void AppendLine_GuildAndWhispers_FormatsPrefixes()
        {
            var builder = new StringBuilder();
            var guild   = new ChatEntry(EChatChannel.Guild, "홍길동", "모여");
            var got     = new ChatEntry(EChatChannel.Whisper, "김철수", "안녕");
            var sent    = new ChatEntry(EChatChannel.WhisperSent, "이영희", "응");

            ChatViewRules.AppendLine(builder, in guild, null, null, null, "76E6BD", "E79CE9");
            ChatViewRules.AppendLine(builder, in got, null, null, null, null, null);
            ChatViewRules.AppendLine(builder, in sent, null, null, null, null, null);

            Assert.That(builder.ToString(), Is.EqualTo(
                "<color=#76E6BD>[길드] 홍길동 : 모여</color>\n[귓속말] 김철수 : 안녕\n[귓속말] 이영희에게 : 응"));
        }

        [Test]
        public void DropdownAndTab_GuildChannel_MapsBothWays()
        {
            Assert.That(ChatViewRules.ToSendChannel(2), Is.EqualTo(EChatChannel.Guild));
            Assert.That(ChatViewRules.ToDropdownIndex(EChatTab.Guild), Is.EqualTo(2));
            Assert.That(ChatViewRules.ChangesInputChannel(EChatTab.Whisper), Is.False);
            Assert.That(ChatViewRules.ChangesInputChannel(EChatTab.System), Is.False);
            Assert.That(ChatViewRules.ChangesInputChannel(EChatTab.Guild), Is.True);
            Assert.That(ChatViewRules.WhisperPrefix("홍길동"), Is.EqualTo("/w 홍길동 "));
        }
    }
}
