// System
using System;
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;
using Shinshi.UI;

// 같은 이름의 Contracts DTO(백엔드 응답)와 구분한다
using SocialGuildView = Shinshi.Network.SocialGuildView;

namespace Shinshi.Tests
{
    public sealed class SocialViewRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long NOW_MS      = 1760000000000L;
        private const long MS_PER_HOUR = 3600L * 1000L;
        private const long MS_PER_DAY  = 24L * MS_PER_HOUR;

        /****************************************
        *            FriendsViewRules
        ****************************************/

        [Test]
        public void SortFriends_TownThenDungeonThenOffline_ThenName()
        {
            var friends = new List<SocialFriendEntry>
            {
                new SocialFriendEntry("1", "다", 0, EPresence.Offline, false),
                new SocialFriendEntry("2", "나", 0, EPresence.Town, false),
                new SocialFriendEntry("3", "가", 0, EPresence.Dungeon, false),
                new SocialFriendEntry("4", "가", 0, EPresence.Town, false)
            };

            FriendsViewRules.SortFriends(friends);

            Assert.That(friends.ConvertAll(f => f.CharacterId), Is.EqualTo(new[] { "4", "2", "3", "1" }));
        }

        [TestCase(EPresence.Town, true, true, EPartyPhase.Open, true)]
        [TestCase(EPresence.Town, false, true, EPartyPhase.Open, false)]
        [TestCase(EPresence.Dungeon, true, true, EPartyPhase.Open, false)]
        [TestCase(EPresence.Town, true, false, EPartyPhase.Open, false)]
        [TestCase(EPresence.Town, true, true, EPartyPhase.Countdown, false)]
        public void CanPartyInvite_Conditions_MatchRules(EPresence presence, bool sameServer, bool inRoom, EPartyPhase phase, bool expected)
        {
            Assert.That(FriendsViewRules.CanPartyInvite(presence, sameServer, inRoom, phase), Is.EqualTo(expected));
        }

        [Test]
        public void CanWhisper_Presence_OnlyWhenConnected()
        {
            Assert.That(FriendsViewRules.CanWhisper(EPresence.Town), Is.True);
            Assert.That(FriendsViewRules.CanWhisper(EPresence.Dungeon), Is.True);
            Assert.That(FriendsViewRules.CanWhisper(EPresence.Offline), Is.False);
        }

        [Test]
        public void TextHelpers_Inputs_FormatCountsTabsAndRemaining()
        {
            Assert.That(FriendsViewRules.CountText(12, SocialLimits.MAX_FRIENDS), Is.EqualTo("12/50"));
            Assert.That(FriendsViewRules.TabLabel(FriendsViewRules.TAB_INCOMING, 3), Is.EqualTo("받은 요청(3)"));
            Assert.That(FriendsViewRules.TabLabel(FriendsViewRules.TAB_INCOMING, 0), Is.EqualTo("받은 요청"));
            Assert.That(FriendsViewRules.RemainingText(NOW_MS + (6 * MS_PER_DAY) + MS_PER_HOUR, NOW_MS), Is.EqualTo("6일 남음"));
            Assert.That(FriendsViewRules.RemainingText(NOW_MS + (5 * MS_PER_HOUR), NOW_MS), Is.EqualTo("5시간 남음"));
            Assert.That(FriendsViewRules.RemainingText(NOW_MS + 1000L, NOW_MS), Is.EqualTo("곧 만료"));
            Assert.That(FriendsViewRules.PresenceText(EPresence.Dungeon), Is.EqualTo("출정 중"));
        }

        [Test]
        public void Paging_RowCounts_AreCountedAndClamped()
        {
            Assert.That(FriendsViewRules.PageCount(0), Is.EqualTo(1));
            Assert.That(FriendsViewRules.PageCount(FriendsViewRules.ROWS_PER_PAGE), Is.EqualTo(1));
            Assert.That(FriendsViewRules.PageCount(FriendsViewRules.ROWS_PER_PAGE + 1), Is.EqualTo(2));
            Assert.That(FriendsViewRules.ClampPage(5, FriendsViewRules.ROWS_PER_PAGE + 1), Is.EqualTo(1));
            Assert.That(FriendsViewRules.ClampPage(-1, 3), Is.EqualTo(0));
        }

        /****************************************
        *             GuildViewRules
        ****************************************/

        [Test]
        public void ModeOf_GuildPresence_SwitchesMode()
        {
            Assert.That(GuildViewRules.ModeOf(Snapshot(SocialGuildView.None)), Is.EqualTo(EGuildWindowMode.NoGuild));
            Assert.That(GuildViewRules.ModeOf(Snapshot(Guild(EGuildRole.Member, 3))), Is.EqualTo(EGuildWindowMode.Guild));
        }

        [Test]
        public void CanManage_Roles_OnlyLeaderOnOtherMembers()
        {
            SocialSnapshotMessage leader = Snapshot(Guild(EGuildRole.Leader, 3));
            SocialSnapshotMessage member = Snapshot(Guild(EGuildRole.Member, 3));
            var self  = new SocialGuildMemberEntry("c1", "m1", "나", 0, EGuildRole.Leader, EPresence.Town, true);
            var other = new SocialGuildMemberEntry("c2", "m2", "너", 0, EGuildRole.Member, EPresence.Town, true);

            Assert.That(GuildViewRules.CanManage(leader, other), Is.True);
            Assert.That(GuildViewRules.CanManage(leader, self), Is.False);
            Assert.That(GuildViewRules.CanManage(member, other), Is.False);
            Assert.That(GuildViewRules.ShowsLeaderTools(leader), Is.True);
            Assert.That(GuildViewRules.ShowsLeaderTools(member), Is.False);
        }

        [Test]
        public void Leave_LeaderWithMembersMustDelegate_AloneDisbands()
        {
            SocialSnapshotMessage crowded = Snapshot(Guild(EGuildRole.Leader, 3));
            SocialSnapshotMessage alone   = Snapshot(Guild(EGuildRole.Leader, 1));
            SocialSnapshotMessage member  = Snapshot(Guild(EGuildRole.Member, 3));

            Assert.That(GuildViewRules.MustDelegateBeforeLeave(crowded), Is.True);
            Assert.That(GuildViewRules.MustDelegateBeforeLeave(alone), Is.False);
            Assert.That(GuildViewRules.LeaveConfirmText(alone), Does.Contain("해체"));
            Assert.That(GuildViewRules.LeaveConfirmText(member), Does.Not.Contain("해체"));
        }

        [Test]
        public void SortMembers_Roles_LeaderFirstThenPresence()
        {
            var members = new List<SocialGuildMemberEntry>
            {
                new SocialGuildMemberEntry("1", "m1", "가", 0, EGuildRole.Member, EPresence.Offline, false),
                new SocialGuildMemberEntry("2", "m2", "하", 0, EGuildRole.Leader, EPresence.Offline, false),
                new SocialGuildMemberEntry("3", "m3", "나", 0, EGuildRole.Member, EPresence.Town, false)
            };

            GuildViewRules.SortMembers(members);

            Assert.That(members.ConvertAll(m => m.CharacterId), Is.EqualTo(new[] { "2", "3", "1" }));
        }

        [TestCase("청룡단", true, "청룡단")]
        [TestCase("  Dragon1 ", true, "Dragon1")]
        [TestCase("ＡＢ", true, "AB")]
        [TestCase("가", false, null)]
        [TestCase("아홉글자길드이름임", false, null)]
        [TestCase("청 룡", false, null)]
        [TestCase("청룡!", false, null)]
        public void TryNormalizeGuildName_Inputs_FollowRules(string input, bool expected, string name)
        {
            Assert.That(GuildViewRules.TryNormalizeGuildName(input, out string normalized), Is.EqualTo(expected));
            Assert.That(normalized, Is.EqualTo(name));
        }

        /****************************************
        *             SocialViewRules
        ****************************************/

        [Test]
        public void Describe_EverySocialReject_HasSpecificText()
        {
            string fallback = SocialViewRules.Describe(ERejectReason.None);
            foreach (ERejectReason reason in Enum.GetValues(typeof(ERejectReason)))
            {
                if (((int)reason < 1600) || ((int)reason >= 1700))
                {
                    continue;
                }

                Assert.That(SocialViewRules.Describe(reason), Is.Not.EqualTo(fallback), reason.ToString());
            }

            Assert.That(SocialViewRules.Describe(ERejectReason.FeatureDisabled), Is.EqualTo("현재 사용할 수 없는 기능입니다."));
            Assert.That(SocialViewRules.Describe(ERejectReason.BackendUnavailable), Is.EqualTo(SocialViewRules.UNKNOWN_TEXT));
        }

        [Test]
        public void DescribeNotice_EveryKind_HasTextAndUnknownName()
        {
            foreach (ESocialNotice kind in Enum.GetValues(typeof(ESocialNotice)))
            {
                Assert.That(SocialViewRules.DescribeNotice(kind, "홍길동", "청룡단"), Is.Not.Empty, kind.ToString());
            }

            Assert.That(SocialViewRules.DescribeNotice(ESocialNotice.FriendRequested, string.Empty, null),
                Does.Contain(SocialViewRules.UNKNOWN_NAME));
        }

        [Test]
        public void ActionGroups_Actions_SplitFriendAndGuild()
        {
            Assert.That(SocialViewRules.IsFriendAction(ESocialAction.FriendRemove), Is.True);
            Assert.That(SocialViewRules.IsFriendAction(ESocialAction.GuildCreate), Is.False);
            Assert.That(SocialViewRules.IsGuildAction(ESocialAction.GuildDelegate), Is.True);
            Assert.That(SocialViewRules.IsGuildAction(ESocialAction.Snapshot), Is.False);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static SocialGuildView Guild(EGuildRole role, int memberCount)
        {
            return new SocialGuildView("g1", "청룡단", memberCount, role, "m1", 0);
        }

        private static SocialSnapshotMessage Snapshot(SocialGuildView guild)
        {
            return new SocialSnapshotMessage(NOW_MS, null, null, null, guild, null, null, null);
        }
    }
}
