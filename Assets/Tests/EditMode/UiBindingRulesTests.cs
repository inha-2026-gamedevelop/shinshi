// System
using System;
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
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class UiBindingRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int LEADER_ID = 10;
        private const int GUEST_ID  = 20;

        private RewardCatalogSO _rewards = null;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _rewards = ScriptableObject.CreateInstance<RewardCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_rewards);
        }

        [Test]
        public void Build_GrowthSnapshot_SplitsCurrencyFromItems()
        {
            var items = new[]
            {
                new TownItemEntry(_rewards.TradeCurrencyItemId, "account", 500, 100),
                new TownItemEntry(_rewards.EssenceItemId, "character", 9, 0),
                new TownItemEntry("item_unknown", "account", 3, 1)
            };
            var equipment = new[] { new TownEquipmentEntry("eq-1", "equip_blade", 0, 2, false, "craft", 5, true, true, true) };
            GrowthSnapshotMessage growth = Growth(1200, 0, equipment, items, 40);
            var into = new List<InventoryItemData>();

            InventoryItemRules.Build(true, in growth, false, default, _rewards, null, null, into, out long yeopjeon, out long essence);

            Assert.That(yeopjeon, Is.EqualTo(400));
            Assert.That(essence, Is.EqualTo(1200));
            Assert.That(into.Count, Is.EqualTo(2));
            Assert.That(into[0].Category, Is.EqualTo(EInventoryCategory.Equipment));
            Assert.That(into[0].Slot.Grade, Is.EqualTo(EItemGrade.Epic));
            Assert.That(into[0].Slot.IsEquipped, Is.True);
            Assert.That(into[0].Slot.Description, Does.Contain("+5"));
            Assert.That(into[1].Slot.Id, Is.EqualTo("item_unknown"));
            Assert.That(into[1].Slot.Count, Is.EqualTo(2));
            Assert.That(into[1].Category, Is.EqualTo(EInventoryCategory.Etc));
        }

        [Test]
        public void Build_LifeSnapshot_AddsHerbAndAvailablePotions()
        {
            var potions = new[]
            {
                new TownItemEntry("potion_small", "character", 4, 1),
                new TownItemEntry("potion_large", "character", 2, 2)
            };
            var life = new LifeSnapshotMessage(0, 0, 0, "item_herb", 7, 99, potions);
            var into = new List<InventoryItemData>();

            InventoryItemRules.Build(false, default, true, in life, null, null, null, into, out _, out _);

            Assert.That(into.Count, Is.EqualTo(2));
            Assert.That(into[0].Slot.Id, Is.EqualTo("item_herb"));
            Assert.That(into[0].Category, Is.EqualTo(EInventoryCategory.Material));
            Assert.That(into[1].Slot.Count, Is.EqualTo(3));
            Assert.That(into[1].Category, Is.EqualTo(EInventoryCategory.Consumable));
        }

        [TestCase((byte)3, EItemGrade.Legendary)]
        [TestCase((byte)200, EItemGrade.Common)]
        public void ToGrade_ServerByte_MapsOrFallsBack(byte grade, EItemGrade expected)
        {
            Assert.That(InventoryItemRules.ToGrade(grade), Is.EqualTo(expected));
        }

        [Test]
        public void BuildMembers_Room_MarksLeaderAndReady()
        {
            PartyRoomStateMessage room = Room(EPartyReadyState.NotReady, EPartyReadyState.Ready);
            var into = new List<SortieMemberData>();

            SortiePrepRules.BuildMembers(in room, into);

            Assert.That(into.Count, Is.EqualTo(2));
            Assert.That(into[0].IsLeader, Is.True);
            Assert.That(into[0].IsReady, Is.False);
            Assert.That(into[1].IsLeader, Is.False);
            Assert.That(into[1].IsReady, Is.True);
            Assert.That(into[0].Level, Is.EqualTo(-1), "경험치가 없는 참가자를 Lv1로 꾸미지 않는다");
        }

        [TestCase(0L, 1)]
        [TestCase(2850L, 5)]
        [TestCase(120350L, 30)]
        [TestCase(-1L, -1)]
        public void BuildMembers_ServerExperience_UsesConfiguredLevelOrUnknown(long totalExperience, int expected)
        {
            CharacterLevelConfigSO config = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            try
            {
                var members = new[]
                {
                    new PartyMemberView(LEADER_ID, "검사", EPlayerClass.Swordsman, EPartyReadyState.Ready, 0, 0, totalExperience)
                };
                var room = new PartyRoomStateMessage(true, 1, 1, "room", EPartyMode.Boss, "battle_wolbaek_easy", 0, false,
                    LEADER_ID, EPartyPhase.Open, 0f, members);
                var into = new List<SortieMemberData>();

                SortiePrepRules.BuildMembers(in room, into, config.ToSettings());

                Assert.AreEqual(expected, into[0].Level);
                Assert.AreEqual(EPlayerClass.Swordsman, into[0].PlayerClass);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void BuildMembers_ChangedLevelCurve_UsesPassedSettings()
        {
            var levels = new CharacterLevelSettings(30)
            {
                FirstLevelCost = 1000, LevelCostStep = 0
            };
            var members = new[]
            {
                new PartyMemberView(LEADER_ID, "궁수", EPlayerClass.Archer, EPartyReadyState.NotReady, 0, 0, 2850)
            };
            var room = new PartyRoomStateMessage(true, 1, 1, "room", EPartyMode.Boss, "battle_wolbaek_easy", 0, false,
                LEADER_ID, EPartyPhase.Open, 0f, members);
            var into = new List<SortieMemberData>();

            SortiePrepRules.BuildMembers(in room, into, levels);

            Assert.AreEqual(3, into[0].Level, "입장 경험치를 고정 레벨이나 기본 곡선으로 대신하지 않는다");
        }

        [Test]
        public void AreGuestsReady_LeaderNotReady_StillTrue()
        {
            PartyRoomStateMessage room = Room(EPartyReadyState.NotReady, EPartyReadyState.Ready);

            Assert.That(SortiePrepRules.AreGuestsReady(in room), Is.True);
            Assert.That(SortiePrepRules.AreAllReady(in room), Is.False);
        }

        [Test]
        public void AreGuestsReady_GuestChecking_False()
        {
            PartyRoomStateMessage room = Room(EPartyReadyState.Ready, EPartyReadyState.Checking);

            Assert.That(SortiePrepRules.AreGuestsReady(in room), Is.False);
        }

        [Test]
        public void TryFindLocal_ByName_FindsGuest()
        {
            PartyRoomStateMessage room = Room(EPartyReadyState.Ready, EPartyReadyState.Ready);

            Assert.That(SortiePrepRules.TryFindLocal(in room, "guest", out PartyMemberView local), Is.True);
            Assert.That(local.ParticipantId, Is.EqualTo(GUEST_ID));
            Assert.That(SortiePrepRules.TryFindLocal(in room, "nobody", out _), Is.False);
        }

        [TestCase((sbyte)1, EBossDifficulty.Hard)]
        [TestCase((sbyte)-1, EBossDifficulty.Easy)]
        public void ToDifficulty_ServerValue_MapsOrFallsBack(sbyte value, EBossDifficulty expected)
        {
            Assert.That(SortiePrepRules.ToDifficulty(value), Is.EqualTo(expected));
        }

        [Test]
        public void ShouldShow_RunningWithoutPersonalClose_False()
        {
            var status = new SortieStatusMessage(ESortiePhase.Running, ESessionEndReason.Success, false, ESessionEndReason.Success,
                false, ESortieSettlementState.None);

            Assert.That(SortieResultRules.ShouldShow(in status), Is.False);
        }

        [Test]
        public void ResolveReason_PersonalClose_UsesPersonalReason()
        {
            var status = new SortieStatusMessage(ESortiePhase.Running, ESessionEndReason.Success, true, ESessionEndReason.Abandon,
                false, ESortieSettlementState.Pending);

            Assert.That(SortieResultRules.ShouldShow(in status), Is.True);
            Assert.That(SortieResultRules.ResolveReason(in status), Is.EqualTo(ESessionEndReason.Abandon));
            Assert.That(SortieResultRules.Build(in status, "battle_x").RegionName, Is.EqualTo("battle_x"));
        }

        [TestCase(ESortieSettlementState.Settled, ESaveStatus.Saved)]
        [TestCase(ESortieSettlementState.Pending, ESaveStatus.Saving)]
        [TestCase(ESortieSettlementState.Deferred, ESaveStatus.Retrying)]
        [TestCase(ESortieSettlementState.None, ESaveStatus.Idle)]
        public void ToSaveStatus_Settlement_Maps(ESortieSettlementState state, ESaveStatus expected)
        {
            Assert.That(SortieResultRules.ToSaveStatus(state), Is.EqualTo(expected));
        }

        private static GrowthSnapshotMessage Growth(long essence, long shards, TownEquipmentEntry[] equipment, TownItemEntry[] items,
            int capacity)
        {
            return new GrowthSnapshotMessage(0, 0, essence, shards, Array.Empty<int>(), equipment, Array.Empty<TownPityEntry>(),
                Array.Empty<TownTalismanEntry>(), Array.Empty<TownLoadoutEntry>(), items, 0, capacity, "0.1.0");
        }

        private static PartyRoomStateMessage Room(EPartyReadyState leaderReady, EPartyReadyState guestReady)
        {
            var members = new[]
            {
                new PartyMemberView(LEADER_ID, "leader", EPlayerClass.Warrior, leaderReady, 0, 0),
                new PartyMemberView(GUEST_ID, "guest", EPlayerClass.Summoner, guestReady, 0, 0)
            };
            return new PartyRoomStateMessage(true, 1, 1, "room", EPartyMode.Boss, "battle_wolbaek_easy", 0, false, LEADER_ID,
                EPartyPhase.Open, 0f, members);
        }
    }
}
