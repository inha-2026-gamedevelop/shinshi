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
    public sealed class PartyInviteRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    LEADER_ID  = 10;
        private const int    GUEST_ID   = 20;
        private const string LEADER     = "방장";
        private const string GUEST      = "손님";
        private const string TARGET     = "초대대상";

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Check_LeaderWithFreeSlot_ReturnsNone()
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 1);

            Assert.AreEqual(EInviteBlock.None, PartyInviteRules.Check(TARGET, LEADER, true, in room, false));
        }

        [Test]
        public void Check_NameWithSpaces_TrimsBeforeJudging()
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 1);

            Assert.AreEqual(EInviteBlock.None, PartyInviteRules.Check("  " + TARGET + " ", LEADER, true, in room, false));
            Assert.AreEqual(TARGET, PartyInviteRules.NormalizeName("  " + TARGET + " "));
        }

        [Test]
        public void Check_Pending_BlocksDoubleClick()
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 1);

            Assert.AreEqual(EInviteBlock.Pending, PartyInviteRules.Check(TARGET, LEADER, true, in room, true));
        }

        [Test]
        public void Check_NotConnected_ReturnsOffline()
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 1);

            Assert.AreEqual(EInviteBlock.Offline, PartyInviteRules.Check(TARGET, LEADER, false, in room, false));
        }

        [Test]
        public void Check_NotInRoom_ReturnsNoRoom()
        {
            PartyRoomStateMessage room = default;

            Assert.AreEqual(EInviteBlock.NoRoom, PartyInviteRules.Check(TARGET, LEADER, true, in room, false));
        }

        [Test]
        public void Check_NonLeaderMember_ReturnsNone()
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 2);

            Assert.AreEqual(EInviteBlock.None, PartyInviteRules.Check(TARGET, GUEST, true, in room, false));
        }

        [Test]
        public void Check_LocalNameNotInMembers_ReturnsNotMember()
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 1);

            Assert.AreEqual(EInviteBlock.NotMember, PartyInviteRules.Check(TARGET, "모르는이", true, in room, false));
        }

        [TestCase(EPartyPhase.Countdown)]
        [TestCase(EPartyPhase.Queued)]
        [TestCase(EPartyPhase.Departing)]
        public void Check_NotOpenPhase_ReturnsWrongPhase(EPartyPhase phase)
        {
            PartyRoomStateMessage room = Room(phase, 1);

            Assert.AreEqual(EInviteBlock.WrongPhase, PartyInviteRules.Check(TARGET, LEADER, true, in room, false));
        }

        [Test]
        public void Check_FullRoom_ReturnsFull()
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, Constants.Session.MAX_PARTY_SIZE);

            Assert.AreEqual(EInviteBlock.Full, PartyInviteRules.Check(TARGET, LEADER, true, in room, false));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Check_EmptyName_ReturnsEmptyName(string name)
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 1);

            Assert.AreEqual(EInviteBlock.EmptyName, PartyInviteRules.Check(name, LEADER, true, in room, false));
        }

        [TestCase(LEADER)]
        [TestCase(" 방장 ")]
        public void Check_OwnName_ReturnsSelf(string name)
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 1);

            Assert.AreEqual(EInviteBlock.Self, PartyInviteRules.Check(name, LEADER, true, in room, false));
        }

        [Test]
        public void CanUse_AnyRoomMemberButNotOutsider()
        {
            PartyRoomStateMessage room = Room(EPartyPhase.Open, 2);
            PartyRoomStateMessage none = default;

            Assert.IsTrue(PartyInviteRules.CanUse(in room, LEADER));
            Assert.IsTrue(PartyInviteRules.CanUse(in room, GUEST));
            Assert.IsFalse(PartyInviteRules.CanUse(in room, "모르는이"));
            Assert.IsFalse(PartyInviteRules.CanUse(in none, LEADER));
        }

        [TestCase(EInviteBlock.Pending)]
        [TestCase(EInviteBlock.Offline)]
        [TestCase(EInviteBlock.NoRoom)]
        [TestCase(EInviteBlock.NotMember)]
        [TestCase(EInviteBlock.WrongPhase)]
        [TestCase(EInviteBlock.Full)]
        [TestCase(EInviteBlock.EmptyName)]
        [TestCase(EInviteBlock.Self)]
        public void Describe_Block_ReturnsKoreanText(EInviteBlock block)
        {
            Assert.IsNotEmpty(PartyInviteRules.Describe(block));
        }

        [Test]
        public void Describe_None_ReturnsEmpty()
        {
            Assert.IsEmpty(PartyInviteRules.Describe(EInviteBlock.None));
        }

        [Test]
        public void DescribeReject_InviteSpecificReasons_DifferFromGenericText()
        {
            Assert.AreNotEqual(RejectText.Describe(ERejectReason.InvalidTarget), PartyInviteRules.DescribeReject(ERejectReason.InvalidTarget));
            Assert.AreNotEqual(RejectText.Describe(ERejectReason.NoCapacity), PartyInviteRules.DescribeReject(ERejectReason.NoCapacity));
        }

        [TestCase(ERejectReason.RoomNotFound)]
        [TestCase(ERejectReason.WrongPhase)]
        [TestCase(ERejectReason.RateLimited)]
        [TestCase(ERejectReason.BackendUnavailable)]
        public void DescribeReject_OtherReasons_UseSharedRejectText(ERejectReason reason)
        {
            Assert.AreEqual(RejectText.Describe(reason), PartyInviteRules.DescribeReject(reason));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static PartyRoomStateMessage Room(EPartyPhase phase, int memberCount)
        {
            var members = new PartyMemberView[memberCount];
            members[0] = new PartyMemberView(LEADER_ID, LEADER, EPlayerClass.Swordsman, EPartyReadyState.NotReady, 0, 0, 0);
            for (int i = 1; i < memberCount; ++i)
            {
                string name = (i == 1) ? GUEST : (GUEST + i);
                members[i] = new PartyMemberView(GUEST_ID + i, name, EPlayerClass.Archer, EPartyReadyState.NotReady, 0, 0, 0);
            }

            return new PartyRoomStateMessage(true, 1, 1, "room", EPartyMode.Boss, "battle_wolbaek_easy", 0, false,
                LEADER_ID, phase, 0f, members);
        }
    }
}
