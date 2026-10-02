// System
using System;

// Project
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Auctions
{
    // 기획 수치(최저가 100, 최소 증액 10, 품목당 최초 시작부터 120초 상한)는 Unity SessionConfigSO의 파티 경매 규칙과 같아야 한다.
    // 기본 30초와 막판 10초 연장은 던전 서버 시계가 판정하고 API는 절대 상한만 검증한다
    // TODO(민성): 경매 규칙 수치의 정본을 한곳으로 모은다. 지금은 SessionConfigSO와 이 상수에 이중으로 있다
    /// <summary> 파티 경매의 API 검증 규칙과 분배 계산 </summary>
    public static class PartyAuctionRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const long MIN_BID       = 100;
        public const long MIN_INCREMENT = 10;
        public const long SOLO_PRICE    = MIN_BID;

        public const int ITEM_HARD_LIMIT_SECONDS = 120;

        // 던전 시계와 DB 시계의 차이를 받아 주는 여유
        public const int CLOCK_SKEW_SECONDS = 5;

        // 보스 성공 정산 뒤 경매 단계 전체 기한. 현재 규칙은 처치당 품목 1개라 120초 상한 + 개시 여유다
        public const int AUCTION_PHASE_SECONDS = 180;

        // 경매 단계 기한이 지난 뒤 복구가 끼어들기 전 기다리는 시간
        public const int PHASE_RECOVERY_GRACE_SECONDS = 60;

        // 현재 기획: 처치당 최대 1품목
        public const int MAX_ITEMS_PER_SESSION = 1;
        public const int MAX_ELIGIBLE          = 4;

        public const string BINDING_KIND = SettlementSanityLimits.ACCOUNT_BINDING_KIND;

        // auctions.mode
        public const string MODE_BIDDING = "Bidding";
        public const string MODE_SOLO    = "Solo";

        // auctions.state
        public const string STATE_OPEN      = "Open";
        public const string STATE_CLOSING   = "Closing";
        public const string STATE_SOLD      = "Sold";
        public const string STATE_UNSOLD    = "Unsold";
        public const string STATE_CANCELLED = "Cancelled";

        /****************************************
        *              Properties
        ****************************************/

        public static TimeSpan ItemHardLimit      => TimeSpan.FromSeconds(ITEM_HARD_LIMIT_SECONDS);
        public static TimeSpan ClockSkew          => TimeSpan.FromSeconds(CLOCK_SKEW_SECONDS);
        public static TimeSpan AuctionPhase       => TimeSpan.FromSeconds(AUCTION_PHASE_SECONDS);
        public static TimeSpan PhaseRecoveryGrace => TimeSpan.FromSeconds(PHASE_RECOVERY_GRACE_SECONDS);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 자격자가 한 명이면 단독 최저가 구매로 연다. 여러 명으로 연 품목은 도중에 줄어도 바꾸지 않는다 </summary>
        public static string ModeFor(int eligibleCount)
        {
            return (eligibleCount == 1) ? MODE_SOLO : MODE_BIDDING;
        }

        /// <summary> 이 입찰이 받아들여지는 금액인가. 단독 모드는 최저가 한 번만, 경쟁 모드는 최저가 또는 직전 최고가 + 최소 증액 이상 </summary>
        public static bool IsAcceptableBid(string mode, long? highBid, long amount)
        {
            if (mode == MODE_SOLO)
            {
                return (!highBid.HasValue) && (amount == SOLO_PRICE);
            }

            // 최소 증액을 더할 수 없는 최고가면 더 높은 입찰이 없다. 더하면 음수로 돌아 낮은 입찰이 통과한다
            if (highBid.HasValue && (highBid.Value > long.MaxValue - MIN_INCREMENT))
            {
                return false;
            }

            long minimum = highBid.HasValue ? highBid.Value + MIN_INCREMENT : MIN_BID;
            return amount >= minimum;
        }

        /// <summary> 낙찰금을 분배 인원으로 내림해 똑같이 나누고 나머지는 소각한다. 대상이 없으면 전액 소각, 수수료 0% </summary>
        public static (long Each, long Burned) Split(long price, int recipients)
        {
            if (price <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(price));
            }
            if (recipients < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(recipients));
            }
            if (recipients == 0)
            {
                return (0, price);
            }

            long each = price / recipients;
            return (each, price - (each * recipients));
        }
    }
}
