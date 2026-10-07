// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.UI
{
    /// <summary> 경매장 화면이 쓰는 순수 계산, 수수료 미리보기와 입력 검사, 이름 검색, 남은 시간 글자 </summary>
    public static class AuctionViewRules
    {
        // 수수료와 수령액은 서버가 확정한다. 여기 값은 등록 화면 안내용이고, 나눠 팔리면 건마다 올림이라 조금 적을 수 있다

        /****************************************
        *                Fields
        ****************************************/

        private const long MS_PER_SECOND = 1000L;
        private const long SECONDS_PER_MINUTE = 60L;
        private const long MINUTES_PER_HOUR = 60L;
        private const long HOURS_PER_DAY = 24L;
        private const string EXPIRED_TEXT = "만료";
        private const string DAY_FORMAT = "{0}일 {1}시간";
        private const string HOUR_FORMAT = "{0}시간 {1}분";
        private const string MINUTE_FORMAT = "{0}분";
        private const string PRICE_FORMAT = "{0:N0}";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 총액, 수수료(5% 올림), 판매자 수령액. 총액이 64비트를 넘으면 거짓 </summary>
        public static bool TryPreview(long unitPrice, int quantity, out long gross, out long fee, out long net)
        {
            gross = 0;
            fee   = 0;
            net   = 0;
            if ((unitPrice < 0) || (quantity < 0) || (!MarketFeeRules.TryGross(unitPrice, quantity, out gross)))
            {
                return false;
            }

            fee = MarketFeeRules.Fee(gross);
            net = gross - fee;
            return true;
        }

        /// <summary> 등록 폼 입력 검사, maxQuantity는 고른 품목의 최대 판매 수량 </summary>
        public static EAuctionFormError ValidateRegister(bool hasItem, long unitPrice, int quantity, int maxQuantity)
        {
            if (!hasItem)
            {
                return EAuctionFormError.NoItem;
            }

            if (!MarketTradeRules.IsUnitPriceInRange(unitPrice))
            {
                return EAuctionFormError.PriceRange;
            }

            if ((!MarketTradeRules.IsQuantityInRange(quantity)) || (quantity > maxQuantity))
            {
                return EAuctionFormError.QuantityRange;
            }

            return EAuctionFormError.None;
        }

        public static string FormErrorText(EAuctionFormError error)
        {
            switch (error)
            {
                case EAuctionFormError.NoItem:
                    return "판매할 품목을 고르세요";
                case EAuctionFormError.PriceRange:
                    return string.Format(CultureInfo.InvariantCulture, "개당 가격은 {0:N0} ~ {1:N0}이어야 합니다", MarketLimits.MIN_UNIT_PRICE,
                        MarketLimits.MAX_UNIT_PRICE);
                case EAuctionFormError.QuantityRange:
                    return "수량을 확인하세요";
                default:
                    return string.Empty;
            }
        }

        public static int ClampQuantity(int quantity, int max)
        {
            if (max < 1)
            {
                return 0;
            }

            return Math.Max(1, Math.Min(quantity, max));
        }

        /// <summary> 숫자 입력 글자를 읽는다, 쉼표와 공백은 무시하고 음수와 숫자가 아닌 글자는 거짓 </summary>
        public static bool TryParseNumber(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string cleaned = text.Replace(",", string.Empty).Trim();
            return (long.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out value));
        }

        public static string FormatPrice(long value)
        {
            return string.Format(CultureInfo.InvariantCulture, PRICE_FORMAT, value);
        }

        /// <summary> 한 페이지에 목록을 다 보여 준 뒤 전체 쪽 수, 항목이 없으면 1쪽 </summary>
        public static int PageCount(int totalCount, int pageSize)
        {
            if ((totalCount <= 0) || (pageSize <= 0))
            {
                return 1;
            }

            return ((totalCount - 1) / pageSize) + 1;
        }

        /// <summary> 서버 시각 기준 남은 시간 글자 </summary>
        public static string FormatRemaining(long expiresAtUnixMs, long serverNowUnixMs)
        {
            long seconds = (expiresAtUnixMs - serverNowUnixMs) / MS_PER_SECOND;
            if (seconds <= 0)
            {
                return EXPIRED_TEXT;
            }

            long minutes = seconds / SECONDS_PER_MINUTE;
            long hours   = minutes / MINUTES_PER_HOUR;
            if (hours >= HOURS_PER_DAY)
            {
                return string.Format(CultureInfo.InvariantCulture, DAY_FORMAT, hours / HOURS_PER_DAY, hours % HOURS_PER_DAY);
            }

            if (hours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, HOUR_FORMAT, hours, minutes % MINUTES_PER_HOUR);
            }

            return string.Format(CultureInfo.InvariantCulture, MINUTE_FORMAT, Math.Max(1L, minutes));
        }

        public static string ListingStateText(string state)
        {
            switch (state)
            {
                case MarketCodes.ListingState.ACTIVE:
                    return "판매 중";
                case MarketCodes.ListingState.SOLD_OUT:
                    return "판매 완료";
                case MarketCodes.ListingState.CANCELLED:
                    return "취소됨";
                case MarketCodes.ListingState.EXPIRED:
                    return "기간 만료";
                default:
                    return string.Empty;
            }
        }

        public static string RoleText(string role)
        {
            return (role == MarketCodes.TradeRole.SELLER) ? "판매" : "구매";
        }

        /// <summary> 입력 글자가 정의 ID에 들어 있는 후보를 모은다. 표시 이름 번역 표가 없어 정의 ID로 찾는다. 빈 글자는 빈 목록 </summary>
        public static void FindDefinitionIds(string text, IEnumerable<string> candidates, int max, List<string> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            if ((string.IsNullOrWhiteSpace(text)) || (candidates == null))
            {
                return;
            }

            string needle = text.Trim();
            foreach (string id in candidates)
            {
                if ((!string.IsNullOrEmpty(id)) && (id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) && (!into.Contains(id)))
                {
                    into.Add(id);
                    if (into.Count >= max)
                    {
                        return;
                    }
                }
            }
        }

        public static string BuyConfirmText(string itemName, int quantity, long unitPrice, long gross)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}개를 개당 {2}, 총 {3}에 구매할까요?", itemName, quantity,
                FormatPrice(unitPrice), FormatPrice(gross));
        }

        public static string ListConfirmText(string itemName, int quantity, long unitPrice, long netIfAllSold)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} {1}개를 개당 {2}에 등록할까요?\n모두 팔리면 수수료를 뺀 {3}을 받습니다.", itemName, quantity, FormatPrice(unitPrice),
                FormatPrice(netIfAllSold));
        }

        public static string CancelConfirmText(string itemName, int remainingQuantity)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} 남은 {1}개의 등록을 취소할까요?", itemName, remainingQuantity);
        }
    }
}
