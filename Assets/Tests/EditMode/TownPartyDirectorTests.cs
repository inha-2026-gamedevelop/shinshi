// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class TownPartyDirectorTests
    {
        // 마을 파티 흐름: 준비 확인 API(계정 진행 해금 포함), 성장 변경으로 준비 해제, NO_CAPACITY 대기열과 방이 바뀌면 줄에서 빠지는 조건,
        // 초대 만료, 예약 요청의 보스 계약 필드, 입장권을 참가자 본인에게만 보내기

        /****************************************
        *                Fields
        ****************************************/

        private const string BATTLE_WOLBAEK  = "battle_wolbaek_easy";
        private const string REGION_FOREST   = "region_forest";
        private const string BOSS_WOLBAEK    = "boss_wolbaek";
        private const string SMALL_POTION    = "item_potion_small";
        private const string BINDING_ACCOUNT = "Account";
        private const string ACCOUNT_A       = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string ACCOUNT_B       = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string CHARACTER_A     = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string CHARACTER_B     = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string LEASE_ID        = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string SESSION_ID      = "4a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string NAME_A          = "alpha";
        private const string NAME_B          = "bravo";
        private const string CONTENT_VERSION = "test-content";
        private const int    PARTICIPANT_A   = 1;
        private const int    PARTICIPANT_B   = 2;
        private const long   REVISION        = 5;
        private const int    STATUS_OK       = 200;
        private const int    STATUS_CONFLICT = 409;
        private const float  COUNTDOWN       = 3f;
        private const double AFTER_COUNTDOWN = 3.5;
        private const double INVITE_EXPIRED  = 31.0;

        private readonly List<Object> _created = new List<Object>();

        private double                    _now;
        private FakeBackendTransport      _transport;
        private BackendGateway            _gateway;
        private FakeTownAudience          _audience;
        private FakeDirectory             _directory;
        private TownPartyDirector         _director;
        private TownMember                _memberA;
        private TownMember                _memberB;
        private volatile int              _reservationReason;
        private volatile bool             _introCompleted;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now               = 0.0;
            _reservationReason = ContractCodes.RejectReason.NONE;
            _introCompleted    = false;
            _transport         = new FakeBackendTransport((request, token) => Task.FromResult(Respond(request)));
            _gateway           = new BackendGateway(_transport, ZeroClock,
                FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null, null);
            _audience          = new FakeTownAudience();
            _directory         = new FakeDirectory();

            var progression = ScriptableObject.CreateInstance<ProgressionCatalogSO>();
            var life        = ScriptableObject.CreateInstance<LifeConfigSO>();
            _created.Add(progression);
            _created.Add(life);
            _director = new TownPartyDirector(_gateway, _audience, _directory, progression, life, COUNTDOWN, CONTENT_VERSION, false,
                ReadClock, null);

            _memberA = new TownMember(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A, LEASE_ID, NAME_A, EPlayerClass.Warrior);
            _memberB = new TownMember(PARTICIPANT_B, ACCOUNT_B, CHARACTER_B, LEASE_ID, NAME_B, EPlayerClass.Sorcerer);
            _directory.Add(_memberA);
            _directory.Add(_memberB);
        }

        [TearDown]
        public void TearDown()
        {
            _gateway.Dispose();
            for (int i = 0; i < _created.Count; ++i)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CreateRoom_AuthenticatedTownExperience_ReachesWireAndPrepLevel()
        {
            _memberA = new TownMember(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A, LEASE_ID, NAME_A, EPlayerClass.Swordsman, 2850);
            _directory.Add(_memberA);

            CreateBossRoom();

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out PartyRoomStateMessage message));
            Assert.AreEqual(2850L, message.Members[0].TotalExperience);
            var writer = new Mirror.NetworkWriter();
            writer.WritePartyRoomStateMessage(message);
            var reader = new Mirror.NetworkReader(writer.ToArraySegment());
            PartyRoomStateMessage received = reader.ReadPartyRoomStateMessage();
            Assert.AreEqual(0, reader.Remaining);
            CharacterLevelConfigSO config = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            _created.Add(config);
            var display = new List<Shinshi.UI.SortieMemberData>();

            Shinshi.UI.SortiePrepRules.BuildMembers(in received, display, config.ToSettings());

            Assert.AreEqual(5, display[0].Level);
            Assert.AreEqual(EPlayerClass.Swordsman, display[0].PlayerClass);
        }

        [Test]
        public void CreateRoom_MemberWithoutExperience_BroadcastsUnknown()
        {
            CreateBossRoom();

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out PartyRoomStateMessage message));
            Assert.AreEqual(-1L, message.Members[0].TotalExperience);
        }

        [Test]
        public void SetReady_ReadinessPasses_MarksReadyWithApiRevision()
        {
            PartyRoom room = CreateBossRoom();

            Send(_memberA, Ready(1));
            Assert.AreEqual(EPartyReadyState.Checking, room.ReadyAt(0));
            Complete(1);

            Assert.AreEqual(EPartyReadyState.Ready, room.ReadyAt(0));
            Assert.AreEqual(REVISION, room.CharacterRevisionAt(0));
            Assert.IsTrue(_transport.Requests.TryDequeue(out BackendRequest request));
            Assert.AreEqual(GameServerRoutes.READINESS, request.Path);
            var body = BackendJson.Deserialize<ReadinessRequest>(request.BodyJson);
            Assert.AreEqual(ACCOUNT_A, body.Participants[0].AccountId);
            Assert.AreEqual(CHARACTER_A, body.Participants[0].CharacterId);
        }

        [Test]
        public void SetReady_ForestWithoutIntro_StaysNotReadyWithMissingUnlock()
        {
            PartyRoom room = CreateForestRoom();

            Send(_memberA, Ready(1));
            Complete(1);

            Assert.AreEqual(EPartyReadyState.NotReady, room.ReadyAt(0));
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out PartyResultMessage result));
            Assert.AreEqual(ERejectReason.MissingUnlock, result.Reason);
        }

        [Test]
        public void SetReady_ForestAfterIntro_MarksReady()
        {
            _introCompleted = true;
            PartyRoom room = CreateForestRoom();

            Send(_memberA, Ready(1));
            Complete(1);

            Assert.AreEqual(EPartyReadyState.Ready, room.ReadyAt(0));
        }

        [Test]
        public void SetReady_TooManyPotions_StaysNotReadyWithInsufficientItem()
        {
            PartyRoom room = CreateBossRoom();

            Send(_memberA, Ready(50));
            Complete(1);

            Assert.AreEqual(EPartyReadyState.NotReady, room.ReadyAt(0));
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out PartyResultMessage result));
            Assert.AreEqual(ERejectReason.InsufficientItem, result.Reason);
        }

        [Test]
        public void OnCharacterChanged_ReadyMember_ClearsReady()
        {
            PartyRoom room = CreateBossRoom();
            Send(_memberA, Ready(1));
            Complete(1);

            _director.OnCharacterChanged(PARTICIPANT_A);

            Assert.AreEqual(EPartyReadyState.NotReady, room.ReadyAt(0));
        }

        [Test]
        public void StartSortie_AfterCountdown_SendsBossReservation()
        {
            CreateBossRoom();
            Send(_memberA, Ready(1));
            Complete(1);
            DrainRequests();

            Send(_memberA, Action(EPartyAction.StartSortie));
            Assert.AreEqual(EPartyPhase.Countdown, _director.RoomOf(PARTICIPANT_A).Phase);
            _now = AFTER_COUNTDOWN;
            _director.Tick();
            FakeBackendTransport.WaitForCompleted(_gateway, 1);

            BackendRequest reservation = FindRequest(GameServerRoutes.RESERVATIONS);
            Assert.IsNotNull(reservation);
            var body = BackendJson.Deserialize<ReservationRequest>(reservation.BodyJson);
            Assert.AreEqual(GameSessionCodes.Mode.BOSS, body.Mode);
            Assert.AreEqual(BOSS_WOLBAEK, body.BossId);
            Assert.AreEqual(GameSessionCodes.Difficulty.EASY, body.Difficulty);
            Assert.AreEqual(REVISION, body.Participants[0].CharacterRevision);
            Assert.AreEqual(reservation.OperationId, body.OperationId);
        }

        [Test]
        public void Reservation_Committed_SendsOwnTicketOnlyAndClosesRoom()
        {
            CreateBossRoom();
            Send(_memberA, Action(EPartyAction.ListRooms));
            Send(_memberB, Join(_director.RoomOf(PARTICIPANT_A).RoomId));
            Send(_memberA, Ready(1));
            Send(_memberB, Ready(0));
            Complete(2);
            Send(_memberA, Action(EPartyAction.StartSortie));
            _now = AFTER_COUNTDOWN;
            _director.Tick();
            Complete(1);
            Complete(1);

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out SortieTicketMessage ticketA));
            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_B, out SortieTicketMessage ticketB));
            Assert.AreEqual("token-" + CHARACTER_A, ticketA.JoinToken);
            Assert.AreEqual("token-" + CHARACTER_B, ticketB.JoinToken);
            Assert.IsNull(_director.RoomOf(PARTICIPANT_A));
            Assert.AreEqual(0, _director.RoomCount);
        }

        [Test]
        public void Reservation_NoCapacity_QueuesUntilRoomChanges()
        {
            _reservationReason = ContractCodes.RejectReason.NO_CAPACITY;
            CreateBossRoom();
            Send(_memberA, Ready(1));
            Complete(1);
            Send(_memberA, Action(EPartyAction.StartSortie));
            _now = AFTER_COUNTDOWN;
            _director.Tick();
            Complete(1);

            PartyRoom room = _director.RoomOf(PARTICIPANT_A);
            Assert.AreEqual(EPartyPhase.Queued, room.Phase);
            Assert.IsTrue(_director.Queue.Contains(room.RoomId));

            // 대기 중 성장 변경으로 준비가 풀리면 줄에서 빠지고 모집으로 돌아간다
            _director.OnCharacterChanged(PARTICIPANT_A);

            Assert.AreEqual(EPartyPhase.Open, room.Phase);
            Assert.IsFalse(_director.Queue.Contains(room.RoomId));
        }

        [Test]
        public void Queue_RetriesHeadWithNewOperationIdAfterInterval()
        {
            _reservationReason = ContractCodes.RejectReason.NO_CAPACITY;
            CreateBossRoom();
            Send(_memberA, Ready(1));
            Complete(1);
            Send(_memberA, Action(EPartyAction.StartSortie));
            _now = AFTER_COUNTDOWN;
            _director.Tick();
            Complete(1);
            string firstOperation = FindRequest(GameServerRoutes.RESERVATIONS).OperationId;

            _now = AFTER_COUNTDOWN + Constants.Town.SORTIE_QUEUE_RETRY_SECONDS;
            _director.Tick();
            Complete(1);

            BackendRequest retry = FindRequest(GameServerRoutes.RESERVATIONS);
            Assert.IsNotNull(retry);
            Assert.AreNotEqual(firstOperation, retry.OperationId);
            Assert.AreEqual(EPartyPhase.Queued, _director.RoomOf(PARTICIPANT_A).Phase);
        }

        [Test]
        public void AcceptInvite_AfterThirtySeconds_ReturnsInviteExpired()
        {
            CreateBossRoom();
            Send(_memberA, Invite(NAME_B));
            Assert.AreEqual(1, _audience.Count<PartyInviteMessage>(PARTICIPANT_B));
            int roomId = _director.RoomOf(PARTICIPANT_A).RoomId;

            _now = INVITE_EXPIRED;
            Send(_memberB, AcceptInvite(roomId));

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_B, out PartyResultMessage result));
            Assert.AreEqual(ERejectReason.InviteExpired, result.Reason);
            Assert.IsNull(_director.RoomOf(PARTICIPANT_B));
        }

        [Test]
        public void AcceptInvite_InTime_JoinsWithoutPassword()
        {
            Send(_memberA, new PartyRequestMessage(EPartyAction.CreateRoom, 0, EPartyMode.Boss, BATTLE_WOLBAEK, "secret room", "pw",
                false, 0, 0));
            Send(_memberA, Invite(NAME_B));
            int roomId = _director.RoomOf(PARTICIPANT_A).RoomId;

            Send(_memberB, AcceptInvite(roomId));

            Assert.AreEqual(roomId, _director.RoomOf(PARTICIPANT_B).RoomId);
        }

        [Test]
        public void JoinRoom_WrongPassword_ReturnsMismatch()
        {
            Send(_memberA, new PartyRequestMessage(EPartyAction.CreateRoom, 0, EPartyMode.Boss, BATTLE_WOLBAEK, "secret room", "pw",
                false, 0, 0));
            int roomId = _director.RoomOf(PARTICIPANT_A).RoomId;

            Send(_memberB, new PartyRequestMessage(EPartyAction.JoinRoom, roomId, EPartyMode.Normal, null, null, "nope", false, 0, 0));

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_B, out PartyResultMessage result));
            Assert.AreEqual(ERejectReason.RoomPasswordMismatch, result.Reason);
        }

        [Test]
        public void LeaderDisconnect_PassesLeadToRemainingMember()
        {
            CreateBossRoom();
            Send(_memberB, Join(_director.RoomOf(PARTICIPANT_A).RoomId));

            _director.OnMemberDisconnected(PARTICIPANT_A);

            PartyRoom room = _director.RoomOf(PARTICIPANT_B);
            Assert.AreEqual(PARTICIPANT_B, room.LeaderId);
            Assert.AreEqual(1, room.Count);
        }

        [Test]
        public void Invite_WithRequestId_EchoesIdInAcceptedResult()
        {
            CreateBossRoom();

            Send(_memberA, new PartyRequestMessage(EPartyAction.Invite, 0, EPartyMode.Normal, null, NAME_B, null, false, 0, 0, 7u));

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out PartyResultMessage result));
            Assert.AreEqual(EPartyAction.Invite, result.Action);
            Assert.IsTrue(result.IsAccepted);
            Assert.AreEqual(7u, result.RequestId);
        }

        [Test]
        public void Invite_WithRequestId_EchoesIdInRejectedResult()
        {
            CreateBossRoom();

            Send(_memberA, new PartyRequestMessage(EPartyAction.Invite, 0, EPartyMode.Normal, null, "없는캐릭터", null, false, 0, 0, 9u));

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out PartyResultMessage result));
            Assert.AreEqual(ERejectReason.InvalidTarget, result.Reason);
            Assert.AreEqual(9u, result.RequestId);
        }

        [Test]
        public void Invite_TwoRequestsInOrder_EachResultKeepsItsOwnId()
        {
            CreateBossRoom();

            Send(_memberA, new PartyRequestMessage(EPartyAction.Invite, 0, EPartyMode.Normal, null, NAME_B, null, false, 0, 0, 1u));
            Send(_memberA, new PartyRequestMessage(EPartyAction.Invite, 0, EPartyMode.Normal, null, "없는캐릭터", null, false, 0, 0, 2u));

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out PartyResultMessage last));
            Assert.AreEqual(2u, last.RequestId);
            Assert.AreEqual(ERejectReason.InvalidTarget, last.Reason);
        }

        [Test]
        public void Invite_WithoutRequestId_ResultIdIsZero()
        {
            CreateBossRoom();

            Send(_memberA, Invite(NAME_B));

            Assert.IsTrue(_audience.TryGetLast(PARTICIPANT_A, out PartyResultMessage result));
            Assert.AreEqual(0u, result.RequestId);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private PartyRoom CreateBossRoom()
        {
            Send(_memberA, new PartyRequestMessage(EPartyAction.CreateRoom, 0, EPartyMode.Boss, BATTLE_WOLBAEK, "room", null, false, 0,
                0));
            PartyRoom room = _director.RoomOf(PARTICIPANT_A);
            Assert.IsNotNull(room);
            return room;
        }

        private PartyRoom CreateForestRoom()
        {
            Send(_memberA, new PartyRequestMessage(EPartyAction.CreateRoom, 0, EPartyMode.Normal, REGION_FOREST, "room", null, false, 0,
                0));
            PartyRoom room = _director.RoomOf(PARTICIPANT_A);
            Assert.IsNotNull(room);
            return room;
        }

        private void Send(TownMember member, PartyRequestMessage message)
        {
            _director.Handle(member, in message);
        }

        private void Complete(int count)
        {
            FakeBackendTransport.WaitForCompleted(_gateway, count);
            _gateway.Pump();
        }

        private void DrainRequests()
        {
            while (_transport.Requests.TryDequeue(out _))
            {
            }
        }

        // 가장 최근에 보낸 경로의 요청, 앞 요청은 버린다
        private BackendRequest FindRequest(string path)
        {
            BackendRequest found = null;
            while (_transport.Requests.TryDequeue(out BackendRequest request))
            {
                if (request.Path == path)
                {
                    found = request;
                }
            }

            return found;
        }

        private BackendTransportResult Respond(BackendRequest request)
        {
            if (request.Path == GameServerRoutes.READINESS)
            {
                var body = BackendJson.Deserialize<ReadinessRequest>(request.BodyJson);
                ReadinessParticipant participant = body.Participants[0];
                var progress = new ReadinessProgress(_introCompleted, 0, Array.Empty<string>(), Array.Empty<string>(),
                    Array.Empty<string>(), Array.Empty<string>());
                var status = new ReadinessParticipantStatus(participant.AccountId, participant.CharacterId,
                    ContractCodes.RejectReason.NONE, REVISION, 1,
                    new[] { new ReservationPotion(SMALL_POTION, BINDING_ACCOUNT, 9) }, progress);
                return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new ReadinessResponse(new[] { status }, 0)));
            }

            if (request.Path == GameServerRoutes.RESERVATIONS)
            {
                var body   = BackendJson.Deserialize<ReservationRequest>(request.BodyJson);
                int reason = _reservationReason;
                if (reason != ContractCodes.RejectReason.NONE)
                {
                    var rejected = new OperationResult(body.OperationId, ContractCodes.OperationStatus.REJECTED, reason, 0, 0, string.Empty);
                    return new BackendTransportResult(STATUS_CONFLICT, BackendJson.Serialize(new ReservationResponse(rejected,
                        string.Empty, string.Empty, string.Empty, 0, 0, Array.Empty<ReservedParticipant>())));
                }

                var committed = new OperationResult(body.OperationId, ContractCodes.OperationStatus.COMMITTED, 0, 1, 0, string.Empty);
                return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new ReservationResponse(committed, SESSION_ID,
                    "instance", "127.0.0.1", 7777, 0, Array.Empty<ReservedParticipant>())));
            }

            if (request.Path.EndsWith("/join-tickets", StringComparison.Ordinal))
            {
                var tickets = new[]
                {
                    new JoinTicket("token-" + CHARACTER_A, 0, ContractCodes.ServerRole.DUNGEON, "instance", "127.0.0.1", 7777,
                        CHARACTER_A, "p-a", 0),
                    new JoinTicket("token-" + CHARACTER_B, 0, ContractCodes.ServerRole.DUNGEON, "instance", "127.0.0.1", 7777,
                        CHARACTER_B, "p-b", 0)
                };
                return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new JoinTicketsResponse(SESSION_ID, tickets, 0, 0)));
            }

            return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new OperationResult("op",
                ContractCodes.OperationStatus.COMMITTED, 0, 0, 0, string.Empty)));
        }

        private static PartyRequestMessage Ready(int smallPotions)
        {
            return new PartyRequestMessage(EPartyAction.SetReady, 0, EPartyMode.Normal, null, null, null, true, smallPotions, 0);
        }

        private static PartyRequestMessage Action(EPartyAction action)
        {
            return new PartyRequestMessage(action, 0, EPartyMode.Normal, null, null, null, false, 0, 0);
        }

        private static PartyRequestMessage Join(int roomId)
        {
            return new PartyRequestMessage(EPartyAction.JoinRoom, roomId, EPartyMode.Normal, null, null, null, false, 0, 0);
        }

        private static PartyRequestMessage Invite(string name)
        {
            return new PartyRequestMessage(EPartyAction.Invite, 0, EPartyMode.Normal, null, name, null, false, 0, 0);
        }

        private static PartyRequestMessage AcceptInvite(int roomId)
        {
            return new PartyRequestMessage(EPartyAction.AcceptInvite, roomId, EPartyMode.Normal, null, null, null, false, 0, 0);
        }

        private double ReadClock() { return _now; }

        private static double ZeroClock() { return 0.0; }

        private sealed class FakeDirectory : ITownMemberDirectory
        {
            private readonly Dictionary<string, TownMember> _members = new Dictionary<string, TownMember>();

            public void Add(TownMember member)
            {
                _members[member.CharacterName] = member;
            }

            public bool TryFindByName(string characterName, out TownMember member)
            {
                return _members.TryGetValue(characterName, out member);
            }
        }
    }
}
