// System
using System.Collections.Generic;

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
    public class TownPartyRulesTests
    {
        // 마을 파티의 순수 판정: 전장 해금(체험판 플래그, 계정 진행), 준비 확인 응답 판정, 출정 시작 조건, 방장 승계, 같은 계정 이중 입장,
        // 대기열 제거 조건, 초대 만료, 예약 요청 구성(보스 필드)

        /****************************************
        *                Fields
        ****************************************/

        private const string REGION_FOREST    = "region_forest";
        private const string REGION_RUINS     = "region_ruins";
        private const string BATTLE_WOLBAEK   = "battle_wolbaek_easy";
        private const string BATTLE_CHEOLGAK  = "battle_mucheol_easy";
        private const string BOSS_WOLBAEK     = "boss_wolbaek";
        private const string SMALL_POTION     = "item_potion_small";
        private const string LARGE_POTION     = "item_potion_large";
        private const string BINDING_ACCOUNT  = "Account";
        private const string BINDING_TRIAL    = "Trial";
        private const string ACCOUNT_A        = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string ACCOUNT_B        = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string CHARACTER_A      = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string CHARACTER_B      = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string CHARACTER_C      = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc03";
        private const string LEASE_ID         = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string OPERATION_ID     = "3a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string CONTENT_VERSION  = "test-content";
        private const int    PARTICIPANT_A    = 1;
        private const int    PARTICIPANT_B    = 2;
        private const int    PARTICIPANT_C    = 3;
        private const int    ROOM_ID          = 7;
        private const int    OTHER_ROOM_ID    = 8;
        private const long   REVISION         = 4;
        private const double INVITE_LIFETIME  = 30.0;
        private const int    TABLE_CAPACITY   = 4;

        private readonly List<Object> _created = new List<Object>();

        private ProgressionCatalogSO _progression;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _progression = ScriptableObject.CreateInstance<ProgressionCatalogSO>();
            _created.Add(_progression);
        }

        [TearDown]
        public void TearDown()
        {
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
        public void CheckContent_TrialForest_OpensRoomAndDefersProgressToReady()
        {
            ERejectReason reason = ReadyRules.CheckContent(_progression, EPartyMode.Normal, REGION_FOREST,
                out PartyContent content);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(GameSessionCodes.Difficulty.NONE, (int)content.Difficulty);
            Assert.IsNull(content.BossId);
        }

        [Test]
        public void CheckContent_RegionNotInTrial_ReturnsMissingUnlock()
        {
            Assert.AreEqual(ERejectReason.MissingUnlock,
                ReadyRules.CheckContent(_progression, EPartyMode.Normal, REGION_RUINS, out _));
            Assert.AreEqual(ERejectReason.MissingUnlock,
                ReadyRules.CheckContent(_progression, EPartyMode.Boss, BATTLE_CHEOLGAK, out _));
        }

        [Test]
        public void CheckProgress_ForestWithoutIntro_ReturnsMissingUnlock()
        {
            ERejectReason reason = ReadyRules.CheckProgress(_progression, EPartyMode.Normal, REGION_FOREST,
                Progress(false), false);

            Assert.AreEqual(ERejectReason.MissingUnlock, reason);
        }

        [Test]
        public void CheckProgress_ForestAfterIntro_ReturnsNone()
        {
            ERejectReason reason = ReadyRules.CheckProgress(_progression, EPartyMode.Normal, REGION_FOREST,
                Progress(true), false);

            Assert.AreEqual(ERejectReason.None, reason);
        }

        [Test]
        public void CheckProgress_TrialPreUnlockedBoss_ReturnsNoneWithoutAnyProgress()
        {
            Assert.AreEqual(ERejectReason.None,
                ReadyRules.CheckProgress(_progression, EPartyMode.Boss, BATTLE_WOLBAEK, Progress(false), false));
            Assert.AreEqual(ERejectReason.None,
                ReadyRules.CheckProgress(_progression, EPartyMode.Boss, BATTLE_WOLBAEK, null, false));
        }

        [Test]
        public void CheckProgress_MissingProgressField_AllowsOnlyInDevelopment()
        {
            Assert.AreEqual(ERejectReason.MissingUnlock,
                ReadyRules.CheckProgress(_progression, EPartyMode.Normal, REGION_FOREST, null, false));
            Assert.AreEqual(ERejectReason.None,
                ReadyRules.CheckProgress(_progression, EPartyMode.Normal, REGION_FOREST, null, true));
        }

        [Test]
        public void CheckProgress_RegionOutsideTrial_StaysLockedEvenWithClearRecord()
        {
            var progress = new ReadinessProgress(true, 0, new[] { REGION_FOREST }, new string[0], new string[0], new string[0]);

            Assert.AreEqual(ERejectReason.MissingUnlock,
                ReadyRules.CheckProgress(_progression, EPartyMode.Normal, REGION_RUINS, progress, false));
        }

        [Test]
        public void CheckProgress_ExplicitUnlockRecord_OpensWithoutRequirement()
        {
            var progress = new ReadinessProgress(false, 0, new string[0], new[] { REGION_FOREST }, new string[0], new string[0]);

            Assert.AreEqual(ERejectReason.None,
                ReadyRules.CheckProgress(_progression, EPartyMode.Normal, REGION_FOREST, progress, false));
        }

        [Test]
        public void CheckContent_TrialPreUnlockedBoss_ReturnsBossContract()
        {
            ERejectReason reason = ReadyRules.CheckContent(_progression, EPartyMode.Boss, BATTLE_WOLBAEK,
                out PartyContent content);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(BOSS_WOLBAEK, content.BossId);
            Assert.AreEqual(GameSessionCodes.Difficulty.EASY, (int)content.Difficulty);
            Assert.Greater(content.TicketCost, 0);
        }

        [Test]
        public void CheckContent_UnknownOrWrongMode_ReturnsInvalidTarget()
        {
            Assert.AreEqual(ERejectReason.InvalidTarget, ReadyRules.CheckContent(_progression, EPartyMode.Normal, "nope", out _));
            Assert.AreEqual(ERejectReason.InvalidTarget,
                ReadyRules.CheckContent(_progression, EPartyMode.Normal, BATTLE_WOLBAEK, out _));
            Assert.AreEqual(ERejectReason.InvalidTarget,
                ReadyRules.CheckContent(_progression, EPartyMode.Boss, REGION_FOREST, out _));
        }

        [Test]
        public void CheckStatus_NotEnoughTickets_ReturnsInsufficientTicket()
        {
            ReadinessParticipantStatus status = Status(0, Potions(SMALL_POTION, BINDING_ACCOUNT, 9));

            ERejectReason reason = ReadyRules.CheckStatus(status, 1, SMALL_POTION, 1, LARGE_POTION, 0, out _);

            Assert.AreEqual(ERejectReason.InsufficientTicket, reason);
        }

        [Test]
        public void CheckStatus_NotEnoughPotions_ReturnsInsufficientItem()
        {
            ReadinessParticipantStatus status = Status(1, Potions(SMALL_POTION, BINDING_ACCOUNT, 2));

            ERejectReason reason = ReadyRules.CheckStatus(status, 1, SMALL_POTION, 3, LARGE_POTION, 0, out _);

            Assert.AreEqual(ERejectReason.InsufficientItem, reason);
        }

        [Test]
        public void CheckStatus_ApiReason_IsReturned()
        {
            var status = new ReadinessParticipantStatus(ACCOUNT_A, CHARACTER_A, ContractCodes.RejectReason.RESERVATION_CONFLICT, 0, 0,
                null);

            Assert.AreEqual(ERejectReason.ReservationConflict,
                ReadyRules.CheckStatus(status, 0, SMALL_POTION, 0, LARGE_POTION, 0, out _));
        }

        [Test]
        public void CheckStatus_PotionsAcrossBindings_UsesSingleBindingThatCoversQuantity()
        {
            var available = new[]
            {
                new ReservationPotion(SMALL_POTION, BINDING_TRIAL, 2),
                new ReservationPotion(SMALL_POTION, BINDING_ACCOUNT, 5),
                new ReservationPotion(LARGE_POTION, BINDING_ACCOUNT, 1)
            };

            ERejectReason reason = ReadyRules.CheckStatus(Status(1, available), 1, SMALL_POTION, 4, LARGE_POTION, 1,
                out ReservationPotion[] picked);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(2, picked.Length);
            Assert.AreEqual(BINDING_ACCOUNT, picked[0].BindingKind);
            Assert.AreEqual(4, picked[0].Quantity);
            Assert.AreEqual(LARGE_POTION, picked[1].DefinitionId);
        }

        [Test]
        public void CheckStatus_QuantityOnlyBySplittingBindings_RejectsInsufficientItem()
        {
            var available = new[]
            {
                new ReservationPotion(SMALL_POTION, BINDING_TRIAL, 3),
                new ReservationPotion(SMALL_POTION, BINDING_ACCOUNT, 3)
            };

            ERejectReason reason = ReadyRules.CheckStatus(Status(1, available), 1, SMALL_POTION, 5, LARGE_POTION, 0,
                out ReservationPotion[] picked);

            Assert.AreEqual(ERejectReason.InsufficientItem, reason);
            Assert.IsNull(picked);
        }

        [Test]
        public void CanStart_ChecksLeaderAndAllReady()
        {
            PartyRoom room = NewRoom(EPartyMode.Normal);
            room.TryAdd(Member(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A));
            room.TryAdd(Member(PARTICIPANT_B, ACCOUNT_B, CHARACTER_B));
            room.MarkReady(PARTICIPANT_A, REVISION, null);

            Assert.AreEqual(ERejectReason.NotLeader, ReadyRules.CanStart(room, PARTICIPANT_B));
            Assert.AreEqual(ERejectReason.NotAllReady, ReadyRules.CanStart(room, PARTICIPANT_A));

            room.MarkReady(PARTICIPANT_B, REVISION, null);
            Assert.AreEqual(ERejectReason.None, ReadyRules.CanStart(room, PARTICIPANT_A));
        }

        [Test]
        public void Remove_Leader_PassesToEarliestConnectedMember()
        {
            PartyRoom room = NewRoom(EPartyMode.Normal);
            room.TryAdd(Member(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A));
            room.TryAdd(Member(PARTICIPANT_B, ACCOUNT_B, CHARACTER_B));
            room.TryAdd(Member(PARTICIPANT_C, "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc03", CHARACTER_C));
            room.SetConnected(PARTICIPANT_B, false);

            Assert.IsTrue(room.Remove(PARTICIPANT_A));

            Assert.AreEqual(PARTICIPANT_C, room.LeaderId);
            Assert.AreEqual(PARTICIPANT_B, room.MemberAt(0).ParticipantId);
        }

        [Test]
        public void Remove_LastMember_LeavesNoLeader()
        {
            PartyRoom room = NewRoom(EPartyMode.Normal);
            room.TryAdd(Member(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A));

            room.Remove(PARTICIPANT_A);

            Assert.IsTrue(room.IsEmpty);
            Assert.AreEqual(0, room.LeaderId);
        }

        [Test]
        public void TryAdd_SameAccountTwice_RejectsSecondCharacter()
        {
            PartyRoom room = NewRoom(EPartyMode.Normal);
            Assert.AreEqual(ERejectReason.None, room.TryAdd(Member(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A)));

            ERejectReason reason = room.TryAdd(Member(PARTICIPANT_B, ACCOUNT_A, CHARACTER_B));

            Assert.AreEqual(ERejectReason.AlreadyConnected, reason);
            Assert.AreEqual(1, room.Count);
        }

        [Test]
        public void TryAdd_FullOrNotOpen_Rejects()
        {
            PartyRoom room = NewRoom(EPartyMode.Normal);
            for (int i = 0; i < Constants.Session.MAX_PARTY_SIZE; ++i)
            {
                room.TryAdd(Member(i + 1, "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc1" + i, "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc1" + i));
            }

            Assert.AreEqual(ERejectReason.NoCapacity, room.TryAdd(Member(99, "acc-x", "char-x")));

            PartyRoom other = NewRoom(EPartyMode.Normal);
            other.Phase = EPartyPhase.Countdown;
            Assert.AreEqual(ERejectReason.WrongPhase, other.TryAdd(Member(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A)));
        }

        [Test]
        public void ClearReady_ChangesRevision()
        {
            PartyRoom room = NewRoom(EPartyMode.Normal);
            room.TryAdd(Member(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A));
            room.MarkReady(PARTICIPANT_A, REVISION, null);
            long before = room.Revision;

            Assert.IsTrue(room.ClearReady(PARTICIPANT_A));

            Assert.Greater(room.Revision, before);
            Assert.AreEqual(EPartyReadyState.NotReady, room.ReadyAt(0));
            Assert.IsFalse(room.ClearReady(PARTICIPANT_A));
        }

        [Test]
        public void PruneStale_RemovesChangedOrMissingRooms_KeepsOrder()
        {
            var queue = new SortieQueue(TABLE_CAPACITY);
            var rooms = new FakeRoomLookup();
            queue.Enqueue(ROOM_ID, REVISION);
            queue.Enqueue(OTHER_ROOM_ID, REVISION);
            queue.Enqueue(9, REVISION);
            rooms.Set(ROOM_ID, REVISION + 1);
            rooms.Set(9, REVISION);

            int removed = queue.PruneStale(rooms);

            Assert.AreEqual(2, removed);
            Assert.IsTrue(queue.TryPeek(out int head, out long revision));
            Assert.AreEqual(9, head);
            Assert.AreEqual(REVISION, revision);
        }

        [Test]
        public void Enqueue_SameRoom_KeepsPosition()
        {
            var queue = new SortieQueue(TABLE_CAPACITY);
            queue.Enqueue(ROOM_ID, REVISION);
            queue.Enqueue(OTHER_ROOM_ID, REVISION);

            queue.Enqueue(ROOM_ID, REVISION + 1);

            Assert.AreEqual(0, queue.PositionOf(ROOM_ID));
            Assert.AreEqual(2, queue.Count);
        }

        [Test]
        public void Add_InviterAtLimit_RejectsNewButRefreshesExistingAndAllowsOthers()
        {
            const int PER_INVITER   = 2;
            const int OTHER_INVITEE = PARTICIPANT_B + 1;
            var invites = new PartyInviteTable(TABLE_CAPACITY, INVITE_LIFETIME, PER_INVITER);

            Assert.IsTrue(invites.Add(PARTICIPANT_B, ROOM_ID, PARTICIPANT_A, 0.0));
            Assert.IsTrue(invites.Add(PARTICIPANT_B, OTHER_ROOM_ID, PARTICIPANT_A, 0.0));
            Assert.IsFalse(invites.Add(OTHER_INVITEE, ROOM_ID, PARTICIPANT_A, 0.0));
            Assert.IsTrue(invites.Add(PARTICIPANT_A, ROOM_ID, OTHER_INVITEE, 0.0));
            Assert.IsTrue(invites.Add(PARTICIPANT_B, ROOM_ID, PARTICIPANT_A, 1.0));
            Assert.AreEqual(3, invites.Count);
        }

        [Test]
        public void TryConsume_AfterLifetime_ReturnsFalseAndRemoves()
        {
            var invites = new PartyInviteTable(TABLE_CAPACITY, INVITE_LIFETIME, TABLE_CAPACITY);
            invites.Add(PARTICIPANT_B, ROOM_ID, PARTICIPANT_A, 0.0);

            Assert.IsFalse(invites.TryConsume(PARTICIPANT_B, ROOM_ID, INVITE_LIFETIME));
            Assert.AreEqual(0, invites.Count);
        }

        [Test]
        public void TryConsume_BeforeLifetime_ReturnsTrueOnce()
        {
            var invites = new PartyInviteTable(TABLE_CAPACITY, INVITE_LIFETIME, TABLE_CAPACITY);
            invites.Add(PARTICIPANT_B, ROOM_ID, PARTICIPANT_A, 0.0);

            Assert.IsTrue(invites.TryConsume(PARTICIPANT_B, ROOM_ID, INVITE_LIFETIME - 1.0));
            Assert.IsFalse(invites.TryConsume(PARTICIPANT_B, ROOM_ID, INVITE_LIFETIME - 1.0));
        }

        [Test]
        public void Build_BossRoom_FillsBossContractAndParticipants()
        {
            PartyRoom room = new PartyRoom(ROOM_ID, "boss", EPartyMode.Boss, BATTLE_WOLBAEK, (sbyte)GameSessionCodes.Difficulty.EASY,
                BOSS_WOLBAEK, 1, null);
            room.TryAdd(new TownMember(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A, LEASE_ID, "a", EPlayerClass.Warrior));
            room.MarkReady(PARTICIPANT_A, REVISION, Potions(SMALL_POTION, BINDING_ACCOUNT, 3));

            ReservationRequest request = SortieReservationRules.Build(OPERATION_ID, room, CONTENT_VERSION);

            Assert.AreEqual(GameSessionCodes.Mode.BOSS, request.Mode);
            Assert.AreEqual(BOSS_WOLBAEK, request.BossId);
            Assert.AreEqual(GameSessionCodes.Difficulty.EASY, request.Difficulty);
            Assert.AreEqual(BATTLE_WOLBAEK, request.BattleId);
            Assert.AreEqual(CONTENT_VERSION, request.ContentVersion);
            Assert.AreEqual(OPERATION_ID, request.OperationId);
            Assert.AreEqual(1, request.Participants.Length);
            Assert.AreEqual(ACCOUNT_A, request.Participants[0].AccountId);
            Assert.AreEqual(CHARACTER_A, request.Participants[0].CharacterId);
            Assert.AreEqual(REVISION, request.Participants[0].CharacterRevision);
            Assert.AreEqual(3, request.Participants[0].Potions[0].Quantity);
        }

        [Test]
        public void Build_NormalRoom_LeavesBossFieldsEmpty()
        {
            PartyRoom room = NewRoom(EPartyMode.Normal);
            room.TryAdd(Member(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A));
            room.MarkReady(PARTICIPANT_A, REVISION, null);

            ReservationRequest request = SortieReservationRules.Build(OPERATION_ID, room, CONTENT_VERSION);

            Assert.AreEqual(GameSessionCodes.Mode.NORMAL, request.Mode);
            Assert.IsNull(request.BossId);
            Assert.AreEqual(GameSessionCodes.Difficulty.NONE, request.Difficulty);
            Assert.AreEqual(REGION_FOREST, request.MapId);
            Assert.AreEqual(0, request.Participants[0].Potions.Length);
        }

        [Test]
        public void Build_NotAllReady_ReturnsNull()
        {
            PartyRoom room = NewRoom(EPartyMode.Normal);
            room.TryAdd(Member(PARTICIPANT_A, ACCOUNT_A, CHARACTER_A));

            Assert.IsNull(SortieReservationRules.Build(OPERATION_ID, room, CONTENT_VERSION));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static PartyRoom NewRoom(EPartyMode mode)
        {
            return new PartyRoom(ROOM_ID, "room", mode, REGION_FOREST, (sbyte)GameSessionCodes.Difficulty.NONE, null, 0, null);
        }

        private static TownMember Member(int participantId, string accountId, string characterId)
        {
            return new TownMember(participantId, accountId, characterId, LEASE_ID, "name" + participantId, EPlayerClass.Warrior);
        }

        private static ReadinessProgress Progress(bool introCompleted)
        {
            return new ReadinessProgress(introCompleted, 0, new string[0], new string[0], new string[0], new string[0]);
        }

        private static ReadinessParticipantStatus Status(long tickets, ReservationPotion[] potions)
        {
            return new ReadinessParticipantStatus(ACCOUNT_A, CHARACTER_A, ContractCodes.RejectReason.NONE, REVISION, tickets, potions);
        }

        private static ReservationPotion[] Potions(string definitionId, string bindingKind, long quantity)
        {
            return new[] { new ReservationPotion(definitionId, bindingKind, quantity) };
        }

        private sealed class FakeRoomLookup : ISortieRoomLookup
        {
            private readonly Dictionary<int, long> _revisions = new Dictionary<int, long>();

            public void Set(int roomId, long revision)
            {
                _revisions[roomId] = revision;
            }

            public bool IsQueuedAt(int roomId, long revision)
            {
                return (_revisions.TryGetValue(roomId, out long current)) && (current == revision);
            }
        }
    }
}
