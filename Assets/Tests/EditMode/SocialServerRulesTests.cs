// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class SocialServerRulesTests
    {
        // 소셜 서버 순수 규칙: 위치 판정, 관리 요청 속도 제한, 쓰기 동시 1건, 요청 형식, 수신 확인 소유 검사와 속도 제한,
        // 발신자별 직렬 대기열, 전달 행 변환, API 응답 분류, 상호작용 패널 버튼 조건

        /****************************************
        *                Fields
        ****************************************/

        private const string ACTOR_ID        = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string TARGET_ID       = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string GUILD_ID        = "3a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc03";
        private const string MEMBERSHIP      = "4a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc04";
        private const string ACTOR_NAME      = "홍길동";
        private const int    PARTICIPANT     = 1;
        private const int    OTHER           = 2;
        private const long   DELIVERY_ID     = 77;
        private const int    STATUS_OK       = 200;
        private const int    STATUS_CONFLICT = 409;
        private const int    STATUS_DENIED   = 401;
        private const int    STATUS_LIMITED  = 429;
        private const int    STATUS_DOWN     = 503;

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(EServerRole.Town, ERejectReason.None)]
        [TestCase(EServerRole.Dungeon, ERejectReason.WrongLocation)]
        public void CheckLocation_Role_OnlyTownAccepts(EServerRole role, ERejectReason expected)
        {
            Assert.That(SocialRequestGate.CheckLocation(role), Is.EqualTo(expected));
        }

        [Test]
        public void Evaluate_MoreWritesThanWindow_RejectsWithRateLimited()
        {
            var gate = new SocialRequestGate();
            for (int i = 0; i < Constants.Social.REQUESTS_PER_WINDOW; ++i)
            {
                Assert.That(gate.Evaluate(ESocialAction.FriendRequest, Guid.NewGuid(), 0.0, out _), Is.EqualTo(ESocialGateVerdict.Accept));
            }

            Assert.That(gate.Evaluate(ESocialAction.FriendRequest, Guid.NewGuid(), 0.0, out ERejectReason reason),
                Is.EqualTo(ESocialGateVerdict.Reject));
            Assert.That(reason, Is.EqualTo(ERejectReason.RateLimited));
            Assert.That(gate.Evaluate(ESocialAction.FriendRequest, Guid.NewGuid(), Constants.Social.REQUEST_WINDOW_SECONDS, out _),
                Is.EqualTo(ESocialGateVerdict.Accept));
        }

        [Test]
        public void Evaluate_SnapshotWindow_OnePerTwoSecondsAndSeparateFromWrites()
        {
            var gate = new SocialRequestGate();
            Assert.That(gate.Evaluate(ESocialAction.Snapshot, Guid.NewGuid(), 0.0, out _), Is.EqualTo(ESocialGateVerdict.Accept));
            Assert.That(gate.Evaluate(ESocialAction.Snapshot, Guid.NewGuid(), 1.0, out ERejectReason reason), Is.EqualTo(ESocialGateVerdict.Reject));
            Assert.That(reason, Is.EqualTo(ERejectReason.RateLimited));
            Assert.That(gate.Evaluate(ESocialAction.FriendRequest, Guid.NewGuid(), 1.0, out _), Is.EqualTo(ESocialGateVerdict.Accept));
            Assert.That(gate.Evaluate(ESocialAction.Snapshot, Guid.NewGuid(), Constants.Social.SNAPSHOT_REQUEST_WINDOW_SECONDS, out _),
                Is.EqualTo(ESocialGateVerdict.Accept));
        }

        [Test]
        public void Evaluate_WhileWriteInFlight_SameIdIgnoredOtherPendingCommitSnapshotAllowed()
        {
            var  gate  = new SocialRequestGate();
            Guid first = Guid.NewGuid();
            Assert.That(gate.Evaluate(ESocialAction.GuildCreate, first, 0.0, out _), Is.EqualTo(ESocialGateVerdict.Accept));
            gate.BeginWrite(first);

            Assert.That(gate.Evaluate(ESocialAction.GuildCreate, first, 0.0, out _), Is.EqualTo(ESocialGateVerdict.IgnoreDuplicate));
            Assert.That(gate.Evaluate(ESocialAction.FriendRequest, Guid.NewGuid(), 0.0, out ERejectReason reason),
                Is.EqualTo(ESocialGateVerdict.Reject));
            Assert.That(reason, Is.EqualTo(ERejectReason.PendingCommit));
            Assert.That(gate.Evaluate(ESocialAction.Snapshot, Guid.NewGuid(), 0.0, out _), Is.EqualTo(ESocialGateVerdict.Accept));

            gate.EndWrite();
            Assert.That(gate.HasInFlight, Is.False);
        }

        [Test]
        public void Evaluate_ResendOfInFlight_IgnoredEvenWhenWindowIsFull()
        {
            var  gate  = new SocialRequestGate();
            Guid first = Guid.NewGuid();
            gate.Evaluate(ESocialAction.FriendRequest, first, 0.0, out _);
            gate.BeginWrite(first);
            for (int i = 0; i < Constants.Social.REQUESTS_PER_WINDOW; ++i)
            {
                gate.Evaluate(ESocialAction.FriendRemove, Guid.NewGuid(), 0.0, out _);
            }

            Assert.That(gate.Evaluate(ESocialAction.FriendRequest, first, 0.0, out _), Is.EqualTo(ESocialGateVerdict.IgnoreDuplicate));
        }

        [Test]
        public void Evaluate_EmptyRequestIdWrite_InvalidTarget()
        {
            var gate = new SocialRequestGate();
            Assert.That(gate.Evaluate(ESocialAction.FriendAccept, Guid.Empty, 0.0, out ERejectReason reason), Is.EqualTo(ESocialGateVerdict.Reject));
            Assert.That(reason, Is.EqualTo(ERejectReason.InvalidTarget));
        }

        [Test]
        public void TryBuild_FriendRequest_NormalizesNameAndRejectsSelf()
        {
            var message = Request(ESocialAction.FriendRequest, " 김철수 ", null, null, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in message, ACTOR_ID, ACTOR_NAME, out string path, out object body),
                Is.EqualTo(ERejectReason.None));
            Assert.That(path, Is.EqualTo(string.Format(SocialRoutes.FRIEND_REQUEST_FORMAT, ACTOR_ID)));
            Assert.That(((SocialTargetNameRequest)body).TargetName, Is.EqualTo("김철수"));

            var self = Request(ESocialAction.FriendRequest, ACTOR_NAME, null, null, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in self, ACTOR_ID, ACTOR_NAME, out _, out _), Is.EqualTo(ERejectReason.SocialTargetSelf));

            var invalid = Request(ESocialAction.FriendRequest, "!", null, null, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in invalid, ACTOR_ID, ACTOR_NAME, out _, out _),
                Is.EqualTo(ERejectReason.SocialTargetNotFound));
        }

        [Test]
        public void TryBuild_TargetActions_RequireUuidAndHaveNoBody()
        {
            var accept = Request(ESocialAction.FriendAccept, null, TARGET_ID.ToUpperInvariant(), null, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in accept, ACTOR_ID, ACTOR_NAME, out string path, out object body),
                Is.EqualTo(ERejectReason.None));
            Assert.That(path, Is.EqualTo(string.Format(SocialRoutes.FRIEND_ACCEPT_FORMAT, ACTOR_ID, TARGET_ID)));
            Assert.That(body, Is.Null);

            var broken = Request(ESocialAction.FriendRemove, null, "../admin", null, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in broken, ACTOR_ID, ACTOR_NAME, out _, out _), Is.EqualTo(ERejectReason.InvalidTarget));
        }

        [Test]
        public void TryBuild_GuildCreate_InvalidNameAndRequestIdInBody()
        {
            var bad = Request(ESocialAction.GuildCreate, "청룡 단", null, null, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in bad, ACTOR_ID, ACTOR_NAME, out _, out _), Is.EqualTo(ERejectReason.GuildNameInvalid));

            var good = Request(ESocialAction.GuildCreate, "청룡단", null, null, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in good, ACTOR_ID, ACTOR_NAME, out _, out object body), Is.EqualTo(ERejectReason.None));
            Assert.That(((SocialGuildCreateRequest)body).CreateRequestId, Is.EqualTo(good.RequestId.ToString("D")));
        }

        [Test]
        public void TryBuild_KickAndDelegate_SelfRejectedAndVersionCarried()
        {
            var kickSelf = Request(ESocialAction.GuildKick, null, ACTOR_ID, GUILD_ID, MEMBERSHIP, 0);
            Assert.That(SocialRequestRules.TryBuild(in kickSelf, ACTOR_ID, ACTOR_NAME, out _, out _), Is.EqualTo(ERejectReason.SocialTargetSelf));

            var delegateTo = Request(ESocialAction.GuildDelegate, null, TARGET_ID, GUILD_ID, MEMBERSHIP, 5);
            Assert.That(SocialRequestRules.TryBuild(in delegateTo, ACTOR_ID, ACTOR_NAME, out _, out object body), Is.EqualTo(ERejectReason.None));
            var request = (SocialGuildDelegateRequest)body;
            Assert.That(request.ExpectedLeaderVersion, Is.EqualTo(5));
            Assert.That(request.NewLeaderCharacterId, Is.EqualTo(TARGET_ID));
            Assert.That(request.MembershipId, Is.EqualTo(MEMBERSHIP));

            var leaveMissing = Request(ESocialAction.GuildLeave, null, null, GUILD_ID, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in leaveMissing, ACTOR_ID, ACTOR_NAME, out _, out _), Is.EqualTo(ERejectReason.InvalidTarget));
        }

        [Test]
        public void TryBuild_NoActor_SessionMismatch()
        {
            var message = Request(ESocialAction.FriendRequest, "김철수", null, null, null, 0);
            Assert.That(SocialRequestRules.TryBuild(in message, null, ACTOR_NAME, out _, out _), Is.EqualTo(ERejectReason.SessionMismatch));
        }

        [Test]
        public void AcceptAcks_OnlyIdsSentToThatConnection_AreQueuedWithRecordedRecipient()
        {
            var ledger = new SocialDeliveryLedger();
            ledger.RecordSent(PARTICIPANT, TARGET_ID, DELIVERY_ID);
            ledger.RecordSent(OTHER, ACTOR_ID, DELIVERY_ID + 1);

            int accepted = ledger.AcceptAcks(PARTICIPANT, new[] { DELIVERY_ID, DELIVERY_ID + 1, 999L }, 0.0);

            Assert.That(accepted, Is.EqualTo(1));
            SocialDeliveryAck[] acks = ledger.TakeAcks(SocialLimits.MAX_ACKS_PER_PULL);
            Assert.That(acks.Length, Is.EqualTo(1));
            Assert.That(acks[0].DeliveryId, Is.EqualTo(DELIVERY_ID));
            Assert.That(acks[0].RecipientCharacterId, Is.EqualTo(TARGET_ID));
            Assert.That(ledger.IsPending(PARTICIPANT, DELIVERY_ID), Is.False);
            Assert.That(ledger.IsPending(OTHER, DELIVERY_ID + 1), Is.True);
        }

        [Test]
        public void AcceptAcks_BeforeClientAck_NothingToSend()
        {
            var ledger = new SocialDeliveryLedger();
            ledger.RecordSent(PARTICIPANT, TARGET_ID, DELIVERY_ID);
            Assert.That(ledger.OutgoingAckCount, Is.EqualTo(0));
            Assert.That(ledger.TakeAcks(SocialLimits.MAX_ACKS_PER_PULL), Is.Empty);
        }

        [Test]
        public void AcceptAcks_RateAndLengthLimits_DropWholeMessage()
        {
            var ledger = new SocialDeliveryLedger();
            for (long id = 1; id <= Constants.Social.ACK_MESSAGES_PER_WINDOW + 1; ++id)
            {
                ledger.RecordSent(PARTICIPANT, TARGET_ID, id);
            }

            for (long id = 1; id <= Constants.Social.ACK_MESSAGES_PER_WINDOW; ++id)
            {
                Assert.That(ledger.AcceptAcks(PARTICIPANT, new[] { id }, 0.0), Is.EqualTo(1));
            }

            long last = Constants.Social.ACK_MESSAGES_PER_WINDOW + 1;
            Assert.That(ledger.AcceptAcks(PARTICIPANT, new[] { last }, 0.0), Is.EqualTo(0));
            Assert.That(ledger.AcceptAcks(PARTICIPANT, new[] { last }, Constants.Social.ACK_WINDOW_SECONDS), Is.EqualTo(1));
            Assert.That(ledger.AcceptAcks(PARTICIPANT, new long[Constants.Social.MAX_ACK_IDS_PER_MESSAGE + 1], 2.0 * Constants.Social.ACK_WINDOW_SECONDS),
                Is.EqualTo(0));
        }

        [Test]
        public void RecordSent_OverCapacity_EvictsOldestAndForgetDropsAll()
        {
            var ledger = new SocialDeliveryLedger(2);
            ledger.RecordSent(PARTICIPANT, TARGET_ID, 1);
            ledger.RecordSent(PARTICIPANT, TARGET_ID, 2);
            ledger.RecordSent(PARTICIPANT, TARGET_ID, 3);
            Assert.That(ledger.IsPending(PARTICIPANT, 1), Is.False);
            Assert.That(ledger.IsPending(PARTICIPANT, 3), Is.True);

            ledger.Forget(PARTICIPANT);
            Assert.That(ledger.AcceptAcks(PARTICIPANT, new[] { 3L }, 0.0), Is.EqualTo(0));
        }

        [Test]
        public void RestoreAcks_FailedPull_PutsAcksBackInFront()
        {
            var ledger = new SocialDeliveryLedger();
            ledger.RecordSent(PARTICIPANT, TARGET_ID, 1);
            ledger.RecordSent(PARTICIPANT, TARGET_ID, 2);
            ledger.AcceptAcks(PARTICIPANT, new[] { 1L }, 0.0);
            SocialDeliveryAck[] sent = ledger.TakeAcks(SocialLimits.MAX_ACKS_PER_PULL);
            ledger.AcceptAcks(PARTICIPANT, new[] { 2L }, 1.0);

            ledger.RestoreAcks(sent);

            SocialDeliveryAck[] again = ledger.TakeAcks(SocialLimits.MAX_ACKS_PER_PULL);
            Assert.That(again.Length, Is.EqualTo(2));
            Assert.That(again[0].DeliveryId, Is.EqualTo(1));
            Assert.That(again[1].DeliveryId, Is.EqualTo(2));
        }

        [Test]
        public void TryEnqueue_SameSender_SerialWithBoundedQueue()
        {
            var queue = new SocialChatQueue();
            Assert.That(queue.TryEnqueue(PARTICIPANT, Line("1"), out bool first), Is.EqualTo(EChatReject.None));
            Assert.That(first, Is.True);
            for (int i = 0; i < Constants.Social.MAX_QUEUED_CHAT_PER_SENDER; ++i)
            {
                Assert.That(queue.TryEnqueue(PARTICIPANT, Line((i + 2).ToString()), out bool now), Is.EqualTo(EChatReject.None));
                Assert.That(now, Is.False);
            }

            Assert.That(queue.TryEnqueue(PARTICIPANT, Line("x"), out _), Is.EqualTo(EChatReject.RateLimited));
            Assert.That(queue.TryEnqueue(OTHER, Line("o"), out bool other), Is.EqualTo(EChatReject.None));
            Assert.That(other, Is.True);

            Assert.That(queue.TryNext(PARTICIPANT, out SocialChatLine next), Is.True);
            Assert.That(next.Text, Is.EqualTo("2"));
            Assert.That(queue.TryNext(PARTICIPANT, out next), Is.True);
            Assert.That(next.Text, Is.EqualTo("3"));
            Assert.That(queue.TryNext(PARTICIPANT, out next), Is.True);
            Assert.That(next.Text, Is.EqualTo("4"));
            Assert.That(queue.TryNext(PARTICIPANT, out _), Is.False);
            Assert.That(queue.CountOf(PARTICIPANT), Is.EqualTo(0));
            Assert.That(queue.TryEnqueue(PARTICIPANT, Line("5"), out bool again), Is.EqualTo(EChatReject.None));
            Assert.That(again, Is.True);
        }

        [Test]
        public void Forget_Sender_DropsWaitingLines()
        {
            var queue = new SocialChatQueue();
            queue.TryEnqueue(PARTICIPANT, Line("1"), out _);
            queue.TryEnqueue(PARTICIPANT, Line("2"), out _);
            queue.Forget(PARTICIPANT);
            Assert.That(queue.TryNext(PARTICIPANT, out _), Is.False);
        }

        [Test]
        public void TryBuildChat_StoredRow_UsesStoredChannelSenderAndText()
        {
            var row = new SocialDeliveryRow(DELIVERY_ID, TARGET_ID, SocialCodes.DeliveryKind.GUILD_CHAT, SocialCodes.ChatChannel.GUILD,
                ACTOR_ID, "김철수", "모여", 0);
            Assert.That(SocialDeliveryRules.TryBuildChat(row, out ChatBroadcastMessage message), Is.True);
            Assert.That(message.Channel, Is.EqualTo(EChatChannel.Guild));
            Assert.That(message.Sender, Is.EqualTo("김철수"));
            Assert.That(message.Text, Is.EqualTo("모여"));
            Assert.That(message.DeliveryId, Is.EqualTo(DELIVERY_ID));

            var badChannel = new SocialDeliveryRow(DELIVERY_ID, TARGET_ID, SocialCodes.DeliveryKind.WHISPER, 0, ACTOR_ID, "a", "b", 0);
            Assert.That(SocialDeliveryRules.TryBuildChat(badChannel, out _), Is.False);
            var tooLong = new SocialDeliveryRow(DELIVERY_ID, TARGET_ID, SocialCodes.DeliveryKind.WHISPER, SocialCodes.ChatChannel.WHISPER,
                ACTOR_ID, "a", new string('x', Constants.Chat.MAX_MESSAGE_LENGTH + 1), 0);
            Assert.That(SocialDeliveryRules.TryBuildChat(tooLong, out _), Is.False);
        }

        [Test]
        public void TryBuildNotice_NoticeRow_MapsKindActorGuild()
        {
            var row = new SocialDeliveryRow(DELIVERY_ID, TARGET_ID, SocialCodes.DeliveryKind.NOTICE, SocialCodes.Notice.GUILD_INVITED,
                string.Empty, "김철수", "청룡단", 0);
            Assert.That(SocialDeliveryRules.TryBuildNotice(row, out SocialNoticeMessage message), Is.True);
            Assert.That(message.Kind, Is.EqualTo(ESocialNotice.GuildInvited));
            Assert.That(message.ActorName, Is.EqualTo("김철수"));
            Assert.That(message.GuildName, Is.EqualTo("청룡단"));
            Assert.That(message.DeliveryId, Is.EqualTo(DELIVERY_ID));

            var unknown = new SocialDeliveryRow(DELIVERY_ID, TARGET_ID, SocialCodes.DeliveryKind.NOTICE, 99, string.Empty, "a", "b", 0);
            Assert.That(SocialDeliveryRules.TryBuildNotice(unknown, out _), Is.False);
        }

        [Test]
        public void Classify_Outcomes_MapToVerdicts()
        {
            var committed = new SocialActionResponse(ContractCodes.OperationStatus.COMMITTED, 0, 0, Array.Empty<string>());
            var rejected  = new SocialActionResponse(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.FRIEND_LIMIT, 0,
                Array.Empty<string>());

            Assert.That(Verdict(new BackendResponse(1, EBackendOutcome.Succeeded, STATUS_OK, 0, committed, 1), committed, out ERejectReason none),
                Is.EqualTo(ESocialApiVerdict.Committed));
            Assert.That(none, Is.EqualTo(ERejectReason.None));
            Assert.That(Verdict(new BackendResponse(1, EBackendOutcome.Rejected, STATUS_CONFLICT, 0, rejected, 1), rejected, out ERejectReason limit),
                Is.EqualTo(ESocialApiVerdict.Rejected));
            Assert.That(limit, Is.EqualTo(ERejectReason.FriendLimit));
            Assert.That(Verdict(new BackendResponse(1, EBackendOutcome.Rejected, STATUS_DENIED, 0, null, 1), null, out _),
                Is.EqualTo(ESocialApiVerdict.Unknown));
            Assert.That(Verdict(new BackendResponse(1, EBackendOutcome.ServerError, STATUS_LIMITED, 0, null, 1), null, out ERejectReason rate),
                Is.EqualTo(ESocialApiVerdict.Rejected));
            Assert.That(rate, Is.EqualTo(ERejectReason.RateLimited));
            Assert.That(Verdict(new BackendResponse(1, EBackendOutcome.ServerError, STATUS_DOWN, 0, null, 1), null, out _),
                Is.EqualTo(ESocialApiVerdict.Unknown));
            Assert.That(Verdict(new BackendResponse(1, EBackendOutcome.Timeout, 0, 0, null, 1), null, out _), Is.EqualTo(ESocialApiVerdict.Unknown));
            Assert.That(Verdict(new BackendResponse(1, EBackendOutcome.CircuitOpen, 0, 0, null, 0), null, out _), Is.EqualTo(ESocialApiVerdict.NotSent));
        }

        [TestCase(ERejectReason.SocialTargetNotFound, EChatReject.WhisperTargetNotFound)]
        [TestCase(ERejectReason.WhisperTargetOffline, EChatReject.WhisperTargetOffline)]
        [TestCase(ERejectReason.SocialTargetSelf, EChatReject.WhisperSelf)]
        [TestCase(ERejectReason.GuildNotMember, EChatReject.NoGuild)]
        [TestCase(ERejectReason.FeatureDisabled, EChatReject.FeatureDisabled)]
        [TestCase(ERejectReason.RateLimited, EChatReject.RateLimited)]
        [TestCase(ERejectReason.SocialNotConnected, EChatReject.BackendUnavailable)]
        public void ToChatReject_ApiReasons_MapToChatReasons(ERejectReason reason, EChatReject expected)
        {
            Assert.That(SocialApiRules.ToChatReject(reason), Is.EqualTo(expected));
        }

        [Test]
        public void CanInviteToGuild_Conditions_OnlyLeaderInTownWithoutPendingWrite()
        {
            bool canSend = PlayerInteractionRules.CanSendSocialRequest(true, false, false);
            Assert.That(canSend, Is.True);
            Assert.That(PlayerInteractionRules.CanSendSocialRequest(false, false, false), Is.False);
            Assert.That(PlayerInteractionRules.CanSendSocialRequest(true, true, false), Is.False);
            Assert.That(PlayerInteractionRules.CanSendSocialRequest(true, false, true), Is.False);
            Assert.That(PlayerInteractionRules.CanInviteToGuild(canSend, true, EGuildRole.Leader), Is.True);
            Assert.That(PlayerInteractionRules.CanInviteToGuild(canSend, true, EGuildRole.Member), Is.False);
            Assert.That(PlayerInteractionRules.CanInviteToGuild(canSend, false, EGuildRole.Leader), Is.False);
        }

        [Test]
        public void DescribeSocialResult_AcceptedAndRejected_UseSocialTexts()
        {
            var ok = new SocialResultMessage(ESocialAction.FriendRequest, Guid.NewGuid(), ERejectReason.None);
            var no = new SocialResultMessage(ESocialAction.GuildInvite, Guid.NewGuid(), ERejectReason.GuildNotLeader);
            Assert.That(PlayerInteractionRules.DescribeSocialResult(in ok), Is.EqualTo(SocialViewRules.SuccessText(ESocialAction.FriendRequest)));
            Assert.That(PlayerInteractionRules.DescribeSocialResult(in no), Is.EqualTo(SocialViewRules.Describe(ERejectReason.GuildNotLeader)));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static SocialRequestMessage Request(ESocialAction action, string name, string targetId, string guildId, string membershipId,
            long version)
        {
            return new SocialRequestMessage(action, Guid.NewGuid(), name, targetId, guildId, membershipId, version);
        }

        private static SocialChatLine Line(string text)
        {
            return new SocialChatLine(EChatChannel.Guild, ACTOR_ID, ACTOR_NAME, null, text, null);
        }

        private static ESocialApiVerdict Verdict(BackendResponse response, SocialActionResponse nested, out ERejectReason reason)
        {
            return SocialApiRules.Classify(in response, nested, out reason);
        }
    }
}
