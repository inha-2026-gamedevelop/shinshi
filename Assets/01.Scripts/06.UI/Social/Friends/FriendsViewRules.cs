// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 친구 창의 정렬, 위치 글자, 버튼 조건, 남은 기한 글자를 정하는 순수 규칙 </summary>
    public static class FriendsViewRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int TAB_FRIENDS  = 0;
        public const int TAB_INCOMING = 1;
        public const int TAB_OUTGOING = 2;
        public const int TAB_COUNT    = 3;

        // 한 쪽에 보이는 줄 수, 빌더가 만드는 줄 칸 수와 같다
        public const int ROWS_PER_PAGE = 8;

        private const string PRESENCE_TOWN      = "신시";
        private const string PRESENCE_DUNGEON   = "출정 중";
        private const string PRESENCE_OFFLINE   = "오프라인";
        private const string TAB_FRIENDS_LABEL  = "친구";
        private const string TAB_INCOMING_LABEL = "받은 요청";
        private const string TAB_OUTGOING_LABEL = "보낸 요청";
        private const string COUNT_FORMAT       = "{0}/{1}";
        private const string BADGE_FORMAT       = "{0}({1})";
        private const string DAYS_LEFT_FORMAT   = "{0}일 남음";
        private const string HOURS_LEFT_FORMAT  = "{0}시간 남음";
        private const string SOON_TEXT          = "곧 만료";
        private const string PAGE_FORMAT        = "{0} / {1}";

        // 위치 정렬 순번
        private const int ORDER_TOWN    = 0;
        private const int ORDER_DUNGEON = 1;
        private const int ORDER_OFFLINE = 2;

        private const long MS_PER_HOUR = 3600L * 1000L;
        private const long MS_PER_DAY  = 24L * MS_PER_HOUR;

        private static readonly Comparison<SocialFriendEntry> G_FriendOrder = CompareFriends;

        /****************************************
        *                Methods
        ****************************************/

        // 신시 -> 출정 중 -> 오프라인, 같은 위치 안에서는 이름순
        /// <summary> 친구 목록을 표시 순서로 정렬한다 </summary>
        public static void SortFriends(List<SocialFriendEntry> friends)
        {
            friends?.Sort(G_FriendOrder);
        }

        /// <summary> 접속 위치의 정렬 순번, 작을수록 위다 </summary>
        public static int PresenceOrder(EPresence presence)
        {
            switch (presence)
            {
                case EPresence.Town:
                    return ORDER_TOWN;
                case EPresence.Dungeon:
                    return ORDER_DUNGEON;
                default:
                    return ORDER_OFFLINE;
            }
        }

        public static string PresenceText(EPresence presence)
        {
            switch (presence)
            {
                case EPresence.Town:
                    return PRESENCE_TOWN;
                case EPresence.Dungeon:
                    return PRESENCE_DUNGEON;
                default:
                    return PRESENCE_OFFLINE;
            }
        }

        /// <summary> 귓속말 버튼을 켤지. 상대가 접속 중이면 마을, 던전 어디든 보낼 수 있다 </summary>
        public static bool CanWhisper(EPresence presence)
        {
            return presence != EPresence.Offline;
        }

        // 파티 초대는 같은 마을 프로세스에서만 대상 이름을 찾는다. 서버가 다시 검증한다
        /// <summary> 파티 초대 버튼을 켤지. 상대가 같은 마을 서버에 있고 내가 모집 중인 파티 방에 있을 때만 </summary>
        public static bool CanPartyInvite(EPresence presence, bool sameServer, bool inRoom, EPartyPhase phase)
        {
            return (presence == EPresence.Town) && (sameServer) && (inRoom) && (phase == EPartyPhase.Open);
        }

        /// <summary> "12/50" 같은 수 표시 </summary>
        public static string CountText(int count, int max)
        {
            return string.Format(CultureInfo.InvariantCulture, COUNT_FORMAT, count, max);
        }

        /// <summary> 탭 글자. 받은 요청 탭은 요청이 있으면 수를 붙인다 </summary>
        public static string TabLabel(int tab, int incomingCount)
        {
            switch (tab)
            {
                case TAB_INCOMING:
                    return (incomingCount > 0)
                        ? string.Format(CultureInfo.InvariantCulture, BADGE_FORMAT, TAB_INCOMING_LABEL, incomingCount)
                        : TAB_INCOMING_LABEL;
                case TAB_OUTGOING:
                    return TAB_OUTGOING_LABEL;
                default:
                    return TAB_FRIENDS_LABEL;
            }
        }

        // 하루 이상이면 일 단위(올림이 아니라 내림), 하루 안이면 시간, 한 시간 안이면 곧 만료
        /// <summary> 요청, 초대의 남은 기한 글자. 서버 시각 기준이다 </summary>
        public static string RemainingText(long expiresAtUnixMs, long nowUnixMs)
        {
            long left = expiresAtUnixMs - nowUnixMs;
            if (left >= MS_PER_DAY)
            {
                return string.Format(CultureInfo.InvariantCulture, DAYS_LEFT_FORMAT, left / MS_PER_DAY);
            }

            if (left >= MS_PER_HOUR)
            {
                return string.Format(CultureInfo.InvariantCulture, HOURS_LEFT_FORMAT, left / MS_PER_HOUR);
            }

            return SOON_TEXT;
        }

        /// <summary> 줄 수에 필요한 쪽 수, 비어 있어도 1쪽이다 </summary>
        public static int PageCount(int itemCount)
        {
            return (itemCount <= 0) ? 1 : ((itemCount + ROWS_PER_PAGE - 1) / ROWS_PER_PAGE);
        }

        public static int ClampPage(int page, int itemCount)
        {
            int last = PageCount(itemCount) - 1;
            return (page < 0) ? 0 : ((page > last) ? last : page);
        }

        public static string PageText(int page, int itemCount)
        {
            return string.Format(CultureInfo.InvariantCulture, PAGE_FORMAT, page + 1, PageCount(itemCount));
        }

        private static int CompareFriends(SocialFriendEntry a, SocialFriendEntry b)
        {
            int byPresence = PresenceOrder(a.Presence).CompareTo(PresenceOrder(b.Presence));
            return (byPresence != 0) ? byPresence : string.CompareOrdinal(a.Name, b.Name);
        }
    }
}
