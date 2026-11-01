// System
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 프로젝트 내부
using Shinshi.Common;
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

        private const string ACQUIRER_NOTE          = "\n(정련편, 정수는 보상을 얻은 캐릭터가 받습니다)";
        private const string LIMIT_FULL_FORMAT      = "{0} 보관 한도에 도달해 받을 수 없습니다{1}. 보유량을 줄인 뒤 다시 받아 주세요";
        private const string LIMIT_ACQUIRER_SUFFIX  = "(정련편은 보상을 얻은 캐릭터 기준)";
        private const string DELETED_ACQUIRER_TEXT  = "보상을 얻은 캐릭터가 삭제되어 받을 수 없습니다";

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
            else if (IsRecoverySource(entry.SourceKind) && HasCharacterAsset(rewards))
            {
                builder.Append(ACQUIRER_NOTE);
            }

            return builder.ToString();
        }

        // 회수함 수령은 품목별 보관 한도(정련편은 캐릭터당, 티켓과 재료는 계정당 합계)까지만 옮긴다.
        // 가방 칸이 남아도 한도에 닿으면 INVENTORY_FULL이라, 어떤 품목이 막혔는지 알려야 한다
        /// <summary> 수령 거절 안내. 보관함 가득은 한도가 있는 품목 이름을 붙인다 </summary>
        public static string ClaimFailureText(ERejectReason reason, InboxEntry entry)
        {
            if (reason == ERejectReason.CharacterNotFound)
            {
                return DELETED_ACQUIRER_TEXT;
            }

            string names = (reason == ERejectReason.InventoryFull) ? LimitedRewardNames(entry) : string.Empty;
            if (names.Length == 0)
            {
                return RejectText.Describe(reason);
            }

            bool characterBound = IsRecoverySource(entry.SourceKind) && HasCharacterAsset(entry.Rewards);
            return string.Format(CultureInfo.InvariantCulture, LIMIT_FULL_FORMAT, names,
                characterBound ? LIMIT_ACQUIRER_SUFFIX : string.Empty);
        }

        private static bool IsRecoverySource(string sourceKind)
        {
            return (sourceKind == SOURCE_SESSION_REWARD) || (sourceKind == SOURCE_AUCTION_REWARD)
                || (sourceKind == SOURCE_MARKET_RETURN);
        }

        private static bool HasCharacterAsset(InboxReward[] rewards)
        {
            if (rewards == null)
            {
                return false;
            }

            for (int i = 0; i < rewards.Length; ++i)
            {
                if ((rewards[i].AssetKind == ASSET_SHARDS) || (rewards[i].AssetKind == ASSET_ESSENCE))
                {
                    return true;
                }
            }

            return false;
        }

        // 보관 한도가 있는 보상(티켓, 정련편, 계정 아이템)의 이름. 엽전, 정수는 한도가 없어 뺀다
        private static string LimitedRewardNames(InboxEntry entry)
        {
            InboxReward[] rewards = entry?.Rewards;
            if (rewards == null)
            {
                return string.Empty;
            }

            var names = new List<string>(rewards.Length);
            for (int i = 0; i < rewards.Length; ++i)
            {
                string kind = rewards[i].AssetKind;
                if ((kind != ASSET_TICKETS) && (kind != ASSET_SHARDS) && (kind != ASSET_ACCOUNT_ITEM))
                {
                    continue;
                }

                string name = RewardName(rewards[i]);
                if ((!string.IsNullOrEmpty(name)) && (!names.Contains(name)))
                {
                    names.Add(name);
                }
            }

            return string.Join(", ", names);
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
