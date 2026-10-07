// System
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.UI
{
    /// <summary> 우편함 화면이 쓰는 순수 계산, 우편에 제목과 본문이 없어 출처별 고정 제목과 보상 목록으로 글자를 만든다 </summary>
    public static class MailBoxViewRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 출처는 Backend RecoveryInbox의 source_kind와 같다
        private const string SOURCE_SESSION_REWARD = "SessionReward";
        private const string SOURCE_AUCTION_REWARD = "AuctionReward";
        private const string SOURCE_MARKET_RETURN  = "MarketReturn";

        // 보상 종류는 Backend EconomyAsset과 같다
        private const string ASSET_CURRENCY     = "Currency";
        private const string ASSET_TICKETS      = "Tickets";
        private const string ASSET_ESSENCE      = "Essence";
        private const string ASSET_SHARDS       = "Shards";
        private const string ASSET_ACCOUNT_ITEM = "AccountItem";

        private const long   MS_PER_DAY = 86400000L;
        private const long   MS_PER_HOUR = 3600000L;
        private const int    MAX_REWARD_LINES_IN_ROW = 1;
        private const string DATE_FORMAT = "MM-dd HH:mm";

        /****************************************
        *                Methods
        ****************************************/

        public static string TitleOf(string sourceKind)
        {
            switch (sourceKind)
            {
                case SOURCE_SESSION_REWARD:
                    return "출정 보상";
                case SOURCE_AUCTION_REWARD:
                    return "경매 보상";
                case SOURCE_MARKET_RETURN:
                    return "경매장 반환";
                default:
                    return "운영 지급";
            }
        }

        public static string SenderOf(string sourceKind)
        {
            return (sourceKind == SOURCE_MARKET_RETURN) ? "경매장" : "신시";
        }

        public static string RewardName(InboxReward reward)
        {
            switch (reward.AssetKind)
            {
                case ASSET_CURRENCY:
                    return "엽전";
                case ASSET_TICKETS:
                    return "보스 입장권";
                case ASSET_ESSENCE:
                    return "정수";
                case ASSET_SHARDS:
                    return "정련편";
                case ASSET_ACCOUNT_ITEM:
                    return reward.AssetId;
                default:
                    return reward.AssetKind ?? string.Empty;
            }
        }

        /// <summary> 보상 한 줄 "이름 x수량" </summary>
        public static string RewardLine(InboxReward reward)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} x{1:N0}", RewardName(reward), reward.Quantity);
        }

        /// <summary> 목록 줄에 보일 요약, 첫 보상과 나머지 개수 </summary>
        public static string Summary(InboxReward[] rewards)
        {
            if ((rewards == null) || (rewards.Length == 0))
            {
                return string.Empty;
            }

            string first = RewardLine(rewards[0]);
            return (rewards.Length > MAX_REWARD_LINES_IN_ROW)
                ? string.Format(CultureInfo.InvariantCulture, "{0} 외 {1}건", first, rewards.Length - MAX_REWARD_LINES_IN_ROW)
                : first;
        }

        /// <summary> 상세 본문, 보상을 줄마다 적고 캐릭터가 필요한 우편이면 안내를 덧붙인다 </summary>
        public static string Body(InboxEntry entry)
        {
            var builder = new StringBuilder();
            builder.Append("받은 보상을 확인하세요.");
            InboxReward[] rewards = entry.Rewards ?? new InboxReward[0];
            for (int i = 0; i < rewards.Length; ++i)
            {
                builder.Append('\n').Append(RewardLine(rewards[i]));
            }

            if (entry.RequiresCharacter)
            {
                builder.Append("\n(현재 캐릭터로 받습니다)");
            }

            return builder.ToString();
        }

        /// <summary> 발신일 글자, 서버 시각을 그대로 날짜로 바꾼다(표시 전용) </summary>
        public static string DateText(long unixMs)
        {
            return System.DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary> 만료까지 남은 날짜 글자 </summary>
        public static string ExpiryText(long expiresAtUnixMs, long serverNowUnixMs)
        {
            long remaining = expiresAtUnixMs - serverNowUnixMs;
            if (remaining <= 0)
            {
                return "만료";
            }

            if (remaining >= MS_PER_DAY)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}일 남음", remaining / MS_PER_DAY);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}시간 남음", System.Math.Max(1L, remaining / MS_PER_HOUR));
        }

        /// <summary> 목록 쪽 수, 한 쪽에 pageSize건 </summary>
        public static int PageCount(int total, int pageSize)
        {
            return AuctionViewRules.PageCount(total, pageSize);
        }

        /// <summary> 쪽 번호에 해당하는 구간을 구한다 </summary>
        public static void PageRange(int total, int pageSize, int page, out int start, out int count)
        {
            start = page * pageSize;
            count = System.Math.Max(0, System.Math.Min(pageSize, total - start));
        }

        public static void BuildRows(InboxEntry[] entries, long serverNowUnixMs, int start, int count, List<ListRowData> into)
        {
            into.Clear();
            for (int i = 0; i < count; ++i)
            {
                InboxEntry entry = entries[start + i];
                into.Add(new ListRowData(0, SenderOf(entry.SourceKind), TitleOf(entry.SourceKind), Summary(entry.Rewards),
                    DateText(entry.CreatedAtUnixMs), ExpiryText(entry.ExpiresAtUnixMs, serverNowUnixMs)));
            }
        }
    }
}
