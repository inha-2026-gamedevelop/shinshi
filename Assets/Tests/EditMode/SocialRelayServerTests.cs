// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

// 같은 이름의 네트워크 구조체와 구분한다
using GuildDto = Shinshi.Contracts.SocialGuildView;

namespace Shinshi.Tests
{
    public class SocialRelayServerTests
    {
        // 가짜 게이트웨이로 소셜 중계를 돌린다: 던전 위치 거절, 신원 없는 연결, 확정 뒤 스냅샷, 결과 불명 뒤 재조회와 조회 실패,
        // 귓속말 로컬 전달과 보낸 확인 줄, 같은 발신자 두 줄의 직렬 처리, 꺼내기의 쉼과 확인 목록, bootId 불일치 멈춤

        /****************************************
        *                Fields
        ****************************************/

        private const string ACTOR_ID        = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string TARGET_ID       = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string INSTANCE_ID     = "5a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc05";
        private const string BOOT_ID         = "6a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc06";
        private const string ACTOR_NAME      = "홍길동";
        private const string TARGET_NAME     = "김철수";
        private const int    SENDER          = 1;
        private const int    RECEIVER        = 2;
        private const int    STATUS_OK       = 200;
        private const int    STATUS_CONFLICT = 409;
        private const int    STATUS_DOWN     = 503;

        private double                _now;
        private FakeBackendTransport  _transport;
        private BackendGateway        _gateway;
        private FakeSocialAudience    _audience;
        private SocialDeliveryRouter  _router;
        private Func<BackendRequest, Task<BackendTransportResult>> _respond;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now       = 0.0;
            _respond   = request => Task.FromResult(new BackendTransportResult(STATUS_DOWN, null));
            _transport = new FakeBackendTransport((request, token) => _respond(request));
            _gateway   = new BackendGateway(_transport, ReadClock, FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()),
                null, null);
            _audience  = new FakeSocialAudience();
            _router    = new SocialDeliveryRouter(_audience, new SocialDeliveryLedger());
        }

        [TearDown]
        public void TearDown()
        {
            _gateway.Dispose();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Handle_DungeonRole_SnapshotIsWrongLocationWithoutApiCall()
        {
            var relay = new SocialRelayServer(_gateway, _audience, EServerRole.Dungeon, ReadClock);
            relay.Handle(SENDER, ACTOR_ID, ACTOR_NAME, Request(ESocialAction.Snapshot));

            SocialResultMessage result = _audience.All<SocialResultMessage>(SENDER)[0];
            Assert.That(result.Reason, Is.EqualTo(ERejectReason.WrongLocation));
            Assert.That(_transport.Calls, Is.EqualTo(0));
        }

        [Test]
        public void Handle_NoCharacterId_SessionMismatch()
        {
            var relay = new SocialRelayServer(_gateway, _audience, EServerRole.Town, ReadClock);
            relay.Handle(SENDER, null, ACTOR_NAME, Request(ESocialAction.FriendRequest, TARGET_NAME));

            Assert.That(_audience.All<SocialResultMessage>(SENDER)[0].Reason, Is.EqualTo(ERejectReason.SessionMismatch));
            Assert.That(_transport.Calls, Is.EqualTo(0));
        }

        [Test]
        public void Handle_CommittedWrite_RepliesNoneThenSendsSnapshot()
        {
            _respond = request => Task.FromResult(request.Path.EndsWith("/snapshot", StringComparison.Ordinal)
                ? Ok(EmptySnapshot())
                : Ok(new SocialActionResponse(ContractCodes.OperationStatus.COMMITTED, 0, 0, Array.Empty<string>())));
            var relay = new SocialRelayServer(_gateway, _audience, EServerRole.Town, ReadClock);
            SocialRequestMessage message = Request(ESocialAction.FriendRequest, TARGET_NAME);

            relay.Handle(SENDER, ACTOR_ID, ACTOR_NAME, in message);
            Assert.That(relay.HasPendingWrite(SENDER), Is.True);
            Complete(1);
            Complete(1);

            List<SocialResultMessage> results = _audience.All<SocialResultMessage>(SENDER);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].RequestId, Is.EqualTo(message.RequestId));
            Assert.That(results[0].Reason, Is.EqualTo(ERejectReason.None));
            Assert.That(_audience.All<SocialSnapshotMessage>(SENDER).Count, Is.EqualTo(1));
            Assert.That(relay.HasPendingWrite(SENDER), Is.False);
        }

        [Test]
        public void Handle_RejectedWrite_RepliesApiReasonWithoutSnapshot()
        {
            _respond = request => Task.FromResult(Json(STATUS_CONFLICT,
                new SocialActionResponse(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.FRIEND_LIMIT, 0, Array.Empty<string>())));
            var relay = new SocialRelayServer(_gateway, _audience, EServerRole.Town, ReadClock);

            relay.Handle(SENDER, ACTOR_ID, ACTOR_NAME, Request(ESocialAction.FriendRequest, TARGET_NAME));
            Complete(1);

            Assert.That(_audience.All<SocialResultMessage>(SENDER)[0].Reason, Is.EqualTo(ERejectReason.FriendLimit));
            Assert.That(_transport.Calls, Is.EqualTo(1));
        }

        [Test]
        public void Handle_UnknownResultAndSnapshotFails_SendsBackendUnavailableThenSnapshotFailure()
        {
            var relay = new SocialRelayServer(_gateway, _audience, EServerRole.Town, ReadClock);
            SocialRequestMessage message = Request(ESocialAction.FriendRequest, TARGET_NAME);

            relay.Handle(SENDER, ACTOR_ID, ACTOR_NAME, in message);
            Complete(1);
            Complete(1);

            List<SocialResultMessage> results = _audience.All<SocialResultMessage>(SENDER);
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].RequestId, Is.EqualTo(message.RequestId));
            Assert.That(results[0].Reason, Is.EqualTo(ERejectReason.BackendUnavailable));
            Assert.That(results[1].Action, Is.EqualTo(ESocialAction.Snapshot));
            Assert.That(results[1].RequestId, Is.EqualTo(Guid.Empty));
            Assert.That(results[1].Reason, Is.EqualTo(ERejectReason.BackendUnavailable));
            Assert.That(_transport.Calls, Is.EqualTo(2));
        }

        [Test]
        public void Handle_UnknownResultThenSnapshotSucceeds_SendsSnapshot()
        {
            _respond = request => Task.FromResult(request.Path.EndsWith("/snapshot", StringComparison.Ordinal)
                ? Ok(EmptySnapshot())
                : new BackendTransportResult(STATUS_DOWN, null));
            var relay = new SocialRelayServer(_gateway, _audience, EServerRole.Town, ReadClock);

            relay.Handle(SENDER, ACTOR_ID, ACTOR_NAME, Request(ESocialAction.GuildCreate, "청룡단"));
            Complete(1);
            Complete(1);

            Assert.That(_audience.All<SocialResultMessage>(SENDER).Count, Is.EqualTo(1));
            Assert.That(_audience.All<SocialSnapshotMessage>(SENDER).Count, Is.EqualTo(1));
        }

        [Test]
        public void Submit_CommittedWhisper_SendsSentLineAndRoutesStoredLocalRowsInOrder()
        {
            _audience.Connect(TARGET_ID, RECEIVER);
            SocialDeliveryRow earlier = ChatRow(10, "다른사람", "먼저 온 줄");
            SocialDeliveryRow mine    = ChatRow(11, ACTOR_NAME, "안녕");
            _respond = request => Task.FromResult(Ok(new SocialWhisperResponse(Committed(), TARGET_NAME,
                new[] { earlier, mine })));
            var chat = new SocialChatRelay(_gateway, _audience, _router);

            Assert.That(chat.Submit(SENDER, Whisper("안녕")), Is.EqualTo(EChatReject.None));
            Complete(1);

            ChatBroadcastMessage sent = _audience.All<ChatBroadcastMessage>(SENDER)[0];
            Assert.That(sent.Channel, Is.EqualTo(EChatChannel.WhisperSent));
            Assert.That(sent.Sender, Is.EqualTo(TARGET_NAME));
            Assert.That(sent.DeliveryId, Is.EqualTo(0));
            List<ChatBroadcastMessage> received = _audience.All<ChatBroadcastMessage>(RECEIVER);
            Assert.That(received.Count, Is.EqualTo(2));
            Assert.That(received[0].DeliveryId, Is.EqualTo(10));
            Assert.That(received[0].Sender, Is.EqualTo("다른사람"));
            Assert.That(received[1].Text, Is.EqualTo("안녕"));
            Assert.That(_router.Ledger.IsPending(RECEIVER, 10), Is.True);
            Assert.That(_router.Ledger.OutgoingAckCount, Is.EqualTo(0));
        }

        [Test]
        public void Submit_RecipientNotHere_NotSentAndNotRecorded()
        {
            _respond = request => Task.FromResult(Ok(new SocialWhisperResponse(Committed(), TARGET_NAME, new[] { ChatRow(10, ACTOR_NAME, "a") })));
            var chat = new SocialChatRelay(_gateway, _audience, _router);

            chat.Submit(SENDER, Whisper("a"));
            Complete(1);

            Assert.That(_audience.All<ChatBroadcastMessage>(RECEIVER), Is.Empty);
            Assert.That(_router.Ledger.IsPending(RECEIVER, 10), Is.False);
        }

        [Test]
        public void Submit_RejectedWhisper_NotifiesListenerWithChatReason()
        {
            _respond = request => Task.FromResult(Json(STATUS_CONFLICT, new SocialWhisperResponse(
                new SocialActionResponse(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.WHISPER_TARGET_OFFLINE, 0,
                    Array.Empty<string>()), string.Empty, Array.Empty<SocialDeliveryRow>())));
            var chat     = new SocialChatRelay(_gateway, _audience, _router);
            var listener = new Listener();
            chat.Listener = listener;

            chat.Submit(SENDER, Whisper("a"));
            Complete(1);

            Assert.That(listener.Rejected, Is.EqualTo(new List<EChatReject> { EChatReject.WhisperTargetOffline }));
            Assert.That(_audience.All<ChatBroadcastMessage>(SENDER), Is.Empty);
        }

        [Test]
        public void Submit_SameSenderTwoLinesSlowGateway_SecondWaitsForFirst()
        {
            _audience.Connect(TARGET_ID, RECEIVER);
            var gate  = new TaskCompletionSource<BackendTransportResult>();
            var texts = new ConcurrentQueue<string>();
            int calls = 0;
            _respond = request =>
            {
                var body = BackendJson.Deserialize<SocialGuildChatRequest>(request.BodyJson);
                texts.Enqueue(body.Text);
                long id = 100 + Interlocked.Increment(ref calls);
                var result = Ok(new SocialGuildChatResponse(Committed(), ACTOR_NAME, new[] { GuildRow(id, body.Text) }));
                return (id == 101) ? gate.Task.ContinueWith(_ => result) : Task.FromResult(result);
            };
            var chat = new SocialChatRelay(_gateway, _audience, _router);

            chat.Submit(SENDER, Guild("첫째"));
            chat.Submit(SENDER, Guild("둘째"));
            Thread.Sleep(50);
            Assert.That(_transport.Calls, Is.EqualTo(1));

            gate.SetResult(default);
            Complete(1);
            Complete(1);

            Assert.That(texts.ToArray(), Is.EqualTo(new[] { "첫째", "둘째" }));
            List<ChatBroadcastMessage> received = _audience.All<ChatBroadcastMessage>(RECEIVER);
            Assert.That(received.Count, Is.EqualTo(2));
            Assert.That(received[0].Text, Is.EqualTo("첫째"));
            Assert.That(received[1].Text, Is.EqualTo("둘째"));
        }

        [Test]
        public void Submit_QueueFull_RateLimited()
        {
            _respond = request => new TaskCompletionSource<BackendTransportResult>().Task;
            var chat = new SocialChatRelay(_gateway, _audience, _router);

            Assert.That(chat.Submit(SENDER, Guild("0")), Is.EqualTo(EChatReject.None));
            for (int i = 0; i < Constants.Social.MAX_QUEUED_CHAT_PER_SENDER; ++i)
            {
                Assert.That(chat.Submit(SENDER, Guild("q")), Is.EqualTo(EChatReject.None));
            }

            Assert.That(chat.Submit(SENDER, Guild("x")), Is.EqualTo(EChatReject.RateLimited));
        }

        [Test]
        public void Tick_NoConnectedCharactersAndNoAcks_DoesNotPull()
        {
            var pump = Pump(() => BOOT_ID);
            Assert.That(pump.Tick(), Is.False);
            Assert.That(_transport.Calls, Is.EqualTo(0));
        }

        [Test]
        public void Tick_NoBootIdYet_DoesNotPull()
        {
            _audience.Connect(TARGET_ID, RECEIVER);
            var pump = Pump(() => null);
            Assert.That(pump.Tick(), Is.False);
        }

        [Test]
        public void Tick_PulledRows_RoutedAndOnlyClientAckedRowsGoToNextPull()
        {
            _audience.Connect(TARGET_ID, RECEIVER);
            var pulls = new ConcurrentQueue<SocialDeliveryPullRequest>();
            _respond = request =>
            {
                pulls.Enqueue(BackendJson.Deserialize<SocialDeliveryPullRequest>(request.BodyJson));
                return Task.FromResult(Ok(new SocialDeliveryPullResponse(0, new[] { ChatRow(20, ACTOR_NAME, "a"), ChatRow(21, ACTOR_NAME, "b") })));
            };
            var pump = Pump(() => BOOT_ID);

            Assert.That(pump.Tick(), Is.True);
            Complete(1);
            Assert.That(_audience.All<ChatBroadcastMessage>(RECEIVER).Count, Is.EqualTo(2));

            _router.Ledger.AcceptAcks(RECEIVER, new[] { 21L, 999L }, _now);
            _now = Constants.Social.DELIVERY_PULL_INTERVAL_SECONDS;
            Assert.That(pump.Tick(), Is.True);
            Complete(1);

            SocialDeliveryPullRequest[] sent = pulls.ToArray();
            Assert.That(sent[0].BootId, Is.EqualTo(BOOT_ID));
            Assert.That(sent[0].AckDeliveries, Is.Empty);
            Assert.That(sent[1].AckDeliveries.Length, Is.EqualTo(1));
            Assert.That(sent[1].AckDeliveries[0].DeliveryId, Is.EqualTo(21));
            Assert.That(sent[1].AckDeliveries[0].RecipientCharacterId, Is.EqualTo(TARGET_ID));
        }

        [Test]
        public void Tick_FailedPull_KeepsAcksForNextRequest()
        {
            _audience.Connect(TARGET_ID, RECEIVER);
            _router.Ledger.RecordSent(RECEIVER, TARGET_ID, 30);
            _router.Ledger.AcceptAcks(RECEIVER, new[] { 30L }, _now);
            var pump = Pump(() => BOOT_ID);

            pump.Tick();
            Complete(1);

            Assert.That(_router.Ledger.OutgoingAckCount, Is.EqualTo(1));
            Assert.That(pump.IsHalted, Is.False);
        }

        [Test]
        public void Tick_SessionMismatch_HaltsPump()
        {
            _audience.Connect(TARGET_ID, RECEIVER);
            _respond = request => Task.FromResult(Json(STATUS_CONFLICT,
                new ApiRejection(ContractCodes.RejectReason.SESSION_MISMATCH, string.Empty, 0)));
            var pump = Pump(() => BOOT_ID);

            pump.Tick();
            Complete(1);
            _now = Constants.Social.DELIVERY_PULL_INTERVAL_SECONDS * 2.0;

            Assert.That(pump.IsHalted, Is.True);
            Assert.That(pump.Tick(), Is.False);
        }

        [Test]
        public void HandleWhisper_SelfOrBadNameOrNoSocial_RejectsBeforeApi()
        {
            var audience = new ChatAudience();
            var chat     = new ChatServer(audience, null, ReadClock, null, new SocialChatRelay(_gateway, _audience, _router));

            Assert.That(chat.HandleWhisper(SENDER, ACTOR_NAME, new ChatWhisperRequestMessage(" " + ACTOR_NAME + " ", "a"),
                ACTOR_ID), Is.EqualTo(EChatReject.WhisperSelf));
            _now += Constants.Chat.SEND_WINDOW_SECONDS;
            Assert.That(chat.HandleWhisper(SENDER, ACTOR_NAME, new ChatWhisperRequestMessage("!", "a"), ACTOR_ID),
                Is.EqualTo(EChatReject.WhisperTargetNotFound));
            Assert.That(_transport.Calls, Is.EqualTo(0));

            var offline = new ChatServer(audience, null, ReadClock);
            Assert.That(offline.HandleWhisper(SENDER, ACTOR_NAME, new ChatWhisperRequestMessage(TARGET_NAME, "a"), ACTOR_ID),
                Is.EqualTo(EChatReject.FeatureDisabled));
            Assert.That(offline.Handle(RECEIVER, TARGET_NAME, new ChatRequestMessage(EChatChannel.Guild, "a"), TARGET_ID),
                Is.EqualTo(EChatReject.FeatureDisabled));
        }

        [Test]
        public void HandleGuild_ApiNotMember_SendsNoGuildRejectAndNoBroadcast()
        {
            _respond = request => Task.FromResult(Json(STATUS_CONFLICT, new SocialGuildChatResponse(
                new SocialActionResponse(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.GUILD_NOT_MEMBER, 0,
                    Array.Empty<string>()), string.Empty, Array.Empty<SocialDeliveryRow>())));
            var audience = new ChatAudience();
            var chat     = new ChatServer(audience, null, ReadClock, null, new SocialChatRelay(_gateway, _audience, _router));

            Assert.That(chat.Handle(SENDER, ACTOR_NAME, new ChatRequestMessage(EChatChannel.Guild, "모여"), ACTOR_ID), Is.EqualTo(EChatReject.None));
            Complete(1);

            Assert.That(audience.Rejects, Is.EqualTo(new List<EChatReject> { EChatReject.NoGuild }));
            Assert.That(audience.Broadcasts, Is.EqualTo(0));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private SocialDeliveryPump Pump(Func<string> bootId)
        {
            return new SocialDeliveryPump(_gateway, _audience, _router, INSTANCE_ID, bootId, ReadClock, null);
        }

        private void Complete(int count)
        {
            FakeBackendTransport.WaitForCompleted(_gateway, count);
            _gateway.Pump();
        }

        private static SocialRequestMessage Request(ESocialAction action, string name = null)
        {
            return new SocialRequestMessage(action, Guid.NewGuid(), name, null, null, null, 0);
        }

        private static SocialChatLine Whisper(string text)
        {
            return new SocialChatLine(EChatChannel.Whisper, ACTOR_ID, ACTOR_NAME, TARGET_NAME, text, null);
        }

        private static SocialChatLine Guild(string text)
        {
            return new SocialChatLine(EChatChannel.Guild, ACTOR_ID, ACTOR_NAME, null, text, null);
        }

        private static SocialDeliveryRow ChatRow(long id, string sender, string text)
        {
            return new SocialDeliveryRow(id, TARGET_ID, SocialCodes.DeliveryKind.WHISPER, SocialCodes.ChatChannel.WHISPER, ACTOR_ID, sender,
                text, 0);
        }

        private static SocialDeliveryRow GuildRow(long id, string text)
        {
            return new SocialDeliveryRow(id, TARGET_ID, SocialCodes.DeliveryKind.GUILD_CHAT, SocialCodes.ChatChannel.GUILD, ACTOR_ID,
                ACTOR_NAME, text, 0);
        }

        private static SocialActionResponse Committed()
        {
            return new SocialActionResponse(ContractCodes.OperationStatus.COMMITTED, 0, 0, Array.Empty<string>());
        }

        private static SocialSnapshotResponse EmptySnapshot()
        {
            return new SocialSnapshotResponse(0, Array.Empty<SocialFriendView>(), Array.Empty<SocialRequestView>(),
                Array.Empty<SocialRequestView>(), new GuildDto(false, string.Empty, string.Empty, 0, 0, string.Empty, 0),
                Array.Empty<SocialGuildMemberView>(), Array.Empty<SocialRequestView>(), Array.Empty<SocialGuildInviteView>());
        }

        private static BackendTransportResult Ok(object body)
        {
            return Json(STATUS_OK, body);
        }

        private static BackendTransportResult Json(int status, object body)
        {
            return new BackendTransportResult(status, BackendJson.Serialize(body));
        }

        private double ReadClock() { return _now; }

        /****************************************
        *                Types
        ****************************************/

        private sealed class ChatAudience : IChatAudience
        {
            public readonly List<EChatReject> Rejects    = new List<EChatReject>();
            public int                        Broadcasts = 0;

            public void Send<T>(int participantId, T message) where T : struct, Mirror.NetworkMessage
            {
                if (message is ChatRejectMessage reject)
                {
                    Rejects.Add(reject.Reason);
                }
            }

            public void SendToAll<T>(T message) where T : struct, Mirror.NetworkMessage
            {
                ++Broadcasts;
            }
        }

        private sealed class Listener : ISocialChatListener
        {
            public readonly List<EChatReject> Rejected = new List<EChatReject>();

            public void OnSocialChatRejected(int participantId, EChatReject reason)
            {
                Rejected.Add(reason);
            }

            public void OnSocialChatCommitted(int participantId, string characterId, string apiParticipantId, EChatChannel channel,
                string sender, string text)
            {
            }
        }
    }
}
