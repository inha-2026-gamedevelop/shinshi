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

namespace Shinshi.Tests
{
    public class TownMessageSerializerTests
    {
        // 마을 메시지의 쓰기, 읽기 순서가 맞는지와 클라이언트가 보낸 배열 길이 상한, 마을 입장 결합의 계정 ID 전달을 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT_ID   = "0A1B2C3D-4E5F-4A6B-8C7D-8899AABBCC01";
        private const string CHARACTER_ID = "1a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string LEASE_ID     = "2a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string INSTANCE_ID  = "7d1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const int    REGISTRY     = 4;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void PartyRoomState_RoundTrip_KeepsMembersAndPhase()
        {
            var members = new[]
            {
                new PartyMemberView(1, "alpha", EPlayerClass.Sorcerer, EPartyReadyState.Ready, 3, 1),
                new PartyMemberView(2, null, EPlayerClass.Summoner, EPartyReadyState.Checking, 0, 0)
            };
            var source = new PartyRoomStateMessage(true, 9, 42, "room", EPartyMode.Boss, "battle_wolbaek_easy", 0, true, 2,
                EPartyPhase.Countdown, 1.5f, members);

            var writer = new NetworkWriter();
            writer.WritePartyRoomStateMessage(source);
            PartyRoomStateMessage read = new NetworkReader(writer.ToArraySegment()).ReadPartyRoomStateMessage();

            Assert.IsTrue(read.InRoom);
            Assert.AreEqual(42, read.Revision);
            Assert.AreEqual(EPartyPhase.Countdown, read.Phase);
            Assert.AreEqual(2, read.LeaderParticipantId);
            Assert.AreEqual(2, read.Members.Length);
            Assert.AreEqual(EPlayerClass.Summoner, read.Members[1].PlayerClass);
            Assert.AreEqual(EPartyReadyState.Ready, read.Members[0].Ready);
            Assert.AreEqual(3, read.Members[0].SmallPotions);
        }

        [Test]
        public void PartyRoomState_RoundTrip_KeepsNewClassMembers()
        {
            var members = new[]
            {
                new PartyMemberView(1, "a", EPlayerClass.Swordsman, EPartyReadyState.Ready, 0, 0),
                new PartyMemberView(2, "b", EPlayerClass.Mageknight, EPartyReadyState.Ready, 0, 0),
                new PartyMemberView(3, "c", EPlayerClass.Archer, EPartyReadyState.Ready, 0, 0)
            };
            var source = new PartyRoomStateMessage(true, 9, 42, "room", EPartyMode.Boss, "battle_wolbaek_easy", 0, false, 1,
                EPartyPhase.Open, 0f, members);

            var writer = new NetworkWriter();
            writer.WritePartyRoomStateMessage(source);
            PartyRoomStateMessage read = new NetworkReader(writer.ToArraySegment()).ReadPartyRoomStateMessage();

            Assert.AreEqual(EPlayerClass.Swordsman, read.Members[0].PlayerClass);
            Assert.AreEqual(EPlayerClass.Mageknight, read.Members[1].PlayerClass);
            Assert.AreEqual(EPlayerClass.Archer, read.Members[2].PlayerClass);
        }

        [Test]
        public void PartyRoomList_RoundTrip_KeepsPerClassCounts()
        {
            var summary = new PartyRoomSummary(7, "room", EPartyMode.Boss, "battle_wolbaek_easy", 0, EPartyPhase.Open,
                4, 2, false, 1, 2, 3, 4, 5, 6);

            var writer = new NetworkWriter();
            writer.WritePartyRoomListMessage(new PartyRoomListMessage(new[] { summary }));
            PartyRoomListMessage read = new NetworkReader(writer.ToArraySegment()).ReadPartyRoomListMessage();

            Assert.AreEqual(1, read.Rooms.Length);
            PartyRoomSummary room = read.Rooms[0];
            Assert.AreEqual(7, room.RoomId);
            Assert.AreEqual(1, room.Warriors);
            Assert.AreEqual(2, room.Sorcerers);
            Assert.AreEqual(3, room.Summoners);
            Assert.AreEqual(4, room.Swordsmen);
            Assert.AreEqual(5, room.Mageknights);
            Assert.AreEqual(6, room.Archers);
        }

        [Test]
        public void GrowthRequest_RoundTrip_KeepsArraysAndOperationId()
        {
            Guid operationId = Guid.NewGuid();
            var source = new GrowthRequestMessage(operationId, EGrowthAction.TalismanLoadout, "t", "s", 3, 4, true,
                new[] { 1, 2, 3, 4, 5 }, new[] { new TownLoadoutEntry("skill", 1, "effect") });

            var writer = new NetworkWriter();
            writer.WriteGrowthRequestMessage(source);
            GrowthRequestMessage read = new NetworkReader(writer.ToArraySegment()).ReadGrowthRequestMessage();

            Assert.AreEqual(operationId, read.OperationId);
            Assert.AreEqual(EGrowthAction.TalismanLoadout, read.Action);
            Assert.AreEqual(5, read.Allocation[4]);
            Assert.AreEqual("effect", read.Loadout[0].EffectId);
            Assert.IsTrue(read.Flag);
        }

        [Test]
        public void GrowthRequest_LoadoutOverLimit_FailsToRead()
        {
            var tooMany = new TownLoadoutEntry[Constants.Town.MAX_GROWTH_LOADOUT_SLOTS + 1];
            var source  = new GrowthRequestMessage(Guid.NewGuid(), EGrowthAction.TalismanLoadout, null, null, 0, 0, false, null, tooMany);

            var writer = new NetworkWriter();
            writer.WriteGrowthRequestMessage(source);
            var reader = new NetworkReader(writer.ToArraySegment());

            Assert.Throws<InvalidDataException>(() => reader.ReadGrowthRequestMessage());
        }

        [Test]
        public void LifeSnapshot_RoundTrip_KeepsPotions()
        {
            var source = new LifeSnapshotMessage(80, 100, 123, "item_herb", 7, 9999,
                new[] { new TownItemEntry("item_potion_small", null, 9, 2) });

            var writer = new NetworkWriter();
            writer.WriteLifeSnapshotMessage(source);
            LifeSnapshotMessage read = new NetworkReader(writer.ToArraySegment()).ReadLifeSnapshotMessage();

            Assert.AreEqual(80, read.ActionValue);
            Assert.AreEqual("item_herb", read.HerbId);
            Assert.AreEqual(2, read.Potions[0].Reserved);
        }

        [Test]
        public void GatherNodeState_RoundTrip_KeepsState()
        {
            var source = new GatherNodeStateMessage(1, 11, EGatherNodeState.Completing, 5, 0f);

            var writer = new NetworkWriter();
            writer.WriteGatherNodeStateMessage(source);
            GatherNodeStateMessage read = new NetworkReader(writer.ToArraySegment()).ReadGatherNodeStateMessage();

            Assert.AreEqual(11, read.Node);
            Assert.AreEqual(EGatherNodeState.Completing, read.State);
            Assert.AreEqual(5, read.ClaimParticipantId);
        }

        [Test]
        public void TownJoin_AccountIdFromExchange_ReachesSessionBinding()
        {
            var response = new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID, ContractCodes.ServerRole.TOWN, null, null, 0,
                LEASE_ID, (int)EPlayerClass.Warrior, null, JoinTokenExchangeResponse.NO_MODE, null, null, null,
                GameSessionCodes.Difficulty.NONE, null, ACCOUNT_ID);
            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response,
                out JoinTokenClaims claims));

            var registry = new ServerSessionRegistry(REGISTRY);
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));

            Assert.AreEqual(ACCOUNT_ID.ToLowerInvariant(), binding.AccountId);
            Assert.AreEqual(CHARACTER_ID, binding.CharacterId);
            TownMember member = TownMember.FromBinding(binding);
            Assert.IsTrue(member.HasBackendIdentity);
        }
    }
}
