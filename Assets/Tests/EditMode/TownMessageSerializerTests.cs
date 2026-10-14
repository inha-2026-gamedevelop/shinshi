// System
using System;
using System.Collections.Generic;
using System.IO;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class TownMessageSerializerTests
    {
        // 마을 메시지의 쓰기, 읽기 순서가 맞는지와 클라이언트가 보낸 배열 길이 상한, 마을 입장 결합의 계정 ID 전달을 본다

        private readonly System.Collections.Generic.List<UnityEngine.Object> _created = new System.Collections.Generic.List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            Shinshi.Data.GameDB.ResetForTests();
            Shinshi.Data.GameDB.Initialize(GameDBTestFixtures.CreateValidRoot(_created));
        }

        [TearDown]
        public void TearDown()
        {
            Shinshi.Data.GameDB.ResetForTests();
            foreach (UnityEngine.Object value in _created) UnityEngine.Object.DestroyImmediate(value);
            _created.Clear();
        }

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
        public void PartyRequest_RoundTrip_KeepsFieldsAndConsumesPayload()
        {
            var source = new PartyRequestMessage(EPartyAction.SetReady, 9, EPartyMode.Boss, "battle_wolbaek_easy",
                "room", "password", true, 3, 1);
            var writer = new NetworkWriter();
            writer.WritePartyRequestMessage(source);
            var reader = new NetworkReader(writer.ToArraySegment());
            PartyRequestMessage read = reader.ReadPartyRequestMessage();

            Assert.AreEqual(source.Action, read.Action);
            Assert.AreEqual(source.RoomId, read.RoomId);
            Assert.AreEqual(source.Mode, read.Mode);
            Assert.AreEqual(source.ContentId, read.ContentId);
            Assert.AreEqual(source.Name, read.Name);
            Assert.AreEqual(source.Password, read.Password);
            Assert.AreEqual(source.Ready, read.Ready);
            Assert.AreEqual(source.SmallPotions, read.SmallPotions);
            Assert.AreEqual(source.LargePotions, read.LargePotions);
            Assert.AreEqual(0, reader.Remaining);
        }

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
            var reader = new NetworkReader(writer.ToArraySegment());
            PartyRoomStateMessage read = reader.ReadPartyRoomStateMessage();

            Assert.IsTrue(read.InRoom);
            Assert.AreEqual(42, read.Revision);
            Assert.AreEqual(EPartyPhase.Countdown, read.Phase);
            Assert.AreEqual(2, read.LeaderParticipantId);
            Assert.AreEqual(2, read.Members.Length);
            Assert.AreEqual(EPlayerClass.Summoner, read.Members[1].PlayerClass);
            Assert.AreEqual(EPartyReadyState.Ready, read.Members[0].Ready);
            Assert.AreEqual(3, read.Members[0].SmallPotions);
            Assert.AreEqual(-1L, read.Members[0].TotalExperience);
            Assert.AreEqual(0, reader.Remaining);
        }

        [Test]
        public void PartyRoomState_RoundTrip_KeepsNewClassMembers()
        {
            var members = new[]
            {
                new PartyMemberView(1, "a", EPlayerClass.Swordsman, EPartyReadyState.Ready, 0, 0, 0),
                new PartyMemberView(2, "b", EPlayerClass.Mageknight, EPartyReadyState.Ready, 0, 0, 2850),
                new PartyMemberView(3, "c", EPlayerClass.Archer, EPartyReadyState.Ready, 0, 0, 120350)
            };
            var source = new PartyRoomStateMessage(true, 9, 42, "room", EPartyMode.Boss, "battle_wolbaek_easy", 0, false, 1,
                EPartyPhase.Open, 0f, members);

            var writer = new NetworkWriter();
            writer.WritePartyRoomStateMessage(source);
            var reader = new NetworkReader(writer.ToArraySegment());
            PartyRoomStateMessage read = reader.ReadPartyRoomStateMessage();

            Assert.AreEqual(EPlayerClass.Swordsman, read.Members[0].PlayerClass);
            Assert.AreEqual(EPlayerClass.Mageknight, read.Members[1].PlayerClass);
            Assert.AreEqual(EPlayerClass.Archer, read.Members[2].PlayerClass);
            Assert.AreEqual(0L, read.Members[0].TotalExperience);
            Assert.AreEqual(2850L, read.Members[1].TotalExperience);
            Assert.AreEqual(120350L, read.Members[2].TotalExperience);
            Assert.AreEqual(0, reader.Remaining);
            var display = new List<SortieMemberData>();
            SortiePrepRules.BuildMembers(in read, display);
            Assert.AreEqual(1, display[0].Level);
            Assert.AreEqual(5, display[1].Level);
            Assert.AreEqual(30, display[2].Level);
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
        public void GrowthSnapshot_RoundTrip_KeepsEquipmentOptionsAndPity()
        {
            var equipment = new[]
            {
                new TownEquipmentEntry("a", "equip_a", 0, 3, false, "craft", 7, true, false, true, false, 1, 380, 4, 190),
                new TownEquipmentEntry("b", "equip_b", 1, 0, false, "craft", 0, false, false, false),
                new TownEquipmentEntry("c", "equip_c", 5, 2, false, "craft", 2, false, true, true, true, 3, 10000, 0, 0)
            };
            var source = new GrowthSnapshotMessage(1, 9, 10, 20, new[] { 1, 2, 3, 4, 5 }, equipment, new[] { new TownPityEntry(0, 1, 2) },
                null, null, new[] { new TownItemEntry("item", "account", 3, 1) }, 4, 40, "v1", 777L, new[] { 0, 9, 3, 0, 1, 5 });

            var writer = new NetworkWriter();
            writer.WriteGrowthSnapshotMessage(source);
            GrowthSnapshotMessage read = new NetworkReader(writer.ToArraySegment()).ReadGrowthSnapshotMessage();

            Assert.AreEqual(3, read.Equipment.Length);
            Assert.AreEqual(1, read.Equipment[0].Option1Kind);
            Assert.AreEqual(380, read.Equipment[0].Option1Value);
            Assert.AreEqual(4, read.Equipment[0].Option2Kind);
            Assert.AreEqual(190, read.Equipment[0].Option2Value);
            Assert.IsTrue(read.Equipment[0].Bound);
            Assert.AreEqual(0, read.Equipment[1].Option1Kind);
            Assert.AreEqual(0, read.Equipment[1].Option1Value);
            Assert.AreEqual(0, read.Equipment[1].Option2Kind);
            Assert.AreEqual(0, read.Equipment[1].Option2Value);
            Assert.AreEqual(3, read.Equipment[2].Option1Kind);
            Assert.AreEqual(10000, read.Equipment[2].Option1Value);
            Assert.IsTrue(read.Equipment[2].InMarket);
            Assert.AreEqual(new[] { 0, 9, 3, 0, 1, 5 }, read.OptionPity);

            // 옵션 뒤에 오는 필드가 밀리지 않았는지 본다
            Assert.AreEqual(777L, read.TotalExperience);
            Assert.AreEqual(40, read.StorageCapacity);
            Assert.AreEqual("v1", read.ContentVersion);
            Assert.AreEqual(2, read.Pity[0].Failures);
        }

        [Test]
        public void GrowthSnapshot_RoundTripWithoutOptionPity_ReadsEmptyArray()
        {
            var source = new GrowthSnapshotMessage(0, 0, 0, 0, null, null, null, null, null, null, 0, 0, "v");

            var writer = new NetworkWriter();
            writer.WriteGrowthSnapshotMessage(source);
            GrowthSnapshotMessage read = new NetworkReader(writer.ToArraySegment()).ReadGrowthSnapshotMessage();

            Assert.AreEqual(0, read.OptionPity.Length);
            Assert.AreEqual(0, source.OptionPity.Length);
        }

        [Test]
        public void GrowthSnapshot_OptionPityOverLimit_FailsToRead()
        {
            // 성장 상태 배열 한 칸 수 상한(1024)을 넘기는 길이
            const int OPTION_PITY_OVER_LIMIT = 2048;

            var source = new GrowthSnapshotMessage(0, 0, 0, 0, null, null, null, null, null, null, 0, 0, "v", 0L,
                new int[OPTION_PITY_OVER_LIMIT]);

            var writer = new NetworkWriter();
            writer.WriteGrowthSnapshotMessage(source);
            var reader = new NetworkReader(writer.ToArraySegment());

            Assert.Throws<InvalidDataException>(() => reader.ReadGrowthSnapshotMessage());
        }

        [Test]
        public void GrowthResult_RerollOptionsAction_RoundTripKeepsAction()
        {
            Guid operationId = Guid.NewGuid();
            var source = new GrowthResultMessage(operationId, EGrowthAction.EquipmentRerollOptions, ERejectReason.None, 1, "item");

            var writer = new NetworkWriter();
            writer.WriteGrowthResultMessage(source);
            GrowthResultMessage read = new NetworkReader(writer.ToArraySegment()).ReadGrowthResultMessage();

            Assert.AreEqual(14, (int)EGrowthAction.EquipmentRerollOptions);
            Assert.AreEqual(EGrowthAction.EquipmentRerollOptions, read.Action);
            Assert.AreEqual(operationId, read.OperationId);
        }

        [Test]
        public void ToMessage_EquipmentOptionsAndPity_ReachesMessage()
        {
            var equipment = new[]
            {
                new EquipmentView("a", "equip_a", 0, 3, false, GrowthCodes.EquipmentOrigin.CRAFT, 7, true, false, true, false, 1, 380, 4, 190),
                new EquipmentView("b", "equip_b", 1, 0, false, GrowthCodes.EquipmentOrigin.CRAFT, 0, false, false, false)
            };
            var snapshot = new CharacterGrowthSnapshot("c", 0, 5, 0, 0, new int[5], equipment, null, null, null, null, 0, 0, "v", 0L, 0L,
                new[] { 0, 9, 3, 0, 1, 5 });

            GrowthSnapshotMessage message = TownSnapshotConverter.ToMessage(snapshot);

            Assert.AreEqual(2, message.Equipment.Length);
            Assert.AreEqual(1, message.Equipment[0].Option1Kind);
            Assert.AreEqual(380, message.Equipment[0].Option1Value);
            Assert.AreEqual(4, message.Equipment[0].Option2Kind);
            Assert.AreEqual(190, message.Equipment[0].Option2Value);
            Assert.AreEqual(0, message.Equipment[1].Option1Kind);
            Assert.AreEqual(0, message.Equipment[1].Option2Value);
            Assert.AreEqual(new[] { 0, 9, 3, 0, 1, 5 }, message.OptionPity);
        }

        [Test]
        public void ToMessage_NullOptionPity_BecomesSixZeros()
        {
            var snapshot = new CharacterGrowthSnapshot("c", 0, 5, 0, 0, new int[5], null, null, null, null, null, 0, 0, "v", 0L);

            GrowthSnapshotMessage message = TownSnapshotConverter.ToMessage(snapshot);

            Assert.AreEqual(new int[Constants.Combat.EQUIPMENT_SLOT_COUNT], message.OptionPity);
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

        [TestCase(0L)]
        [TestCase(2850L)]
        [TestCase(120350L)]
        public void TownJoin_AccountIdAndExperienceFromExchange_ReachesTownMember(long totalExperience)
        {
            var response = new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID, ContractCodes.ServerRole.TOWN, null, null, 0,
                LEASE_ID, (int)EPlayerClass.Warrior, null, JoinTokenExchangeResponse.NO_MODE, null, null, null,
                GameSessionCodes.Difficulty.NONE, null, ACCOUNT_ID, null, null,
                new CharacterExperienceSnapshot(totalExperience, null, 0, CharacterLevelRules.SettingsHash(Shinshi.Data.GameDB.CharacterLevel.ToSettings())));
            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response,
                out JoinTokenClaims claims));

            var registry = new ServerSessionRegistry(REGISTRY);
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));

            Assert.AreEqual(ACCOUNT_ID.ToLowerInvariant(), binding.AccountId);
            Assert.AreEqual(CHARACTER_ID, binding.CharacterId);
            TownMember member = TownMember.FromBinding(binding);
            Assert.IsTrue(member.HasBackendIdentity);
            Assert.AreEqual(totalExperience, binding.Experience.TotalExperience);
            Assert.AreEqual(totalExperience, member.TotalExperience);
        }

        [Test]
        public void FromBinding_DevelopmentIdentityWithoutExperience_UsesUnknown()
        {
            var binding = new SessionBinding("dev", 1, null, EPlayerClass.Archer, "궁수");

            TownMember member = TownMember.FromBinding(binding);

            Assert.AreEqual(-1L, member.TotalExperience);
        }
    }
}
