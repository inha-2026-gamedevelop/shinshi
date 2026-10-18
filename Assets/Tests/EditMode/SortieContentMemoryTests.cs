// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 출정 직전 방의 콘텐츠를 기억해 결과 창 지역 이름으로 쓰는 흐름 </summary>
    public sealed class SortieContentMemoryTests
    {
        private const int LEADER_ID = 1;

        [SetUp]
        public void SetUp()
        {
            SortieContentMemory.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SortieContentMemory.Clear();
        }

        [Test]
        public void TryRecall_NothingRemembered_ReturnsFalse()
        {
            Assert.IsFalse(SortieContentMemory.TryRecall(out _, out string contentId));
            Assert.AreEqual(string.Empty, contentId);
        }

        [Test]
        public void Remember_RoomWithContent_RecallsModeAndContent()
        {
            PartyRoomStateMessage room = Room(true, EPartyMode.Boss, "battle_wolbaek_hard");

            SortieContentMemory.Remember(in room);

            Assert.IsTrue(SortieContentMemory.TryRecall(out EPartyMode mode, out string contentId));
            Assert.AreEqual(EPartyMode.Boss, mode);
            Assert.AreEqual("battle_wolbaek_hard", contentId);
        }

        [Test]
        public void Remember_LeftRoomAfterDeparture_KeepsLastRoom()
        {
            PartyRoomStateMessage room = Room(true, EPartyMode.Boss, "battle_mucheol_easy");
            SortieContentMemory.Remember(in room);

            PartyRoomStateMessage left = PartyRoomStateMessage.Left();
            SortieContentMemory.Remember(in left);

            Assert.IsTrue(SortieContentMemory.TryRecall(out _, out string contentId));
            Assert.AreEqual("battle_mucheol_easy", contentId);
        }

        [Test]
        public void Remember_RoomWithoutContent_IsIgnored()
        {
            PartyRoomStateMessage room = Room(true, EPartyMode.Normal, string.Empty);

            SortieContentMemory.Remember(in room);

            Assert.IsFalse(SortieContentMemory.TryRecall(out _, out _));
        }

        [Test]
        public void Remember_NewRoom_ReplacesPreviousOne()
        {
            PartyRoomStateMessage first  = Room(true, EPartyMode.Normal, "region_forest");
            PartyRoomStateMessage second = Room(true, EPartyMode.Boss, "battle_wolbaek_easy");
            SortieContentMemory.Remember(in first);

            SortieContentMemory.Remember(in second);

            Assert.IsTrue(SortieContentMemory.TryRecall(out EPartyMode mode, out string contentId));
            Assert.AreEqual(EPartyMode.Boss, mode);
            Assert.AreEqual("battle_wolbaek_easy", contentId);
        }

        [Test]
        public void ContentName_RememberedForestRegion_ShowsRegionName()
        {
            PartyRoomStateMessage room = Room(true, EPartyMode.Normal, "region_forest");
            SortieContentMemory.Remember(in room);

            SortieContentMemory.TryRecall(out EPartyMode mode, out string contentId);

            Assert.AreEqual("숲", DungeonEntryRules.ContentName(null, mode, contentId));
        }

        [Test]
        public void BossName_KnownBosses_ReturnKoreanNamesAndUnknownKeepsId()
        {
            Assert.AreEqual("월백", DungeonEntryRules.BossName("boss_wolbaek"));
            Assert.AreEqual("무철", DungeonEntryRules.BossName("boss_mucheol"));
            Assert.AreEqual("boss_unknown", DungeonEntryRules.BossName("boss_unknown"));
            Assert.AreEqual(string.Empty, DungeonEntryRules.BossName(string.Empty));
        }

        private static PartyRoomStateMessage Room(bool inRoom, EPartyMode mode, string contentId)
        {
            return new PartyRoomStateMessage(inRoom, 1, 1, "room", mode, contentId, 0, false, LEADER_ID, EPartyPhase.Departing, 0f,
                Array.Empty<PartyMemberView>());
        }
    }
}
