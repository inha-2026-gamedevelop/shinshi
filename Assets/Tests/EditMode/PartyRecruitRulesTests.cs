// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class PartyRecruitRulesTests
    {
        private static PartyRoomSummary Room(int id, string name, string content, EPartyPhase phase, byte members)
        {
            return new PartyRoomSummary(id, name, EPartyMode.Normal, content, -1, phase, members, 0, false, 1, 0, 0, 0, 0, 0);
        }

        [Test]
        public void Filter_EmptyQuery_ReturnsAllIndices()
        {
            var rooms = new[] { Room(1, "알파", "forest", EPartyPhase.Open, 1), Room(2, "베타", "ruins", EPartyPhase.Open, 2) };
            var into  = new List<int>();

            PartyRecruitRules.Filter(rooms, "  ", into);

            CollectionAssert.AreEqual(new[] { 0, 1 }, into);
        }

        [Test]
        public void Filter_QueryMatchesNameOrContent_ReturnsOnlyMatches()
        {
            var rooms = new[] { Room(1, "알파", "forest", EPartyPhase.Open, 1), Room(2, "베타", "ruins", EPartyPhase.Open, 2) };
            var into  = new List<int>();

            PartyRecruitRules.Filter(rooms, "RUINS", into);

            CollectionAssert.AreEqual(new[] { 1 }, into);
        }

        [Test]
        public void Filter_NullRooms_ClearsResult()
        {
            var into = new List<int> { 7 };

            PartyRecruitRules.Filter(null, "x", into);

            Assert.AreEqual(0, into.Count);
        }

        [Test]
        public void IsJoinable_FullRoom_ReturnsFalse()
        {
            PartyRoomSummary full = Room(1, "a", "c", EPartyPhase.Open, (byte)PartyRecruitRules.MAX_MEMBERS);

            Assert.IsFalse(PartyRecruitRules.IsJoinable(in full));
        }

        [Test]
        public void IsJoinable_CountdownRoom_ReturnsFalse()
        {
            PartyRoomSummary room = Room(1, "a", "c", EPartyPhase.Countdown, 1);

            Assert.IsFalse(PartyRecruitRules.IsJoinable(in room));
        }

        [Test]
        public void IsJoinable_OpenRoomWithSeat_ReturnsTrue()
        {
            PartyRoomSummary room = Room(1, "a", "c", EPartyPhase.Open, 1);

            Assert.IsTrue(PartyRecruitRules.IsJoinable(in room));
        }

        [Test]
        public void DescribeClasses_NoMembers_ReturnsEmpty()
        {
            PartyRoomSummary room = new PartyRoomSummary(1, "a", EPartyMode.Normal, "c", -1, EPartyPhase.Open, 0, 0, false, 0, 0, 0, 0, 0, 0);

            Assert.AreEqual(string.Empty, PartyRecruitRules.DescribeClasses(in room));
        }

        [Test]
        public void DescribeClasses_WarriorAndArcher_ListsBoth()
        {
            PartyRoomSummary room = new PartyRoomSummary(1, "a", EPartyMode.Normal, "c", -1, EPartyPhase.Open, 3, 0, false, 1, 0, 0, 0, 0, 2);

            Assert.AreEqual("전사 1 · 궁수 2", PartyRecruitRules.DescribeClasses(in room));
        }
    }
}
