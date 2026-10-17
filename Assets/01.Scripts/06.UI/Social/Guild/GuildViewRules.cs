// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 길드 창의 상태 전환, 길드원 정렬, 길드장 전용 버튼, 탈퇴 확인 글자를 정하는 순수 규칙 </summary>
    public static class GuildViewRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int TAB_MEMBERS      = 0;
        public const int TAB_APPLICATIONS = 1;
        public const int TAB_COUNT        = 2;

        // 한 쪽에 보이는 줄 수, 빌더가 만드는 줄 칸 수와 같다
        public const int ROWS_PER_PAGE = 8;

        private const string TITLE_NO_GUILD     = "길드";
        private const string TITLE_FORMAT       = "길드: {0}";
        private const string LEADER_MARK        = "(길드장)";
        private const string TAB_MEMBERS_LABEL  = "길드원";
        private const string TAB_APPLY_LABEL    = "가입 신청";
        private const string BADGE_FORMAT       = "{0}({1})";
        private const string DISBAND_CONFIRM    = "혼자 남은 길드장이 탈퇴하면 길드가 해체됩니다. 탈퇴할까요?";
        private const string LEAVE_CONFIRM      = "길드에서 탈퇴할까요?";
        private const string MUST_DELEGATE_TEXT = "길드장은 다른 길드원에게 위임한 뒤 탈퇴할 수 있습니다.";

        private static readonly Comparison<SocialGuildMemberEntry> G_MemberOrder = CompareMembers;

        /****************************************
        *                Methods
        ****************************************/

        public static EGuildWindowMode ModeOf(in SocialSnapshotMessage snapshot)
        {
            return snapshot.Guild.HasGuild ? EGuildWindowMode.Guild : EGuildWindowMode.NoGuild;
        }

        public static bool IsLeader(in SocialSnapshotMessage snapshot)
        {
            return (snapshot.Guild.HasGuild) && (snapshot.Guild.MyRole == EGuildRole.Leader);
        }

        public static string TitleText(in SocialSnapshotMessage snapshot)
        {
            return snapshot.Guild.HasGuild
                ? string.Format(CultureInfo.InvariantCulture, TITLE_FORMAT, snapshot.Guild.Name)
                : TITLE_NO_GUILD;
        }

        // 길드장이 맨 위, 그다음 신시 -> 출정 중 -> 오프라인, 같은 위치 안에서는 이름순
        /// <summary> 길드원 목록을 표시 순서로 정렬한다 </summary>
        public static void SortMembers(List<SocialGuildMemberEntry> members)
        {
            members?.Sort(G_MemberOrder);
        }

        /// <summary> 길드원 줄의 위임, 추방 버튼을 보일지. 길드장에게만, 길드장 자신의 줄에는 보이지 않는다 </summary>
        public static bool CanManage(in SocialSnapshotMessage snapshot, in SocialGuildMemberEntry member)
        {
            return (IsLeader(snapshot)) && (member.Role != EGuildRole.Leader)
                && (!string.Equals(member.MembershipId, snapshot.Guild.MyMembershipId, StringComparison.Ordinal));
        }

        /// <summary> 가입 신청 탭과 초대 입력을 보일지. 길드장만 </summary>
        public static bool ShowsLeaderTools(in SocialSnapshotMessage snapshot)
        {
            return IsLeader(snapshot);
        }

        public static string RoleText(EGuildRole role)
        {
            return (role == EGuildRole.Leader) ? LEADER_MARK : string.Empty;
        }

        public static string TabLabel(int tab, int applicationCount)
        {
            if (tab != TAB_APPLICATIONS)
            {
                return TAB_MEMBERS_LABEL;
            }

            return (applicationCount > 0)
                ? string.Format(CultureInfo.InvariantCulture, BADGE_FORMAT, TAB_APPLY_LABEL, applicationCount)
                : TAB_APPLY_LABEL;
        }

        // 다른 길드원이 있는 길드장은 서버도 GuildLeaderMustDelegate로 거절한다. 여기서는 확인 없이 안내만 보인다
        /// <summary> 탈퇴 버튼을 눌렀을 때 바로 거절 안내를 보일지 </summary>
        public static bool MustDelegateBeforeLeave(in SocialSnapshotMessage snapshot)
        {
            return (IsLeader(snapshot)) && (snapshot.Guild.MemberCount > 1);
        }

        public static string MustDelegateText()
        {
            return MUST_DELEGATE_TEXT;
        }

        /// <summary> 탈퇴 확인 글자. 혼자 남은 길드장이면 해체 경고다 </summary>
        public static string LeaveConfirmText(in SocialSnapshotMessage snapshot)
        {
            return ((IsLeader(snapshot)) && (snapshot.Guild.MemberCount <= 1)) ? DISBAND_CONFIRM : LEAVE_CONFIRM;
        }

        // 서버가 같은 규칙으로 다시 검사한다. 여기서는 잘못된 이름으로 요청을 보내지 않으려고 미리 거른다
        /// <summary> 길드 이름 입력을 백엔드와 같은 GuildNameRules로 정규화한다 </summary>
        public static bool TryNormalizeGuildName(string input, out string name)
        {
            return GuildNameRules.TryNormalize(input, out name, out _);
        }

        private static int CompareMembers(SocialGuildMemberEntry a, SocialGuildMemberEntry b)
        {
            int byRole = (b.Role == EGuildRole.Leader).CompareTo(a.Role == EGuildRole.Leader);
            if (byRole != 0)
            {
                return byRole;
            }

            int byPresence = FriendsViewRules.PresenceOrder(a.Presence).CompareTo(FriendsViewRules.PresenceOrder(b.Presence));
            return (byPresence != 0) ? byPresence : string.CompareOrdinal(a.Name, b.Name);
        }
    }
}
